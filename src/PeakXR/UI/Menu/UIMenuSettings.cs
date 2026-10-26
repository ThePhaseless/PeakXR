using BepInEx.Configuration;

namespace PeakXR.UI.Menu;

/// <summary>Config entries for the title stage and the loading space.</summary>
internal sealed class UIMenuSettings
{
    public ConfigEntry<bool> TitleOnBeach;
    public ConfigEntry<float> TitleMenuDistance;
    public ConfigEntry<float> TitleReach;
    public ConfigEntry<bool> LoadingSpace;
    public ConfigEntry<bool> PauseOnDashboard;

    public static UIMenuSettings Bind(ConfigFile cfg) =>
        new()
        {
            TitleOnBeach = cfg.Bind(
                "UI",
                "TitleOnBeach",
                true,
                "Stand on the beach where the title camera is, with the main menu floating in front of you."
            ),
            TitleMenuDistance = cfg.Bind(
                "UI",
                "TitleMenuDistance",
                3f,
                new ConfigDescription(
                    "Distance of the title screen (logo, menu, settings pages) on the beach, in metres. Its width grows with the distance, so it always looks as big.",
                    new AcceptableValueRange<float>(1.2f, 4f)
                )
            ),
            TitleReach = cfg.Bind(
                "UI",
                "TitleReach",
                4f,
                new ConfigDescription(
                    "Distance of the title menu's buttons from your eyes, in metres. At the menu distance or more (the default) everything stays on one big screen and the game's own title animation plays. At 0.45 the buttons come to your hand to press with a fingertip; the laser works at any distance. Avoid 0.5 to 0.8, too far to touch and too near to point at comfortably.",
                    new AcceptableValueRange<float>(0.35f, 4f)
                )
            ),
            PauseOnDashboard = cfg.Bind(
                "UI",
                "PauseOnDashboard",
                true,
                "Open the pause menu when the VR runtime takes over (SteamVR dashboard open, headset off). Under SteamVR, Quest and Rift controllers send their Menu button to the SteamVR dashboard, so this is how Menu pauses there."
            ),
            LoadingSpace = cfg.Bind(
                "UI.Loading",
                "LoadingSpace",
                true,
                "Show a dark room with a floor grid and the loading panel while levels load. Off: stay black."
            ),
        };
}
