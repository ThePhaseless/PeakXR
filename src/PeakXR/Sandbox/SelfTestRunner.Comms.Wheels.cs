using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Emotes;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.Wheels;
using PeakXR.VR;
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
        yield return HandStep("vr.443-thumbs-up-no-interact", 20f, ThumbsUpNoInteractStep());
        yield return HandStep("vr.447-thumbs-up-cooldown", 20f, ThumbsUpCooldownStep());
        yield return HandStep("vr.446-posed-tap-picks-up", 20f, PosedTapStep());
        yield return HandStep("vr.128-backpack-wheel-cancel", 30f, BackpackWheelCancelStep());
    }

    /// <summary>
    /// vr.126: on a wall (the vr.15 grip), holding the emote binding keeps the wheel shut (GUIManager.UpdateEmoteWheel,
    /// A/GUIManager.cs:563 tests isClimbing) and the opening hand gets NotWhileClimbing.
    /// </summary>
    IEnumerator WheelClimbingStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        if (gui == null || gui.emoteWheel == null)
        {
            Fail("no GUIManager emote wheel");
            yield break;
        }
        // The climb steps' own wall (SelfTestRunner.Climb.cs: ToWall, GripWall): offline the sandbox's 7 m test wall
        // 0.6 m ahead, so the hold is inside the game's reach. Run 37 and 40's map wall here was not, and the grip
        // never climbed. The test wall goes after the step.
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool grip = VRInput.LeftHanded;
        yield return GripWall(grip);
        if (!c.data.isClimbing)
            Fail(
                $"did not start climbing on the wall grip ({PeakXR.Interact.PhysicalInteraction.LastGripNote})"
            );
        else
        {
            bool hand = RadialWheels.OpeningHandLeft(VRAction.Emote);
            float mark = CueMark();
            Script(hand).Secondary = true;
            yield return ExpectCue(
                hand,
                DenyReason.NotWhileClimbing,
                mark,
                EmoteOpenSeconds,
                seen: false
            );
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
        // ToWall's undo (the test wall); this step runs outside the climb steps' Restoring wrapper.
        var restore = bodyRestore;
        bodyRestore = null;
        restore?.Invoke();
        SandboxActions.WarpToSpawn();
        yield return Wait(2f);
    }

    /// <summary>
    /// vr.127: the thumbs-up needs clear intent (EmoteGestures.ThumbIntent). First the negatives, before the emote's
    /// cooldown could hide them: a plain grip at the side, thumb off the buttons, and the full pose on the hand that
    /// holds an Apple, each held 1.5 s, play nothing (a false emote is seen by every peer). Then a fist held still out
    /// in front, finger on the trigger, thumb lifted off the stick it rested on and pointing up, plays the game's
    /// thumbs-up emote through CharacterAnimations.PlayEmote (A/CharacterAnimations.cs:410; emoting :432), once and
    /// with no refusal cue.
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
        var notes = new List<string>();

        // The thumb rests on the stick first, as real thumbs do: the controller shows it senses the thumb.
        Script(hand).StickTouch = true;
        yield return Wait(0.2f);

        // Negative: an ordinary grip at the side, thumb off every button, held still.
        int before = GestureEmotes.PlayCount;
        yield return MoveTrack(
            hand,
            head + side * 0.3f + Vector3.down * 0.6f + forward * 0.05f,
            0.3f
        );
        Fist(hand, forward);
        yield return Wait(1.5f);
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        notes.Add($"grip at the side: {GestureEmotes.PlayCount - before} emotes");
        if (GestureEmotes.PlayCount != before)
        {
            Fail($"a plain grip at the side played '{GestureEmotes.LastPlayed}'");
            yield break;
        }

        // Negative: the full pose out in front on the hand that holds an item.
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        bool holdLeft = LocalHold.ItemInLeftHand;
        yield return MoveTrack(holdLeft, Out(holdLeft), 0.3f);
        Fist(holdLeft, forward);
        yield return Wait(1.5f);
        notes.Add(
            $"pose while holding {Name(HeldItem())}: {GestureEmotes.PlayCount - before} emotes"
        );
        if (GestureEmotes.PlayCount != before)
        {
            Fail($"the pose on a hand holding an item played '{GestureEmotes.LastPlayed}'");
            yield break;
        }
        Script(holdLeft).StickTouch = true;
        yield return DropHeld();
        if (!stepOk)
            yield break;
        Script(holdLeft).Grip = 0f;
        yield return Wait(0.3f);

        // Positive: the deliberate pose, held still out in front.
        yield return MoveTrack(hand, Out(hand), 0.3f);
        float mark = CueMark();
        before = GestureEmotes.PlayCount;
        Fist(hand, forward);
        bool started = false;
        yield return WaitFor(
            () =>
                started =
                    emoting(c.refs.animations)
                    && GestureEmotes.PlayCount != before
                    && GestureEmotes.LastPlayed == GestureEmotes.ThumbsUpAnim,
            2f
        );
        // Still held: it must not repeat.
        yield return Wait(0.8f);
        int played = GestureEmotes.PlayCount - before;
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        if (!started)
        {
            Fail(
                $"no thumbs-up emote within 2 s of the pose (last gesture emote '{GestureEmotes.LastPlayed}')"
            );
            yield break;
        }
        if (played != 1)
        {
            Fail($"the held pose played {played} emotes, not 1");
            yield break;
        }
        yield return ExpectCue(hand, DenyReason.HappenedHere, mark);
        notes.Add($"thumbs up: played {GestureEmotes.LastPlayed} once, confirmed: {cueNote}");
        if (stepOk)
            stepNote = string.Join("; ", notes);

        Vector3 Out(bool left) =>
            head + forward * 0.4f + (left ? -right : right) * 0.1f + Vector3.down * 0.2f;
    }

    /// <summary>
    /// vr.443 (issue #82): the thumbs-up's fist, closed out in front with nothing at the hand, is a grip on empty air,
    /// which falls back to the head-aimed interact. It must wait while the pose holds and be dropped once the emote
    /// plays: the thumbs-up plays and the grip's interact (PhysicalInteraction.GripInteractActive) never presses.
    /// </summary>
    IEnumerator ThumbsUpNoInteractStep()
    {
        if (!EmotesFeature.Gestures.Value || GestureEmotes.ThumbsUpAnim == null)
        {
            stepSkip = "Emotes.GestureEmotes off or no thumbs-up emote";
            yield break;
        }
        // Past vr.127's emote cooldown (2 s).
        yield return Wait(2.2f);
        bool hand = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        Script(hand).StickTouch = true;
        yield return MoveTrack(
            hand,
            head + forward * 0.4f + (hand ? -right : right) * 0.1f + Vector3.down * 0.2f,
            0.3f
        );
        int before = GestureEmotes.PlayCount;
        bool pressed = false;
        Fist(hand, forward);
        float played = float.PositiveInfinity;
        for (float until = Now + 2.5f; Now < until && Now < played + 0.5f; )
        {
            pressed |= Interact.PhysicalInteraction.GripInteractActive;
            if (GestureEmotes.PlayCount != before && float.IsPositiveInfinity(played))
                played = Now;
            yield return null;
        }
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        var note = F(
            $"thumbs-up played {GestureEmotes.PlayCount != before} ('{GestureEmotes.LastPlayed}'), grip interact pressed {pressed}"
        );
        if (GestureEmotes.PlayCount == before)
            Fail("no thumbs-up within 2.5 s: " + note);
        else if (pressed)
            Fail("the thumbs-up's fist pressed the head-aimed interact: " + note);
        else
            Note(note);
        yield return Wait(0.3f);
    }

    /// <summary>
    /// vr.447 (issue #82): a thumbs-up made inside the 2 s emote cooldown plays nothing, so it never stamped the hand
    /// and its fist pressed the head-aimed interact at GripAt + 1.2 s. One thumbs-up plays, the hand opens, a second
    /// one is made at once and held past the 1.2 s cap: it must not play, the grip's interact
    /// (PhysicalInteraction.GripInteractActive) must never press, and the hand gets the light TooSoon notice.
    /// </summary>
    IEnumerator ThumbsUpCooldownStep()
    {
        if (!EmotesFeature.Gestures.Value || GestureEmotes.ThumbsUpAnim == null)
        {
            stepSkip = "Emotes.GestureEmotes off or no thumbs-up emote";
            yield break;
        }
        // Past the last step's emote cooldown (2 s).
        yield return Wait(2.2f);
        bool hand = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        Script(hand).StickTouch = true;
        yield return MoveTrack(
            hand,
            head + forward * 0.4f + (hand ? -right : right) * 0.1f + Vector3.down * 0.2f,
            0.3f
        );
        int before = GestureEmotes.PlayCount;
        Fist(hand, forward);
        yield return Until(
            () => GestureEmotes.PlayCount != before,
            2.5f,
            "the first thumbs-up did not play"
        );
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        if (!stepOk)
            yield break;
        yield return Wait(0.15f);
        int played = GestureEmotes.PlayCount;
        Affordance.ResetGate();
        float mark = CueMark();
        bool pressed = false;
        Fist(hand, forward);
        for (float until = Now + GripIntent.GestureMaxWait + 0.5f; Now < until; )
        {
            pressed |= Interact.PhysicalInteraction.GripInteractActive;
            yield return null;
        }
        bool took = GestureEmotes.TookAt(hand) > GestureEmotes.PlayedAt(hand);
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        var note = F(
            $"second thumbs-up played {GestureEmotes.PlayCount != played}, took the hand {took}, grip interact pressed {pressed}"
        );
        if (GestureEmotes.PlayCount != played)
            Fail("the thumbs-up inside the emote cooldown played: " + note);
        else if (!took)
            Fail("the second thumbs-up never fired, so the step proves nothing: " + note);
        else if (pressed)
            Fail("the cooldown-blocked thumbs-up's fist pressed the head-aimed interact: " + note);
        if (!stepOk)
            yield break;
        yield return ExpectCue(hand, DenyReason.TooSoon, mark, within: 0f);
        if (stepOk)
            Note(note + "; " + cueNote);
        yield return Wait(0.3f);
    }

    /// <summary>
    /// vr.446 (issue #82): a quick grip tap in the thumbs-up pose, with an Apple on the head's interact ray, is a gaze
    /// grab. The pose holds the interact back (Core <c>GripIntent.GestureInteractAt</c>, 0.25 s), and a grip opened
    /// sooner pressed nothing; now the release presses it once (<c>GripIntent.ReleaseTaps</c>). The fist closes for
    /// 0.1 s: the press frame must be posed (<c>GestureEmotes.ThumbPosed</c>), and the Apple must reach the hand.
    /// </summary>
    IEnumerator PosedTapStep()
    {
        if (!EmotesFeature.Gestures.Value)
        {
            stepSkip = "Emotes.GestureEmotes off";
            yield break;
        }
        var c = Character.localCharacter;
        yield return DropHeld();
        if (c.data.currentItem != null)
        {
            stepSkip = "an item stayed in hand";
            yield break;
        }
        var headT = VRRig.Instance.Head;
        var world = SpawnTestItem(
            AppleFragment,
            headT.position + Flat(headT.forward) * 0.8f + Vector3.down * 0.2f
        );
        if (world == null)
            yield break;
        bool hand = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        Script(hand).StickTouch = true;
        yield return MoveTrack(
            hand,
            head + forward * 0.3f + (hand ? -right : right) * 0.15f + Vector3.down * 0.25f,
            0.3f
        );
        for (float until = Now + 3f; Now < until; )
        {
            HoldOnHeadRay(world, headT);
            if (ReferenceEquals(Interaction.instance.currentHovered, world))
                break;
            yield return null;
        }
        if (!ReferenceEquals(Interaction.instance.currentHovered, world))
        {
            Fail("the Apple never became the hovered target");
            yield break;
        }
        bool posed = false,
            pressed = false;
        Fist(hand, forward);
        for (float until = Now + 0.1f; Now < until; )
        {
            HoldOnHeadRay(world, headT);
            posed |= GestureEmotes.ThumbPosed(hand);
            pressed |= Interact.PhysicalInteraction.GripInteractActive;
            yield return null;
        }
        bool early = pressed;
        Script(hand).Grip = 0f;
        Script(hand).StickTouch = true;
        for (float until = Now + 1.5f; Now < until && c.data.currentItem == null; )
        {
            HoldOnHeadRay(world, headT);
            pressed |= Interact.PhysicalInteraction.GripInteractActive;
            yield return null;
        }
        bool holding = c.data.currentItem != null;
        var note = F(
            $"posed {posed}, pressed while closed {early}, grip interact pressed {pressed}, holding {holding}"
        );
        if (!posed)
            Fail("the tap was not in the thumbs-up pose, so the step proves nothing: " + note);
        else if (!holding)
            Fail("the quick posed tap picked nothing up: " + note);
        else
            Note(note);
        yield return DropHeld();
    }

    /// <summary>A fist on <paramref name="left"/>: grip closed, finger on the trigger, the thumb off every button, the
    /// controller's up axis pointing up.</summary>
    void Fist(bool left, Vector3 forward)
    {
        ref var s = ref Script(left);
        s.Rotation = HandLook(forward, Vector3.up);
        s.TriggerTouch = true;
        s.Trigger = 0f;
        s.PrimaryTouch = s.SecondaryTouch = s.StickTouch = false;
        s.Grip = 1f;
    }

    /// <summary>
    /// vr.128: stooping, a grip held on a backpack on the ground opens its wheel (Backpack.cs:262); letting go with the hand
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
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no open ground to drop the pack on in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.5f);
        var spot = Flat(rig.Head.forward) * 0.5f + rig.Head.position;
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
        // The player stoops to the pack on the ground (ReachDown also crouches the body), as for an apple (vr.10).
        yield return ReachDown(left, () => at, 0.6f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        float reach = Vector3.Distance(Script(left).Position, VRInput.HeadPosition);
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
            stepNote =
                $"released on no pocket: {cueNote}; stooped {headDrop:0.00} m, grip {reach:0.00} m from the eyes";
    }
}
