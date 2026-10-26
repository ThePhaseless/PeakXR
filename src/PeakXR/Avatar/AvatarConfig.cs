using BepInEx.Configuration;

namespace PeakXR.Avatar;

/// <summary>Avatar settings, bound by <see cref="AvatarFeature"/>. Read every frame through cheap properties.</summary>
internal sealed class AvatarConfig
{
    public const string ShowVanilla = "Vanilla";
    public const string ShowArms = "Arms";
    public const string ShowAll = "Everything";

    /// <summary>Defaults stand in until the feature binds the real entries (and in unit-less contexts).</summary>
    public static AvatarConfig Instance { get; private set; } = new();

    private ConfigEntry<bool> armIK,
        fingers,
        headLook,
        bodyLean,
        retargetItems,
        reachGhostHand;
    private ConfigEntry<string> ownBody;
    private ConfigEntry<float> wristBack,
        wristSide,
        wristUp,
        handPitch,
        handYaw,
        handRoll;

    public bool ArmIK => armIK?.Value ?? true;
    public bool Fingers => fingers?.Value ?? true;
    public bool HeadLook => headLook?.Value ?? true;
    public bool BodyLean => bodyLean?.Value ?? true;
    public bool RetargetItems => retargetItems?.Value ?? true;
    public bool ReachGhostHand => reachGhostHand?.Value ?? true;
    public string OwnBody => ownBody?.Value ?? ShowArms;
    public ConfigEntry<string> OwnBodyEntry => ownBody;
    public float WristBack => wristBack?.Value ?? 0.07f;
    public float WristSide => wristSide?.Value ?? 0f;
    public float WristUp => wristUp?.Value ?? 0f;
    public float HandPitch => handPitch?.Value ?? 0f;
    public float HandYaw => handYaw?.Value ?? 0f;
    public float HandRoll => handRoll?.Value ?? 0f;

    public static void Bind(ConfigFile file)
    {
        const string s = "Avatar";
        var c = new AvatarConfig
        {
            armIK = file.Bind(
                s,
                "ArmIK",
                true,
                "Arms follow tracked hands (yours and other modded players'). Off: vanilla arm animation."
            ),
            fingers = file.Bind(
                s,
                "FingerCurls",
                true,
                "Curl the character's fingers from controller touch/grip, with grip poses when holding items."
            ),
            headLook = file.Bind(
                s,
                "HeadLook",
                true,
                "Other modded players' heads follow their headset, including tilt."
            ),
            bodyLean = file.Bind(
                s,
                "BodyLean",
                true,
                "The character's upper body leans toward your headset (yours and other modded players'), so its "
                    + "shoulders and hands come along when you lean over something."
            ),
            reachGhostHand = file.Bind(
                s,
                "ReachGhostHand",
                true,
                "When your real hand reaches further than your character's arm can (a deep stoop, a long reach), a "
                    + "faint ghost hand shows where your hand really is. Only you see it."
            ),
            retargetItems = file.Bind(
                s,
                "ItemsFollowHand",
                true,
                "Held items sit in the hand that holds them instead of in front of the face. Only changes what you see."
            ),
            ownBody = file.Bind(
                s,
                "OwnBody",
                ShowArms,
                new ConfigDescription(
                    "What of your own body you see in VR. Vanilla: the game's first-person hiding. Arms: body and arms "
                        + "visible, head, face and hats hidden. Everything: nothing hidden.",
                    new AcceptableValueList<string>(ShowVanilla, ShowArms, ShowAll)
                )
            ),
            wristBack = file.Bind(
                s,
                "WristOffsetBack",
                0.07f,
                new ConfigDescription(
                    "Metres from the controller grip back toward the forearm to the wrist.",
                    new AcceptableValueRange<float>(-0.1f, 0.25f)
                )
            ),
            wristSide = file.Bind(
                s,
                "WristOffsetSide",
                0f,
                new ConfigDescription(
                    "Metres from the controller grip toward the back of the hand (mirrored for the left hand).",
                    new AcceptableValueRange<float>(-0.1f, 0.1f)
                )
            ),
            wristUp = file.Bind(
                s,
                "WristOffsetUp",
                0f,
                new ConfigDescription(
                    "Metres from the controller grip toward the thumb side to the wrist.",
                    new AcceptableValueRange<float>(-0.1f, 0.1f)
                )
            ),
            handPitch = file.Bind(
                s,
                "HandPitch",
                0f,
                new ConfigDescription(
                    "Hand rotation tweak in degrees (pitch).",
                    new AcceptableValueRange<float>(-90f, 90f)
                )
            ),
            handYaw = file.Bind(
                s,
                "HandYaw",
                0f,
                new ConfigDescription(
                    "Hand rotation tweak in degrees (yaw, mirrored for the left hand).",
                    new AcceptableValueRange<float>(-90f, 90f)
                )
            ),
            handRoll = file.Bind(
                s,
                "HandRoll",
                0f,
                new ConfigDescription(
                    "Hand rotation tweak in degrees (roll, mirrored for the left hand).",
                    new AcceptableValueRange<float>(-90f, 90f)
                )
            ),
        };
        Instance = c;
    }
}
