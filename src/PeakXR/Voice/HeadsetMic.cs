using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Voice;

/// <summary>
/// Selects the headset microphone once per VR session in the game's own setting (MicrophoneSetting.cs:22
/// <c>Value</c>), which CharacterVoiceHandler.cs:254-258 hands to the recorder on its next update. Not saved
/// (<c>SetValue</c>, MicrophoneSetting.cs:97-102, would write it to disk), and put back when VR stops, so flat play,
/// this session or the next launch, keeps the player's own microphone. Retries for a while because the HMD's audio
/// device can appear after the game starts.
/// </summary>
internal sealed class HeadsetMic
{
    private const float RetrySeconds = 3f;
    private const int MaxTries = 10;

    private bool done;
    private int tries;
    private float nextAt;

    /// <summary>The microphone before the switch, and the headset mic switched to (null: no switch to undo).</summary>
    internal MicrophoneSetting.MicrophoneInfo Previous { get; private set; }
    internal string Chosen { get; private set; }

    public void Tick(bool enabled)
    {
        if (done || !enabled || Time.unscaledTime < nextAt)
            return;
        nextAt = Time.unscaledTime + RetrySeconds;

        var setting = Setting();
        if (setting == null)
            return;

        var devices = Microphone.devices;
        var current = setting.Value.id;
        var index = VoiceMic.PreferredIndex(devices, current);
        if (index >= 0)
        {
            Plugin.Log.LogInfo($"[voice] microphone: '{current}' -> headset '{devices[index]}'");
            Previous = setting.Value;
            Chosen = devices[index];
            setting.Value = new MicrophoneSetting.MicrophoneInfo { id = Chosen, name = Chosen };
            done = true;
            return;
        }
        // A non-default choice is the player's own; leave it for the rest of the session.
        var explicitChoice =
            devices.Length > 0 && !string.IsNullOrEmpty(current) && current != devices[0];
        if (explicitChoice || ++tries >= MaxTries)
            done = true;
    }

    /// <summary>VR stopped: back to the microphone from before the switch, unless the player picked another since;
    /// a restart picks again.</summary>
    public void Restore()
    {
        var setting = Chosen != null ? Setting() : null;
        if (setting != null && setting.Value.id == Chosen)
        {
            var back = Previous;
            // The voice handler skips an empty id (CharacterVoiceHandler.cs:255): name the game's default instead.
            var devices = Microphone.devices;
            if (string.IsNullOrEmpty(back.id) && devices.Length > 0)
                back = new MicrophoneSetting.MicrophoneInfo { id = devices[0], name = devices[0] };
            Plugin.Log.LogInfo($"[voice] microphone back to '{back.id}' (VR stopped)");
            setting.Value = back;
        }
        Chosen = null;
        done = false;
        tries = 0;
        nextAt = 0f;
    }

    private static MicrophoneSetting Setting() =>
        SettingsHandler.Instance != null
            ? SettingsHandler.Instance.GetSetting<MicrophoneSetting>()
            : null;
}
