using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// Body gestures for <c>player.locomotion</c>, written into the same <see cref="CharacterInput"/> fields as the
/// buttons: a real crouch holds <c>crouchIsPressed</c>, and brisk alternating arm swings hold <c>sprintIsPressed</c>
/// while the stick already walks forward (CharacterMovement.cs:241, :263). Vanilla peers see the game's crouch and
/// sprint. While a physical crouch lowers the body, <see cref="VRRig.CrouchLift"/> lifts the view by the torso's
/// drop, so the view stays at the real head instead of dropping twice. An opt-in hop (<see cref="JumpGesture"/>)
/// presses <c>jumpWasPressed</c> once, like the jump button (CharacterMovement.cs:204).
/// </summary>
internal static class BodyGestures
{
    /// <summary>Largest view lift (metres): more than the crouch pose can lower the torso.</summary>
    private const float MaxLift = 0.8f;

    private static readonly PhysicalCrouch Crouch = new();
    private static readonly ArmSwing Swing = new();
    private static readonly JumpGesture Hop = new();
    private static readonly LongCrouchHint LongCrouch = new();
    private static ConfigEntry<bool> physicalCrouch;
    private static ConfigEntry<float> crouchDepth;
    private static ConfigEntry<bool> armSwingSprint;
    private static ConfigEntry<bool> hopToJump;
    private static ConfigEntry<bool> heartbeat;
    private static ConfigEntry<bool> shiver;
    private static ConfigEntry<bool> heat;
    private static ConfigEntry<bool> hazard;
    private static ConfigEntry<bool> wallTouch;

    /// <summary>Body feels in <see cref="BodyFeedback"/>, each its own switch (all also need Interaction.Haptics).</summary>
    internal static bool HeartbeatOn => heartbeat?.Value ?? true;

    internal static bool ShiverOn => shiver?.Value ?? true;
    internal static bool HeatOn => heat?.Value ?? true;
    internal static bool HazardOn => hazard?.Value ?? true;
    internal static bool WallTouchOn => wallTouch?.Value ?? true;
    private static float standAbove;
    private static bool haveStand;
    private static float uprightFor;
    private static float lift;

    internal static bool CrouchOn => physicalCrouch?.Value ?? true;
    private static bool SwingOn => armSwingSprint?.Value ?? true;
    private static bool HopOn => hopToJump?.Value ?? false;

    /// <summary>The arm-swing setting, so the self-test can switch it on for its step.</summary>
    internal static ConfigEntry<bool> ArmSwingSetting => armSwingSprint;

    /// <summary>The hop setting, so the self-test can switch it on for its step.</summary>
    internal static ConfigEntry<bool> HopSetting => hopToJump;

    /// <summary>The arm-swing sprint holds this frame (self-test).</summary>
    internal static bool Swinging => Swing.Active;

    /// <summary>Long crouch-walk notices shown (self-test).</summary>
    internal static int LongCrouchHints { get; private set; }

    /// <summary>The long-crouch rule, so the self-test can shorten its wait.</summary>
    internal static LongCrouchHint LongCrouchRule => LongCrouch;

    /// <summary>Frame of the last recognised hop, so the jump cue knows the press came from the body.</summary>
    internal static int HopFrame { get; private set; } = -10;

