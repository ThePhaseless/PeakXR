using System;
using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Locomotion;

/// <summary>
/// The <c>Comfort</c> settings, shared by player.look (turning) and player.locomotion (vignette, smoothing, seated).
/// Whichever feature installs first binds them; the second call is a no-op. Choosing a preset writes the individual
/// settings; editing one afterwards turns the preset into Custom unless the values still match a preset.
/// </summary>
internal static class ComfortConfig
{
    private static readonly string[] PresetNames =
    {
        "Comfortable",
        "Moderate",
        "Intense",
        "Custom",
    };
    private static readonly string[] TurnModes = { "Snap", "Smooth" };

    private static ConfigEntry<string> preset;
    private static ConfigEntry<string> turnMode;
    private static ConfigEntry<float> snapAngle;
    private static ConfigEntry<float> smoothSpeed;
    private static ConfigEntry<bool> vignette;
    private static ConfigEntry<float> vignetteStrength;
    private static ConfigEntry<float> moveSmoothing;
    private static ConfigEntry<float> vignetteStart;
    private static ConfigEntry<float> vignetteFull;
    private static ConfigEntry<bool> seated;
    private static bool applying;

    /// <summary>The preset in force before the current change, for <see cref="ComfortPresets.ChaseFollowAfter"/>.</summary>
    private static ComfortPreset appliedPreset = ComfortPreset.Moderate;

    public static TurnMode Turn => turnMode?.Value == "Smooth" ? TurnMode.Smooth : TurnMode.Snap;
    public static float SnapAngle => snapAngle?.Value ?? 30f;
    public static float SmoothTurnSpeed => smoothSpeed?.Value ?? 60f;
    public static bool Vignette => vignette?.Value ?? true;
    public static float VignetteStrength => vignetteStrength?.Value ?? 0.5f;
    public static float MoveSmoothing => moveSmoothing?.Value ?? 0f;
    public static float VignetteStartSpeed => vignetteStart?.Value ?? 0.2f;
    public static float VignetteFullSpeed => vignetteFull?.Value ?? 2.5f;
    public static bool Seated => seated?.Value ?? false;

