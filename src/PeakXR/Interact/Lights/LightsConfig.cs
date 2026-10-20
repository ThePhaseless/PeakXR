using BepInEx.Configuration;

namespace PeakXR.Interact.Lights;

/// <summary>Hand-held light settings, bound by <see cref="HandLightsFeature"/>.</summary>
internal sealed class LightsConfig
{
    public static LightsConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> strikeToLight,
        heldLightShadows;
    private ConfigEntry<float> strikeSpeed;

    public bool StrikeToLight => strikeToLight?.Value ?? true;
    public float StrikeSpeed => strikeSpeed?.Value ?? 1.5f;
    public bool HeldLightShadows => heldLightShadows?.Value ?? false;

    public static void Bind(ConfigFile file)
    {
        const string s = "Interaction.Lights";
        Instance = new LightsConfig
        {
            strikeToLight = file.Bind(
                s,
                "StrikeToLight",
                true,
                "Swipe your free hand quickly over a held flare to strike it, or over a held candle's wick to light "
                    + "or snuff it. The trigger of the holding hand still works."
            ),
            strikeSpeed = file.Bind(
                s,
                "StrikeSpeed",
                1.5f,
                new ConfigDescription(
                    "How fast (metres per second) the free hand must move for a strike to count.",
                    new AcceptableValueRange<float>(0.5f, 4f)
                )
            ),
            heldLightShadows = file.Bind(
                s,
                "HeldLightShadows",
                false,
                "Let the light in your hand cast shadows. Off keeps your own VR hand and arm, which sit right next "
                    + "to the light, from throwing large moving shadows over the scene, and skips its shadow maps."
            ),
        };
    }
}
