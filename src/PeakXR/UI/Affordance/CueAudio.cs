using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// The cue sounds through the game's own SFX system: one runtime <c>SFX_Instance</c> per <see cref="CueSound"/>
/// with a clip synthesised once (<see cref="CueTones"/>), played by <c>SFX_Player.PlaySFX</c> (SFX_Player.cs:83),
/// which pools its sources, routes them to its default mixer group (so the game's SFX volume applies) and plays them
/// 3D at the spot. Quiet, no pitch or volume randomness, a short range, and the instance cooldown on top of the
/// service's own rate limit. Where the game has no SFX player or main camera (menus, loading), a small pool of our
/// own sources plays the same clip instead, with no mixer group (so it never touches the game's mixer state), so a
/// cue is never silent.
/// </summary>
internal static class CueAudio
{
    private const int Rate = 44100;
    private const int Count = (int)CueSound.Danger + 1;
    private const int FallbackSources = 4;
    private static readonly SFX_Instance[] Instances = new SFX_Instance[Count];
    private static readonly AudioClip[] Clips = new AudioClip[Count];
    private static readonly AudioSource[] Fallback = new AudioSource[FallbackSources];
    private static int nextFallback;
    private static bool failed;

    /// <summary>Self-test only: play through the fallback sources even when the game's SFX player is up.</summary>
    internal static bool ForceFallback;

    /// <summary>The last sound went through the fallback sources (self-test).</summary>
    internal static bool LastWasFallback { get; private set; }

    /// <summary>Plays the cue; false only when neither path could play it (clips failed to build).</summary>
    public static bool Play(CueSound sound, Vector3 position, float volume)
    {
        bool played = TryPlay(sound, position, volume);
        CueLog.Capture.Sound(sound, position.N(), played, Time.unscaledTime);
        return played;
    }

    private static bool TryPlay(CueSound sound, Vector3 position, float volume)
    {
        if (failed || (int)sound >= Count)
            return false;
        var player = SFX_Player.instance;
        // PlaySFX reads MainCamera for its range check (SFX_Player.cs:95).
        if (!ForceFallback && player != null && MainCamera.instance != null)
        {
            var sfx = Instance(sound);
            if (sfx == null)
                return false;
            player.PlaySFX(sfx, position, null, null, volume);
            LastWasFallback = false;
            return true;
        }
        return PlayFallback(sound, position, volume);
    }

    private static bool PlayFallback(CueSound sound, Vector3 position, float volume)
    {
        var clip = Clip(sound);
        if (clip == null)
            return false;
        var source = Fallback[nextFallback];
        if (source == null)
        {
            var go = new GameObject("PeakXR_CueAudio" + nextFallback);
            Object.DontDestroyOnLoad(go);
            source = Fallback[nextFallback] = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 25f;
            // No mixer group: the game's mixer snapshots and volumes stay untouched.
            source.outputAudioMixerGroup = null;
            // Plays while the game is paused (AudioListener.pause would silence the pause-menu cue).
            source.ignoreListenerPause = true;
        }
        nextFallback = (nextFallback + 1) % FallbackSources;
        source.transform.position = position;
        source.Stop();
        source.clip = clip;
        source.volume = 0.3f * volume;
        source.Play();
        LastWasFallback = true;
        return true;
    }

    private static AudioClip Clip(CueSound sound)
    {
        ref var clip = ref Clips[(int)sound];
        if (clip != null || failed)
            return clip;
        try
        {
            var samples = CueTones.Make(sound, Rate);
            clip = AudioClip.Create("PeakXR_Cue_" + sound, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            // A runtime asset, not a scene object: keep it through scene loads and asset unloads.
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (System.Exception e)
        {
            // Sound is a nicety: rings and haptics still carry the cue.
            failed = true;
            Plugin.Log.LogWarning($"[ui.affordance] cue sounds off: {e.Message}");
            clip = null;
        }
        return clip;
    }

    private static SFX_Instance Instance(CueSound sound)
    {
        ref var sfx = ref Instances[(int)sound];
        if (sfx != null)
            return sfx;
        var clip = Clip(sound);
        if (clip == null)
            return null;
        sfx = ScriptableObject.CreateInstance<SFX_Instance>();
        sfx.name = "PeakXR_Cue_" + sound;
        sfx.clips = new[] { clip };
        sfx.settings = new SFX_Settings
        {
            volume = 0.3f,
            volume_Variation = 0f,
            pitch = 1f,
            pitch_Variation = 0f,
            spatialBlend = 1f,
            dopplerLevel = 0f,
            range = 25f,
            cooldown = 0.12f,
        };
        sfx.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sfx;
    }
}
