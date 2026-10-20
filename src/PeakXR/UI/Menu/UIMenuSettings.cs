using BepInEx.Configuration;

namespace PeakXR.UI.Menu;

/// <summary>Config entries for the title stage and the loading space.</summary>
internal sealed class UIMenuSettings
{
    public ConfigEntry<bool> TitleOnBeach;
    public ConfigEntry<float> TitleMenuDistance;
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
                    "Distance of the main menu panel on the beach, in metres.",
                    new AcceptableValueRange<float>(1.2f, 4f)
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
