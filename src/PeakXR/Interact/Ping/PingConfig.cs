using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.VR;

namespace PeakXR.Interact.Ping;

/// <summary>Ping settings, bound by <see cref="PingFeature"/>.</summary>
internal sealed class PingConfig
{
    private const string Section = "Interaction.Ping";

    private static readonly string[] Hands = { "Pointing", "Right", "Left" };

    private static readonly string[] Buttons =
    {
        "None",
        "LeftPrimary",
        "LeftSecondary",
        "RightPrimary",
        "RightSecondary",
        "LeftStickClick",
        "RightStickClick",
    };

    public static PingConfig Instance { get; private set; } = new();

    private ConfigEntry<string> hand,
        pingButton;
    private ConfigEntry<bool> fingerGun,
        aimPreview;
    private ConfigEntry<float> markerAngle;

    public PingAimHand Hand =>
        (hand?.Value ?? "Pointing") switch
        {
            "Left" => PingAimHand.Left,
            "Right" => PingAimHand.Right,
            _ => PingAimHand.Pointing,
        };
    public bool FingerGun => fingerGun?.Value ?? true;
    public bool AimPreview => aimPreview?.Value ?? true;
    public float MarkerAngle => markerAngle?.Value ?? 8f;

    /// <summary>State of the configured ping fallback button this frame (false for None).</summary>
    public bool PingButtonDown()
    {
        switch (pingButton?.Value ?? "None")
        {
            case "LeftPrimary":
                return VRInput.Left.Primary;
            case "LeftSecondary":
                return VRInput.Left.Secondary;
            case "RightPrimary":
                return VRInput.Right.Primary;
            case "RightSecondary":
                return VRInput.Right.Secondary;
            case "LeftStickClick":
                return VRInput.Left.StickClick;
            case "RightStickClick":
                return VRInput.Right.StickClick;
            default:
                return false;
        }
    }

    public static void Bind(ConfigFile file)
    {
        Instance = new PingConfig
        {
            hand = file.Bind(
                Section,
                "Hand",
                "Pointing",
                new ConfigDescription(
                    "Which hand aims the ping. Pointing: the finger gun works on either hand and the ping goes where "
                        + "that hand points; the button pings from whichever hand is pointing, else the right hand. "
                        + "Right or Left: only that hand aims and fires the finger gun.",
                    new AcceptableValueList<string>(Hands)
                )
            ),
            fingerGun = file.Bind(
                Section,
                "FingerGunPing",
                true,
                "Point (grip closed, index straight), then squeeze the trigger while the grip stays closed to ping. "
                    + "That trigger squeeze does not also use the held item."
            ),
            aimPreview = file.Bind(
                Section,
                "AimPreview",
                true,
                "While a hand makes the finger gun, a thin line runs from it to a small dot where the ping would land, "
                    + "so you see the spot before you squeeze. The dot turns red where the ray finds no ground (the sky)."
            ),
            pingButton = file.Bind(
                Section,
                "PingButton",
                "None",
                new ConfigDescription(
                    "Button fallback for the game's ping, aimed with the hand chosen in Hand. Off by default because "
                        + "the face buttons and stick clicks already drive movement, drop and menus; pick one that "
                        + "no other setting uses.",
                    new AcceptableValueList<string>(Buttons)
                )
            ),
            markerAngle = file.Bind(
                Section,
                "MarkerAngle",
                8f,
                new ConfigDescription(
                    "Apparent size of the ping marker in degrees of view near you; far away the game's own size "
                        + "limit takes over, as in flat play. The flat game sizes it from the monitor's field of view "
                        + "(8 degrees matches a 70 degree monitor view), which the headset's wide eye view makes look wrong.",
                    new AcceptableValueRange<float>(2f, 15f)
                )
            ),
        };
    }
}
