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
/// The <c>vr.6x</c> steps: ui.affordance. Scripted hands try things the game refuses and each step proves that
/// <see cref="Affordance.Deny"/> fired with the right reason (<see cref="Affordance.LastReason"/>), not just that the
/// action failed.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator AffordanceSteps()
    {
        yield return HandStep("vr.60-deny-piton-range", 20f, DenyPitonStep());
        yield return HandStep("vr.61-deny-two-hands", 20f, DenyTwoHandStep());
        yield return HandStep("vr.62-deny-unusable", 20f, DenyUnusableStep());
        yield return HandStep("vr.63-sticky-throw", 20f, StickyThrowStep());
    }

    /// <summary>vr.60: a held climbing spike aimed at the open sky. The trigger press reaches
    /// <c>RaycastClimbingSpikeStart</c> (CharacterItems.cs:1072-1076), which finds no terrain in range, so no cast
    /// starts; SpikeCue denies it as <see cref="DenyReason.NoWall"/>.</summary>
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
        yield return PressAndExpect(left, DenyReason.NoWall);
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
    /// the success tick.</summary>
    IEnumerator DenyUnusableStep()
    {
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var usability = GameMember.Field<Item, Optionable<bool>>("overrideUsability");
        usability(HeldItem()) = Optionable<bool>.Some(false);
        yield return PressAndExpect(LocalHold.ItemInLeftHand, DenyReason.CantUse);
        if (HeldItem() != null)
            usability(HeldItem()) = Optionable<bool>.None;
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
        yield return GiveAndHold(sticky.GetComponent<Item>());
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out var right);
        var windup = head + (left ? -right : right) * 0.25f + Vector3.down * 0.15f;
        yield return MoveTrack(left, windup, 0.4f);
        yield return Wait(0.3f);
        yield return SwingRelease(left, windup, windup + forward * 0.9f + Vector3.up * 0.15f);
        yield return Until(
            () => HeldItem() == null,
            1.5f,
            "sticky item still in hand after the swing"
        );
        if (stepOk)
            stepNote = $"released (charge requirement {sticky.throwChargeRequirement:0.00})";
    }

    /// <summary>Presses the trigger of <paramref name="left"/> and waits up to 1 s for a denial with
    /// <paramref name="expected"/> on that hand; other denials meanwhile do not end the wait.</summary>
    IEnumerator PressAndExpect(bool left, DenyReason expected)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "UI.ActionHints off or ui.affordance not installed";
            yield break;
        }
        Affordance.ResetGate();
        int before = Affordance.Count;
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        float end = Now + 1f;
        bool Got() =>
            Affordance.Count > before
            && Affordance.LastReason == expected
            && Affordance.LastLeft == left;
        while (Now < end && !Got())
            yield return null;
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
        var note =
            $"denials {Affordance.Count - before}, last {Affordance.LastReason} "
            + $"({(Affordance.LastLeft ? "left" : "right")}) at {Affordance.LastPosition}";
        if (Affordance.Count == before)
            Fail($"no denial for {expected}: {note}");
        else if (!Got())
            Fail($"expected {expected} on the {(left ? "left" : "right")} hand: {note}");
        else if (cue != null)
            Fail($"{note}; {cue}");
        else
            stepNote = note;
    }

    /// <summary>Null when the red preview is up on every renderer it covers (the held item or a placement
    /// ghost); else why not.</summary>
    static string TintCheck(Renderer[] tinted)
    {
        if (tinted.Length == 0)
            return "no red preview while the attempt is held";
        for (int i = 0; i < tinted.Length; i++)
        {
            if (tinted[i] != null && !DenyTint.IsTinted(tinted[i], i))
                return $"red preview missing on {tinted[i].name}";
        }
        return null;
    }

    /// <summary>Null when the live refusal's ring is drawn, near the refused spot and big enough to read
    /// (<see cref="CueLayout.Readable"/>); else why not.</summary>
    static string CueCheck()
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
            return "no head";
        if (!AffordanceRuntime.DeniedRing(out var center, out float radius))
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
