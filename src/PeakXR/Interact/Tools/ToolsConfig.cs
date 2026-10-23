using BepInEx.Configuration;

namespace PeakXR.Interact.Tools;

/// <summary>Tool gesture settings, bound by <see cref="ToolsFeature"/>.</summary>
internal sealed class ToolsConfig
{
    private const string Section = "Interaction.Tools";

    public static ToolsConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> twistScroll,
        pullRope,
        hammerSpikes,
        handSteerGlider;
    private ConfigEntry<float> twistStepDegrees;
    private ConfigEntry<int> hammerStrikes;

    public bool TwistScroll => twistScroll?.Value ?? true;
    public float TwistStepDegrees => twistStepDegrees?.Value ?? 30f;
    public bool PullRope => pullRope?.Value ?? true;
    public bool HammerSpikes => hammerSpikes?.Value ?? true;
    public int HammerStrikes => hammerStrikes?.Value ?? 3;
    public bool HandSteerGlider => handSteerGlider?.Value ?? true;

    public static void Bind(ConfigFile file)
    {
        Instance = new ToolsConfig
        {
            twistScroll = file.Bind(
                Section,
                "TwistScroll",
                true,
                new ConfigDescription(
                    "Twist the wrist like a knob to scroll: the holding hand pays rope out of a spool or reels it in, "
                        + "either hand tilts a scout cannon's barrel. Thumbstick scroll keeps working."
                )
            ),
            twistStepDegrees = file.Bind(
                Section,
                "TwistStepDegrees",
                30f,
                new ConfigDescription(
                    "Degrees of wrist twist per scroll step (one cannon angle step).",
                    new AcceptableValueRange<float>(10f, 90f)
                )
            ),
            pullRope = file.Bind(
                Section,
                "PullRope",
                true,
                new ConfigDescription(
                    "Close the free hand's grip at a held rope spool and pull away to pay rope out, push back to reel it in."
                )
            ),
            hammerSpikes = file.Bind(
                Section,
                "HammerSpikes",
                true,
                new ConfigDescription(
                    "While you hold the use button, striking the preview with either hand drives a climbing spike in "
                        + "sooner, up to a third faster than the hold timer. Holding alone still places it on time. "
                        + "Off: the game's hold timer only."
                )
            ),
            hammerStrikes = file.Bind(
                Section,
                "HammerStrikes",
                3,
                new ConfigDescription(
                    "Strikes that drive a climbing spike in.",
                    new AcceptableValueRange<int>(1, 8)
                )
            ),
            handSteerGlider = file.Bind(
                Section,
                "HandSteerGlider",
                true,
                new ConfigDescription(
                    "While gliding with both grips closed, the glide heads where your spread arms face (lean to turn) "
                        + "instead of where your body faces. Your view never turns."
                )
            ),
        };
    }
}