    public static void Bind(ConfigFile file)
    {
        const string s = "Locomotion";
        physicalCrouch = file.Bind(
            s,
            "PhysicalCrouch",
            true,
            "Crouch for real (head lower than your calibrated standing height by Locomotion.CrouchDepth) to crouch "
                + "in game. Never in seated mode. The crouch button keeps working."
        );
        crouchDepth = file.Bind(
            s,
            "CrouchDepth",
            PhysicalCrouch.DefaultDepth,
            new ConfigDescription(
                "How far your head drops (share of your standing height) before your character crouches; the "
                    + "default crouches it as you stoop to the ground, so its hands reach where yours are.",
                new AcceptableValueRange<float>(0.1f, 0.45f)
            )
        );
        armSwingSprint = file.Bind(
            s,
            "ArmSwingSprint",
            true,
            "Swing your arms like running while the move stick walks forward to sprint. The stick still decides "
                + "where you go. Only a running swing below the chest counts, and only while walking: gestures "
                + "made standing still never sprint. Off while climbing or holding an item with both hands."
        );
        // D1: on by default now; a saved false that was never chosen moves once.
        Framework.ConfigMigration.FromOldDefault(armSwingSprint, false);
        hopToJump = file.Bind(
            s,
            "HopToJump",
            false,
            "Swing both arms up and hop (head rising above your standing height) to jump. Standing up from a "
                + "crouch, looking down and running arms do not jump. Off by default: a false jump near a cliff "
                + "can kill. The jump button keeps working."
        );
        heartbeat = file.Bind(
            s,
            "Heartbeat",
            true,
            "Feel a heartbeat in both hands while you spend stamina low on the bar, slower and heavier when weighed down."
        );
        shiver = file.Bind(
            s,
            "Shiver",
            true,
            "Feel a fine shivering rumble in both hands while you are getting colder, stronger the faster the cold "
                + "(night, fog, snowstorm) gets to you."
        );
        heat = file.Bind(
            s,
            "Heat",
            true,
            "Feel a slow swelling rumble in both hands while you are getting hotter, stronger the faster the heat "
                + "(desert sun, lava) gets to you. A parasol or sunscreen that keeps the sun off keeps it quiet."
        );
        hazard = file.Bind(
            s,
            "HazardZones",
            true,
            "Feel a throbbing rumble in both hands while a hazard zone, such as a spore cloud, is affecting you, "
                + "stronger the faster it gets to you."
        );
        wallTouch = file.Bind(
            s,
            "WallTouch",
            true,
            "Feel a soft tick in the hand on that side when your head comes within 30 cm of rock."
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
            Crouch.SetDepth(crouchDepth?.Value ?? PhysicalCrouch.DefaultDepth);
            crouch = Crouch.Update(
                CrouchOn && !rig.SeatedMode && VRInput.HeadTracked,
                VRInput.HeadPosition.y,
                eye,
                standing
            );
            UpdateLift(c, rig, dt);
        }

        bool walking = active && stickForward > 0.3f && !d.isClimbingAnything && !LocalHold.TwoHand;
        var side = VRInput.HeadRotation * Vector3.right;
        side.y = 0f;
        bool swing =
            SwingOn
            && Swing.Update(
                VRInput.Left.Tracked,
                (VRInput.Left.Position - VRInput.HeadPosition).N(),
                VRInput.Right.Tracked,
                (VRInput.Right.Position - VRInput.HeadPosition).N(),
                side.normalized.N(),
                walking,
                // A sprint jump, or a stride that leaves the ground for a frame, keeps the sprint (Core ArmSwing).
                !d.isGrounded,
                dt
            );
        if (!SwingOn)
            Swing.Reset();
        bool hop = UpdateHop(c, rig, active, crouch, swing, dt);

        if (!active)
            return;
        var v = d.avarageVelocity;
        if (LongCrouch.Update(crouch, v.x * v.x + v.z * v.z > 0.25f, dt))
            ShowLongCrouchHint(rig);
        if (crouch)
            input.crouchIsPressed = true;
        if (swing)
            input.sprintIsPressed = true;
        if (hop)
        {
            input.jumpWasPressed = true;
            HopFrame = Time.frameCount;
        }
    }

    /// <summary>Sat down mid-run and forgot: a notice at the wrist says the body is still crouched and how to get
    /// the standing height back (stand, or recenter). Once per crouch episode, so the string is fine.</summary>
    private static void ShowLongCrouchHint(VRRig rig)
    {
        LongCrouchHints++;
        Plugin.Log.LogInfo(
            "[player.locomotion] long physical crouch while moving: recenter hint shown"
        );
        bool left = VRInput.OffHandLeft;
        var hand = rig.Hand(left);
        if (hand != null)
            Affordance.Inform(
                left,
                hand.position,
                DenyReason.HappenedHere,
                DenyHint.Wrist(),
                // One-handed play has one stick: Menu where it reaches the game, else the VR tab's Recenter button.
                VRInput.OneHanded
                    ? System.FormattableString.Invariant(
                        $"Still crouched: stand up, or recenter: hold Menu for {CameraFeature.RecenterSeconds:0.#} s, or Recenter in the VR tab"
                    )
                    : System.FormattableString.Invariant(
                        $"Still crouched: stand up, or hold both sticks in for {CameraFeature.RecenterSeconds:0.#} s to recenter"
                    )
            );
    }

    /// <summary>The opt-in hop: standing play only, on the ground, head and both hands tracked.</summary>
    private static bool UpdateHop(
        Character c,
        VRRig rig,
        bool active,
        bool crouched,
        bool swinging,
        float dt
    )
    {
        var d = c.data;
        bool on =
            HopOn
            && active
            && rig != null
            && !rig.SeatedMode
            && VRInput.HeadTracked
            && VRInput.Left.Tracked
            && VRInput.Right.Tracked
            && d.isGrounded
            && !d.isClimbingAnything;
        var fwd = VRInput.HeadRotation * Vector3.forward;
        float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        return Hop.Update(
            on,
            VRInput.HeadPosition.y,
            on ? RigMath.TrackedEyeHeight(rig.HeightOffset) : 0f,
            pitch,
            VRInput.Left.Position.y,
            VRInput.Right.Position.y,
            crouched,
            swinging,
            dt
        );
    }

    /// <summary>
    /// The hip's drop (the view root's height, <see cref="RigDriver.ViewRoot"/>) below its learned upright height above the ground (<c>CharacterData.groundPos</c>, written
    /// by CharacterMovement.cs:752) while a physical crouch holds; after it ends, the lift only shrinks as the torso
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
            standAbove = haveStand
                ? Mathf.Lerp(standAbove, above, ExpSmooth.Factor(1f, dt))
                : above;
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
        Hop.Reset();
        LongCrouch.Update(false, false, 0f);
        haveStand = false;
        uprightFor = 0f;
        lift = 0f;
        if (VRRig.Instance != null)
            VRRig.Instance.CrouchLift = 0f;
    }
}
