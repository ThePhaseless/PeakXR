using PeakXR.Framework;

namespace PeakXR.Voice;

/// <summary>
/// voice: makes the game's proximity voice chat usable in a headset. Push-to-talk by a hand at the mouth like a
/// walkie-talkie (or an optional button, <see cref="VoiceInput"/>), mute by a flat palm over the mouth, a click and a
/// mic icon at the hand and wrist for your own mic, the headset mic picked once when the
/// game is still on its default device, and speaking icons above remote players. Playback, muffling,
/// lip sync and the raven voice thief stay vanilla: they follow the MainCamera listener and the
/// character's own mouth.
/// </summary>
public sealed class VoiceFeature : Feature
{
    public override string Id => "voice";

    public override int Order => 55;

    internal static VoiceSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = VoiceSettings.Bind(Plugin.Settings);
        Patch(typeof(PushToTalkSamplePatch));
        MicGatePatch.Resolve();
        Patch(typeof(MicGatePatch));
        VoiceRuntime.Create();
    }
}
