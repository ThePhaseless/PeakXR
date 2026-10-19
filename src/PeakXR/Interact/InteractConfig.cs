using BepInEx.Configuration;
using PeakXR.VR;

namespace PeakXR.Interact;

/// <summary>Physical interaction settings, bound by <see cref="PhysicalInteractionFeature"/>.</summary>
internal sealed class InteractConfig
{
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

    public static InteractConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> physicalGrab,
        handAim,
        triggerUse,
        eatAtMouth,
        physicalThrow,
        releaseToDrop,
        physicalClimb,
        haptics;
    private ConfigEntry<float> grabRadius,
        throwSpeed,
        throwScale,
        climbRadius,
        climbGain,
        hapticStrength;
    private ConfigEntry<string> dropButton;

    public bool PhysicalGrab => physicalGrab?.Value ?? true;
    public bool HandAimedInteract => handAim?.Value ?? true;
    public bool TriggerUse => triggerUse?.Value ?? true;
    public bool EatAtMouth => eatAtMouth?.Value ?? true;
    public bool PhysicalThrow => physicalThrow?.Value ?? true;
    public bool ReleaseToDrop => releaseToDrop?.Value ?? false;
    public bool PhysicalClimb => physicalClimb?.Value ?? true;
    public bool Haptics => haptics?.Value ?? true;
    public float GrabRadius => grabRadius?.Value ?? 0.12f;
    public float ThrowSpeed => throwSpeed?.Value ?? 2f;
    public float ThrowScale => throwScale?.Value ?? 1f;
    public float ClimbRadius => climbRadius?.Value ?? 0.1f;
    public float ClimbGain => climbGain?.Value ?? 1f;
    public float HapticStrength => hapticStrength?.Value ?? 0.5f;

    /// <summary>State of the configured drop/throw-charge button this frame.</summary>
    public bool DropButtonDown()
    {
        switch (dropButton?.Value ?? "RightSecondary")
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
        const string s = "Interaction";
        Instance = new InteractConfig
        {
            physicalGrab = file.Bind(
                s,
                "GrabToPickUp",
                true,
                "Close your grip near an item, rope, vine or handle to grab it through the game's normal pickup. "
                    + "Off, or with nothing in reach: grip works as the interact button (aimed by head, or by a pointing hand)."
            ),
            grabRadius = file.Bind(
                s,
                "GrabRadius",
                0.12f,
                new ConfigDescription(
                    "How far from the hand (metres) things can be grabbed.",
                    new AcceptableValueRange<float>(0.05f, 0.3f)
                )
            ),
            handAim = file.Bind(
                s,
                "HandAimedInteract",
                true,
                "Point (grip closed, index straight) to aim interaction with that hand instead of your head."
            ),
            triggerUse = file.Bind(
                s,
                "TriggerUse",
                true,
                "Trigger of the hand holding the item uses it (primary), the other trigger is secondary use. "
                    + "Empty-handed: right trigger climbs like the game's button, left trigger reaches for friends."
            ),
            eatAtMouth = file.Bind(
                s,
                "EatAtMouth",
                true,
                "Bring food to your mouth to eat it (holds primary use while it stays there). Trigger still works."
            ),
            physicalThrow = file.Bind(
                s,
                "ThrowByHand",
                true,
                "Let go of the grip while swinging the holding hand to throw the item with your hand's velocity."
            ),
            throwSpeed = file.Bind(
                s,
                "ThrowMinSpeed",
                2f,
                new ConfigDescription(
                    "Hand speed (m/s) at release that counts as a throw.",
                    new AcceptableValueRange<float>(0.5f, 6f)
                )
            ),
            throwScale = file.Bind(
                s,
                "ThrowVelocityScale",
                1f,
                new ConfigDescription(
                    "Multiplier on the hand's release velocity.",
                    new AcceptableValueRange<float>(0.5f, 2.5f)
                )
            ),
            releaseToDrop = file.Bind(
                s,
                "ReleaseToDrop",
                false,
                "A slow grip release drops the held item. Off: the item stays in hand until thrown or dropped with the button."
            ),
            dropButton = file.Bind(
                s,
                "DropButton",
                "RightSecondary",
                new ConfigDescription(
                    "Button fallback for the game's drop: hold to charge a throw, release to throw or drop.",
                    new AcceptableValueList<string>(Buttons)
                )
            ),
            physicalClimb = file.Bind(
                s,
                "GrabToClimb",
                true,
                "Grip a wall to start climbing and pull yourself with your arms. Uses the game's climbing and stamina. "
                    + "Let go with both hands to fall. The trigger climb (game button) keeps working."
            ),
            climbRadius = file.Bind(
                s,
                "ClimbGrabRadius",
                0.1f,
                new ConfigDescription(
                    "How close (metres) a hand must be to a wall to grab it.",
                    new AcceptableValueRange<float>(0.03f, 0.25f)
                )
            ),
            climbGain = file.Bind(
                s,
                "ClimbArmGain",
                1f,
                new ConfigDescription(
                    "Body movement per metre of arm pull while climbing.",
                    new AcceptableValueRange<float>(0.5f, 2f)
                )
            ),
            haptics = file.Bind(
                s,
                "Haptics",
                true,
                "Controller vibration on grab, use and climb grip."
            ),
            hapticStrength = file.Bind(
                s,
                "HapticStrength",
                0.5f,
                new ConfigDescription(
                    "Vibration strength.",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            ),
        };
    }
}
