using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Inventory;
using PeakXR.Locomotion;
using PeakXR.UI.Wheels;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>vr.1x</c>/<c>vr.20</c> steps: scripted controllers. Nobody can move the real controllers during the run, so
/// <see cref="VRInput.TestOverride"/> replaces both hands right after the sampler, and the rest of the mod (grab,
/// throw, climb, wheels, laser, body slots) sees ordinary tracked hands. Each step proves its effect from the
/// game's own state. Poses are written in tracking space; world targets go through <see cref="VRRig.Origin"/>.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const string AppleFragment = "Apple";

    /// <summary>Items the two-hand step tries, in order; all are two-hand capable in <see cref="ItemHoldTable"/>.</summary>
    static readonly string[] TwoHandItems = { "RopeSpool", "Binoculars", "RopeShooter" };

    HandState scriptLeft;
    HandState scriptRight;
    Vector3 lastLeft;
    Vector3 lastRight;
    Action applyHands;
    readonly HashSet<int> itemsBefore = new();

    /// <summary>The hand steps, after vr.07 on the beach.</summary>
    IEnumerator HandSteps()
    {
        yield return HandStep("vr.10-grab-right", 25f, GrabStep(false));
        yield return HandStep("vr.11-grab-left", 25f, GrabStep(true));
        yield return HandStep("vr.12-eat", 30f, EatStep());
        yield return HandStep("vr.13-throw", 25f, ThrowStep());
        yield return HandStep("vr.14-two-hand", 25f, TwoHandStep());
        yield return HandStep("vr.15-climb", 40f, ClimbStep());
        yield return HandStep("vr.16-snap-turn", 15f, SnapTurnStep());
        yield return HandStep("vr.17-move", 15f, MoveStep());
        yield return HandStep("vr.18-emote-wheel", 20f, EmoteStep());
        yield return HandStep("vr.19-laser", 20f, LaserStep());
        yield return HandStep("vr.20-holster", 30f, HolsterStep());
    }

    /// <summary>One scripted-hands step: a clean start (empty hands and hotbar), the body, then a guarded teardown
    /// that lets go and clears the override whether the step passed, failed, threw or timed out.</summary>
    IEnumerator HandStep(string id, float timeout, IEnumerator body)
    {
        if (!VRRun)
            yield break;
        if (!VRSession.Active)
        {
            Skip(id, "VR not active");
            yield break;
        }
        yield return Run(id, timeout, HandSetup(body));
        yield return Guard(
            HandsRelease(),
            Now + 5f,
            e =>
                Plugin.Log.LogWarning(
                    $"[selftest] {id} teardown: {(e == null ? "timeout" : e.Message)}"
                )
        );
        EndHands();
        ClosePause();
        // Whatever is still in hand drops as a new world item, which the sweep then removes (the step is already
        // recorded, so DropHeld's own verdict no longer counts).
        yield return Guard(DropHeld(), Now + 4f, _ => { });
        yield return Wait(0.3f);
        Safe("hands.despawn", Despawn);
        Safe("hands.sweep", SweepNewItems);
        Recover();
        yield return Wait(0.5f);
    }

    IEnumerator HandSetup(IEnumerator body)
    {
        Recover();
        ClosePause();
        yield return DropHeld();
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (c == null || rig == null || rig.Origin == null || rig.Head == null)
        {
            Fail("no character or rig");
            yield break;
        }
        EmptyHotbar(c);
        SnapshotItems();
        BeginHands();
        // Let the grip and trigger edges see the scripted rest values before anything presses.
        yield return Wait(0.3f);
        yield return body;
    }

    // ---------------------------------------------------------------- steps

    /// <summary>vr.10 / vr.11: a hand reaches an Apple on the ground and grips; the game equips it in that hand
    /// (<c>CharacterData.currentItem</c>, CharacterData.cs:417). A slow release keeps it unless
    /// <c>Interaction.ReleaseToDrop</c>.</summary>
    IEnumerator GrabStep(bool left)
    {
        var c = Character.localCharacter;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        if (apple == null)
        {
            Fail("no Apple item");
            yield break;
        }
        var spot = Flat(VRRig.Instance.Head.forward) * 0.45f + VRRig.Instance.Head.position;
        spot.y = c.Center.y;
        if (!SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var world = go != null ? go.GetComponent<Item>() : null;
        yield return Wait(0.4f);
        if (world == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        var last = world.Center();
        yield return MoveTo(
            left,
            () =>
            {
                if (world != null)
                    last = world.Center();
                return last;
            },
            0.5f
        );
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(() => Holding(apple), 3f, "not picked up within 3 s");
        if (!stepOk)
            yield break;
        yield return null;
        yield return null;
        var pose = PoseOf(c);
        var notes = new List<string>
        {
            $"holding {HeldItem().name}, LocalHold left {LocalHold.ItemInLeftHand}, pose left {pose.ItemInLeftHand}",
        };
        var fails = new List<string>();
        if (LocalHold.ItemInLeftHand != left || pose.ItemInLeftHand != left)
            fails.Add($"item not in the {(left ? "left" : "right")} hand");
        yield return Shot(left ? "vr-11-grab-left" : "vr-10-grab-right");

        yield return GripTo(left, 0f, 0.4f);
        yield return Wait(1f);
        bool dropExpected = InteractConfig.Instance.ReleaseToDrop;
        bool held = Holding(apple);
        notes.Add($"after a slow release held {held} (ReleaseToDrop {dropExpected})");
        if (held == dropExpected)
            fails.Add(dropExpected ? "slow release did not drop" : "slow release dropped it");
        Conclude(fails, notes);
    }

    /// <summary>vr.12: the hand holding an Apple, steered to the mouth point (<see cref="MouthGesture.MouthPoint"/>),
    /// eats it: the game's <c>Item.Consume</c> RPC stamps <c>CharacterData.lastConsumedItem</c> (Item.cs:1096,
    /// CharacterData.cs:169). The gesture reads the tracked grip; the note also gives the physics item's closest
    /// approach, which the holder's own head collider keeps away from the mouth.</summary>
    IEnumerator EatStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);

        float hunger = c.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger);
        float consumedBefore = c.data.lastConsumedItem;
        float useTime = item.usingTimePrimary;
        float end = Now + useTime + 6f;
        float best = float.MaxValue;
        float bestItem = float.MaxValue;
        float progress = 0f;
        bool shot = false;
        var goal = MouthGesture
            .MouthPoint(
                VRInput.HeadPosition.N(),
                VRInput.HeadRotation.N(),
                MouthGesture.DefaultOffset
            )
            .U();
        while (c.data.lastConsumedItem == consumedBefore && Now < end)
        {
            if (item == null || c.data.currentItem != item)
                break;
            var mouth = MouthGesture
                .MouthPoint(
                    rig.Head.position.N(),
                    rig.Head.rotation.N(),
                    MouthGesture.DefaultOffset
                )
                .U();
            best = Mathf.Min(best, Vector3.Distance(mouth, rig.Hand(left).position));
            bestItem = Mathf.Min(bestItem, Vector3.Distance(mouth, item.Center()));
            var at = Script(left).Position;
            SetHand(left, HandScript.Approach(at.N(), goal.N(), 1.5f, Time.unscaledDeltaTime).U());
            progress = Mathf.Max(progress, item.castProgress);
            if (!shot && progress > 0.3f)
            {
                shot = true;
                yield return Shot("vr-12-eat");
                continue;
            }
            yield return null;
        }
        yield return Wait(0.5f);
        float hungerAfter = c.refs.afflictions.GetCurrentStatus(
            CharacterAfflictions.STATUSTYPE.Hunger
        );
        var note =
            $"grip closest {best:0.00} m to the mouth (item {bestItem:0.00} m), use progress {progress:0.00} of {useTime:0.0} s, "
            + $"hunger {hunger:0.00} -> {hungerAfter:0.00}";
        if (c.data.lastConsumedItem == consumedBefore)
            Fail("not eaten: " + note);
        else
            stepNote = "eaten; " + note;
    }

    /// <summary>
    /// vr.13: a 6 m/s forward swing released mid-swing throws the held Apple through the game's drop path
    /// (<c>CharacterItems.DropItemRpc</c>, CharacterItems.cs:385). It lands 2 m or more away, launched no faster than
    /// the clamp interact.physical applies (hip speed plus half <c>maxThrowForce</c>, CharacterItems.cs:21).
    /// </summary>
    IEnumerator ThrowStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var held = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        float multiplier = held.throwForceMultiplier;
        yield return GripTo(left, 1f, 0.1f);

        HeadFrame(out var head, out var forward, out var right);
        var side = left ? -right : right;
        var windup = head + side * 0.25f + Vector3.down * 0.15f;
        var release = windup + forward * 0.9f + Vector3.up * 0.15f;
        yield return MoveTrack(left, windup, 0.4f);
        yield return Wait(0.3f);
        SnapshotItems();

        float hipSpeed = c.refs.hip != null ? c.refs.hip.Rig.linearVelocity.magnitude : 0f;
        float clamp = hipSpeed + 0.5f * c.refs.items.maxThrowForce * multiplier;
        var from = c.Center;
        // Constant speed (no easing): 0.9 m in 0.15 s, the grip opening at 70 % while the hand still moves.
        float start = Now;
        while (true)
        {
            float t = Mathf.Clamp01((Now - start) / 0.15f);
            SetHand(left, Vector3.Lerp(windup, release, t));
            if (t >= 0.7f)
                Script(left).Grip = 0f;
            if (t >= 1f)
                break;
            yield return null;
        }
        yield return Until(() => HeldItem() == null, 1.5f, "item still in hand after the swing");
        if (!stepOk)
            yield break;

        Item thrown = null;
        yield return WaitFor(() => (thrown = NewItem(apple.itemID)) != null, 1f);
        if (thrown == null)
        {
            Fail("no thrown Apple in the world");
            yield break;
        }
        float launch = 0f;
        for (int i = 0; i < 5 && thrown != null; i++)
        {
            if (thrown.rig != null)
                launch = Mathf.Max(launch, thrown.rig.linearVelocity.magnitude);
            yield return null;
        }
        yield return Wait(0.25f);
        yield return Shot("vr-13-throw");
        float still = 0f;
        float end = Now + 5f;
        while (thrown != null && Now < end && still < 0.3f)
        {
            float speed = thrown.rig != null ? thrown.rig.linearVelocity.magnitude : 0f;
            still = speed < 0.2f ? still + Time.unscaledDeltaTime : 0f;
            yield return null;
        }
        if (thrown == null)
        {
            Fail($"thrown Apple vanished (launch {launch:0.0} m/s)");
            yield break;
        }
        var landed = thrown.Center();
        float distance = Vector2.Distance(
            new Vector2(from.x, from.z),
            new Vector2(landed.x, landed.z)
        );
        float range = SelfTestChecks.MaxThrowRange(
            clamp,
            from.y - landed.y + 1f,
            Physics.gravity.magnitude
        );
        var note =
            $"landed {distance:0.0} m away, launch {launch:0.0} m/s (clamp {clamp:0.0}), max range {range:0.0} m";
        if (distance < 2f)
            Fail("throw too short: " + note);
        else if (launch > clamp * 1.1f + 0.5f || distance > range + 1f)
            Fail("throw beyond the vanilla clamp: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.14: the free hand gripping a held two-hand item sets <see cref="AvatarPose.TwoHandHold"/>; letting
    /// go clears it.</summary>
    IEnumerator TwoHandStep()
    {
        var c = Character.localCharacter;
        var all = new List<Item>(SandboxActions.Items);
        Item pick = null;
        foreach (var fragment in TwoHandItems)
        {
            pick = FindItem(all, fragment);
            if (pick != null)
                break;
        }
        if (pick == null)
        {
            Fail("no two-hand item (" + string.Join(", ", TwoHandItems) + ")");
            yield break;
        }
        yield return GiveAndHold(pick);
        if (!stepOk)
            yield break;
        var held = HeldItem();
        var info = new ItemHoldInfo();
        info.Bind(held);
        if (info.Profile.Handedness == Handedness.OneHanded)
        {
            Fail($"{held.name} is one-handed ({info.Profile})");
            yield break;
        }
        bool holdLeft = LocalHold.ItemInLeftHand;
        bool free = !holdLeft;
        yield return GripTo(holdLeft, 1f, 0.1f);
        var last = held.Center();
        yield return MoveTo(
            free,
            () =>
            {
                if (held != null)
                    last = held.Center();
                return last;
            },
            0.5f
        );
        yield return GripTo(free, 1f, 0.1f);
        yield return Until(() => LocalHold.TwoHand, 2f, "second hand did not join");
        if (!stepOk)
            yield break;
        yield return null;
        yield return null;
        bool posed = PoseOf(c).TwoHandHold;
        yield return Shot("vr-14-two-hand");
        yield return GripTo(free, 0f, 0.2f);
        yield return WaitFor(() => !LocalHold.TwoHand, 2f);
        var note =
            $"{held.name} ({info.Profile}) in the {(holdLeft ? "left" : "right")} hand, pose TwoHandHold {posed}, after letting go {LocalHold.TwoHand}";
        if (!posed)
            Fail("pose not two-handed: " + note);
        else if (LocalHold.TwoHand)
            Fail("two-hand hold stuck after letting go: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.15: at the nearest steep terrain face (rays on <c>HelperFunctions.terrainMapMask</c>, HelperFunctions.cs:27),
    /// a gripping hand on the surface starts the game's wall climb (<c>CharacterData.isClimbing</c>, CharacterData.cs:118),
    /// pulling it down lifts the body, and letting go ends the climb. Warps there with <c>Character.WarpPlayerRPC</c>
    /// (Character.cs:1822) and back to spawn after. Skips when no such face is within 40 m of the spawn or a ring of points around it.
    /// </summary>
    IEnumerator ClimbStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!FindWall(c, out var wall, out var stand))
        {
            stepSkip =
                "no climbable wall with walkable ground in front within 40 m of spawn or 8 points around it";
            yield break;
        }
        var normal = Flat(wall.normal);
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -normal, Vector3.up));
        yield return Wait(0.6f);

        var from = c.Center + Vector3.up * 0.1f;
        if (
            !Physics.Raycast(
                from,
                -normal,
                out var hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            Fail($"wall not in front after the warp (wall {wall.point}, body {c.Center})");
            yield break;
        }
        bool left = VRInput.LeftHanded;
        var hold = hit.point + hit.normal * 0.04f;
        yield return MoveTo(left, () => hold, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => c.data.isClimbing,
            2f,
            $"climb did not start ({hit.distance:0.00} m to the wall)"
        );
        if (!stepOk)
        {
            yield return Back();
            yield break;
        }
        yield return Wait(0.3f);
        float y0 = c.Center.y;
        yield return Shot("vr-15-climb");
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.4f, 0.6f);
        yield return Wait(0.4f);
        float rise = c.Center.y - y0;
        bool stillClimbing = c.data.isClimbing;
        yield return GripTo(left, 0f, 0.1f);
        yield return WaitFor(() => !c.data.isClimbing, 2f);
        var note =
            $"wall {Vector3.Angle(hit.normal, Vector3.up):0} deg from up, body rose {rise:0.00} m, climbing after the pull {stillClimbing}, after letting go {c.data.isClimbing}";
        if (rise < 0.1f)
            Fail("arm pull did not lift the body: " + note);
        else if (c.data.isClimbing)
            Fail("climb did not stop: " + note);
        else
            stepNote = note;
        yield return Back();

        IEnumerator Back()
        {
            Script(left).Grip = 0f;
            yield return Wait(0.5f);
            if (c.data.isClimbing)
                c.refs.climbing.StopClimbing();
            SandboxActions.WarpToSpawn();
            yield return Wait(2f);
        }
    }

    /// <summary>vr.16: the turn stick snaps the rig by <c>Comfort.SnapTurnAngle</c> (smooth turning: just turns).</summary>
    IEnumerator SnapTurnStep()
    {
        var rig = VRRig.Instance;
        bool hand = VRInput.LeftHanded;
        float before = rig.Origin.eulerAngles.y;
        Script(hand).Stick = new Vector2(1f, 0f);
        yield return Wait(0.3f);
        Script(hand).Stick = Vector2.zero;
        yield return Wait(0.3f);
        yield return Shot("vr-16-snap-turn");
        float delta = SelfTestChecks.YawDelta(before, rig.Origin.eulerAngles.y);
        bool snap = ComfortConfig.Turn == TurnMode.Snap;
        float expected = ComfortConfig.SnapAngle;
        var note = $"turned {delta:0.0} deg ({ComfortConfig.Turn}, snap angle {expected:0})";
        if (snap ? Mathf.Abs(delta - expected) > 1f : delta < 5f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.17: the move stick held forward for 1 s walks the character (<c>Character.Center</c>, Character.cs:273).</summary>
    IEnumerator MoveStep()
    {
        var c = Character.localCharacter;
        bool hand = !VRInput.LeftHanded;
        var from = c.Center;
        Script(hand).Stick = new Vector2(0f, 1f);
        yield return Wait(0.6f);
        yield return Shot("vr-17-move");
        yield return Wait(0.4f);
        Script(hand).Stick = Vector2.zero;
        yield return Wait(0.3f);
        var to = c.Center;
        float moved = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
        var note = $"walked {moved:0.00} m in 1 s";
        if (moved < 0.5f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.18: holding the emote binding (off hand, upper button) opens the game's wheel (<c>GUIManager.emoteWheel</c>,
    /// GUIManager.cs:48), the hand moved up hovers a slice (<c>EmoteWheel.selectedEmoteName</c>) and releasing plays
    /// it (<c>CharacterAnimations.RPCA_PlayRemove</c> sets the private <c>emoting</c>, CharacterAnimations.cs:117/:432;
    /// Play Dead makes the character fall instead, :421).
    /// </summary>
    IEnumerator EmoteStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        if (gui == null || gui.emoteWheel == null)
        {
            Fail("no GUIManager emote wheel");
            yield break;
        }
        var emoting = GameMember.Field<CharacterAnimations, bool>("emoting");
        bool hand = !VRInput.LeftHanded;
        Script(hand).Secondary = true;
        yield return Until(() => gui.emoteWheel.activeInHierarchy, 2f, "emote wheel did not open");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        float travel = Mathf.Max(0.15f, UIWheelsFeature.FlickDistance.Value * 1.5f);
        yield return MoveTrack(hand, Script(hand).Position + Vector3.up * travel, 0.3f);
        yield return Wait(0.3f);
        var wheel = gui.emoteWheel.GetComponentInChildren<EmoteWheel>(true);
        string chosen =
            wheel != null && wheel.selectedEmoteName != null ? wheel.selectedEmoteName.text : "";
        yield return Shot("vr-18-emote-wheel");
        Script(hand).Secondary = false;
        yield return Until(
            () => !gui.emoteWheel.activeInHierarchy,
            1f,
            "emote wheel did not close"
        );
        bool started = false;
        yield return WaitFor(
            () => started = emoting(c.refs.animations) || c.data.fallSeconds > 0f,
            1.5f
        );
        var note = $"hovered '{chosen}', emote started {started}";
        if (string.IsNullOrEmpty(chosen))
            Fail("no slice hovered by the hand: " + note);
        else if (!started)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.19: both aim rays on the pause menu's Resume button (<c>PauseMenuMainPage.resumeButton</c>,
    /// PauseMenuMainPage.cs:50) and a trigger pull close the menu (<c>GUIManager.InPauseMenu</c>, GUIManager.cs:304).</summary>
    IEnumerator LaserStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        var rig = VRRig.Instance;
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(
            () => GUIManager.InPauseMenu && FindAnyObjectByType<PauseMenuHandler>() != null,
            5f,
            "pause menu did not open"
        );
        if (!stepOk)
            yield break;
        var page = FindAnyObjectByType<PauseMenuMainPage>();
        if (page == null || !page.gameObject.activeInHierarchy)
            page = FindAnyObjectByType<PauseMenuHandler>().TransistionToPage<PauseMenuMainPage>();
        // ui.world places the panel; let it settle before aiming.
        yield return Wait(0.8f);
        var button = page != null ? page.resumeButton : null;
        if (button == null)
        {
            Fail("no PauseMenuMainPage.resumeButton");
            yield break;
        }
        var rect = (RectTransform)button.transform;
        var target = rect.TransformPoint(rect.rect.center);
        HeadFrame(out var head, out var forward, out var right);
        // LastUsed pointer mode keeps the active hand when both pull at once, so aim and pull both.
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            var p = head + (left ? -right : right) * 0.15f + Vector3.down * 0.2f + forward * 0.2f;
            var dir = rig.Origin.InverseTransformDirection(target - rig.Origin.TransformPoint(p));
            Script(left).Position = p;
            Script(left).Rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
        yield return Wait(0.3f);
        yield return Shot("vr-19-laser");
        Script(true).Trigger = Script(false).Trigger = 1f;
        yield return Wait(0.15f);
        Script(true).Trigger = Script(false).Trigger = 0f;
        yield return Until(
            () => !GUIManager.InPauseMenu,
            2f,
            $"Resume click did not close the menu (button {Vector3.Distance(rig.Head.position, target):0.00} m away)"
        );
    }

    /// <summary>
    /// vr.20: the holding hand opened over its body slot holsters the item into that hotbar slot
    /// (<c>Player.GetItemSlot</c>, Player.cs:246, <c>ItemSlot.prefab</c>), and a grip there draws it back into the same hand.
    /// </summary>
    IEnumerator HolsterStep()
    {
        var c = Character.localCharacter;
        var inv = BodyInventory.Instance;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var selected = c.refs.items.currentSelectedSlot;
        if (!inv.Valid || selected.IsNone || selected.Value > 2)
        {
            Fail($"body slots off or no hotbar slot selected (valid {inv.Valid})");
            yield break;
        }
        int slot = -1;
        for (int i = 0; i < BodyInventory.SlotCount; i++)
        {
            if (inv.Slot(i).GameSlot == selected.Value)
                slot = i;
        }
        if (slot < 0)
        {
            Fail($"no body slot for hotbar slot {selected.Value}");
            yield break;
        }
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        var rig = VRRig.Instance;
        yield return MoveTo(left, () => rig.Origin.TransformPoint(inv.Anchor(slot)), 0.6f);
        yield return Wait(0.3f);
        yield return GripTo(left, 0f, 0.3f);
        yield return Until(() => HeldItem() == null, 2f, "not holstered on release");
        if (!stepOk)
            yield break;
        var itemSlot = c.player.GetItemSlot(selected.Value);
        bool inSlot =
            itemSlot != null && !itemSlot.IsEmpty() && itemSlot.prefab.itemID == apple.itemID;
        yield return Shot("vr-20-holster");
        if (!inSlot)
        {
            Fail($"hotbar slot {selected.Value} does not hold the Apple");
            yield break;
        }
        yield return Wait(0.4f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(() => Holding(apple), 2f, "grip at the slot did not draw it");
        if (!stepOk)
            yield break;
        stepNote =
            $"holstered into hotbar slot {selected.Value} (body slot {slot}), drawn into the {(LocalHold.ItemInLeftHand ? "left" : "right")} hand";
        if (LocalHold.ItemInLeftHand != left)
            Fail("drawn into the other hand");
    }

    // ---------------------------------------------------------------- scripted hands

    ref HandState Script(bool left) => ref left ? ref scriptLeft : ref scriptRight;

    void BeginHands()
    {
        HeadFrame(out var head, out var forward, out var right);
        scriptLeft = Rest(head, forward, -right);
        scriptRight = Rest(head, forward, right);
        lastLeft = scriptLeft.Position;
        lastRight = scriptRight.Position;
        VRInput.TestOverride = applyHands ??= ApplyHands;
    }

    /// <summary>Hands the controllers back to the idle pose (or to nobody once the run has finished).</summary>
    void EndHands() => VRInput.TestOverride = finished ? null : IdleHands;

    /// <summary>
    /// Between hand steps of a VR run (never in hold mode): both hands rest in front of the face, so the desktop
    /// mirror and screenshots show them. Nothing is pressed and nothing moves, so the rest pose triggers nothing.
    /// </summary>
    internal static readonly Action IdleHands = ApplyIdle;

    static void ApplyIdle()
    {
        HeadFrame(out var head, out var forward, out var right);
        VRInput.Left = Idle(head, forward, -right, -30f);
        VRInput.Right = Idle(head, forward, right, 30f);
    }

    /// <summary>Head-relative rest in tracking space; <paramref name="roll"/> turns the palm (grip +X / -X) down.</summary>
    static HandState Idle(Vector3 head, Vector3 forward, Vector3 side, float roll) =>
        new()
        {
            Tracked = true,
            Position = head + side * 0.18f + Vector3.down * 0.25f + forward * 0.35f,
            Rotation =
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, roll),
        };

    static HandState Rest(Vector3 head, Vector3 forward, Vector3 side)
    {
        var p = head + side * 0.25f + Vector3.down * 0.35f + forward * 0.4f;
        return new HandState
        {
            Tracked = true,
            Position = p,
            Rotation = Quaternion.LookRotation(p - head, Vector3.up),
            // A finger resting on the trigger: a closed grip is a fist, not a pointing hand.
            TriggerTouch = true,
        };
    }

    /// <summary>Called by the sampler every frame while a hand step runs; velocity comes from the scripted motion.</summary>
    void ApplyHands()
    {
        float dt = Time.unscaledDeltaTime;
        Emit(ref VRInput.Left, ref scriptLeft, ref lastLeft, dt);
        Emit(ref VRInput.Right, ref scriptRight, ref lastRight, dt);
    }

    static void Emit(ref HandState target, ref HandState script, ref Vector3 last, float dt)
    {
        script.Velocity = dt > 0f ? (script.Position - last) / dt : Vector3.zero;
        last = script.Position;
        target = script;
    }

    /// <summary>Back to rest, then the grips open and every button lets go, so nothing fires once the real
    /// controllers return.</summary>
    IEnumerator HandsRelease()
    {
        if (VRInput.TestOverride != applyHands || VRRig.Instance == null)
            yield break;
        Calm(ref scriptLeft);
        Calm(ref scriptRight);
        HeadFrame(out var head, out var forward, out var right);
        var restLeft = Rest(head, forward, -right).Position;
        var restRight = Rest(head, forward, right).Position;
        yield return MoveTrack(true, restLeft, 0.3f);
        yield return MoveTrack(false, restRight, 0.3f);
        yield return Wait(0.2f);
        scriptLeft.Grip = scriptRight.Grip = 0f;
        yield return Wait(0.3f);
    }

    static void Calm(ref HandState s)
    {
        s.Trigger = 0f;
        s.Stick = Vector2.zero;
        s.Primary = s.Secondary = s.Menu = false;
    }

    void SetHand(bool left, Vector3 trackingPosition)
    {
        ref var s = ref Script(left);
        s.Position = trackingPosition;
        var away = trackingPosition - VRInput.HeadPosition;
        if (away.sqrMagnitude > 1e-4f)
            s.Rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    /// <summary>Eased move of one hand onto a world point, re-read every frame (it may move).</summary>
    IEnumerator MoveTo(bool left, Func<Vector3> world, float seconds)
    {
        var rig = VRRig.Instance;
        var from = Script(left).Position;
        float start = Now;
        while (true)
        {
            float t = (Now - start) / seconds;
            var goal = rig.Origin.InverseTransformPoint(world());
            SetHand(left, HandScript.Along(from.N(), goal.N(), t).U());
            if (t >= 1f)
                yield break;
            yield return null;
        }
    }

    /// <summary>Eased move of one hand to a tracking-space point.</summary>
    IEnumerator MoveTrack(bool left, Vector3 tracking, float seconds)
    {
        var from = Script(left).Position;
        float start = Now;
        while (true)
        {
            float t = (Now - start) / seconds;
            SetHand(left, HandScript.Along(from.N(), tracking.N(), t).U());
            if (t >= 1f)
                yield break;
            yield return null;
        }
    }

    IEnumerator GripTo(bool left, float value, float seconds)
    {
        float from = Script(left).Grip;
        float start = Now;
        while (true)
        {
            float t = Mathf.Clamp01((Now - start) / seconds);
            Script(left).Grip = Mathf.Lerp(from, value, t);
            if (t >= 1f)
                yield break;
            yield return null;
        }
    }

    /// <summary>The head and its level forward and right in tracking space.</summary>
    static void HeadFrame(out Vector3 head, out Vector3 forward, out Vector3 right)
    {
        head = VRInput.HeadPosition;
        forward = Flat(VRInput.HeadRotation * Vector3.forward);
        right = Vector3.Cross(Vector3.up, forward);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ---------------------------------------------------------------- game state helpers

    /// <summary>Gives an item to the hand through the game (<c>GameUtils.InstantiateAndGrab</c>) and waits for it.</summary>
    IEnumerator GiveAndHold(Item item)
    {
        if (item == null)
        {
            Fail("item not in the database");
            yield break;
        }
        if (!SandboxActions.GiveItem(item))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Until(() => Holding(item), 5f, "given item not in hand");
        // As master the give equips synchronously, so Until can pass before CharacterInput.Sample's
        // PhysicalInteraction.TrackHeldItem picks the holding hand: wait one Update so LocalHold.ItemInLeftHand is current.
        yield return null;
        if (stepOk)
            spawned.Add(HeldItem().gameObject);
    }

    static AvatarPose PoseOf(Character c)
    {
        var driver = AvatarPoseDriver.Of(c);
        return driver != null ? driver.Pose : default;
    }

    /// <summary>Empties hotbar slots 0-2 (<c>Player.EmptySlot</c>, Player.cs:212) so a given or grabbed item gets a
    /// real slot, not the overflow one. Only with empty hands.</summary>
    static void EmptyHotbar(Character c)
    {
        if (c.player == null || c.data.currentItem != null)
            return;
        for (byte i = 0; i < 3; i++)
        {
            var slot = c.player.GetItemSlot(i);
            if (slot != null && !slot.IsEmpty())
                c.player.EmptySlot(Optionable<byte>.Some(i));
        }
    }

    void SnapshotItems()
    {
        itemsBefore.Clear();
        foreach (var item in FindObjectsByType<Item>(FindObjectsSortMode.None))
            itemsBefore.Add(item.GetInstanceID());
    }

    /// <summary>A world item (<c>Item.itemState</c> Ground, Item.cs:286) that appeared since the snapshot.</summary>
    Item NewItem(ushort itemID)
    {
        foreach (var item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (
                item.itemID == itemID
                && item.itemState == ItemState.Ground
                && !itemsBefore.Contains(item.GetInstanceID())
            )
                return item;
        }
        return null;
    }

    /// <summary>Items dropped or thrown during a hand step are new world instances; remove them.</summary>
    void SweepNewItems()
    {
        foreach (var item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (item.itemState != ItemState.Ground || itemsBefore.Contains(item.GetInstanceID()))
                continue;
            var view = item.GetComponent<PhotonView>();
            if (view != null && (view.IsMine || PhotonNetwork.IsMasterClient))
                PhotonNetwork.Destroy(item.gameObject);
        }
    }

    /// <summary>
    /// The nearest steep terrain face around the character with a walkable spot 0.6 m in front of it. A wall that is
    /// still there 1.2 m higher, so the climb has room to rise.
    /// </summary>
    static bool FindWall(Character c, out RaycastHit wall, out Vector3 stand)
    {
        wall = default;
        stand = default;
        const int Rays = 24;
        const float Range = 40f;
        float best = float.MaxValue;
        var home = c.Center;
        // The beach spawn and the daily level vary, so also cast from a ring of points around the character and
        // at three heights each; the wall nearest the character wins.
        for (int o = 0; o < 9; o++)
        {
            var spot =
                o == 0
                    ? home
                    : home + Quaternion.Euler(0f, (o - 1) * 45f, 0f) * Vector3.forward * 15f;
            foreach (float h in new[] { 0.2f, 2f, 5f })
            {
                var origin = spot + Vector3.up * h;
                for (int i = 0; i < Rays; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 360f / Rays, 0f) * Vector3.forward;
                    if (
                        !Physics.Raycast(
                            origin,
                            dir,
                            out var hit,
                            Range,
                            HelperFunctions.terrainMapMask,
                            QueryTriggerInteraction.Ignore
                        )
                    )
                        continue;
                    float away = Vector3.Distance(home, hit.point);
                    if (away >= best)
                        continue;
                    float angle = Vector3.Angle(hit.normal, Vector3.up);
                    if (angle < 65f || angle > 115f)
                        continue;
                    if (
                        !Physics.Raycast(
                            origin + Vector3.up * 1.2f,
                            dir,
                            out var high,
                            hit.distance + 1.5f,
                            HelperFunctions.terrainMapMask,
                            QueryTriggerInteraction.Ignore
                        ) || !ClimbRules.GrabAngleOk(Vector3.Angle(high.normal, Vector3.up))
                    )
                        continue;
                    var front = hit.point + Flat(hit.normal) * 0.6f;
                    if (
                        !Physics.Raycast(
                            front + Vector3.up * 2f,
                            Vector3.down,
                            out var ground,
                            6f,
                            HelperFunctions.terrainMapMask,
                            QueryTriggerInteraction.Ignore
                        )
                        || Vector3.Angle(ground.normal, Vector3.up) > 40f
                    )
                        continue;
                    // Chest height over that ground still meets the wall: the face starts at the standing spot's level.
                    float rise = hit.point.y - ground.point.y;
                    if (rise < 0.3f || rise > 1.6f)
                        continue;
                    best = away;
                    wall = hit;
                    stand = ground.point + Vector3.up * 0.1f;
                }
            }
        }
        return best < float.MaxValue;
    }
}
