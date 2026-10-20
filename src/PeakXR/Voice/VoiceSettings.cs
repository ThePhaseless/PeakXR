using BepInEx.Configuration;

namespace PeakXR.Voice;

/// <summary>Config entries for VR voice chat. Read live every frame, so changes apply at once.</summary>
internal sealed class VoiceSettings
{
    public ConfigEntry<bool> HandToMouthTalk;
    public ConfigEntry<bool> PreferHeadsetMic;
    public ConfigEntry<bool> SpeakingIcons;
    public ConfigEntry<float> SpeakingIconRange;
    public ConfigEntry<float> SpeakingIconSize;

    public static VoiceSettings Bind(ConfigFile cfg)
    {
        const string section = "Voice";
        return new VoiceSettings
        {
            HandToMouthTalk = cfg.Bind(
                section,
                "HandToMouthTalk",
                // On by default: every controller button already has a game action (docs/controls.md),
                // and the gesture only matters in the game's push-to-talk modes.
                true,
                "Push-to-talk: hold a free hand at your mouth like a walkie-talkie. Only matters when the game's microphone mode is Push To Talk or Push To Mute."
            ),
            PreferHeadsetMic = cfg.Bind(
                section,
                "PreferHeadsetMic",
                true,
                "In VR, switch the game's microphone to the headset mic once, when the game is still on its default device. A device you picked yourself is never changed."
            ),
            SpeakingIcons = cfg.Bind(
                section,
                "SpeakingIcons",
                true,
                "Show a small speaking icon above other players while they talk, without having to look at them."
            ),
            SpeakingIconRange = cfg.Bind(
                section,
                "SpeakingIconRange",
                40f,
                new ConfigDescription(
                    "Speaking icons show for players within this distance, in metres.",
                    new AcceptableValueRange<float>(5f, 200f)
                )
            ),
            SpeakingIconSize = cfg.Bind(
                section,
                "SpeakingIconSize",
                1.5f,
                new ConfigDescription(
                    "Size of a speaking icon as seen from your head, in degrees of view (the same at any distance).",
                    new AcceptableValueRange<float>(0.5f, 5f)
                )
            ),
        };
    }
}
