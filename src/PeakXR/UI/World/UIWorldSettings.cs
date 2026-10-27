using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.VR;
using MenuBackplateMath = PeakXR.Core.MenuBackplate;

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

    /// <summary>The hand that is not dominant (<c>Input.DominantHand</c>), so it follows a left-handed layout.</summary>
    OffHand,
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
    /// <summary>The game's status flashes are hidden and shown as a soft coloured glow at the edge of the
    /// view (ui.status). Falls back to <see cref="HeadLocked"/> when that feature is not running.</summary>
    Peripheral,

    /// <summary>Status effect overlays drawn on a head-locked quad (correct in both eyes).</summary>
    HeadLocked,
    Off,
}

/// <summary>Config entries for world-space UI. Read live every frame, so changes apply at once.</summary>
internal sealed class UIWorldSettings
{
    /// <summary>Menu and HUD placement in world units: the settings are real metres from your eyes, kept at any
    /// <c>Camera.WorldScale</c> (<see cref="Rig.RealScale"/>), and their widths grow with the distance, so the
    /// angles are the same.</summary>
    public float MenuDistanceWorld => Rig.RealScale.World(MenuDistance.Value);

    public float MenuWidthWorld => Rig.RealScale.World(MenuWidth.Value);
    public float HudDistanceWorld => Rig.RealScale.World(HudDistance.Value);
    public float HudWidthWorld => Rig.RealScale.World(HudWidth.Value);

    public ConfigEntry<float> MenuDistance,
        MenuWidth,
        MenuTilt,
        FadeSeconds,
        NameTagScale,
        TextScale;
    public ConfigEntry<bool> MenuPanels,
        MenuBackplate,
        MenuBackplateAdaptive;
    public ConfigEntry<float> MenuBackplateOpacity;
    public ConfigEntry<StatusOverlayMode> StatusOverlays;
    public ConfigEntry<HudMode> Hud;
    public ConfigEntry<float> HudDistance,
        HudWidth,
        HudTilt,
        HudFollowAngle,
        WristWidth;
    public ConfigEntry<WristSide> WristHand;

    /// <summary>The wrist HUD is on the left controller; one-handed (Accessibility.OneHanded), on the one there is.</summary>
    public bool WristLeft =>
        VRInput.OneHanded ? VRInput.LeftHanded
        : WristHand.Value == WristSide.OffHand ? !VRInput.LeftHanded
        : WristHand.Value == WristSide.Left;
    public ConfigEntry<ReticleMode> Reticle;
    public ConfigEntry<PointerHandMode> PointerHand;
    public ConfigEntry<float> ScrollSpeed,
        HoverHaptics;

    public static UIWorldSettings Bind(ConfigFile cfg)
    {
        const string ui = "UI",
            hud = "UI.HUD",
            pointer = "UI.Pointer";
        var s = new UIWorldSettings
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
                Range(
                    "Width of a full-screen menu panel, in metres. Smaller menus (pause) grow until their text is "
                        + "comfortable to read.",
                    0.8f,
                    3f
                )
            ),
            MenuTilt = cfg.Bind(
                ui,
                "MenuTilt",
                8f,
                Range("Degrees below eye level where menus appear.", 0f, 30f)
            ),
            MenuPanels = cfg.Bind(
                ui,
                "MenuPanels",
                false,
                "Show the game's flat panel backgrounds behind menus (full-screen dimmers stay hidden). Off: "
                    + "buttons, text and icons float in the world on their own, and text gets a thin outline to stay "
                    + "readable."
            ),
            MenuBackplate = cfg.Bind(
                ui,
                "MenuBackplate",
                true,
                "A dark rounded plate behind each menu, sized to the buttons and text it holds (it grows and shrinks "
                    + "with pages, tabs and open lists), so text stays readable over sand, snow or sky. Off: menus "
                    + "float with nothing behind them."
            ),
            MenuBackplateAdaptive = cfg.Bind(
                ui,
                "MenuBackplateAdaptive",
                true,
                "Fit the menu plate to what is behind it as seen from your eyes: as dark as MenuBackplateOpacity over "
                    + "bright snow or sky, lighter over a dark background, so text keeps the same contrast either way. "
                    + "Off: always MenuBackplateOpacity."
            ),
            MenuBackplateOpacity = cfg.Bind(
                ui,
                "MenuBackplateOpacity",
                MenuBackplateMath.DefaultOpacity,
                Range(
                    "How opaque the menu plate is (with MenuBackplateAdaptive, the most it gets). 0.85 keeps white text readable even against snow in sunlight.",
                    0.5f,
                    1f
                )
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
            TextScale = cfg.Bind(
                ui,
                "TextScale",
                1f,
                Range(
                    "Size of the text the mod puts in the world: the target prompt, item labels by the buttons, "
                        + "refusal labels and the wrist HUD. 1 is the readable minimum; it only grows from there.",
                    1f,
                    ReadableText.MaxTextScale
                )
            ),
            StatusOverlays = cfg.Bind(
                ui,
                "StatusOverlays",
                StatusOverlayMode.Peripheral,
                "Full-screen status effects (poison, cold, injury, spore warning...). Peripheral turns them into a soft glow at the edge of your view; HeadLocked draws the game's flashes on a quad in front of your eyes."
            ),

            Hud = cfg.Bind(
                hud,
                "HudMode",
                HudMode.Wrist,
                "Wrist: stamina, afflictions and the dying bar on your off-hand wrist, readable while your hands are "
                    + "on a wall. Body: one HUD panel that follows you."
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
                WristSide.OffHand,
                "Wrist that carries the status bar in Wrist mode."
            ),
            WristWidth = cfg.Bind(
                hud,
                "WristWidth",
                0.16f,
                Range(
                    "Width of the wrist HUD in metres when the wrist is 40 cm from the eyes; it keeps that angular size nearer or farther.",
                    0.08f,
                    0.35f
                )
            ),
            Reticle = cfg.Bind(
                hud,
                "Reticle",
                ReticleMode.Hidden,
                "Where the aiming reticle is shown. In VR you aim with your hand and the prompt shows at the target "
                    + "(UI > TargetPrompt), so it is hidden by default; Head pins it to your view."
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
        Migrate(cfg, s, hud);
        return s;
    }

    /// <summary>
    /// Layout 1 changed three defaults (HUD on the wrist, the off hand's wrist, no head-locked reticle). A config
    /// saved before it still holds the old defaults, so move each entry that equals its old default, once.
    /// </summary>
    private static void Migrate(ConfigFile cfg, UIWorldSettings s, string section)
    {
        var version = cfg.Bind(
            section,
            "LayoutVersion",
            0,
            new ConfigDescription(
                "Internal: the default layout this file was upgraded to.",
                null,
                UI.Settings.VRTab.HiddenTag
            )
        );
        if (version.Value >= 1)
            return;
        if (s.Hud.Value == HudMode.Body)
            s.Hud.Value = HudMode.Wrist;
        if (s.WristHand.Value == WristSide.Left)
            s.WristHand.Value = WristSide.OffHand;
        if (s.Reticle.Value == ReticleMode.Head)
            s.Reticle.Value = ReticleMode.Hidden;
        version.Value = 1;
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new(text, new AcceptableValueRange<float>(min, max));
}
