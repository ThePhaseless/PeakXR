using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Inventory;

/// <summary>Body-slot inventory settings, bound by <see cref="InventoryFeature"/>.</summary>
internal sealed class InventoryConfig
{
    public static InventoryConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> bodySlots,
        holsterOnRelease,
        backpackOverShoulder,
        ghosts,
        wheelByHand;
    private ConfigEntry<string> layout;
    private ConfigEntry<float> radius,
        height;

    public bool BodySlots => bodySlots?.Value ?? true;
    public bool HolsterOnRelease => holsterOnRelease?.Value ?? true;
    public bool BackpackOverShoulder => backpackOverShoulder?.Value ?? true;
    public bool Ghosts => ghosts?.Value ?? true;
    public bool WheelByHand => wheelByHand?.Value ?? true;
    public string Layout => layout?.Value ?? BodySlotLayout.Belt;
    public float Radius => radius?.Value ?? 0.12f;
    public float Height => height?.Value ?? 0f;

    /// <summary>Raised when a layout setting changes, so the slot anchors are rebuilt.</summary>
    public int Revision { get; private set; }

    public static void Bind(ConfigFile file)
    {
        const string s = "Inventory";
        var cfg = new InventoryConfig
        {
            bodySlots = file.Bind(
                s,
                "BodySlots",
                true,
                "Physical hotbar on your body: reach to a slot and close your grip to draw that item into the hand, "
                    + "open the grip over a slot to put the held item away. Hotbar buttons keep working."
            ),
            layout = file.Bind(
                s,
                "SlotLayout",
                BodySlotLayout.Belt,
                new ConfigDescription(
                    "Where the hotbar slots sit: on a curved belt in front of your hips, or the same curve across "
                        + "your chest. The backpack is always over either shoulder.",
                    new AcceptableValueList<string>(BodySlotLayout.Names)
                )
            ),
            radius = file.Bind(
                s,
                "SlotRadius",
                0.12f,
                new ConfigDescription(
                    "How close (metres) a hand must be to a hotbar slot to use it. Slots spread out to at least "
                        + "twice this apart, so a hand never catches the neighbour.",
                    new AcceptableValueRange<float>(0.06f, 0.25f)
                )
            ),
            height = file.Bind(
                s,
                "SlotHeight",
                0f,
                new ConfigDescription(
                    "Moves the hotbar slots up or down (metres). Slots follow your tracked head height and body "
                        + "turn, never your head pitch.",
                    new AcceptableValueRange<float>(-0.25f, 0.25f)
                )
            ),
            holsterOnRelease = file.Bind(
                s,
                "HolsterOnRelease",
                true,
                "Opening the grip of the holding hand over a slot puts the item away. Off: only the hotbar buttons holster."
            ),
            backpackOverShoulder = file.Bind(
                s,
                "BackpackOverShoulder",
                true,
                "Reach over a shoulder and hold the grip to open your backpack; move the hand toward a pocket and "
                    + "let go to take or stash. Open the held item's grip over the shoulder to stash it. Grip both "
                    + "shoulders to take the pack off. While carrying a friend, the shoulder grip drops them."
            ),
            wheelByHand = file.Bind(
                s,
                "WheelByHand",
                true,
                "While a backpack wheel is open and a grip is held, moving that hand selects the pocket in that direction."
            ),
            ghosts = file.Bind(
                s,
                "SlotGhosts",
                true,
                "Show the hotbar on your body: each slot shows its item's icon, or a faint ring when empty, and lights "
                    + "up while a hand is in it (red when the belt is full). The shoulder slots show only when a hand "
                    + "is near."
            ),
        };
        cfg.layout.SettingChanged += (_, _) => cfg.Revision++;
        cfg.radius.SettingChanged += (_, _) => cfg.Revision++;
        cfg.height.SettingChanged += (_, _) => cfg.Revision++;
        Instance = cfg;
    }
}
