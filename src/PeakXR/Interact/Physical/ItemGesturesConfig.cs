using BepInEx.Configuration;

namespace PeakXR.Interact.Physical;

/// <summary>Physical item gesture settings, bound by <see cref="ItemGesturesFeature"/>. Each turns one gesture off
/// and leaves its button path (grip-hold interact, trigger use) as the only way.</summary>
internal sealed class ItemGesturesConfig
{
    public static ItemGesturesConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> cookOverFire,
        liftLid,
        bugleAtMouth,
        lightWithFlame,
        pressToBody,
        warmHands,
        parasolRunner,
        readAtFace,
        communeAtFace,
        triggerEats;

    public bool CookOverFire => cookOverFire?.Value ?? true;
    public bool LiftLid => liftLid?.Value ?? true;
    public bool BugleAtMouth => bugleAtMouth?.Value ?? true;
    public bool LightWithFlame => lightWithFlame?.Value ?? true;
    public bool PressToBody => pressToBody?.Value ?? true;
    public bool WarmHands => warmHands?.Value ?? true;
    public bool ParasolRunner => parasolRunner?.Value ?? true;
    public bool ReadAtFace => readAtFace?.Value ?? true;
    public bool CommuneAtFace => communeAtFace?.Value ?? true;
    public bool TriggerEats => triggerEats?.Value ?? false;

    public static void Bind(ConfigFile file)
    {
        const string s = "Interaction";
        Instance = new ItemGesturesConfig
        {
            cookOverFire = file.Bind(
                s,
                "CookOverFire",
                true,
                "Hold a cookable item over a lit campfire to cook it: it cooks while it stays in the heat and stops "
                    + "when you pull it out. Gripping the campfire still works."
            ),
            liftLid = file.Bind(
                s,
                "LiftLidToOpen",
                true,
                "Grip the top of a suitcase and lift to open it; the game's open timer only runs as far as the lid is "
                    + "raised. Respawn chests take both hands. Gripping low on the case holds it open as before."
            ),
            bugleAtMouth = file.Bind(
                s,
                "BugleAtMouth",
                true,
                "Raise a held bugle to your mouth to play it, and tilt the bell up or down for a higher or lower note "
                    + "(the trigger plays too)."
            ),
            lightWithFlame = file.Bind(
                s,
                "LightWithFlame",
                true,
                "Touch a lit torch, flare, candle or lantern to a cannon or rocket fuse, a ghost fire or an unlit "
                    + "campfire to light it. A bare-hand grip still lights them."
            ),
            pressToBody = file.Bind(
                s,
                "PressToBody",
                true,
                "Press a bandage or other healing item to your chest or your other forearm to use it, or to a "
                    + "friend's body to use it on them. Food stays at the mouth; the trigger still works."
            ),
            warmHands = file.Bind(
                s,
                "WarmHandsAtFire",
                true,
                "Open palms held toward a lit campfire give a steady low warm vibration; near one while cold, a faint "
                    + "shiver. Feel only; the game's warmth is unchanged."
            ),
            parasolRunner = file.Bind(
                s,
                "ParasolRunner",
                true,
                "Grip a held parasol's shaft with your free hand and push up to open it, pull down to close it. "
                    + "The trigger still toggles it."
            ),
            readAtFace = file.Bind(
                s,
                "ReadAtFace",
                true,
                "Reading the Book of Bones turns you into a skeleton, so it starts only when you hold it open at eye "
                    + "level for a moment, and lowering it stops. The trigger alone is refused with a warning, and near "
                    + "a skeleton the book never reads (there it revives). Off: the trigger reads it."
            ),
            triggerEats = file.Bind(
                s,
                "TriggerEats",
                false,
                "Also eat and drink with the trigger, for seated or one-handed play. Off: food and drink are used "
                    + "by raising them to your mouth, and the trigger alone points you there."
            ),
            communeAtFace = file.Bind(
                s,
                "CommuneAtFace",
                true,
                "Items that kill or warp you when used (Cursed Skull, Scout's Honor) start only when held eye to eye "
                    + "for the whole use; the trigger alone is refused with a warning. Off: the trigger uses them."
            ),
        };
    }
}
