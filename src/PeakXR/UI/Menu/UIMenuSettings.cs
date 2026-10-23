using BepInEx.Configuration;

namespace PeakXR.UI.Menu;

/// <summary>Config entries for the title stage and the loading space.</summary>
internal sealed class UIMenuSettings
{
    public ConfigEntry<bool> TitleOnBeach;
    public ConfigEntry<float> TitleMenuDistance;
    public ConfigEntry<float> TitleReach;
    public ConfigEntry<bool> LoadingSpace;

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
                2.2f,
                new ConfigDescription(
                    "Distance of the title panel (logo, timer, settings pages) on the beach, in metres.",
                    new AcceptableValueRange<float>(1.2f, 4f)
                )
            ),
            TitleReach = cfg.Bind(
                "UI",
                "TitleReach",
                Core.TitleLayout.TouchReach,
                new ConfigDescription(
                    "Distance of the title menu's buttons from your eyes, in metres. At 0.45 you press them with a fingertip; the laser works at any distance. Avoid 0.5 to 0.8, too far to touch and too near to point at comfortably. Set it to the menu distance to keep everything on one panel.",
                    new AcceptableValueRange<float>(0.35f, 4f)
                )
            ),
            LoadingSpace = cfg.Bind(
                "UI.Loading",
                "LoadingSpace",
                true,
                "Show a dark room with a floor grid and the loading panel while levels load. Off: stay black."
            ),
        };
}
