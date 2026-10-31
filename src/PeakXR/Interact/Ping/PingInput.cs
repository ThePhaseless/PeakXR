using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>
/// The ping is a button only: the layout's tap (off-hand B/Y, written by player.locomotion) or the optional
/// <c>Interaction.Ping.PingButton</c>. This remembers which hand a press aims from so <see cref="PingPatches.PingHit"/>
/// casts the ping ray from it, shows the aim preview while the thumb rests on the button, and gives a press the game
/// would drop silently (passed out, in the cooldown) a refusal cue at the hand. The trigger never pings, so it stays the
/// held item's use.
/// </summary>
internal static class PingInput
{
    private static readonly ButtonEdge Button = new();
    private static readonly PingButtonPreview ButtonPreview = new();

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
        buttonAimLeft = AimLeft(PingConfig.Instance.Hand);
    }

    /// <summary>The hand a button ping aims from (<see cref="PingRules.ButtonHandLeft"/>): a hand in the ping-aim point
    /// (grip open, so it never interacts with what it points at) wins over the button's own hand.</summary>
    private static bool AimLeft(PingAimHand setting) =>
        PingRules.ButtonHandLeft(setting, AimPoint(true), AimPoint(false), VRInput.LeftHanded);

    private static bool AimPoint(bool left)
    {
        ref var state = ref VRInput.Hand(left);
        return state.Tracked
            && GripIntent.IsPingAimPoint(LocalAvatar.ControllerCurls(left), state.Grip);
    }

    public static void Tick(Character c, CharacterInput input, bool playerMovementActive)
    {
        var cfg = PingConfig.Instance;
        TickButtonPreview(c, playerMovementActive, Time.unscaledDeltaTime);
        Button.Update(cfg.PingButtonDown(), Time.deltaTime);
        // The optional button, or the layout's ping tap that player.locomotion already wrote (LocomotionInput.Apply).
        if (Button.Pressed && playerMovementActive)
            input.pingWasPressed = true;
        if (!input.pingWasPressed)
            return;
        PingLeft = AimLeft(cfg.Hand);
        CheckPress(c);
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
}
