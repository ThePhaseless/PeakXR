using BepInEx.Configuration;

namespace PeakXR.UI.RunFlow;

/// <summary>How the forced wake-up on the beach at run start is shown in VR.</summary>
internal enum BeachWakeupMode
{
    /// <summary>Stay black while the character lies on the sand, then fade in. No eyelid overlay.</summary>
    Fade,

    /// <summary>Skip the 7 s fall entirely, like the game's own SkipWakeup tag. Local only.</summary>
    Skip,

    /// <summary>Unchanged game behaviour, eyelid overlay included.</summary>
    Vanilla,
}

/// <summary>Config entries for scripted run transitions. Defaults favour comfort: every forced
/// transition is hidden behind a fade.</summary>
internal sealed class RunFlowSettings
{
    private const string Section = "UI.RunFlow";

    public ConfigEntry<bool> FadeOnWarp;
    public ConfigEntry<bool> FadeOnSegmentChange;
    public ConfigEntry<bool> FadeOnRunEnd;
    public ConfigEntry<bool> CutsceneScreen;
    public ConfigEntry<float> CutsceneScreenWidth;
    public ConfigEntry<bool> BlackDuringCutscene;
    public ConfigEntry<bool> PortalFade;
    public ConfigEntry<BeachWakeupMode> BeachWakeup;
    public ConfigEntry<float> ShakeHaptics;
    public ConfigEntry<float> EndScreenWidth;
    public ConfigEntry<float> EndScreenDistance;

    public static RunFlowSettings Bind(ConfigFile cfg) =>
        new()
        {
            FadeOnWarp = cfg.Bind(
                Section,
                "FadeOnWarp",
                true,
                "Blink to black when the game teleports you (warp items, respawns, segment jumps), then fade back in."
            ),
            FadeOnSegmentChange = cfg.Bind(
                Section,
                "FadeOnSegmentChange",
                true,
                "Fade out briefly while a lit campfire swaps in the next part of the mountain, hiding the loading hitch."
            ),
            FadeOnRunEnd = cfg.Bind(
                Section,
                "FadeOnRunEnd",
                true,
                "Go black when the run ends and fade in on the end screen or cutscene."
            ),
            CutsceneScreen = cfg.Bind(
                Section,
                "CutsceneScreen",
                true,
                "Show the end cutscene and credits on a large screen in front of you instead of moving your view."
            ),
            CutsceneScreenWidth = cfg.Bind(
                Section,
                "CutsceneScreenWidth",
                2.4f,
                new ConfigDescription(
                    "Width of the cutscene screen, 3 m in front of you, in metres.",
                    new AcceptableValueRange<float>(1.2f, 4f)
                )
            ),
            BlackDuringCutscene = cfg.Bind(
                Section,
                "BlackDuringCutscene",
                true,
                "When the cutscene screen is off or no cutscene camera is found, stay black while the end cutscene and credits play (audio keeps playing). Off: stay where you are."
            ),
            PortalFade = cfg.Bind(
                Section,
                "PortalFade",
                true,
                "Replace the summit portal's screen warp with a fade to black that follows your hold."
            ),
            BeachWakeup = cfg.Bind(
                Section,
                "BeachWakeup",
                BeachWakeupMode.Fade,
                "Run start wake-up on the beach: Fade = the loading room stays up while you lie on the sand (black without it), then fade in; Skip = no fall; Vanilla = game eyelids."
            ),
            ShakeHaptics = cfg.Bind(
                Section,
                "ShakeHaptics",
                0.5f,
                new ConfigDescription(
                    "Controller rumble strength for big world events that shake the camera in flat play (rising lava, freed soul, portal). 0 = off.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            ),
            EndScreenWidth = cfg.Bind(
                Section,
                "EndScreenWidth",
                1.6f,
                new ConfigDescription(
                    "Width of the end-of-run report panel, in metres.",
                    new AcceptableValueRange<float>(1f, 2.5f)
                )
            ),
            EndScreenDistance = cfg.Bind(
                Section,
                "EndScreenDistance",
                1.8f,
                new ConfigDescription(
                    "Distance of the end-of-run report panel, in metres.",
                    new AcceptableValueRange<float>(1f, 3f)
                )
            ),
        };
}
