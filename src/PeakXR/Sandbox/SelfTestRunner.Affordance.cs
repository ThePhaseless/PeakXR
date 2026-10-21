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
        // Straight up from the shoulder: nothing to hit on the beach.
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.25f + Vector3.up * 0.2f);
        Script(left).Rotation = Quaternion.LookRotation(Vector3.up, -forward);
        yield return Wait(0.3f);
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

    /// <summary>Presses the trigger of <paramref name="left"/> and waits up to 1 s for a denial with
    /// <paramref name="expected"/>.</summary>
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
        while (Now < end && Affordance.Count == before)
            yield return null;
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        var note =
            $"denials {Affordance.Count - before}, last {Affordance.LastReason} "
            + $"({(Affordance.LastLeft ? "left" : "right")}) at {Affordance.LastPosition}";
        if (Affordance.Count == before)
            Fail($"no denial for {expected}: {note}");
        else if (Affordance.LastReason != expected || Affordance.LastLeft != left)
            Fail($"expected {expected} on the {(left ? "left" : "right")} hand: {note}");
        else
            stepNote = note;
        yield return Shot("vr-6x-deny-" + expected);
    }
}
