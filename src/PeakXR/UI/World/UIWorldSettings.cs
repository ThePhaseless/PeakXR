using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.UI.World;

public enum HudMode
{
    /// <summary>Whole HUD on one panel that follows the body.</summary>
    Body,

    /// <summary>Stamina and hotbar on the wrist, the rest on the body panel.</summary>
    Wrist,
}

public enum WristSide
{
    Left,
    Right,
}

public enum ReticleMode
{
    /// <summary>Small dot locked to the view, where the game aims interaction rays.</summary>
    Head,

    /// <summary>Left on the body HUD panel.</summary>
    Hud,
    Hidden,
}

public enum StatusOverlayMode
{
    /// <summary>Status effect overlays drawn on a head-locked quad (correct in both eyes).</summary>
    HeadLocked,
    Off,
}

/// <summary>Config entries for world-space UI. Read live every frame, so changes apply at once.</summary>
internal sealed class UIWorldSettings
{
    public ConfigEntry<float> MenuDistance,
        MenuWidth,
        MenuTilt,
        FadeSeconds,
        NameTagScale;
    public ConfigEntry<StatusOverlayMode> StatusOverlays;
    public ConfigEntry<HudMode> Hud;
    public ConfigEntry<float> HudDistance,
        HudWidth,
        HudTilt,
        HudFollowAngle,
        WristWidth;
    public ConfigEntry<WristSide> WristHand;
    public ConfigEntry<ReticleMode> Reticle;
    public ConfigEntry<PointerHandMode> PointerHand;
    public ConfigEntry<float> ScrollSpeed,
        HoverHaptics;

    public static UIWorldSettings Bind(ConfigFile cfg)
    {
        const string ui = "UI",
            hud = "UI.HUD",
            pointer = "UI.Pointer";
        return new UIWorldSettings
        {
            MenuDistance = cfg.Bind(
                ui,
                "MenuDistance",
                1.8f,
                Range("Distance of menu panels from your head, in metres.", 1f, 3f)
            ),
            MenuWidth = cfg.Bind(
                ui,
                "MenuWidth",
                1.6f,
                Range("Width of a full-screen menu panel, in metres.", 0.8f, 3f)
            ),
            MenuTilt = cfg.Bind(
                ui,
                "MenuTilt",
                8f,
                Range("Degrees below eye level where menus appear.", 0f, 30f)
            ),
            FadeSeconds = cfg.Bind(
                ui,
                "FadeSeconds",
                0.3f,
                Range("Duration of a full fade to or from black.", 0.1f, 1.5f)
            ),
            NameTagScale = cfg.Bind(
                ui,
                "NameTagScale",
                1f,
                Range("Size of player name tags.", 0.5f, 2f)
            ),
            StatusOverlays = cfg.Bind(
                ui,
                "StatusOverlays",
                StatusOverlayMode.HeadLocked,
                "Full-screen status effects (poison, cold, injury...). HeadLocked draws them on a quad in front of your eyes."
            ),

            Hud = cfg.Bind(
                hud,
                "HudMode",
                HudMode.Body,
                "Body: one HUD panel that follows you. Wrist: stamina and hotbar on your wrist."
            ),
            HudDistance = cfg.Bind(
                hud,
                "HudDistance",
                1.2f,
                Range("Distance of the body HUD panel, in metres.", 0.6f, 2.5f)
            ),
            HudWidth = cfg.Bind(
                hud,
                "HudWidth",
                1.5f,
                Range("Width of the body HUD panel, in metres.", 0.6f, 3f)
            ),
            HudTilt = cfg.Bind(
                hud,
                "HudTilt",
                12f,
                Range("Degrees below eye level of the HUD panel centre.", 0f, 40f)
            ),
            HudFollowAngle = cfg.Bind(
                hud,
                "HudFollowAngle",
                35f,
                Range("Degrees you can turn your head before the HUD re-centres.", 10f, 90f)
            ),
            WristHand = cfg.Bind(
                hud,
                "WristHand",
                WristSide.Left,
                "Wrist that carries the stamina bar and hotbar in Wrist mode."
            ),
            WristWidth = cfg.Bind(
                hud,
                "WristWidth",
                0.16f,
                Range("Width of the wrist HUD, in metres.", 0.08f, 0.35f)
            ),
            Reticle = cfg.Bind(
                hud,
                "Reticle",
                ReticleMode.Head,
                "Where the aiming reticle is shown."
            ),

            PointerHand = cfg.Bind(
                pointer,
                "PointerHand",
                PointerHandMode.LastUsed,
                "Controller that points at menus. LastUsed follows the last trigger you pulled."
            ),
            ScrollSpeed = cfg.Bind(
                pointer,
                "ScrollSpeed",
                900f,
                Range(
                    "Scroll speed with the thumbstick while pointing at a list, in pixels per second.",
                    100f,
                    3000f
                )
            ),
            HoverHaptics = cfg.Bind(
                pointer,
                "HoverHaptics",
                0.2f,
                Range(
                    "Strength of the controller tick when the laser moves onto a button. 0 turns it off.",
                    0f,
                    1f
                )
            ),
        };
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new(text, new AcceptableValueRange<float>(min, max));
}
