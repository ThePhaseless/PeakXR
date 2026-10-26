using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>
/// Turns the finger-gun gesture or the fallback button into the game's ping press, and remembers which hand
/// fired so <see cref="PingPatches.PingHit"/> casts the ping ray from it. Only an empty hand fires the finger gun; a
/// hand holding or steadying an item sends its trigger to the item's use. The trigger squeeze that fires a
/// finger gun would also reach the game as a use press (interact.physical maps triggers to use), so that
/// hand's use input is held off until its trigger is released. A press the game would drop silently (passed out, in
/// the cooldown) gets a refusal cue at the hand; a holding hand's finger gun whose squeeze did nothing points the
/// player at the free hand.
/// </summary>
internal static class PingInput
{
    private sealed class Hand
    {
        public readonly bool Left;
        public readonly AnalogButton Grip = new(0.6f, 0.4f);
        public readonly AnalogButton Trigger = new(0.6f, 0.4f);
        public readonly FingerGun Gun = new();

        /// <summary>The same gesture on a hand that holds an item: only to tell the player to use the free hand.</summary>
        public readonly FingerGun HeldGun = new();
        public bool Pointing;
        public bool SuppressUse;

        /// <summary>The finger gun may fire from this hand this frame (empty hand, setting, input active).</summary>
        public bool Armed;

        public Hand(bool left) => Left = left;
    }

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly ButtonEdge Button = new();
    private static readonly PingButtonPreview ButtonPreview = new();

    /// <summary>Trigger travel that counts as the finger on its way to the squeeze, not a hand that let go.</summary>
    private const float PartialPull = 0.05f;

    /// <summary>The layout's long press (the emote wheel shares the ping button), the same threshold as
    /// player.locomotion's binding table.</summary>
    private static readonly float LongPressSeconds = new ActionMapper().LongPressSeconds;

    private static float buttonHeldFor;
    private static bool buttonAiming,
        buttonAimLeft;
    private static int buttonFrame = -10;

    /// <summary>The private <c>PointPinger._timeLastPinged</c> (PointPinger.cs:18), resolved by <see cref="PingFeature"/>;
    /// the cooldown itself is the public <c>coolDown</c> (:10), a prefab value.</summary>
    internal static HarmonyLib.AccessTools.FieldRef<PointPinger, float> LastPinged;

    private static PointPinger pinger;
    private static Character pingerOwner;

    /// <summary>The hand the last ping press came from; the ping ray starts there.</summary>
    public static bool PingLeft { get; private set; }

    /// <summary>This hand holds the finger-gun pose with the grip closed and may fire: the aim preview shows.</summary>
    public static bool Aiming(bool left)
    {
        var h = left ? LeftHand : RightHand;
        return h.Armed && h.Gun.Aiming;
    }

    /// <summary>The thumb rests on or presses the layout's ping button (not yet the emote long press): the aim
    /// preview shows from <paramref name="left"/>, the hand a tap would ping from.</summary>
    public static bool ButtonAiming(out bool left)
    {
        left = buttonAimLeft;
        return buttonAiming && Time.frameCount - buttonFrame <= 1;
    }

    /// <summary>Thumb on the button that carries the table's Ping (BindingTable.Default), like a finger resting on a
    /// laser pointer's switch: the preview shows before the tap.</summary>
    private static void TickButtonPreview(Character c, bool active, float dt)
    {
        buttonFrame = Time.frameCount;
        bool touched = false,
            held = false;
        // The thumb is read even while input is blocked: the long press opens the emote wheel, which blocks input, and
        // reading those frames as "thumb off" cleared the long press, so the preview came back on release (vr34).
        if (
            ControllerLabels.ForAction(
                VRAction.Ping,
                VRInput.LeftHanded,
                LocalHold.ItemInLeftHand,
                out bool onLeft,
                out var control
            )
        )
        {
            ref var state = ref VRInput.Hand(onLeft);
            if (state.Tracked)
            {
                switch (control)
                {
                    case VRControl.Primary:
                        touched = state.PrimaryTouch;
                        held = state.Primary;
                        break;
                    case VRControl.Secondary:
                        touched = state.SecondaryTouch;
                        held = state.Secondary;
                        break;
                    case VRControl.StickClick:
                        held = state.StickClick;
                        break;
                }
            }
        }
        buttonHeldFor = held ? buttonHeldFor + dt : 0f;
        buttonAiming = ButtonPreview.Update(
            touched,
            held,
            buttonHeldFor,
            LongPressSeconds,
            active && c.data.fullyConscious
        );
        buttonAimLeft = PingRules.ButtonHandLeft(
            PingConfig.Instance.Hand,
            LeftHand.Pointing,
            RightHand.Pointing,
            VRInput.LeftHanded
        );
    }

