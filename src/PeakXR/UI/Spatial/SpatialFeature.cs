using BepInEx.Configuration;
using PeakXR.Framework;

namespace PeakXR.UI.Spatial;

/// <summary>
/// ui.spatial: HUD parts that mean something about a place in the world go to that place in VR. The interact prompt
/// floats at the target, item prompts sit beside the controller buttons, the biome title and "fog rises" banners are
/// distant signs, the rope length and item fuel ride on the held item, and while spectating the friend's name,
/// status bar and items surround them. Nothing is patched: the game keeps driving its own widgets, which are only
/// carried out of the HUD panel (<see cref="SpatialBox"/>) and put back when VR or a setting stops.
/// </summary>
public sealed class SpatialFeature : Feature
{
    public override string Id => "ui.spatial";

    // After ui.world (converts the HUD canvas) and ui.hand.
    public override int Order => 57;

    internal static SpatialSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = SpatialSettings.Bind(Plugin.Settings);
        SpatialRuntime.Create();
    }
}

/// <summary>One switch per placement, so each can go back to the HUD panel. Read live every frame.</summary>
internal sealed class SpatialSettings
{
    public ConfigEntry<bool> TargetPrompt,
        ButtonLabels,
        WorldBanners,
        HeldItemGauges,
        SpectateOnPlayer,
        StatusAlerts;

    public static SpatialSettings Bind(ConfigFile cfg)
    {
        const string ui = "UI";
        return new SpatialSettings
        {
            TargetPrompt = cfg.Bind(
                ui,
                "TargetPrompt",
                true,
                "The interact prompt (name and action) floats at the thing you point at, and a held interaction's "
                    + "progress ring shows there too. Off: they stay on the HUD."
            ),
            ButtonLabels = cfg.Bind(
                ui,
                "ButtonLabels",
                true,
                "A held item's prompts (use, drop, scroll...) show as small labels beside the controller buttons for "
                    + "a few seconds after you draw it and while you look at your hand. Off: they stay on the HUD."
            ),
            WorldBanners = cfg.Bind(
                ui,
                "WorldBanners",
                true,
                "The biome title and the 'fog rises' style banners appear as large signs in the distance ahead of "
                    + "you. Off: they show on the HUD."
            ),
            HeldItemGauges = cfg.Bind(
                ui,
                "HeldItemGauges",
                true,
                "The rope length shows next to a held rope spool and item fuel as an arc by your hand. Off: rope "
                    + "length stays on the HUD."
            ),
            SpectateOnPlayer = cfg.Bind(
                ui,
                "SpectateOnPlayer",
                true,
                "While spectating, the friend's name and status bar float above them, their items around their "
                    + "belt, and arrows on your controller show how to switch. Off: the HUD shows them."
            ),
            StatusAlerts = cfg.Bind(
                ui,
                "StatusAlerts",
                true,
                "Low stamina or a new affliction makes the status bar pulse and the controller tick."
            ),
        };
    }
}
