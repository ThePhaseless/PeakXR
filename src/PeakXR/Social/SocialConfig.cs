using BepInEx.Configuration;

namespace PeakXR.Social;

/// <summary>Player-to-player interaction settings, bound by <see cref="SocialFeature"/>.</summary>
internal sealed class SocialConfig
{
    public static SocialConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> handInteract,
        carryBothHands,
        feedAtMouth,
        struggle,
        giveUpGuard,
        graspHaptics,
        pullToPluck,
        shoveKick,
        carriedSway,
        palJumpFeel;
    private ConfigEntry<float> reach;

    public bool HandInteract => handInteract?.Value ?? true;
    public bool CarryBothHands => carryBothHands?.Value ?? true;
    public bool FeedAtMouth => feedAtMouth?.Value ?? true;
    public bool Struggle => struggle?.Value ?? true;
    public bool GiveUpGuard => giveUpGuard?.Value ?? true;
    public bool GraspHaptics => graspHaptics?.Value ?? true;
    public bool PullToPluck => pullToPluck?.Value ?? true;
    public bool ShoveKick => shoveKick?.Value ?? true;
    public bool CarriedSway => carriedSway?.Value ?? true;
    public bool PalJumpFeel => palJumpFeel?.Value ?? true;
    public float Reach => reach?.Value ?? 0.2f;

    public static void Bind(ConfigFile file)
    {
        const string s = "Social";
        Instance = new SocialConfig
        {
            handInteract = file.Bind(
                s,
                "HandInteract",
                true,
                "Close your grip on a friend to interact through the game's own actions: pull a stuck or climbing "
                    + "friend up (empty hand), carry or drop a passed-out friend, open their backpack from behind, "
                    + "pluck thorns, arrows and ticks (yours or theirs) and pull off stuck items. Hold the grip as long as "
                    + "the game's hold time."
            ),
            reach = file.Bind(
                s,
                "HandReach",
                0.2f,
                new ConfigDescription(
                    "How close (metres) a hand must be to a friend's body to grab it. Thorns and stuck items need "
                        + "about half of this.",
                    new AcceptableValueRange<float>(0.1f, 0.4f)
                )
            ),
            carryBothHands = file.Bind(
                s,
                "CarryWithBothHands",
                true,
                "Lifting a passed-out friend needs both hands on their body. Off: one grip carries."
            ),
            feedAtMouth = file.Bind(
                s,
                "FeedAtMouth",
                true,
                "Bring food to a friend's mouth (a bandage or medicine to their chest) and squeeze the trigger to give "
                    + "it to them (the game's use-on-friend); let go to stop. Standing close never uses it. Your hand "
                    + "replaces the face-each-other check; items with their own angle rule keep it."
            ),
            struggle = file.Bind(
                s,
                "StruggleByShaking",
                true,
                "Shake or tear with your hands to struggle out of webs and fly traps. The jump button still works."
            ),
            giveUpGuard = file.Bind(
                s,
                "GiveUpGuard",
                true,
                "While passed out, holding interact makes you give up faster. With this on, a grip no longer "
                    + "counts: hold both grips and both triggers to give up on purpose."
            ),
            graspHaptics = file.Bind(
                s,
                "GraspHaptics",
                true,
                "Vibrate when your reach catches a friend or a ledge's helping hand catches you."
            ),
            pullToPluck = file.Bind(
                s,
                "PullToPluck",
                true,
                "Pinch a thorn or arrow, then pull your hand back a few centimetres to start pulling it out. Off: the "
                    + "grip alone starts it."
            ),
            shoveKick = file.Bind(
                s,
                "ShoveKick",
                true,
                "In a custom run with the helping hand set to kick, push both open palms at a friend to kick them. "
                    + "Gripping a friend never kicks."
            ),
            carriedSway = file.Bind(
                s,
                "CarriedSway",
                true,
                "While a friend or a condor carries you, feel a slow, light sway in your hands as they move. Only "
                    + "vibration: the view never moves."
            ),
            palJumpFeel = file.Bind(
                s,
                "PalJumpFeel",
                true,
                "Feel a light pressure in your hands while you stand on a crouched friend (or one stands on you), and "
                    + "a thump when their jump boosts you."
            ),
        };
    }
}
