using BepInEx.Configuration;

namespace PeakXR.Locomotion;

/// <summary>The <c>Locomotion</c> settings of <see cref="LocomotionFeature"/>.</summary>
internal static class LocomotionConfig
{
    private static ConfigEntry<string> moveRelativeTo;
    private static ConfigEntry<string> sprintMode;
    private static ConfigEntry<string> crouchMode;
    private static ConfigEntry<float> deadzone;
    private static ConfigEntry<bool> roomScale;
    private static ConfigEntry<float> leanRadius;

    public static bool HandRelative => moveRelativeTo?.Value == "Hand";
    public static bool SprintToggle => (sprintMode?.Value ?? "Toggle") == "Toggle";
    public static bool CrouchToggle => (crouchMode?.Value ?? "Toggle") == "Toggle";
    public static float Deadzone => deadzone?.Value ?? 0.15f;
    public static bool RoomScale => roomScale?.Value ?? true;
    public static float LeanRadius => leanRadius?.Value ?? 0.35f;

    /// <summary>The room-scale setting, so the self-test can hold the body still for its blocked step.</summary>
    internal static ConfigEntry<bool> RoomScaleSetting => roomScale;

    public static void Bind(ConfigFile file)
    {
        const string s = "Locomotion";
        moveRelativeTo = file.Bind(
            s,
            "MoveRelativeTo",
            "Head",
            new ConfigDescription(
                "Pushing the move stick forward walks where you look (Head) or where the move hand points (Hand).",
                new AcceptableValueList<string>("Head", "Hand")
            )
        );
        sprintMode = file.Bind(
            s,
            "SprintMode",
            "Toggle",
            new ConfigDescription(
                "Click the move stick once to sprint until you stop (Toggle), or hold it down (Hold).",
                new AcceptableValueList<string>("Toggle", "Hold")
            )
        );
        crouchMode = file.Bind(
            s,
            "CrouchMode",
            "Toggle",
            new ConfigDescription(
                "Click the turn stick once to crouch until you click again or jump (Toggle), or hold it down (Hold).",
                new AcceptableValueList<string>("Toggle", "Hold")
            )
        );
        deadzone = file.Bind(
            s,
            "StickDeadzone",
            0.15f,
            new ConfigDescription(
                "Move stick travel ignored around the centre (stick drift).",
                new AcceptableValueRange<float>(0.05f, 0.4f)
            )
        );
        roomScale = file.Bind(
            s,
            "RoomScaleWalking",
            true,
            "Walking around your play space walks your character after you, without moving your view. Off: your "
                + "view can drift from your character; past 1 m it fades and re-centres on the character."
        );
        // A new key, not LeanDistance: its saved 0.2 m walked the character on every lean and back on standing up.
        leanRadius = file.Bind(
            s,
            "LeanLimit",
            0.35f,
            new ConfigDescription(
                "How far (metres) you can lean before your character steps after your head. Inside it the "
                    + "character's upper body leans with you and its feet stay put.",
                new AcceptableValueRange<float>(0.15f, 0.6f)
            )
        );
    }
}
