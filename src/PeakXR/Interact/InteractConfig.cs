using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.VR;

namespace PeakXR.Interact;

/// <summary>Physical interaction settings, bound by <see cref="PhysicalInteractionFeature"/>.</summary>
internal sealed class InteractConfig
{
    public static InteractConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> physicalGrab,
        handAim,
        triggerUse,
        eatAtMouth,
        physicalThrow,
        releaseToDrop,
        physicalClimb,
        staminaHaptics,
        armLunge,
        haptics;
    private ConfigEntry<float> grabRadius,
        throwSpeed,
        throwScale,
        climbRadius,
        climbGain,
        regripGrace,
        lungeSpeed,
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
    public float ClimbRegripGrace => regripGrace?.Value ?? 0.25f;
    public bool StaminaHaptics => staminaHaptics?.Value ?? true;
    public bool ArmLunge => armLunge?.Value ?? true;
    public float LungeSpeed => lungeSpeed?.Value ?? 2.5f;
    public float HapticStrength => hapticStrength?.Value ?? 0.5f;

    /// <summary>The configured drop button (<c>DominantSecondary</c>, <c>LeftPrimary</c> ... or <c>None</c>), so the
    /// locomotion layout can keep its own actions off it. Parse with <see cref="ButtonName.TryParse"/>.</summary>
    public string DropButton => dropButton?.Value ?? DefaultDropButton;

    private const string DefaultDropButton = "DominantSecondary";

    /// <summary>State of the configured drop/throw-charge button this frame.</summary>
    public bool DropButtonDown()
    {
        if (!ButtonName.TryParse(DropButton, VRInput.LeftHanded, out bool left, out var control))
            return false;
        ref var h = ref VRInput.Hand(left);
        return control switch
        {
            VRControl.Primary => h.Primary,
            VRControl.Secondary => h.Secondary,
            VRControl.StickClick => h.StickClick,
            _ => false,
        };
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
                DefaultDropButton,
                new ConfigDescription(
                    "Button fallback for the game's drop: hold to charge a throw, release to throw or drop. "
                        + "Dominant and OffHand follow Input.DominantHand; Left and Right stay on one controller.",
                    new AcceptableValueList<string>(ButtonName.All)
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
            regripGrace = file.Bind(
                s,
                "ClimbRegripGrace",
                0.25f,
                new ConfigDescription(
                    "Seconds a grip climb survives with no hand on the wall, so hand-over-hand regrips do not drop you. "
                        + "0 ends the climb the moment both hands let go.",
                    new AcceptableValueRange<float>(0f, 0.6f)
                )
            ),
            staminaHaptics = file.Bind(
                s,
                "StaminaHaptics",
                true,
                "While climbing, the gripping hands pulse faster as stamina runs low, beat when it is gone and buzz while sliding."
            ),
            armLunge = file.Bind(
                s,
                "ArmLunge",
                true,
                "Throw your last climbing hand upward as you let go to do the game's climb jump (lunge) in that direction. "
                    + "Sprint plus stick keeps working."
            ),
            lungeSpeed = file.Bind(
                s,
                "LungeSpeed",
                2.5f,
                new ConfigDescription(
                    "Upward hand speed (m/s, relative to your body) at release that counts as a lunge.",
                    new AcceptableValueRange<float>(1f, 6f)
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
        // Before hand roles existed the default was written to every config file as RightSecondary. That is the
        // same button as DominantSecondary for a right-handed player, so move it to the role name and let it
        // follow Input.DominantHand (a left-handed player who wants the right B button can pick it again).
        var drop = Instance.dropButton;
        if (drop.Value == "RightSecondary")
            drop.Value = DefaultDropButton;
    }
}
