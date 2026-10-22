using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Voice;

/// <summary>Config entries for VR voice chat. Read live every frame, so changes apply at once.</summary>
internal sealed class VoiceSettings
{
    public ConfigEntry<bool> HandToMouthTalk;
    public ConfigEntry<bool> PalmMute;
    public ConfigEntry<string> PushToTalkButton;
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
                // The main way to talk (D4): it needs no button, and every button already has a job.
                true,
                "Push-to-talk by holding a free hand at your mouth like a walkie-talkie. A click and a mic icon by the hand show the mic is open. Only matters when the game's microphone mode is Push To Talk; in Voice Activation the mic is always open."
            ),
            PalmMute = cfg.Bind(
                section,
                "PalmMute",
                true,
                "Mute while a flat, open palm covers your mouth: a muffled click and a red mic icon show it. Works in every microphone mode without changing the game's setting."
            ),
            PushToTalkButton = cfg.Bind(
                section,
                "PushToTalkButton",
                "None",
                new ConfigDescription(
                    "A button that also works as the game's push-to-talk key while held. None by default: every button already has a job (jump, crouch, sprint, ping, drop, pocket), so the button you pick still does that too. The log names the clash.",
                    new AcceptableValueList<string>(ButtonName.All)
                )
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
