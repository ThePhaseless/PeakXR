using System.Collections;
using PeakXR.Core;
using PeakXR.Emotes;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.Wheels;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>WP6 wheel and gesture-emote steps: the emote wheel refused on a wall, the thumbs-up emote and the
/// backpack wheel let go on nothing. CommsSteps yields <see cref="WheelGapSteps"/>.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator WheelGapSteps()
    {
        yield return HandStep("vr.126-wheel-climbing-refused", 40f, WheelClimbingStep());
        yield return HandStep("vr.127-thumbs-up", 20f, ThumbsUpStep());
        yield return HandStep("vr.128-backpack-wheel-cancel", 30f, BackpackWheelCancelStep());
    }

    /// <summary>
    /// vr.126: on a wall (the vr.15 grip), holding the emote binding keeps the wheel shut (GUIManager.UpdateEmoteWheel,
    /// A/GUIManager.cs:563 tests isClimbing) and the opening hand gets NotWhileClimbing.
    /// </summary>
    IEnumerator WheelClimbingStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var gui = GUIManager.instance;
        if (gui == null || gui.emoteWheel == null)
        {
            Fail("no GUIManager emote wheel");
            yield break;
        }
        if (!FindWall(c, out var wall, out var stand))
        {
            stepSkip = NoWall;
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(wall.normal), Vector3.up));
        yield return Wait(0.6f);
        if (!WallInFront(c, wall, 0.1f, out var hit))
        {
            Fail($"wall not in front (wall {wall.point}, body {c.Center})");
            yield break;
        }
        bool grip = VRInput.LeftHanded;
        var hold = hit.point + hit.normal * 0.04f;
        yield return MoveTo(grip, () => hold, 0.5f);
        yield return GripTo(grip, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        if (!c.data.isClimbing)
            Fail("did not start climbing on the wall grip");
        else
        {
            bool hand = RadialWheels.OpeningHandLeft(VRAction.Emote);
            float mark = CueMark();
            Script(hand).Secondary = true;
            yield return ExpectCue(hand, DenyReason.NotWhileClimbing, mark, EmoteOpenSeconds);
            bool opened = gui.emoteWheel.activeInHierarchy;
            Script(hand).Secondary = false;
            if (stepOk && opened)
                Fail("emote wheel opened on the wall");
            else if (stepOk)
                stepNote = cueNote;
        }
        Script(grip).Grip = 0f;
        yield return Wait(0.5f);
        if (c.data.isClimbing)
            c.refs.climbing.StopClimbing();
        SandboxActions.WarpToSpawn();
        yield return Wait(2f);
    }

    /// <summary>
    /// vr.127: a fist held out in front, finger on the trigger, thumb off every button and pointing up
    /// (EmoteGestures.ThumbsUp) plays the game's thumbs-up emote through CharacterAnimations.PlayEmote
    /// (A/CharacterAnimations.cs:410; emoting :432), with no refusal cue.
    /// </summary>
    IEnumerator ThumbsUpStep()
    {
        if (!EmotesFeature.Gestures.Value)
        {
            stepSkip = "Emotes.GestureEmotes off";
            yield break;
        }
        yield return Wait(0.2f);
        if (GestureEmotes.ThumbsUpAnim == null)
        {
            stepSkip = "no thumbs-up emote in EmoteWheel.data";
            yield break;
        }
        var c = Character.localCharacter;
        var emoting = GameMember.Field<CharacterAnimations, bool>("emoting");
        bool hand = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        var side = hand ? -right : right;
        yield return MoveTrack(
            hand,
            head + forward * 0.35f + side * 0.1f + Vector3.down * 0.2f,
            0.3f
        );
        float mark = CueMark();
        int before = GestureEmotes.PlayCount;
        ref var s = ref Script(hand);
        s.Rotation = Quaternion.LookRotation(forward, Vector3.up);
        s.TriggerTouch = true;
        s.Trigger = 0f;
        s.PrimaryTouch = s.SecondaryTouch = s.StickTouch = false;
        s.Grip = 1f;
        bool started = false;
        yield return WaitFor(
            () =>
                started =
                    emoting(c.refs.animations)
                    && GestureEmotes.PlayCount != before
                    && GestureEmotes.LastPlayed == GestureEmotes.ThumbsUpAnim,
            2f
        );
        Script(hand).Grip = 0f;
        if (!started)
        {
            Fail(
                $"no thumbs-up emote within 2 s of the pose (last gesture emote '{GestureEmotes.LastPlayed}')"
            );
            yield break;
        }
        yield return ExpectNoCue(hand, mark, 0f);
        if (stepOk)
            stepNote = $"thumbs up: played {GestureEmotes.LastPlayed}";
    }

    /// <summary>
    /// vr.128: a grip held on a backpack on the ground opens its wheel (Backpack.cs:262); letting go with the hand
    /// still, so no pocket is hovered, commits nothing (BackpackWheel.Choose, A/BackpackWheel.cs:122-126) and the
    /// hand gets a Cancel tick, no refusal. The pack stays on the ground.
    /// </summary>
    IEnumerator BackpackWheelCancelStep()
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
        var rig = VRRig.Instance;
        if (!ClearHeading(c, rig, out float turn, out string blocked))
        {
            Fail("no open ground to drop the pack on in any heading: " + blocked);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.5f);
        var spot = Flat(rig.Head.forward) * 0.6f + rig.Head.position;
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
        float openTime = pack is Backpack bp ? bp.GetInteractTime(c) : 0f;
        yield return Until(
            () => c.data.usingBackpackWheel,
            GameTiming.Budget(openTime, 1f),
            "the backpack wheel did not open"
        );
        if (!stepOk)
            yield break;
        yield return Wait(0.4f);
        int denials = Affordance.Count;
        float mark = CueMark();
        yield return GripTo(left, 0f, 0.1f);
        yield return ExpectFeel(left, CueFeel.Cancel, mark, 1f);
        if (!stepOk)
            yield break;
        if (Affordance.Count != denials)
            Fail($"release on nothing was refused ({Affordance.LastReason}), not cancelled");
        else if (!c.player.backpackSlot.IsEmpty())
            Fail("the pack was worn, not left on the ground");
        else
            stepNote = $"released on no pocket: {cueNote}";
    }
}
