using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Inventory;
using PeakXR.Locomotion;
using PeakXR.UI.Common;
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
        yield return HandStep("vr.29-ground-backpack", 25f, GroundBackpackStep());
        yield return HandStep("vr.32-shoulder-stash", 30f, ShoulderStashStep());
        // Later steps expect no worn pack (the over-the-shoulder slot would claim grips); Player.EmptySlot(3) takes
        // it off (Player.cs:211, GetItemSlot :250).
        Safe(
            "hands.unwear",
            () =>
            {
                var p = Character.localCharacter != null ? Character.localCharacter.player : null;
                if (p != null && !p.backpackSlot.IsEmpty())
                    p.EmptySlot(Optionable<byte>.Some(3));
            }
        );
        yield return BodySteps();
        yield return ItemGestureSteps();
        yield return AffordanceSteps();
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
        yield return ShotAt(left ? "vr-11-grab-left" : "vr-10-grab-right", HeldItem().Center());

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
    /// approach, which the holder's own head collider keeps away from the mouth. Eating must also move the status the
    /// item changes (<see cref="StatusEffect"/>) the right way, so the step sets that status first.</summary>
    IEnumerator EatStep()
    {
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        yield return EatHeld();
    }

    /// <summary>The body of vr.12 for the item already in hand (also the mp test's <c>eat</c>).</summary>
    IEnumerator EatHeld()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);

        // God mode locks statuses (Character.statusesLocked blocks SubtractStatus, CharacterAfflictions.cs:886) and
        // clears statuses on its tick, so eating could not change anything: lift it and set the status the game's way
        // (SetStatus, CharacterAfflictions.cs:632). Run puts god mode back if the step ends early.
        var afflictions = c.refs.afflictions;
        bool measured = StatusEffect(item, out var status, out float change);
        bool god = SandboxActions.GodMode;
        string eaten = item.name;
        if (measured)
        {
            if (god)
                SandboxActions.SetGodMode(false);
            // Room to move: a restore from the middle, an added status from zero.
            afflictions.SetStatus(status, change < 0f ? 0.5f : 0f);
        }
        float before = afflictions.GetCurrentStatus(status);
        float consumedBefore = c.data.lastConsumedItem;
        // The item's own use time after up to 3 s bringing it to the mouth.
        float useTime = GameTiming.UseSeconds(item);
        float end = Now + GameTiming.Budget(useTime, 3f);
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
                // The item is at the mouth, under the eye: back off to frame the hand and the item.
                yield return ShotAt("vr-12-eat", item.Center(), 0.45f);
                continue;
            }
            yield return null;
        }
        yield return Wait(0.5f);
        float after = afflictions.GetCurrentStatus(status);
        if (god)
            SandboxActions.SetGodMode(true);
        float moved = (after - before) * Mathf.Sign(change);
        // Room the status has in the change's direction (statuses stay within 0..1).
        float room = change < 0f ? before : 1f - before;
        var note =
            $"grip closest {best:0.00} m to the mouth (item {bestItem:0.00} m), use progress {progress:0.00} of {useTime:0.0} s, "
            + $"{status} {before:0.00} -> {after:0.00} (item changes it by {change:+0.00;-0.00})";
        if (c.data.lastConsumedItem == consumedBefore)
            Fail("not eaten: " + note);
        else if (!measured)
            Fail(
                $"{eaten} carries no status action (Action_RestoreHunger, Action_ModifyStatus), nothing to measure: "
                    + note
            );
        // Half the amount: statuses creep between the reads, and the start value caps a large change.
        else if (moved < Mathf.Min(Mathf.Abs(change), room) * 0.5f)
            Fail($"eaten but {status} did not change: " + note);
        else
            stepNote = "eaten; " + note;
    }

    /// <summary>The status eating <paramref name="item"/> changes and by how much: Hunger first (food runs
    /// <c>Action_RestoreHunger</c>, Action_RestoreHunger.cs:7, or <c>Action_ModifyStatus</c> on Hunger, the game's
    /// default food script, Item.cs:1138-1141), else the largest <c>Action_ModifyStatus</c> (Action_ModifyStatus.cs:12).
    /// Skeleton-only changes are skipped. False when the item changes no status.</summary>
    static bool StatusEffect(
        Item item,
        out CharacterAfflictions.STATUSTYPE status,
        out float change
    )
    {
        status = CharacterAfflictions.STATUSTYPE.Hunger;
        change = 0f;
        foreach (var r in item.GetComponents<Action_RestoreHunger>())
            change -= r.restorationAmount;
        var modifies = item.GetComponents<Action_ModifyStatus>();
        foreach (var m in modifies)
            if (!m.ifSkeleton && m.statusType == CharacterAfflictions.STATUSTYPE.Hunger)
                change += m.changeAmount;
        if (change != 0f)
            return true;
        foreach (var m in modifies)
        {
            if (!m.ifSkeleton && Mathf.Abs(m.changeAmount) > Mathf.Abs(change))
            {
                status = m.statusType;
                change = m.changeAmount;
            }
        }
        return change != 0f;
    }

    /// <summary>
    /// vr.13: a 6 m/s forward swing released mid-swing throws the held Apple through the game's drop path
    /// (<c>CharacterItems.DropItemRpc</c>, CharacterItems.cs:385). It lands 2 m or more away, launched no faster than
    /// the clamp interact.physical applies (hip speed plus half <c>maxThrowForce</c>, CharacterItems.cs:21).
    /// </summary>
    IEnumerator ThrowStep()
    {
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        yield return ThrowHeld();
    }

    /// <summary>Where the last <see cref="ThrowHeld"/> came to rest; null when it did not land.</summary>
    Vector3? lastLanding;

    /// <summary>The body of vr.13 for the item already in hand, thrown along the head's level forward.</summary>
    IEnumerator ThrowHeld()
    {
        var c = Character.localCharacter;
        lastLanding = null;
        var held = HeldItem();
        ushort heldId = held.itemID;
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
        yield return SwingRelease(left, windup, release);
        yield return Until(() => HeldItem() == null, 1.5f, "item still in hand after the swing");
        if (!stepOk)
            yield break;

        Item thrown = null;
        yield return WaitFor(() => (thrown = NewItem(heldId)) != null, 1f);
        if (thrown == null)
        {
            Fail("no thrown " + held.name + " in the world");
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
        yield return ShotAt("vr-13-throw", thrown != null ? thrown.Center() : HandsMid());
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
        lastLanding = landed;
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

    /// <summary>Constant speed (no easing): 0.9 m in 0.15 s, the grip opening at 70 % while the hand still moves.</summary>
    IEnumerator SwingRelease(bool left, Vector3 windup, Vector3 release)
    {
        float start = Now;
        while (true)
        {
            float t = Mathf.Clamp01((Now - start) / 0.15f);
            SetHand(left, Vector3.Lerp(windup, release, t));
            if (t >= 0.7f)
                Script(left).Grip = 0f;
            if (t >= 1f)
                yield break;
            yield return null;
        }
    }

    /// <summary>vr.14: the free hand gripping a held two-hand item sets <see cref="AvatarPose.TwoHandHold"/>; letting
    /// go clears it.</summary>
    IEnumerator TwoHandStep()
    {
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
        yield return TwoHandHeld();
    }

    /// <summary>The body of vr.14 for the item already in hand.</summary>
    IEnumerator TwoHandHeld()
    {
        var c = Character.localCharacter;
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
        yield return ShotAt("vr-14-two-hand", held != null ? held.Center() : HandsMid());
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
            stepSkip = NoWall;
            yield break;
        }
        var normal = Flat(wall.normal);
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -normal, Vector3.up));
        yield return Wait(0.6f);
        yield return ClimbWall(wall);
        yield return Back();

        IEnumerator Back()
        {
            Script(VRInput.LeftHanded).Grip = 0f;
            yield return Wait(0.5f);
            if (c.data.isClimbing)
                c.refs.climbing.StopClimbing();
            SandboxActions.WarpToSpawn();
            yield return Wait(2f);
        }
    }

    /// <summary>The body of vr.15 once the character stands facing <paramref name="wall"/>; ends with the grip open.</summary>
    IEnumerator ClimbWall(RaycastHit wall)
    {
        var c = Character.localCharacter;
        if (!WallInFront(c, wall, 0.1f, out var hit))
        {
            Fail($"wall not in front (wall {wall.point}, body {c.Center})");
            yield break;
        }
        bool left = VRInput.LeftHanded;
        var hold = hit.point + hit.normal * 0.04f;
        var rig = VRRig.Instance;
        Rig.RigDriver.TraceLeanUntil = Time.unscaledTime + 3.5f;
        yield return MoveTo(left, () => hold, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        Rig.RigDriver.TraceLeanUntil = 0f;
        if (!c.data.isClimbing)
        {
            // Names the stage that refused: the grip's decision, the hand against the wall, the camera the game's
            // climb ray starts from (CharacterClimbing.cs:541) against the torso, and the view root.
            var handPos = rig.Hand(left).position;
            var cam =
                MainCamera.instance != null
                    ? MainCamera.instance.transform.position
                    : rig.Head.position;
            var torso = c.Center;
            var f = -Flat(hit.normal);
            var r = Vector3.Cross(Vector3.up, f);
            var root = Rig.RigDriver.ViewRoot(c);
            Fail(
                F(
                    $"climb did not start ({hit.distance:0.00} m to the wall); hand {Vector3.Distance(handPos, hold):0.00} m off the hold, {Vector3.Dot(hit.point - handPos, f):0.00} m before the wall; camera from torso f{Vector3.Dot(cam - torso, f):0.00} r{Vector3.Dot(cam - torso, r):0.00} u{cam.y - torso.y:0.00}; view root from torso f{Vector3.Dot(root - torso, f):0.00} r{Vector3.Dot(root - torso, r):0.00}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
                )
            );
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
    }

    /// <summary>vr.16: the turn stick snaps the rig by <c>Comfort.SnapTurnAngle</c> (smooth turning: just turns), and the eye
    /// slides less than 2 cm sideways while it does.</summary>
    IEnumerator SnapTurnStep()
    {
        var rig = VRRig.Instance;
        bool hand = VRInput.LeftHanded;
        float before = rig.Origin.eulerAngles.y;
        // Turns pivot on the eye (RigDriver.UpdateTurnShift): the view must not slide sideways.
        // Per-frame trace of eye, rig and body yaw, torso and turn shift, from a few frames before the turn.
        Rig.RigDriver.TraceTurnsUntil = Time.unscaledTime + 1f;
        yield return null;
        yield return null;
        var eye0 = rig.Head.position;
        float slide = 0f;
        Script(hand).Stick = new Vector2(1f, 0f);
        float end = Now + 0.6f;
        while (Now < end)
        {
            if (Now > end - 0.3f)
                Script(hand).Stick = Vector2.zero;
            var d = rig.Head.position - eye0;
            slide = Mathf.Max(slide, new Vector2(d.x, d.z).magnitude);
            yield return null;
        }
        Script(hand).Stick = Vector2.zero;
        yield return Shot("vr-16-snap-turn");
        float delta = SelfTestChecks.YawDelta(before, rig.Origin.eulerAngles.y);
        bool snap = ComfortConfig.Turn == TurnMode.Snap;
        float expected = ComfortConfig.SnapAngle;
        var note =
            $"turned {delta:0.0} deg ({ComfortConfig.Turn}, snap angle {expected:0}), eye slid {slide * 100f:0.0} cm";
        if (snap ? Mathf.Abs(delta - expected) > 1f : delta < 5f)
            Fail(note);
        else if (slide >= 0.02f)
            Fail("eye moved sideways in the turn: " + note);
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
    /// vr.29: a grip on a ground backpack opens its wheel (<c>Backpack.Interact</c>, Backpack.cs:34) and the wheel stays
    /// open every frame while the grip stays closed (it closes on interact up, BackpackWheel.cs:96); opening the grip
    /// closes it, and the move stick walks again afterwards.
    /// </summary>
    IEnumerator GroundBackpackStep()
    {
        var c = Character.localCharacter;
        Item prefab = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it is Backpack b && b.backpackType == BackpackSlot.BackpackType.Backpack)
                prefab = it;
        }
        if (prefab == null)
        {
            Fail("no Backpack item");
            yield break;
        }
        var head = VRRig.Instance.Head;
        var spot = Flat(head.forward) * 0.6f + head.position;
        spot.y = c.Center.y;
        if (!SandboxActions.SpawnItemAt(prefab, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var pack = go != null ? go.GetComponent<Item>() : null;
        yield return Wait(0.6f);
        if (pack == null)
        {
            Fail("spawned Backpack has no Item");
            yield break;
        }
        bool left = !VRInput.LeftHanded;
        var at = pack.Center();
        yield return MoveTo(left, () => at, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        // The pack's own hold before its wheel opens (openRadialMenuTime, Backpack.cs:14, :262).
        float openTime = pack is Backpack bp ? bp.GetInteractTime(c) : 0f;
        yield return Until(
            () => c.data.usingBackpackWheel,
            GameTiming.Budget(openTime, 1f),
            "the backpack wheel did not open"
        );
        if (!stepOk)
        {
            stepNote =
                $"grip {PhysicalInteraction.HandAction(left)} on {Label(PhysicalInteraction.HandTarget(left))}";
            yield break;
        }
        // The hand stays still, so DriveWheel hovers no pocket and the release commits nothing.
        int closed = 0;
        for (float until = Now + 1f; Now < until; )
        {
            if (!c.data.usingBackpackWheel)
                closed++;
            yield return null;
        }
        var groundWheel = GUIManager.instance.backpackWheel;
        var covered = WheelCovered(groundWheel);
        yield return ShotAt("vr-29-ground-backpack", WheelAim(groundWheel));
        yield return GripTo(left, 0f, 0.1f);
        yield return Until(
            () => !c.data.usingBackpackWheel,
            1f,
            "the wheel stayed open after the grip opened"
        );
        if (!stepOk)
            yield break;
        // Turn away first, so the walk does not run into the pack.
        VRRig.Instance.RotateYaw(180f);
        yield return Wait(0.3f);
        bool hand = !VRInput.LeftHanded;
        var from = c.Center;
        Script(hand).Stick = Vector2.up;
        yield return Wait(1f);
        Script(hand).Stick = Vector2.zero;
        yield return Wait(0.3f);
        var to = c.Center;
        float moved = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
        var note =
            $"wheel closed on {closed} frame(s) of 1 s with the grip shut, walked {moved:0.00} m after";
        if (closed > 0)
            Fail("the wheel flickered: " + note);
        else if (covered != null)
            Fail(covered + ": " + note);
        else if (moved < 0.5f)
            Fail("could not walk after the wheel: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.32: with a backpack worn and an Apple grabbed in one hand whose grip stays closed, the other hand's grip over
    /// the shoulder opens the worn pack's wheel (BodyInventory.BeginShoulder), a hand move hovers a pocket, and opening
    /// that grip commits the stash and closes the wheel (BackpackWheel.cs:96-99, TryStash :166). The closed item grip
    /// must not keep the wheel open.
    /// </summary>
    IEnumerator ShoulderStashStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        var inv = BodyInventory.Instance;
        var all = new List<Item>(SandboxActions.Items);
        Item packPrefab = null;
        foreach (var it in all)
        {
            if (it is Backpack b && b.backpackType == BackpackSlot.BackpackType.Backpack)
                packPrefab = it;
        }
        var apple = FindItem(all, AppleFragment);
        if (packPrefab == null || apple == null || gui == null || !inv.Valid)
        {
            Fail($"needs a Backpack and an Apple item and body slots (valid {inv.Valid})");
            yield break;
        }
        var head = VRRig.Instance.Head;
        var spot = Flat(head.forward) * 0.45f + head.position;
        spot.y = c.Center.y;

        // Wear a pack: the wheel's Wear slice calls Backpack.Wear (BackpackWheel.cs:133), the plain pickup into
        // Player.backpackSlot (Player.cs:274).
        if (!SandboxActions.SpawnItemAt(packPrefab, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var pack = SandboxActions.LastSpawned.GetComponent<Backpack>();
        yield return Wait(0.5f);
        pack.Wear(c);
        yield return Until(() => !c.player.backpackSlot.IsEmpty(), 3f, "the backpack was not worn");
        if (!stepOk)
            yield break;

        bool hold = !VRInput.LeftHanded;
        bool reach = !hold;
        if (!SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var world = SandboxActions.LastSpawned.GetComponent<Item>();
        // The apple drops from chest height and, after vr.29's walk, can land on a slope and roll: let it settle
        // (up to 2 s), then follow it every frame like GrabStep so the grip closes at the apple, not where it was.
        yield return Wait(0.4f);
        for (float end = Now + 2f; Now < end && world != null && world.rig != null; )
        {
            if (world.rig.linearVelocity.sqrMagnitude < 0.05f * 0.05f)
                break;
            yield return null;
        }
        var at = world != null ? world.Center() : spot;
        Vector3 Apple() => world != null ? at = world.Center() : at;
        yield return MoveTo(hold, Apple, 0.5f);
        yield return GripTo(hold, 1f, 0.1f);
        for (float pickEnd = Now + 3f; !Holding(apple) && Now < pickEnd; )
            yield return null;
        if (!Holding(apple))
        {
            var hand = VRRig.Instance.Hand(hold);
            Fail(
                $"Apple not picked up: hand {(hand != null ? Vector3.Distance(hand.position, Apple()) : -1f):0.00} m from it"
            );
            yield break;
        }
        HeadFrame(out var headPos, out var forward, out var right);
        yield return MoveTrack(hold, Rest(headPos, forward, hold ? -right : right).Position, 0.3f);

        int slot = -1;
        for (int i = 0; i < inv.Count; i++)
        {
            if (inv.Slot(i).GameSlot == 3)
                slot = i;
        }
        if (slot < 0)
        {
            Fail("no body slot for the backpack");
            yield break;
        }
        var rig = VRRig.Instance;
        yield return MoveTo(reach, () => rig.Origin.TransformPoint(inv.Anchor(slot)), 0.6f);
        yield return GripTo(reach, 1f, 0.1f);
        yield return Until(() => c.data.usingBackpackWheel, 2f, "the shoulder wheel did not open");
        if (!stepOk)
            yield break;

        // Sweep the hand round the wheel (BodyInventory.DriveWheel reads it head-relative) until a pocket offers the stash.
        var wheel = gui.backpackWheel;
        var start = Script(reach).Position;
        for (int deg = 0; deg < 360 && !Stashable(wheel); deg += 30)
        {
            float a = deg * Mathf.Deg2Rad;
            SetHand(
                reach,
                start + VRInput.HeadRotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.15f
            );
            yield return Wait(0.15f);
        }
        if (!Stashable(wheel))
        {
            Fail("no wheel pocket offered the stash");
            yield break;
        }
        var covered = WheelCovered(wheel);
        yield return ShotAt("vr-32-shoulder-stash", WheelAim(wheel));
        yield return GripTo(reach, 0f, 0.1f);
        yield return Until(
            () => !c.data.usingBackpackWheel && HeldItem() == null,
            2f,
            "the wheel did not commit and close with the item grip still closed"
        );
        if (!stepOk)
            yield break;
        bool inPack = false;
        foreach (var s in BackpackReference.GetFromEquippedBackpack(c).GetData().itemSlots)
            inPack |= s != null && !s.IsEmpty() && s.prefab.itemID == apple.itemID;
        var note =
            $"stashed with the {(hold ? "left" : "right")} grip still closed, Apple in the pack {inPack}";
        if (!inPack)
            Fail("the Apple is not in the pack: " + note);
        else if (covered != null)
            Fail(covered + ": " + note);
        else
            stepNote = note;

        static bool Stashable(BackpackWheel w) =>
            w.chosenSlice.IsSome
            && !w.chosenSlice.Value.isBackpackWear
            && !w.chosenSlice.Value.isJetpackFuelSlice;
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
        // Most of the way across the drawn ring (WheelSize is its outer diameter): past any PickDepth, inside the edge.
        float travel = UIWheelsFeature.Size.Value * 0.5f * 0.85f;
        yield return MoveTrack(hand, Script(hand).Position + Vector3.up * travel, 0.3f);
        yield return Wait(0.3f);
        var wheel = gui.emoteWheel.GetComponentInChildren<EmoteWheel>(true);
        string chosen =
            wheel != null && wheel.selectedEmoteName != null ? wheel.selectedEmoteName.text : "";
        var pointer = wheel != null ? ProbeEmoteWheel(wheel) : "no EmoteWheel";
        pointer ??= WheelCovered(wheel);
        yield return ShotAt(
            "vr-18-emote-wheel",
            wheel != null ? WheelAim(wheel) : CentreOf(gui.emoteWheel.transform)
        );
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
        var note = $"hovered '{chosen}', emote started {started}, pointer {pointer ?? "ok"}";
        if (string.IsNullOrEmpty(chosen))
            Fail("no slice hovered by the hand: " + note);
        else if (pointer != null)
            Fail(note);
        else if (!started)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>The open wheel's drawn centre (RadialWheels places it in front of the hand), for aimed shots.</summary>
    static Vector3 WheelAim(UIWheel wheel) =>
        RadialWheels.TryDrawnCentre(wheel, out var centre) ? centre : CentreOf(wheel.transform);

    /// <summary>
    /// Null when the open wheel's slices draw without the depth test (OnTop), so the hand, forearm and a
    /// backpack under the hand cannot cover it; else what is wrong.
    /// </summary>
    static string WheelCovered(UIWheel wheel)
    {
        var slice = wheel != null ? wheel.GetComponentInChildren<UIWheelSlice>() : null;
        var graphic = slice != null ? slice.GetComponentInChildren<UnityEngine.UI.Graphic>() : null;
        if (graphic == null)
            return "no wheel slice graphic";
        return OnTop.IsOnTop(graphic) ? null : "wheel slices are depth tested";
    }

    /// <summary>
    /// The open wheel as the pointer sees it: its dimmer ("BG") is off, and a ray from the head at the empty
    /// centre or past a corner hits no slice, while one at the middle of a slice's drawn wedge hits that slice
    /// (WheelRaycaster, WheelSegment). Only slice hits count: the name label in the centre is not a slice. Aims the laser's event camera, which LaserPointer re-poses
    /// in its own LateUpdate. Returns null when all hold, else what failed.
    /// </summary>
    static string ProbeEmoteWheel(EmoteWheel wheel)
    {
        var bg = wheel.transform.Find("BG");
        if (bg != null && bg.TryGetComponent<UnityEngine.UI.Graphic>(out var dim) && dim.enabled)
            return "backdrop BG still drawn";
        var slice = wheel.slices.Length > 0 ? wheel.slices[0] : null;
        var cam = PeakXR.UI.World.LaserPointer.EventCamera;
        var system = UnityEngine.EventSystems.EventSystem.current;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (slice == null || cam == null || system == null || head == null)
            return "no slice, laser camera, event system or head";
        var rect = (RectTransform)slice.transform;
        float w = rect.rect.width;
        float a = WheelSegment.MidDegrees * Mathf.Deg2Rad;
        var mid = rect.TransformPoint(
            new Vector3(
                -Mathf.Sin(a) * WheelSegment.MidFraction * w,
                Mathf.Cos(a) * WheelSegment.MidFraction * w,
                0f
            )
        );
        // Past the outer edge on both in-plane axes: the empty corner of the slice rects.
        float far = WheelSegment.OuterFraction * w * 1.2f;
        var corner = rect.TransformPoint(new Vector3(far, far, 0f));

        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        GameObject Hit(Vector3 point)
        {
            cam.transform.SetPositionAndRotation(
                head.position,
                Quaternion.LookRotation(point - head.position)
            );
            var data = new UnityEngine.EventSystems.PointerEventData(system)
            {
                position = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f),
            };
            results.Clear();
            system.RaycastAll(data, results);
            foreach (var r in results)
                if (
                    r.gameObject != null
                    && r.gameObject.GetComponentInParent<UIWheelSlice>() != null
                )
                    return r.gameObject;
            return null;
        }

        var centreHit = Hit(rect.position);
        if (centreHit != null)
            return $"centre hit '{centreHit.name}'";
        var cornerHit = Hit(corner);
        if (cornerHit != null)
            return $"corner hit '{cornerHit.name}'";
        var midHit = Hit(mid);
        if (midHit == null || midHit.GetComponentInParent<UIWheelSlice>() != slice)
            return $"wedge middle hit '{(midHit != null ? midHit.name : "nothing")}', not {slice.name}";
        return null;
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
    /// vr.20: the holding hand opened over its belt slot holsters the item into that hotbar slot
    /// (<c>Player.GetItemSlot</c>, Player.cs:246, <c>ItemSlot.prefab</c>), and a grip there draws it back into the same hand.
    /// The game's 2D hotbar must be hidden meanwhile.
    /// </summary>
    IEnumerator HolsterStep()
    {
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        yield return HolsterAndDraw();
    }

    /// <summary>The body of vr.20 for the item already in hand.</summary>
    IEnumerator HolsterAndDraw()
    {
        var c = Character.localCharacter;
        var inv = BodyInventory.Instance;
        ushort id = HeldItem().itemID;
        bool HoldingIt() => HeldItem() != null && HeldItem().itemID == id;
        var selected = c.refs.items.currentSelectedSlot;
        if (!inv.Valid || selected.IsNone || selected.Value > 2)
        {
            Fail($"body slots off or no hotbar slot selected (valid {inv.Valid})");
            yield break;
        }
        if (!HotbarHider.Active)
        {
            Fail("the 2D hotbar is still on the HUD next to the body slots");
            yield break;
        }
        int slot = -1;
        for (int i = 0; i < inv.Count; i++)
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
        bool inSlot = itemSlot != null && !itemSlot.IsEmpty() && itemSlot.prefab.itemID == id;
        yield return ShotAt("vr-20-holster", rig.Origin.TransformPoint(inv.Anchor(slot)));
        if (!inSlot)
        {
            Fail($"hotbar slot {selected.Value} does not hold the item");
            yield break;
        }
        yield return Wait(0.4f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(HoldingIt, 2f, "grip at the slot did not draw it");
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
    /// The face <see cref="FindWall"/> picked, seen from the body after the warp: along -normal at
    /// <paramref name="upFromCenter"/> above the body centre, then at the height FindWall found the face, then straight
    /// at that point. FindWall only proves the face 0.3-1.6 m above the standing spot, so a low ledge can end below
    /// the body centre: vr13 cast at 2.96 m (vr.15) and 3.15 m (vr.44) over faces found at 2.71 m and 2.68 m.
    /// </summary>
    static bool WallInFront(Character c, RaycastHit wall, float upFromCenter, out RaycastHit hit)
    {
        var normal = Flat(wall.normal);
        var body = c.Center;
        var atFace = new Vector3(body.x, wall.point.y, body.z);
        return Physics.Raycast(
                body + Vector3.up * upFromCenter,
                -normal,
                out hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            || Physics.Raycast(
                atFace,
                -normal,
                out hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            || Physics.Raycast(
                atFace,
                (wall.point - atFace).normalized,
                out hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
    }

    /// <summary>
    /// The nearest steep terrain face with a walkable spot 0.6 m in front of it and still there 1.2 m higher, so the
    /// climb has room to rise. The daily map varies, so the search casts from the character and two rings around it
    /// (15 m and 35 m); with nothing there, from every suitcase and campfire of the active segment, which the game
    /// scatters along its whole climb. A warp reaches whichever wins, the one nearest the character.
    /// </summary>
    const string NoWall =
        "no climbable wall with walkable ground in front near the player or any suitcase or campfire of this segment";

    static bool FindWall(Character c, out RaycastHit wall, out Vector3 stand)
    {
        wall = default;
        stand = default;
        float best = float.MaxValue;
        var home = c.Center;
        WallsFrom(home, home, ref best, ref wall, ref stand);
        foreach (float radius in new[] { 15f, 35f })
        {
            for (int o = 0; o < 8; o++)
            {
                var spot = home + Quaternion.Euler(0f, o * 45f, 0f) * Vector3.forward * radius;
                WallsFrom(spot, home, ref best, ref wall, ref stand);
            }
        }
        if (best < float.MaxValue)
            return true;
        foreach (var lug in FindObjectsByType<Luggage>(FindObjectsSortMode.None))
            WallsFrom(lug.transform.position, home, ref best, ref wall, ref stand);
        foreach (var fire in FindObjectsByType<Campfire>(FindObjectsSortMode.None))
            WallsFrom(fire.transform.position, home, ref best, ref wall, ref stand);
        // Nothing on today's map: the sandbox's test wall ahead of the player.
        if (best == float.MaxValue && SandboxActions.PlaceWall() != null)
            WallsFrom(home, home, ref best, ref wall, ref stand);
        return best < float.MaxValue;
    }

    /// <summary>One <see cref="FindWall"/> seed: 24 level rays at three heights within 40 m of
    /// <paramref name="spot"/>; keeps a face nearer <paramref name="home"/> than <paramref name="best"/>.</summary>
    static void WallsFrom(
        Vector3 spot,
        Vector3 home,
        ref float best,
        ref RaycastHit wall,
        ref Vector3 stand
    )
    {
        const int Rays = 24;
        const float Range = 40f;
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
}
