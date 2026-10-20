using BepInEx.Configuration;

namespace PeakXR.Interact.Aim;

/// <summary>Aimed-tool settings, bound by <see cref="AimFeature"/>.</summary>
internal sealed class AimConfig
{
    private const string Section = "Interaction.Aim";

    public static AimConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> handAim,
        aimLine,
        shotHaptics;
    private ConfigEntry<float> twoHandMinSeparation;

    /// <summary>Aimed tools aim from the holding hand instead of the head.</summary>
    public bool AimFromHand => handAim?.Value ?? true;

    /// <summary>Thin line from the holding hand to the aim point while aiming a tool.</summary>
    public bool AimLine => aimLine?.Value ?? true;

    /// <summary>Recoil pulses on the holding hand when a launcher fires.</summary>
    public bool ShotHaptics => shotHaptics?.Value ?? true;

    public static void Bind(ConfigFile file)
    {
        var cfg = new AimConfig
        {
            handAim = file.Bind(
                Section,
                "HandAim",
                true,
                "Rope tie-off, rope and vine shooters, rescue hook, pitons, darts, antizooka, clone staff and "
                    + "placeable items aim where the holding hand points instead of where you look. "
                    + "Off: they aim from your head like the flat game."
            ),
            twoHandMinSeparation = file.Bind(
                Section,
                "TwoHandMinSeparation",
                0.15f,
                new ConfigDescription(
                    "While your free hand also grips the held tool, it aims along the line between your hands. "
                        + "Hands closer than this (metres) aim with the holding hand alone.",
                    new AcceptableValueRange<float>(0.05f, 0.5f)
                )
            ),
            aimLine = file.Bind(
                Section,
                "AimLine",
                true,
                "Show a thin line from the holding hand to the aim point while your finger is on the trigger of an aimed tool."
            ),
            shotHaptics = file.Bind(
                Section,
                "ShotHaptics",
                true,
                "Pulse the holding hand (and the supporting hand) when a launcher fires. Strength follows Interaction.HapticStrength."
            ),
        };
        Instance = cfg;
        HandAim.TwoHandMinSeparation = cfg.twoHandMinSeparation.Value;
        cfg.twoHandMinSeparation.SettingChanged += (_, _) =>
            HandAim.TwoHandMinSeparation = cfg.twoHandMinSeparation.Value;
    }
}
