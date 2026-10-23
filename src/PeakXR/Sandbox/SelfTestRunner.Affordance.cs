using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>vr.6x</c> steps: ui.affordance. Scripted hands try things the game refuses and each step proves the whole
/// cue (<see cref="ExpectCue(bool, DenyReason, float, bool, float)"/>: reason, ring, sound, haptic, red preview),
/// not just that the action failed.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator AffordanceSteps()
    {
        yield return HandStep("vr.60-deny-piton-range", 20f, DenyPitonStep());
        yield return HandStep("vr.61-deny-two-hands", 20f, DenyTwoHandStep());
        yield return HandStep("vr.62-deny-unusable", 20f, DenyUnusableStep());
        yield return HandStep("vr.63-sticky-throw", 25f, StickyThrowStep());
        yield return VRStep("vr.64-cue-in-menu", 20f, CueInMenuStep());
        yield return HandStep("vr.65-hints-off", 25f, HintsOffStep());
        yield return VRStep("vr.66-refusal-over-ambient", 10f, RefusalOverAmbientStep());
        yield return HandStep("vr.67-edge-ring", 15f, EdgeRingStep());
        yield return VRStep("vr.220-first-refusal-label", 20f, FirstRefusalLabelStep());
    }

    /// <summary>vr.67: a refusal at a hand held behind the head. Its real ring is out of sight, so ui.affordance also
    /// draws the in-view edge copy (<see cref="CueEdge"/>) toward it; the copy must lie wholly inside the headset's
    /// view while the real ring stays where it is.</summary>
    IEnumerator EdgeRingStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        const bool left = false;
        HeadFrame(out var head, out var forward, out var right);
        SetHand(left, head - forward * 0.3f + right * 0.1f + Vector3.up * 0.05f);
        yield return Wait(0.3f);
        Affordance.ResetGate();
        float mark = CueMark();
        Affordance.DenyAtHand(left, DenyReason.NotNow);
        // The head stays put: the hand is behind it, and the edge copy is what must show.
        yield return ExpectCue(left, DenyReason.NotNow, mark, within: 1f, seen: false);
        if (!stepOk)
            yield break;
        yield return Wait(0.2f);
        ref var h = ref CueLog.Capture.Hand(left);
        var rig = VRRig.Instance;
        float off =
            rig != null && rig.Head != null
                ? Vector3.Angle(rig.Head.forward, h.RingCenter.U() - rig.Head.position)
                : 0f;
        if (h.EdgeAt < mark)
            Fail(F($"no edge ring drawn for a ring {off:0} deg off the view"));
        else if (RingOutOfView(left, out var view) is { } outside)
            Fail(outside);
        else if (off <= CueEdge.HalfAngle)
            Fail(F($"the hand's ring was in view ({off:0} deg), so the step proved nothing"));
        else
            stepNote = cueNote + view;
    }

    /// <summary>vr.66: an ambient beat (heartbeat, wind, texture) asking for the right hand every frame must not cut a
    /// refusal there: the whole triple tick goes out, and no ambient pulse slips in until it ends and the duck tail
    /// passes (Interact.Haptics priority channel). The beat is played here, so the step needs no exertion or wind.</summary>
    IEnumerator RefusalOverAmbientStep()
    {
        if (!Affordance.Enabled || Interact.Haptics.Muted)
        {
            stepSkip = "ui.affordance not installed or Interaction.Haptics off";
            yield break;
        }
        const bool left = false;
        const float beat = 0.05f;
        float end = CueMark() + 0.3f;
        bool any = false;
        while (CueMark() < end)
        {
            any |= Interact.Haptics.Pulse(left, 0.3f, beat, HapticPriority.Ambient);
            yield return null;
        }
        if (!any)
        {
            stepSkip = "no impulse-capable right controller";
            yield break;
        }
        var pattern = DenyHaptics.For(DenyReason.NotNow);
        float length = pattern[pattern.Length - 1].At + pattern[pattern.Length - 1].Seconds;
        Affordance.ResetGate();
        float mark = CueMark();
        Affordance.DenyAtHand(left, DenyReason.NotNow);
        int pulses = 0;
        float lastPulse = float.NegativeInfinity;
        string cut = null;
        end = mark + length + Core.HapticChannel.DuckTail + 0.2f;
        while (CueMark() < end)
        {
            Interact.Haptics.Pulse(left, 0.3f, beat, HapticPriority.Ambient);
            ref var hand = ref CueLog.Capture.Hand(left);
            if (hand.PulseAt >= mark && hand.PulseAt != lastPulse)
            {
                lastPulse = hand.PulseAt;
                if (hand.Delivered || hand.Muted)
                    pulses++;
            }
            float ambientAt = Interact.Haptics.LastSent(left, HapticPriority.Ambient);
            if (
                cut == null
                && ambientAt >= mark
                && ambientAt < mark + length + Core.HapticChannel.DuckTail
            )
                cut =
                    $"ambient pulse at {ambientAt - mark:0.000} s inside the {length:0.00} s refusal or its duck tail";
            yield return null;
        }
        string note =
            $"{pulses} of {pattern.Length} refusal pulses delivered, {Interact.Haptics.Dropped} dropped by priority in all";
        if (cut != null)
            Fail(cut + "; " + note);
        else if (pulses != pattern.Length)
            Fail("refusal pattern not intact: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.64: a refusal while the pause menu is up still sounds, through the game's SFX player and through
    /// the fallback source used where the game has none (menus, loading). NotNow is the reason a paused action
    /// gets.</summary>
    IEnumerator CueInMenuStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        var gui = GUIManager.instance;
        var c = Character.localCharacter;
        if (gui == null || c == null)
        {
            Fail("no GUIManager or character");
            yield break;
        }
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(() => GUIManager.InPauseMenu, 5f, "pause menu did not open");
        string note = null;
        try
        {
            for (int pass = 0; pass < 2 && stepOk; pass++)
            {
                bool fallback = pass == 1;
                CueAudio.ForceFallback = fallback;
                Affordance.ResetGate();
                float mark = CueMark();
                if (!Affordance.DenyAtHand(false, DenyReason.NotNow))
                {
                    Fail("NotNow refusal not accepted in the pause menu");
                    break;
                }
                yield return ExpectCue(false, DenyReason.NotNow, mark);
                if (stepOk && CueAudio.LastWasFallback != fallback)
                    Fail($"sound went through the {(fallback ? "game's SFX player" : "fallback")}");
                note += (fallback ? "; fallback: " : "SFX player: ") + cueNote;
            }
        }
        finally
        {
            CueAudio.ForceFallback = false;
            ClosePause();
        }
        if (stepOk)
            stepNote = note;
    }

    /// <summary>vr.65: with <c>UI.ActionHints</c> off a refusal still gets its whole cue (the two-handed item held
    /// in one hand: ring, sound, haptic, red item) but none of the teal "would work" parts (no valid-spot ring, no
    /// rising note, no reach ray), and with <c>UI.ActionHintText</c> off no label.</summary>
    IEnumerator HintsOffStep()
    {
        var hook = FindItem(new List<Item>(SandboxActions.Items), "RescueHook");
        if (hook == null)
        {
            stepSkip = "no RescueHook item";
            yield break;
        }
        bool hints = AffordanceFeature.ActionHints.Value;
        bool text = AffordanceFeature.ActionHintText.Value;
        bool firstTime = AffordanceFeature.FirstTimeRefusalLabels.Value;
        try
        {
            AffordanceFeature.ActionHints.Value = false;
            AffordanceFeature.ActionHintText.Value = false;
            // A first firing would label it (vr.220 covers that); this step is about the text staying off.
            AffordanceFeature.FirstTimeRefusalLabels.Value = false;
            yield return GiveAndHold(hook);
            if (!stepOk)
                yield break;
            bool left = LocalHold.ItemInLeftHand;
            Affordance.ResetGate();
            LookAtHand(left);
            float mark = CueMark();
            Script(left).TriggerTouch = true;
            Script(left).Trigger = 1f;
            yield return ExpectCue(left, DenyReason.NeedsTwoHands, mark, tint: true);
            string note = cueNote;
            // Past the would-work delay: the rising note would have played by now.
            yield return Wait(0.4f);
            string extra =
                AffordanceRuntime.HintDrawn ? "teal would-work ring drawn"
                : CueLog.Capture.LastSound(CueSound.WouldWork).At >= mark ? "rising note played"
                : AffordanceRuntime.RayShown ? "reach ray drawn"
                : AffordanceRuntime.LabelShown ? "text label shown"
                : null;
            yield return ShotAt("vr-65-hints-off", Affordance.LastPosition);
            Script(left).Trigger = 0f;
            Script(left).TriggerTouch = false;
            if (!stepOk)
                yield break;
            if (extra != null)
                Fail($"hints off, yet {extra}; {note}");
            else
                stepNote = note;
        }
        finally
        {
            AffordanceFeature.ActionHints.Value = hints;
            AffordanceFeature.ActionHintText.Value = text;
            AffordanceFeature.FirstTimeRefusalLabels.Value = firstTime;
        }
    }

    /// <summary>vr.220: with <c>UI.ActionHintText</c> off, a reason's first refusal shows its text label beside the
    /// ring (<c>UI.FirstTimeRefusalLabels</c>); once that cue is gone, the same refusal again shows the ring only. The
    /// seen-reasons mask is put back after.</summary>
    IEnumerator FirstRefusalLabelStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        const DenyReason reason = DenyReason.Locked;
        var seenEntry = AffordanceFeature.SeenRefusals;
        ulong seen = seenEntry.Value;
        bool text = AffordanceFeature.ActionHintText.Value;
        bool firstTime = AffordanceFeature.FirstTimeRefusalLabels.Value;
        try
        {
            AffordanceFeature.ActionHintText.Value = false;
            AffordanceFeature.FirstTimeRefusalLabels.Value = true;
            seenEntry.Value = seen & ~(1ul << (int)reason);
            Affordance.ResetGate();
            if (!Affordance.DenyAtHand(false, reason))
            {
                Fail("Locked refusal not accepted");
                yield break;
            }
            yield return Until(
                () => AffordanceRuntime.LabelShown,
                1f,
                "no label on the first Locked refusal"
            );
            if (!stepOk)
                yield break;
            string first = AffordanceRuntime.LabelText;
            bool marked = FirstRefusalLabel.Seen(seenEntry.Value, reason);
            yield return ShotAt("vr-220-first-label", Affordance.LastPosition);
            if (
                first == null
                || !first.StartsWith(DenyText.Label(reason), System.StringComparison.Ordinal)
            )
            {
                Fail($"first label reads '{first}', not '{DenyText.Label(reason)}'");
                yield break;
            }
            if (!marked)
            {
                Fail("Locked not saved as seen after its first label");
                yield break;
            }
            yield return Until(() => !AffordanceRuntime.LabelShown, 6f, "first label never faded");
            if (!stepOk)
                yield break;
            Affordance.ResetGate();
            LookAtHand(false);
            float mark = CueMark();
            if (!Affordance.DenyAtHand(false, reason))
            {
                Fail("second Locked refusal not accepted");
                yield break;
            }
            yield return ExpectCue(false, reason, mark);
            string note = cueNote;
            if (!stepOk)
                yield break;
            if (AffordanceRuntime.LabelShown)
                Fail($"label shown again on the second Locked refusal; {note}");
            else
                stepNote = $"first: '{first.Split('\n')[0]}', second: ring only; {note}";
        }
        finally
        {
            seenEntry.Value = seen;
            AffordanceFeature.ActionHintText.Value = text;
            AffordanceFeature.FirstTimeRefusalLabels.Value = firstTime;
        }
    }

    /// <summary>vr.60: a held climbing spike aimed at the open sky, then level at a test wall just past the spike's
    /// grounded reach. The trigger press reaches <c>RaycastClimbingSpikeStart</c> (CharacterItems.cs:1072-1076),
    /// which finds no terrain within <c>climbingSpikeStartDistanceGrounded</c>, so no cast starts; SpikeCue denies both
    /// as <see cref="DenyReason.NoWall"/>, and for the wall puts the ring on the wall itself (its Closer hint), so
    /// the player sees how far off it is.</summary>
    IEnumerator DenyPitonStep()
    {
        var spike = FindItemComponent<ClimbingSpikeComponent>();
        if (spike == null)
        {
            stepSkip = "no climbing spike item";
            yield break;
        }
        yield return GiveAndHold(spike.GetComponent<Item>());
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        var rig = VRRig.Instance;
        HeadFrame(out var head, out var forward, out var right);
        SetHand(left, head + forward * 0.25f + Vector3.up * 0.2f);
        // A direction proven empty: the hand's real aim ray finds no terrain within 60 m (far past any spike range).
        var tries = new[]
        {
            Vector3.up,
            Vector3.up + forward,
            Vector3.up - forward,
            Vector3.up + right,
            Vector3.up - right,
        };
        bool clear = false;
        foreach (var t in tries)
        {
            Script(left).Rotation = Quaternion.LookRotation(t.normalized, -forward);
            yield return Wait(0.15f);
            var aim = rig.Aim(left);
            if (
                aim != null
                && !Physics.Raycast(
                    aim.position,
                    aim.forward,
                    60f,
                    HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
                )
            )
            {
                clear = true;
                break;
            }
        }
        if (!clear)
        {
            stepSkip = "no aim direction free of terrain within 60 m";
            yield break;
        }
        // The player looks where the spike points, at the empty sky the refusal is about (run 48: 70 deg off).
        yield return PressAndExpect(left, DenyReason.NoWall, along: true);
        if (!stepOk)
            yield break;
        string sky = "open sky: " + stepNote;
        var wall = SandboxActions.PlaceWall();
        if (wall == null)
        {
            Fail("no test wall for the out-of-range try: " + SandboxActions.LastMessage);
            yield break;
        }
        try
        {
            yield return PitonPastRange(spike, left, wall);
            if (stepOk)
                stepNote = sky + "; " + stepNote;
        }
        finally
        {
            UnityEngine.Object.Destroy(wall);
        }
    }

    /// <summary>The second half of vr.60: <paramref name="wall"/> moved so its face stands 1 m past the spike's
    /// grounded reach straight ahead of the hand, the hand aimed level at it, the press refused with the ring on the
    /// wall (SpikeCue: the wall point, not the end of the reach).</summary>
    IEnumerator PitonPastRange(ClimbingSpikeComponent spike, bool left, GameObject wall)
    {
        var rig = VRRig.Instance;
        float range = spike.climbingSpikeStartDistanceGrounded;
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
        Script(left).Rotation = Quaternion.LookRotation(forward, Vector3.up);
        yield return Wait(0.15f);
        var aim = rig.Aim(left);
        var ahead = rig.Origin.TransformDirection(forward);
        // Half the wall's 0.5 m depth behind the face.
        var at = aim.position + ahead * (range + 1.25f);
        wall.transform.SetPositionAndRotation(
            new Vector3(at.x, wall.transform.position.y, at.z),
            Quaternion.LookRotation(ahead)
        );
        Physics.SyncTransforms();
        yield return Wait(0.15f);
        if (
            !Physics.Raycast(
                aim.position,
                aim.forward,
                out var face,
                range + 4f,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
            )
            || face.collider.gameObject != wall
            || face.distance <= range
        )
        {
            Fail(
                F(
                    $"the aim does not meet the test wall past the {range:0.00} m reach (hit {(face.collider != null ? face.collider.name : "nothing")} at {face.distance:0.00} m)"
                )
            );
            yield break;
        }
        yield return PressAndExpect(left, DenyReason.NoWall);
        if (!stepOk)
            yield break;
        float off = Vector3.Distance(Affordance.LastPosition, face.point);
        var note = F(
            $"wall {face.distance:0.00} m away past the {range:0.00} m reach, ring {off:0.00} m from the wall point: {stepNote}"
        );
        if (off > 0.3f)
            Fail("the out-of-range ring is not on the wall: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.61: a two-handed item (rescue hook, <see cref="ItemHoldTable"/>) held in one hand. interact.physical
    /// blocks its use and denies it as <see cref="DenyReason.NeedsTwoHands"/>, with the free hand's grip as the
    /// hint.</summary>
    IEnumerator DenyTwoHandStep()
    {
        var hook = FindItem(new List<Item>(SandboxActions.Items), "RescueHook");
        if (hook == null)
        {
            stepSkip = "no RescueHook item";
            yield break;
        }
        yield return GiveAndHold(hook);
        if (!stepOk)
            yield break;
        yield return PressAndExpect(LocalHold.ItemInLeftHand, DenyReason.NeedsTwoHands);
    }

    /// <summary>vr.62: an Apple the game will not use (<c>Item.overrideUsability</c> false, read first by
    /// <c>CanUsePrimary</c>, Item.cs:797-802). The trigger press gets <see cref="DenyReason.CantUse"/> instead of
    /// the success tick, and starts no use cast (<c>Item.StartUsePrimary</c>, counted by
    /// <see cref="SelfTestUseProbe.Starts"/>). Set directly, named as a shortcut: the game sets the same field on a
    /// spent biome warp (Action_WarpToBiome.cs:10), whose use would warp the run away, and every other unusable item
    /// has its own reason (UseDenial.Why), covered by vr.94 and the vr.16x steps.</summary>
    IEnumerator DenyUnusableStep()
    {
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var usability = GameMember.Field<Item, Optionable<bool>>("overrideUsability");
        usability(HeldItem()) = Optionable<bool>.Some(false);
        int starts = SelfTestUseProbe.Starts;
        yield return PressAndExpect(LocalHold.ItemInLeftHand, DenyReason.CantUse);
        if (HeldItem() != null)
            usability(HeldItem()) = Optionable<bool>.None;
        if (!stepOk)
            yield break;
        if (SelfTestUseProbe.Starts != starts)
            Fail("the refused press still started a use cast (Item.StartUsePrimary): " + stepNote);
        else
            stepNote =
                "shortcut: unusable through Item.overrideUsability (as Action_WarpToBiome.cs:10 leaves a spent warp); no use cast started; "
                + stepNote;
    }

    /// <summary>vr.63: a sticky item (cactus ball, <c>StickyItemComponent</c>) thrown by a hand swing leaves the hand.
    /// DoDropping aborts any drop below its <c>throwChargeRequirement</c> (CharacterItems.cs:277-289), so this proves
    /// the hand throw sends a real charge.</summary>
    IEnumerator StickyThrowStep()
    {
        var sticky = FindItemComponent<StickyItemComponent>();
        if (sticky == null)
        {
            stepSkip = "no sticky item";
            yield break;
        }
        // Straight into a slot: the hip spawn of GiveAndHold let the cactus stick to the body first (run 35).
        yield return EquipDirect(sticky.GetComponent<Item>());
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        // Run 37: the cactus stayed in hand 0.5 s after equipping, yet the drop tap found the hand empty. Say whether
        // closing the holding hand's grip lost it.
        yield return Wait(0.2f);
        if (HeldItem() == null)
        {
            Fail(
                $"the sticky item left the hand when its grip closed; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
            );
            yield break;
        }
        // A gentle drop stays stuck, as in vanilla (InteractPatches.Dropping): the StuckToHand cue with the item red.
        // A drop button tap is the gentle drop that reaches DoDropping by default. A slow grip open only does with
        // Interaction.ReleaseToDrop on; off (the default) it keeps any item, so it is no refusal (VR run 31).
        Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        yield return TapDrop();
        yield return ExpectCue(left, DenyReason.StuckToHand, mark, tint: true, within: 1.5f);
        string gentle = cueNote;
        if (!stepOk)
            yield break;
        if (HeldItem() == null)
        {
            Fail("sticky item left the hand on a drop button tap");
            yield break;
        }
        ThrowArc(left, out var windup, out var release);
        yield return MoveTrack(left, windup, 0.4f);
        yield return Wait(0.3f);
        int throwsBefore = PeakXR.Interact.PhysicalInteraction.ThrowCount;
        yield return SwingRelease(left, windup, release);
        yield return Until(
            () => HeldItem() == null,
            1.5f,
            "sticky item still in hand after the swing"
        );
        // Thrown, not dropped: interact.physical counted the release as a throw.
        int throws = PeakXR.Interact.PhysicalInteraction.ThrowCount - throwsBefore;
        if (stepOk && throws < 1)
            Fail($"the sticky item left the hand without a throw (throws {throws})");
        if (stepOk)
            stepNote =
                $"thrown (charge requirement {sticky.throwChargeRequirement:0.00}); gentle release: {gentle}";
    }

    /// <summary>Presses the trigger of <paramref name="left"/> and waits up to 1 s for the cue of
    /// <paramref name="expected"/> on that hand on every channel, the held item's red preview included
    /// (<see cref="ExpectCue(bool, DenyReason, float, bool, float)"/>); other denials meanwhile do not end the
    /// wait. The held item must also stay in the hand with its uses unspent: a cue over a use that went through
    /// is no refusal.</summary>
    IEnumerator PressAndExpect(bool left, DenyReason expected, bool along = false)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        Affordance.ResetGate();
        int before = Affordance.Count;
        // The refused item, to prove the refusal also held: it stays in the hand with every use left.
        var held = HeldItem();
        ushort heldId = held != null ? held.itemID : (ushort)0;
        int usesBefore = held != null ? UsesLeft(held) : -1;
        LookAtHand(left, along);
        float mark = CueMark();
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        bool Got() =>
            Affordance.Count > before
            && Affordance.LastReason == expected
            && Affordance.LastLeft == left;
        // Reason, readable ring, refused sound, a haptic pulse on that hand and the red preview (item or ghost).
        yield return ExpectCue(left, expected, mark, tint: true);
        // Keep holding past the one-shot fade: the cue must stay for as long as the attempt is held.
        if (Got())
            yield return Wait(DenyFade.Duration + 0.3f);
        string cue = CueCheck();
        var tinted = DenyTint.Tinted.ToArray();
        cue ??= TintCheck(tinted);
        // Shot while the trigger is still held, so the red preview and the reason label are up, aimed at the denial.
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(left) : null;
        yield return ShotAt(
            "vr-6x-deny-" + expected,
            Affordance.Count > before ? Affordance.LastPosition
                : hand != null ? hand.position
                : HandsMid()
        );
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        // Released: the cue fades and the red preview goes, every renderer back to what the game set.
        yield return Wait(DenyFade.Out + 0.3f);
        if (cue == null)
        {
            foreach (var r in tinted)
            {
                if (!DenyTint.IsClean(r))
                {
                    cue = $"red tint left on {r.name} after release";
                    break;
                }
            }
        }
        // By id: a given item can be swapped for its reconnected copy (see vr.87).
        var after = HeldItem();
        string spent =
            held == null ? null
            : after == null || after.itemID != heldId ? $"the refused {held.name} left the hand"
            : UsesLeft(after) != usesBefore
                ? $"the refused use spent a use of {held.name} ({usesBefore} -> {UsesLeft(after)})"
            : null;
        var note =
            $"denials {Affordance.Count - before}, last {Affordance.LastReason} "
            + $"({(Affordance.LastLeft ? "left" : "right")}) at {Affordance.LastPosition}"
            + (cueNote != null ? "; " + cueNote : "");
        if (spent != null)
            Fail($"{spent}: {note}");
        else if (Affordance.Count == before)
            Fail($"no denial for {expected}: {note}");
        else if (!Got())
            Fail($"expected {expected} on the {(left ? "left" : "right")} hand: {note}");
        else if (cue != null)
            Fail($"{note}; {cue}");
        else
            stepNote = note;
    }

    /// <summary>The item's uses left (<c>DataEntryKey.ItemUses</c>, read by <c>CanUsePrimary</c>, Item.cs:797-813),
    /// or -1 when it has no count; for "a refused use spent nothing".</summary>
    static int UsesLeft(Item item)
    {
        var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        return uses.HasData ? uses.Value : -1;
    }

    /// <summary>Null when the red preview is up on every renderer it covers (the held item or a placement
    /// ghost); else why not.</summary>
    static string TintCheck(Renderer[] tinted)
    {
        if (tinted.Length == 0)
            return "no red preview while the attempt is held";
        int drawn = 0;
        for (int i = 0; i < tinted.Length; i++)
        {
            var r = tinted[i];
            // Only a renderer that draws counts: a red block on a hidden ghost or a stowed mesh shows nothing.
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (!DenyTint.IsTinted(r, i))
                return $"red preview missing on {r.name}";
            drawn++;
        }
        return drawn > 0 ? null : $"red preview on {tinted.Length} renderer(s), none drawn";
    }

    /// <summary>Null when the live refusal's ring is drawn, near the refused spot and big enough to read
    /// (<see cref="CueLayout.Readable"/>); else why not.</summary>
    static string CueCheck()
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
            return "no head";
        if (!AffordanceRuntime.DeniedRing(Affordance.LastLeft, out var center, out float radius))
            return "deny cue not drawn while the attempt is held";
        return CueLayout.Readable(
            rig.Head.position.N(),
            Affordance.LastPosition.N(),
            center.N(),
            radius,
            out var why
        )
            ? null
            : why;
    }
}
