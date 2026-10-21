using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// Body gestures for <c>player.locomotion</c>, written into the same <see cref="CharacterInput"/> fields as the
/// buttons: a real crouch holds <c>crouchIsPressed</c>, and brisk alternating arm swings hold <c>sprintIsPressed</c>
/// while the stick already walks forward (CharacterMovement.cs:241, :263). Vanilla peers see the game's crouch and
/// sprint. While a physical crouch lowers the body, <see cref="VRRig.CrouchLift"/> lifts the view by the torso's
/// drop, so the view stays at the real head instead of dropping twice.
/// </summary>
internal static class BodyGestures
{
    /// <summary>Largest view lift (metres): more than the crouch pose can lower the torso.</summary>
    private const float MaxLift = 0.8f;

    private static readonly PhysicalCrouch Crouch = new();
    private static readonly ArmSwing Swing = new();
    private static ConfigEntry<bool> physicalCrouch;
    private static ConfigEntry<bool> armSwingSprint;
    private static float standAbove;
    private static bool haveStand;
    private static float uprightFor;
    private static float lift;

    internal static bool CrouchOn => physicalCrouch?.Value ?? true;
    private static bool SwingOn => armSwingSprint?.Value ?? false;

    /// <summary>The arm-swing setting, so the self-test can switch it on for its step.</summary>
    internal static ConfigEntry<bool> ArmSwingSetting => armSwingSprint;

    public static void Bind(ConfigFile file)
    {
        const string s = "Locomotion";
        physicalCrouch = file.Bind(
            s,
            "PhysicalCrouch",
            true,
            "Crouch for real (head about a third lower than your calibrated standing height) to crouch in game. "
                + "Never in seated mode. The crouch button keeps working."
        );
        armSwingSprint = file.Bind(
            s,
            "ArmSwingSprint",
            false,
            "Swing your arms like running while the move stick walks forward to sprint. The stick still decides "
                + "where you go; off while climbing or holding an item with both hands."
        );
    }

    /// <param name="active">The game's <c>playerMovementActive</c>; false only keeps the view lift up to date.</param>
    /// <param name="stickForward">Move stick forward value this frame.</param>
    public static void Apply(
        Character c,
        CharacterInput input,
        bool active,
        float stickForward,
        float dt
    )
    {
        var rig = VRRig.Instance;
        var d = c.data;
        bool crouch = false;
        if (rig != null)
        {
            // Recenter calibrates this eye height (RigDriver.CalibrateHeight); without a floor it starts at the head.
            float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
            float standing = XRRuntimeInfo.FloorTracking
                ? eye
                : HeightCalibration.DefaultStandingEyeHeight;
            crouch = Crouch.Update(
                CrouchOn && !rig.SeatedMode && VRInput.HeadTracked,
                VRInput.HeadPosition.y,
                eye,
                standing
            );
            UpdateLift(c, rig, dt);
        }

        bool swing =
            SwingOn
            && Swing.Update(
                VRInput.Left.Tracked,
                (VRInput.Left.Position - VRInput.HeadPosition).N(),
                VRInput.Right.Tracked,
                (VRInput.Right.Position - VRInput.HeadPosition).N(),
                dt
            );
        if (!SwingOn)
            Swing.Reset();

        if (!active)
            return;
        if (crouch)
            input.crouchIsPressed = true;
        if (
            swing
            && stickForward > 0.3f
            && d.isGrounded
            && !d.isClimbingAnything
            && !LocalHold.TwoHand
        )
            input.sprintIsPressed = true;
    }

    /// <summary>
    /// The torso's drop below its learned upright height above the ground (<c>CharacterData.groundPos</c>, written
    /// by CharacterMovement.cs:757) while a physical crouch holds; after it ends, the lift only shrinks as the torso
    /// rises, and in the air it fades out.
    /// </summary>
    private static void UpdateLift(Character c, VRRig rig, float dt)
    {
        var d = c.data;
        // The hip, like the view anchor (RigDriver.ViewRoot): a lean lowers the torso a little and is not a crouch.
        float above = RigDriver.ViewRoot(c).y - d.groundPos.y;
        bool upright =
            d.isGrounded
            && d.fullyConscious
            && !d.isCrouching
            && !d.isClimbingAnything
            && d.currentRagdollControll >= 0.9f
            && above > 0.2f
            && above < 2f;
        // Learn only once the torso has been upright for a while: right after a crouch it is still rising, and a
        // sample from then would pull the standing height down.
        uprightFor = upright && !Crouch.Crouched ? uprightFor + dt : 0f;
        if (uprightFor > 0.5f)
        {
            standAbove = haveStand ? Mathf.Lerp(standAbove, above, 1f - Mathf.Exp(-dt)) : above;
            haveStand = true;
        }

        // In the air, or crouched by the button or toggle after the physical crouch ended: the game's own crouch
        // should lower the view again, so the lift fades out instead of holding.
        if (!haveStand || !d.isGrounded || (!Crouch.Crouched && d.isCrouching))
            lift *= Mathf.Exp(-6f * dt);
        else
        {
            float drop = Mathf.Clamp(standAbove - above, 0f, MaxLift);
            lift = Crouch.Crouched ? drop : Mathf.Min(lift, drop);
        }
        if (lift < 0.005f)
            lift = 0f;
        rig.CrouchLift = lift;
    }

    /// <summary>The game stopped sampling input (scene change, no character): nothing stays crouched or lifted.</summary>
    public static void Reset()
    {
        Crouch.Update(false, 0f, 0f, 0f);
        Swing.Reset();
        haveStand = false;
        uprightFor = 0f;
        lift = 0f;
        if (VRRig.Instance != null)
            VRRig.Instance.CrouchLift = 0f;
    }
}