    public static void Tick(Character c, CharacterInput input, bool playerMovementActive)
    {
        var cfg = PingConfig.Instance;
        float dt = Time.deltaTime;
        var handSetting = cfg.Hand;
        bool fingerGun = cfg.FingerGun && playerMovementActive;
        // A holding hand's grip and straight index read as pointing, so its use squeeze fired a ping and the use
        // was suppressed (vr14: a held torch never lit). Only an empty hand pings.
        var item = c.data.currentItem;
        bool holding = item != null;
        bool leftFree = FingerGun.HandMayFire(
            holding && LocalHold.ItemInLeftHand,
            holding && LocalHold.TwoHand && !LocalHold.ItemInLeftHand
        );
        bool rightFree = FingerGun.HandMayFire(
            holding && !LocalHold.ItemInLeftHand,
            holding && LocalHold.TwoHand && LocalHold.ItemInLeftHand
        );

        bool fired = Update(
            LeftHand,
            fingerGun && handSetting != PingAimHand.Right,
            leftFree,
            item,
            rightFree,
            dt
        );
        bool firedLeft = fired;
        if (
            Update(
                RightHand,
                fingerGun && handSetting != PingAimHand.Left,
                rightFree,
                item,
                leftFree,
                dt
            ) && !fired
        )
        {
            fired = true;
            firedLeft = false;
        }

        TickButtonPreview(c, playerMovementActive, Time.unscaledDeltaTime);
        Button.Update(cfg.PingButtonDown(), dt);
        if (fired)
            PingLeft = firedLeft;
        // The fallback button, or the layout's ping tap that player.locomotion already wrote (LocomotionInput.Apply).
        else if (input.pingWasPressed || (Button.Pressed && playerMovementActive))
            PingLeft = PingRules.ButtonHandLeft(
                handSetting,
                LeftHand.Pointing,
                RightHand.Pointing,
                VRInput.LeftHanded
            );

        if (fired || (Button.Pressed && playerMovementActive))
            input.pingWasPressed = true;
        if (input.pingWasPressed)
            CheckPress(c);

        // Use mapping of interact.physical: the holding hand's trigger is primary, the other secondary
        // (right is primary when empty-handed).
        bool primaryLeft = holding && LocalHold.ItemInLeftHand;
        Suppress(LeftHand, input, primaryLeft);
        Suppress(RightHand, input, !primaryLeft);
    }

    /// <summary>The press PointPinger.Update would drop (PointPinger.cs:75-81, canPing :38-48): passed out or still
    /// in the cooldown. The sky is refused where the ray is cast (<see cref="PingPatches.PingHit"/>).</summary>
    private static void CheckPress(Character c)
    {
        if (pingerOwner != c)
        {
            pingerOwner = c;
            pinger = c.GetComponent<PointPinger>();
        }
        if (pinger == null || LastPinged == null)
            return;
        var verdict = PingRules.Press(
            c.data.fullyConscious,
            Time.time,
            LastPinged(pinger),
            pinger.coolDown
        );
        if (verdict == PingVerdict.TooSoon)
            Affordance.DenyAtHand(PingLeft, DenyReason.TooSoon);
        else if (verdict == PingVerdict.NotNow)
            Affordance.DenyAtHand(PingLeft, DenyReason.NotNow);
    }

    private static bool Update(
        Hand hand,
        bool enabled,
        bool free,
        Item item,
        bool otherFree,
        float dt
    )
    {
        ref var state = ref VRInput.Hand(hand.Left);
        bool tracked = state.Tracked;
        hand.Grip.Update(tracked ? state.Grip : 0f, dt);
        hand.Trigger.Update(tracked ? state.Trigger : 0f, dt);
        // The finger gun needs the thumb up (GripIntent.IsFingerGun): pointing with the thumb resting is the
        // pointed interact's, so the two never take each other's grip (external review #13).
        hand.Pointing = tracked && GripIntent.IsFingerGun(LocalAvatar.ControllerCurls(hand.Left));
        hand.Armed = enabled && free;
        bool fired = hand.Gun.Update(
            hand.Armed && hand.Pointing,
            hand.Armed && hand.Grip.Held,
            hand.Armed && hand.Trigger.Held,
            dt,
            // A light pull curls the index off "pointing"; it is the squeeze on its way, so the aim stays up.
            hand.Armed
                && tracked
                && state.Trigger > PartialPull
        );
        if (fired)
            hand.SuppressUse = true;

        bool holds = enabled && !free;
        bool heldGesture = hand.HeldGun.Update(
            holds && hand.Pointing,
            holds && hand.Grip.Held,
            holds && hand.Trigger.Held,
            dt
        );
        // GUIManager.cs:1179 shows the use prompt only for these: otherwise the squeeze did nothing.
        bool itemHasUse = item != null && item.UIData.hasMainInteract && item.CanUsePrimary();
        if (PingRules.UseFreeHand(heldGesture, itemHasUse, otherFree))
            InformFreeHand(!hand.Left);
        return fired;
    }

    /// <summary>The free hand gets a notice: this hand is full, ping with the other one.</summary>
    private static void InformFreeHand(bool freeLeft)
    {
        var rig = VRRig.Instance;
        var free = rig != null ? rig.Hand(freeLeft) : null;
        if (free != null)
            Affordance.Inform(freeLeft, free.position, DenyReason.UseOtherHand, DenyHint.On(free));
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
