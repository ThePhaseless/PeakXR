using BepInEx.Configuration;
using PeakXR.Framework;

namespace PeakXR.UI.HandWidgets;

public enum RingPlacement
{
    /// <summary>Above the hand doing the hold (item hand, or the interacting hand).</summary>
    Hand,

    /// <summary>Left on the HUD panel, as in vanilla.</summary>
    Hud,
}

public enum FeedPlacement
{
    LeftHand,
    RightHand,
    Hud,
}

/// <summary>
/// ui.hand: the hold-to-use progress ring (items, climbing spike, held interactables) and the friend
/// item-use feed leave the centre of the HUD panel and ride on the hands, where the player is already
/// looking. Holds also tick the controller so long casts can be timed by feel.
/// </summary>
public sealed class HandWidgetsFeature : Feature
{
    public override string Id => "ui.hand";

    // After ui.world, which converts the HUD canvas these widgets live on.
    public override int Order => 56;

    internal static HandWidgetsSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = HandWidgetsSettings.Bind(Plugin.Settings);
        HandWidgetsRuntime.Create();
    }
}

/// <summary>Config entries for the hand-held HUD widgets. Read live every frame.</summary>
internal sealed class HandWidgetsSettings
{
    public ConfigEntry<RingPlacement> Ring;
    public ConfigEntry<float> RingSize,
        RingHaptics;
    public ConfigEntry<FeedPlacement> Feed;
    public ConfigEntry<float> FeedWidth;

    public static HandWidgetsSettings Bind(ConfigFile cfg)
    {
        const string section = "UI.Hand";
        return new HandWidgetsSettings
        {
            Ring = cfg.Bind(
                section,
                "ProgressRing",
                RingPlacement.Hand,
                "Where the hold-to-use progress ring shows: above the acting hand, or on the HUD."
            ),
            RingSize = cfg.Bind(
                section,
                "ProgressRingSize",
                0.06f,
                Range("Diameter of the progress ring on the hand, in metres.", 0.03f, 0.15f)
            ),
            RingHaptics = cfg.Bind(
                section,
                "ProgressHaptics",
                0.3f,
                Range(
                    "Controller tick strength at each quarter of a hold and on completion. 0 turns it off.",
                    0f,
                    1f
                )
            ),
            Feed = cfg.Bind(
                section,
                "FriendFeed",
                FeedPlacement.LeftHand,
                "Where the progress of friends using items on you shows."
            ),
            FeedWidth = cfg.Bind(
                section,
                "FriendFeedWidth",
                0.12f,
                Range("Width of the friend feed on the hand, in metres.", 0.05f, 0.3f)
            ),
        };
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new(text, new AcceptableValueRange<float>(min, max));
}