    public static void Bind(ConfigFile file)
    {
        if (preset != null)
            return;
        const string s = "Comfort";
        var defaults = ComfortPresets.Get(ComfortPreset.Moderate);
        preset = file.Bind(
            s,
            "Preset",
            "Moderate",
            new ConfigDescription(
                "Sets turning, vignette and movement smoothing at once. Comfortable: snap turn, strong vignette, "
                    + "instant start and stop. Moderate: snap turn, medium vignette, instant moves. Intense: smooth "
                    + "turn, no vignette. Changing any of those settings makes this Custom.",
                new AcceptableValueList<string>(PresetNames)
            )
        );
        turnMode = file.Bind(
            s,
            "TurnMode",
            "Snap",
            new ConfigDescription(
                "Right stick (left stick when left-handed) turns you in steps (Snap) or continuously (Smooth).",
                new AcceptableValueList<string>(TurnModes)
            )
        );
        snapAngle = file.Bind(
            s,
            "SnapTurnAngle",
            defaults.SnapAngle,
            new ConfigDescription(
                "Degrees per snap turn.",
                new AcceptableValueRange<float>(15f, 90f)
            )
        );
        smoothSpeed = file.Bind(
            s,
            "SmoothTurnSpeed",
            defaults.SmoothTurnSpeed,
            new ConfigDescription(
                "Smooth turn speed at full stick, degrees per second. Comfort research puts 60 as the ceiling.",
                new AcceptableValueRange<float>(30f, 360f)
            )
        );
        vignette = file.Bind(
            s,
            "Vignette",
            defaults.Vignette,
            "Darken the edges of your view while your character moves, falls or smooth-turns without you moving."
        );
        vignetteStrength = file.Bind(
            s,
            "VignetteStrength",
            defaults.VignetteStrength,
            new ConfigDescription(
                "How far the vignette closes at full speed.",
                new AcceptableValueRange<float>(0.1f, 1f)
            )
        );
        moveSmoothing = file.Bind(
            s,
            "MoveSmoothing",
            defaults.MoveSmoothing,
            new ConfigDescription(
                "Seconds the move stick takes to reach full speed. 0 (instant) is the most comfortable: a ramp "
                    + "stretches the acceleration that causes motion sickness.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );
        vignetteStart = file.Bind(
            s,
            "VignetteStartSpeed",
            0.2f,
            new ConfigDescription(
                "Speed (m/s) at which the vignette starts to close; at the default any artificial motion shows it.",
                new AcceptableValueRange<float>(0f, 2f)
            )
        );
        vignetteFull = file.Bind(
            s,
            "VignetteFullSpeed",
            2.5f,
            new ConfigDescription(
                "Speed (m/s) at which the vignette reaches its full strength; the default is walking speed.",
                new AcceptableValueRange<float>(0.5f, 8f)
            )
        );
        seated = file.Bind(
            s,
            "SeatedMode",
            false,
            "Play sitting down: your seated head height becomes the character's standing eye height."
        );

        // Configs written before the vr-practices review: a preset still holding its old values moves to the new
        // ones (Comfortable lost its 0.3 s ramp); then any single setting still at its old default moves too.
        // A player's own choices stay (ComfortPresets.FromOld, ConfigMigration.FromOldDefault).
        bool known = Enum.TryParse(preset.Value, out ComfortPreset saved);
        appliedPreset = known ? saved : ComfortPreset.Custom;
        if (
            known
            && Framework.ConfigMigration.Once("Comfort.Preset.FromOld.VrPractices")
            && ComfortPresets.FromOld(saved, Current(), out var updated)
        )
        {
            Apply(updated);
            // Into Comfortable as if picked from the default (Moderate) chase follow: still the view.
            SetChaseFollow(ComfortPreset.Moderate, saved);
        }
        Framework.ConfigMigration.FromOldDefault(smoothSpeed, 120f);
        Framework.ConfigMigration.FromOldDefault(moveSmoothing, 0.15f);

        preset.SettingChanged += OnPresetChanged;
        turnMode.SettingChanged += OnValueChanged;
        snapAngle.SettingChanged += OnValueChanged;
        smoothSpeed.SettingChanged += OnValueChanged;
        vignette.SettingChanged += OnValueChanged;
        vignetteStrength.SettingChanged += OnValueChanged;
        moveSmoothing.SettingChanged += OnValueChanged;
        // A hand-edited config file can disagree with its preset name.
        OnValueChanged(null, EventArgs.Empty);
    }

    private static ComfortProfile Current() =>
        new()
        {
            Turn = Turn,
            SnapAngle = SnapAngle,
            SmoothTurnSpeed = SmoothTurnSpeed,
            Vignette = Vignette,
            VignetteStrength = VignetteStrength,
            MoveSmoothing = MoveSmoothing,
        };

    private static void OnPresetChanged(object sender, EventArgs e)
    {
        if (applying || !Enum.TryParse(preset.Value, out ComfortPreset p))
            return;
        var from = appliedPreset;
        appliedPreset = p;
        if (p == ComfortPreset.Custom)
            return;
        Apply(ComfortPresets.Get(p));
        SetChaseFollow(from, p);
    }

    /// <summary>
    /// Comfortable also stills the detached view (Camera.ThirdPersonFollow 0: it stays put and re-places behind a
    /// fade), so a watched body's climbs and falls never glide the eyes; the others restore the slow follow. Only
    /// while the setting still holds the previous preset's value: a player's own value stays. Not part of a preset's
    /// identity, so changing it never leaves the preset.
    /// </summary>
    private static void SetChaseFollow(ComfortPreset from, ComfortPreset to)
    {
        var follow = Rig.CameraFeature.ThirdPersonFollow;
        if (follow != null)
            follow.Value = ComfortPresets.ChaseFollowAfter(from, to, follow.Value);
    }

    private static void Apply(ComfortProfile profile)
    {
        applying = true;
        try
        {
            turnMode.Value = profile.Turn == TurnMode.Smooth ? "Smooth" : "Snap";
            snapAngle.Value = profile.SnapAngle;
            smoothSpeed.Value = profile.SmoothTurnSpeed;
            vignette.Value = profile.Vignette;
            vignetteStrength.Value = profile.VignetteStrength;
            moveSmoothing.Value = profile.MoveSmoothing;
        }
        finally
        {
            applying = false;
        }
    }

    private static void OnValueChanged(object sender, EventArgs e)
    {
        if (applying)
            return;
        var name = ComfortPresets.Identify(Current()).ToString();
        if (preset.Value == name)
            return;
        applying = true;
        try
        {
            preset.Value = name;
            if (Enum.TryParse(name, out ComfortPreset now))
                appliedPreset = now;
        }
        finally
        {
            applying = false;
        }
    }
}
