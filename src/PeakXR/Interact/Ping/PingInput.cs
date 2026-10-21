using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>
/// Turns the finger-gun gesture or the fallback button into the game's ping press, and remembers which hand
/// fired so <see cref="PingPatches.PingHit"/> casts the ping ray from it. Only an empty hand fires the finger gun; a
/// hand holding or steadying an item sends its trigger to the item's use. The trigger squeeze that fires a
/// finger gun would also reach the game as a use press (interact.physical maps triggers to use), so that
/// hand's use input is held off until its trigger is released.
/// </summary>
internal static class PingInput
{
    private sealed class Hand
    {
        public readonly bool Left;
        public readonly AnalogButton Grip = new(0.6f, 0.4f);
        public readonly AnalogButton Trigger = new(0.6f, 0.4f);
        public readonly FingerGun Gun = new();
        public bool Pointing;
        public bool SuppressUse;

        public Hand(bool left) => Left = left;
    }

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly ButtonEdge Button = new();

    /// <summary>The hand the last ping press came from; the ping ray starts there.</summary>
    public static bool PingLeft { get; private set; }

    public static void Tick(Character c, CharacterInput input, bool playerMovementActive)
    {
        var cfg = PingConfig.Instance;
        float dt = Time.deltaTime;
        var handSetting = cfg.Hand;
        bool fingerGun = cfg.FingerGun && playerMovementActive;
        // A holding hand's grip and straight index read as pointing, so its use squeeze fired a ping and the use
        // was suppressed (vr14: a held torch never lit). Only an empty hand pings.
        bool holding = c.data.currentItem != null;
        bool leftFree = FingerGun.HandMayFire(
            holding && LocalHold.ItemInLeftHand,
            holding && LocalHold.TwoHand && !LocalHold.ItemInLeftHand
        );
        bool rightFree = FingerGun.HandMayFire(
            holding && !LocalHold.ItemInLeftHand,
            holding && LocalHold.TwoHand && LocalHold.ItemInLeftHand
        );

        bool fired = Update(LeftHand, fingerGun && leftFree && handSetting != PingHand.Right, dt);
        bool firedLeft = fired;
        if (Update(RightHand, fingerGun && rightFree && handSetting != PingHand.Left, dt) && !fired)
        {
            fired = true;
            firedLeft = false;
        }

        Button.Update(cfg.PingButtonDown(), dt);
        if (!fired && Button.Pressed && playerMovementActive)
        {
            fired = true;
            firedLeft = handSetting switch
            {
                PingHand.Left => true,
                PingHand.Right => false,
                _ => LeftHand.Pointing && !RightHand.Pointing,
            };
        }

        if (fired)
        {
            PingLeft = firedLeft;
            input.pingWasPressed = true;
        }

        // Use mapping of interact.physical: the holding hand's trigger is primary, the other secondary
        // (right is primary when empty-handed).
        bool primaryLeft = c.data.currentItem != null && LocalHold.ItemInLeftHand;
        Suppress(LeftHand, input, primaryLeft);
        Suppress(RightHand, input, !primaryLeft);
    }

    private static bool Update(Hand hand, bool enabled, float dt)
    {
        ref var state = ref VRInput.Hand(hand.Left);
        bool tracked = state.Tracked;
        hand.Grip.Update(tracked ? state.Grip : 0f, dt);
        hand.Trigger.Update(tracked ? state.Trigger : 0f, dt);
        hand.Pointing = tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(hand.Left));
        bool fired = hand.Gun.Update(
            enabled && hand.Pointing,
            enabled && hand.Grip.Held,
            enabled && hand.Trigger.Held,
            dt
        );
        if (fired)
            hand.SuppressUse = true;
        return fired;
    }

    private static void Suppress(Hand hand, CharacterInput input, bool primary)
    {
        if (!hand.SuppressUse)
            return;
        bool releaseFrame = !hand.Trigger.Held;
        if (primary)
        {
            input.usePrimaryWasPressed = input.usePrimaryIsPressed = false;
            if (releaseFrame)
                input.usePrimaryWasReleased = false;
        }
        else
        {
            input.useSecondaryWasPressed = input.useSecondaryIsPressed = false;
            if (releaseFrame)
                input.useSecondaryWasReleased = false;
        }
        if (releaseFrame)
            hand.SuppressUse = false;
    }
}
