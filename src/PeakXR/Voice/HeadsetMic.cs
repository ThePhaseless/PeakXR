using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Voice;

/// <summary>
/// Selects the headset microphone once per session through the game's own setting (MicrophoneSetting.cs:97
/// <c>SetValue</c>, which saves it); CharacterVoiceHandler.cs:258-263 then hands the new device to the
/// recorder. Retries for a while because the HMD's audio device can appear after the game starts.
/// </summary>
internal sealed class HeadsetMic
{
    private const float RetrySeconds = 3f;
    private const int MaxTries = 10;

    private bool done;
    private int tries;
    private float nextAt;

    public void Tick(bool enabled)
    {
        if (done || !enabled || Time.unscaledTime < nextAt)
            return;
        nextAt = Time.unscaledTime + RetrySeconds;

        var handler = SettingsHandler.Instance;
        if (handler == null)
            return;
        var setting = handler.GetSetting<MicrophoneSetting>();
        if (setting == null)
            return;

        var devices = Microphone.devices;
        var current = setting.Value.id;
        var index = VoiceMic.PreferredIndex(devices, current);
        if (index >= 0)
        {
            Plugin.Log.LogInfo($"[voice] microphone: '{current}' -> headset '{devices[index]}'");
            setting.SetValue(index, handler, fromUI: false);
            done = true;
            return;
        }
        // A non-default choice is the player's own; leave it for the rest of the session.
        var explicitChoice =
            devices.Length > 0 && !string.IsNullOrEmpty(current) && current != devices[0];
        if (explicitChoice || ++tries >= MaxTries)
            done = true;
    }
}
