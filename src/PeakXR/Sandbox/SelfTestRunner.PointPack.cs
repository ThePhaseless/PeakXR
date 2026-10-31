using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Inventory;
using PeakXR.UI.Wheels;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Issue #71 steps: with an Apple in one hand, the other hand points at a backpack on the ground and pulls its
/// trigger (<see cref="PackStash"/>'s point to open). vr.430 opens and stashes, vr.431 cancels, vr.432 refuses (far,
/// full, an item packs refuse), vr.433 keeps the held item's secondary use when the pull did not start on an armed
/// pick. vr.434 (a friend's worn pack) is an MpStep in <c>SelfTestRunner.MpTest.cs</c>.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The ground pack <see cref="PointSetup"/> spawned, the free hand, and the given Apple's prefab.</summary>
    Backpack pointPack;
    bool pointFree;
    Item pointApple;

    /// <summary>The last <see cref="SpawnPointPack"/>'s pack, null when it failed.</summary>
    Backpack spawnedPack;

    /// <summary>Level distance (m) from the eye of the near pack, within the 2 m reach from the eye, and the far one.</summary>
    const float PointNear = 1f;
    const float PointFar = 3f;

    /// <summary>Seen on any frame since the last clear: the held item's secondary use, an open backpack wheel.</summary>
    bool pointSawSecondary,
        pointSawWheel;

    void PointWatch()
    {
        var c = Character.localCharacter;
        var held = HeldItem();
        pointSawSecondary |=
            c.input.useSecondaryIsPressed || (held != null && held.isUsingSecondary);
        pointSawWheel |= c.data.usingBackpackWheel;
    }

    IEnumerator PointWait(float seconds)
    {
        for (float end = Now + seconds; Now < end; )
        {
            PointWatch();
            yield return null;
        }
    }

    static Item BackpackPrefab()
    {
        foreach (var it in SandboxActions.Items)
        {
            if (it is Backpack b && b.backpackType == BackpackSlot.BackpackType.Backpack)
                return it;
        }
        return null;
    }

    /// <summary>A Backpack set on the ground <paramref name="outward"/> m (level) from the eye, <paramref name="yaw"/>
    /// degrees off the view, and left to settle; <see cref="spawnedPack"/>.</summary>
    IEnumerator SpawnPointPack(float outward, float yaw)
    {
        spawnedPack = null;
        var head = VRRig.Instance.Head;
        var ahead = head.position + Quaternion.Euler(0f, yaw, 0f) * Flat(head.forward) * outward;
        float feet = TrackedFloorY();
        var spot =
            Terrain(new Vector3(ahead.x, feet + 0.6f, ahead.z), Vector3.down, 1.2f, out var g)
            && Mathf.Abs(g.point.y - feet) <= 0.3f
                ? g.point + Vector3.up * PropDrop
                : new Vector3(ahead.x, feet + PropDrop, ahead.z);
        if (!SandboxActions.SpawnItemAt(BackpackPrefab(), spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        spawnedPack = SandboxActions.LastSpawned.GetComponent<Backpack>();
        yield return Wait(0.8f);
    }

    /// <summary>Open ground ahead, a pack <see cref="PointNear"/> out, an Apple in hand with its grip closed.</summary>
    IEnumerator PointSetup()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        pointPack = null;
        if (!InventoryConfig.Instance.PointToOpenPack || !BodyInventory.Instance.Valid)
        {
            stepSkip = "Inventory.PointToOpenPack or Inventory.BodySlots is off";
            yield break;
        }
        pointApple = FindItem(SandboxActions.Items, AppleFragment);
        if (BackpackPrefab() == null || pointApple == null)
        {
            Fail("needs a Backpack and an Apple item");
            yield break;
        }
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no open ground to drop the pack on in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.5f);
        yield return SpawnPointPack(PointNear, 0f);
        if (!stepOk)
            yield break;
        yield return GiveAndHold(pointApple);
        if (!stepOk)
            yield break;
        pointPack = spawnedPack;
        pointFree = !LocalHold.ItemInLeftHand;
        yield return GripTo(!pointFree, 1f, 0.1f);
        pointSawSecondary = pointSawWheel = false;
    }

    /// <summary>The scripted hand on the eye's line to <paramref name="world"/>, turned <paramref name="offYaw"/>
    /// degrees about the eye, so it aims along that line (a scripted hand aims along its fingers).</summary>
    void PointAt(bool left, Vector3 world, float offYaw = 0f)
    {
        var head = VRInput.HeadPosition;
        var to = VRRig.Instance.Origin.InverseTransformPoint(world) - head;
        SetHand(left, head + Quaternion.Euler(0f, offYaw, 0f) * to.normalized * 0.55f);
    }

    static int HandIndex(bool left) => left ? 0 : 1;

    /// <summary>The free hand's pick is <paramref name="pack"/> with <paramref name="verdict"/>.</summary>
    static bool Points(bool left, Object pack, DenyReason verdict)
    {
        var s = PackStash.Instance;
        return s.PointHand == HandIndex(left)
            && ReferenceEquals(s.PointPack, pack)
            && s.PointVerdict == verdict;
    }

    static string PointState()
    {
        var s = PackStash.Instance;
        return $"point hand {s.PointHand}, pack {(s.PointPack != null ? s.PointPack.name : "none")}, verdict {s.PointVerdict}";
    }

    /// <summary>The hovered slice is a pocket, not Wear or jetpack fuel. A pocket's <c>isStashSlice</c> stays false:
    /// <c>stashSlice</c> belongs to <c>InitStashSlot</c>, which <c>BackpackWheel.InitWheel</c> never calls
    /// (BackpackWheel.cs:29-71); an empty pocket hovered with an item in hand is chosen through the item-slot path
    /// (:219-240) and <c>Choose</c> stashes into it (:148-151). Issue #71, run 12: vr.430 waited for isStashSlice.</summary>
    static bool PocketChosen(BackpackWheel w) =>
        w.chosenSlice.IsSome
        && !w.chosenSlice.Value.isBackpackWear
        && !w.chosenSlice.Value.isJetpackFuelSlice;

    static int PackItems(Backpack pack, ushort itemID)
    {
        int n = 0;
        foreach (var s in pack.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots)
        {
            if (s != null && !s.IsEmpty() && s.prefab.itemID == itemID)
                n++;
        }
        return n;
    }

    /// <summary>Points at the near pack until it arms with nothing refusing, then pulls: the wheel must open on the
    /// free hand without the secondary use. Fails the step otherwise.</summary>
    IEnumerator PointOpen()
    {
        var c = Character.localCharacter;
        bool free = pointFree;
        PointAt(free, pointPack.Center());
        yield return Until(
            () => Points(free, pointPack, DenyReason.None),
            1f,
            "the pack pointed at did not arm"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        pointSawSecondary = pointSawWheel = false;
        Script(free).Trigger = 1f;
        yield return Until(
            () =>
            {
                PointWatch();
                return c.data.usingBackpackWheel;
            },
            0.5f,
            "the trigger did not open the pack's wheel"
        );
        if (!stepOk)
            yield break;
        // RadialWheels places it in LateUpdate.
        yield return PointWait(0.1f);
        var rig = VRRig.Instance;
        if (BodyInventory.Instance.WheelHand != HandIndex(free))
            Fail(
                $"the wheel is held by hand {BodyInventory.Instance.WheelHand}, not the trigger's"
            );
        else if (PhysicalInteraction.WheelHeld)
            Fail("a grip latch holds the wheel open");
        else if (
            !RadialWheels.TryDrawnCentre(GUIManager.instance.backpackWheel, out var centre)
            || (Vector3.Dot(centre - rig.Head.position, rig.Head.right) < 0f) != free
        )
            Fail("the wheel is not drawn on the trigger hand's side");
        else if (pointSawSecondary)
            Fail("the pull ran the held item's secondary use");
    }

    /// <summary>Moves the holding hand past the wheel deadzone with its grip closed: it must pick no pocket.</summary>
    IEnumerator PointHolderMoves()
    {
        bool holder = !pointFree;
        yield return MoveTrack(holder, Script(holder).Position + Vector3.up * 0.15f, 0.2f);
        yield return PointWait(0.2f);
        var wheel = GUIManager.instance.backpackWheel;
        if (wheel.chosenSlice.IsSome)
            Fail("the holding hand picked a pocket of the wheel");
    }

    /// <summary>The laser rule (vr.430): a pack 14 degrees beside the near one, inside its 15 degree hold. With the near
    /// pack picked, aimed straight at the beside one the ray hits it and it must take the pick. Sets
    /// <see cref="stepNote"/>; the check is reported, not failed, when the rig cannot pose it (the ray hits something
    /// else, or the near pack is not inside the hold angle).</summary>
    IEnumerator PointRayWins(bool free)
    {
        yield return SpawnPointPack(PointNear, 14f);
        if (!stepOk)
            yield break;
        var beside = spawnedPack;
        var s = PackStash.Instance;
        PointAt(free, pointPack.Center());
        yield return Until(
            () => s.PointHand == HandIndex(free) && ReferenceEquals(s.PointPack, pointPack),
            1f,
            "the near pack aimed at was not the pick"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        PointAt(free, beside.Center());
        // The rig poses the aim in LateUpdate (RigDriver.UpdateHands) from the next sample, after this coroutine
        // resumes: one frame read the old aim still on the near pack (run 12: "near pack 2 deg off").
        for (int f = 0; f < 3; f++)
            yield return null;
        var aim = VRRig.Instance.Aim(free);
        float held = ReachOut.AimDegrees(
            aim.position.N(),
            (aim.position + aim.forward).N(),
            pointPack.Center().N()
        );
        bool rayHit = Physics.Raycast(
            aim.position,
            aim.forward,
            out var hit,
            PackDrop.PointSearch + 1f
        );
        bool rayOn = rayHit && hit.collider.GetComponentInParent<Backpack>() == beside;
        if (!rayOn || held > PackDrop.PointHold)
        {
            stepNote =
                $"laser rule not posed (ray on the beside pack {rayOn}, hit {(rayHit ? hit.collider.name : "nothing")}, near pack {held:0} deg off)";
            yield break;
        }
        yield return Until(
            () => s.PointHand == HandIndex(free) && ReferenceEquals(s.PointPack, beside),
            1f,
            $"the pack the ray hits lost to the held pick {held:0} deg off"
        );
        if (stepOk)
            stepNote = $"the ray-hit pack took the pick from one {held:0} deg off";
        else
            stepNote = PointState();
    }

    /// <summary>
    /// vr.430-point-pack-open: a pack 3 m away, aimed at, is the pick and shows OutOfReach (the aim picks, never the
    /// distance); the pack the ray hits wins over a held pick inside its hold angle (<see cref="PointRayWins"/>); the near pack aimed at arms with nothing refusing (screenshot of the teal ring). The pull opens its
    /// wheel on the free hand's side, held by that trigger, with no secondary use. The holding hand moving picks
    /// nothing; the free hand moved toward a pocket picks it, and the trigger's release puts the Apple into that
    /// pocket with the holding grip still closed. Then a pull aimed away from the pack is the secondary use again.
    /// </summary>
    IEnumerator PointPackOpenStep()
    {
        yield return PointSetup();
        if (!stepOk || pointPack == null)
            yield break;
        var c = Character.localCharacter;
        bool free = pointFree;
        yield return SpawnPointPack(PointFar, 35f);
        if (!stepOk)
            yield break;
        var far = spawnedPack;
        PointAt(free, far.Center());
        yield return Until(
            () => Points(free, far, DenyReason.OutOfReach),
            1f,
            "the pack aimed at 3 m away was not the pick with OutOfReach"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        yield return PointRayWins(free);
        if (!stepOk)
            yield break;
        string laser = stepNote;
        PointAt(free, pointPack.Center());
        yield return Until(
            () => Points(free, pointPack, DenyReason.None),
            1f,
            "the near pack aimed at did not arm"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        yield return ShotAt("vr-430-point-pack", pointPack.Center(), 1f);
        yield return PointOpen();
        if (!stepOk)
            yield break;
        yield return PointHolderMoves();
        if (!stepOk)
            yield break;
        var wheel = GUIManager.instance.backpackWheel;
        var start = Script(free).Position;
        for (int deg = 0; deg < 360 && !PocketChosen(wheel); deg += 30)
        {
            float a = deg * Mathf.Deg2Rad;
            SetHand(
                free,
                start + VRInput.HeadRotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.15f
            );
            yield return PointWait(0.15f);
        }
        if (!PocketChosen(wheel))
        {
            Fail("moving the trigger hand offered no pocket to stash in");
            yield break;
        }
        byte pocket = wheel.chosenSlice.Value.slotID;
        Script(free).Trigger = 0f;
        yield return Until(
            () =>
            {
                PointWatch();
                return !c.data.usingBackpackWheel && HeldItem() == null;
            },
            2f,
            "the trigger's release did not commit and close the wheel"
        );
        if (!stepOk)
            yield break;
        yield return PointWait(0.5f);
        var slot = pointPack.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots[pocket];
        if (slot == null || slot.IsEmpty() || slot.prefab.itemID != pointApple.itemID)
        {
            Fail($"the Apple is not in pocket {pocket}, the one the trigger hand picked");
            yield break;
        }
        if (pointSawSecondary)
        {
            Fail("the held item's secondary use ran during the pull");
            yield break;
        }
        // The trigger is the secondary use again away from a pack.
        yield return GiveAndHold(pointApple);
        if (!stepOk)
            yield break;
        free = !LocalHold.ItemInLeftHand;
        PointAt(free, pointPack.Center(), 60f);
        yield return PointWait(0.3f);
        pointSawSecondary = pointSawWheel = false;
        Script(free).Trigger = 1f;
        yield return PointWait(0.4f);
        Script(free).Trigger = 0f;
        yield return Wait(0.2f);
        if (!pointSawSecondary || pointSawWheel)
            Fail($"aimed away, the pull: secondary use {pointSawSecondary}, wheel {pointSawWheel}");
        else
            stepNote =
                $"far pack OutOfReach, {laser}, near pack opened by the trigger and the Apple went into pocket {pocket}; aimed away the trigger is the secondary use";
    }

    /// <summary>
    /// vr.431-point-pack-cancel: the wheel opened as in vr.430, the holding hand moved, the trigger let go with the
    /// free hand still: the wheel closes with the cancel feel on the free hand, the Apple stays in hand and the pack
    /// stays empty.
    /// </summary>
    IEnumerator PointPackCancelStep()
    {
        yield return PointSetup();
        if (!stepOk || pointPack == null)
            yield break;
        var c = Character.localCharacter;
        bool free = pointFree;
        yield return PointOpen();
        if (!stepOk)
            yield break;
        yield return PointHolderMoves();
        if (!stepOk)
            yield break;
        float mark = CueMark();
        Script(free).Trigger = 0f;
        yield return Until(
            () => !c.data.usingBackpackWheel,
            1f,
            "the wheel stayed open after the trigger opened"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(free, CueFeel.Cancel, mark);
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        if (!Holding(pointApple))
            Fail("the Apple left the hand on a release over no pocket");
        else if (PackItems(pointPack, pointApple.itemID) > 0)
            Fail("the Apple went into the pack on a release over no pocket");
        else
            stepNote = "released on no pocket: cancel feel, Apple still in hand, pack empty";
    }

    /// <summary>A pull on the armed pick that must be refused with <paramref name="reason"/>: the cue, no wheel and
    /// no secondary use.</summary>
    IEnumerator PointRefused(Backpack pack, DenyReason reason)
    {
        bool free = pointFree;
        PointAt(free, pack.Center());
        yield return Until(
            () => Points(free, pack, reason),
            1f,
            $"the pack pointed at did not show {reason}"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        LookAtHand(free);
        yield return Wait(0.3f);
        pointSawSecondary = pointSawWheel = false;
        float mark = CueMark();
        Script(free).Trigger = 1f;
        yield return ExpectCue(free, reason, mark);
        yield return PointWait(0.3f);
        Script(free).Trigger = 0f;
        EndGlance();
        yield return Wait(0.2f);
        if (stepOk && (pointSawWheel || pointSawSecondary))
            Fail($"{reason}: wheel {pointSawWheel}, secondary use {pointSawSecondary}");
    }

    /// <summary>
    /// vr.432-point-pack-refusals: (a) a pack 3 m away gives OutOfReach (with the range hint), (b) a pack with every
    /// pocket filled (Backpack.Stash, Backpack.cs:119) gives PackFull, (c) an Apple packs refuse (its instance's
    /// <c>UIData.canBackpack</c> off) gives CantHolster. Each pull gets the cue, no wheel and no secondary use.
    /// </summary>
    IEnumerator PointPackRefusalsStep()
    {
        yield return PointSetup();
        if (!stepOk || pointPack == null)
            yield break;
        var c = Character.localCharacter;
        // (c) first, on the empty near pack.
        var held = HeldItem();
        held.UIData.canBackpack = false;
        try
        {
            yield return PointRefused(pointPack, DenyReason.CantHolster);
        }
        finally
        {
            if (held != null)
                held.UIData.canBackpack = true;
        }
        if (!stepOk)
            yield break;
        // (a) a pack past reach.
        yield return SpawnPointPack(PointFar, 35f);
        if (!stepOk)
            yield break;
        yield return PointRefused(spawnedPack, DenyReason.OutOfReach);
        if (!stepOk)
            yield break;
        // (b) fill every pocket of the near pack with Apples, then point a held one at it.
        var slots = pointPack.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots;
        for (int i = 0; i < Mathf.Min(slots.Length, pointPack.slotCount); i++)
        {
            if (HeldItem() == null)
            {
                yield return GiveAndHold(pointApple);
                if (!stepOk)
                    yield break;
            }
            if (slots[i] == null || !slots[i].IsEmpty())
                continue;
            pointPack.Stash(c, (byte)i);
            yield return Until(
                () => HeldItem() == null,
                2f,
                $"Backpack.Stash did not take pocket {i}"
            );
            if (!stepOk)
                yield break;
        }
        if (HeldItem() == null)
            yield return GiveAndHold(pointApple);
        if (!stepOk)
            yield break;
        pointFree = !LocalHold.ItemInLeftHand;
        yield return GripTo(!pointFree, 1f, 0.1f);
        yield return PointRefused(pointPack, DenyReason.PackFull);
        if (stepOk)
            stepNote =
                "CantHolster, OutOfReach and PackFull each refused the pull with their cue: "
                + cueNote;
    }

    /// <summary>
    /// vr.433-point-pack-secondary-kept: aimed 40 degrees off the pack, a pull is the secondary use; swung onto the
    /// pack mid-pull it stays so, with no wheel. Armed on the pack and then aimed away for 0.1 s (inside the lost
    /// pick's grace), a pull is still the secondary use. With empty hands the free trigger reaches and no pick arms.
    /// </summary>
    IEnumerator PointPackSecondaryKeptStep()
    {
        yield return PointSetup();
        if (!stepOk || pointPack == null)
            yield break;
        var c = Character.localCharacter;
        bool free = pointFree;
        var stash = PackStash.Instance;
        PointAt(free, pointPack.Center(), 40f);
        yield return PointWait(0.3f);
        pointSawSecondary = pointSawWheel = false;
        Script(free).Trigger = 1f;
        yield return PointWait(0.3f);
        bool offSecondary = pointSawSecondary;
        PointAt(free, pointPack.Center());
        pointSawSecondary = false;
        yield return PointWait(0.4f);
        bool swungSecondary = c.input.useSecondaryIsPressed;
        Script(free).Trigger = 0f;
        yield return PointWait(0.2f);
        if (!offSecondary || !swungSecondary || pointSawWheel)
        {
            Fail(
                $"a pull started off the pack: secondary {offSecondary}, still secondary on the pack {swungSecondary}, wheel {pointSawWheel}"
            );
            yield break;
        }
        yield return Until(
            () => Points(free, pointPack, DenyReason.None),
            1f,
            "the pack did not arm"
        );
        if (!stepOk)
            yield break;
        PointAt(free, pointPack.Center(), 40f);
        yield return Wait(0.1f);
        pointSawSecondary = pointSawWheel = false;
        Script(free).Trigger = 1f;
        yield return PointWait(0.4f);
        Script(free).Trigger = 0f;
        yield return PointWait(0.2f);
        if (!pointSawSecondary || pointSawWheel)
        {
            Fail(
                $"a pull in the lost pick's grace: secondary {pointSawSecondary}, wheel {pointSawWheel}"
            );
            yield break;
        }
        // Empty hands: the trigger is the game's reach, and no pack pick arms.
        yield return DropHeld();
        if (!stepOk)
            yield break;
        yield return GripTo(!free, 0f, 0.1f);
        bool reach = PhysicalInteraction.UseHandLeft(c, secondary: true);
        PointAt(reach, pointPack.Center());
        yield return PointWait(0.3f);
        bool armed = stash.PointHand >= 0;
        pointSawSecondary = pointSawWheel = false;
        Script(reach).Trigger = 1f;
        yield return PointWait(0.4f);
        Script(reach).Trigger = 0f;
        yield return Wait(0.2f);
        if (armed || !pointSawSecondary || pointSawWheel)
            Fail(
                $"empty hands: a pick armed {armed}, the reach pressed {pointSawSecondary}, wheel {pointSawWheel}"
            );
        else
            stepNote =
                "a pull not started on an armed pick stayed the secondary use (off the pack, swung on, in the grace); empty hands reach";
    }
}
