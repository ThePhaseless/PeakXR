using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Interact.Climb;

/// <summary>Climb feels with no cue-core word: recorded so the self-test can prove them.</summary>
internal enum ClimbCue
{
    /// <summary>A falling catch's ratchet (<see cref="ClimbFeel.Catch"/>).</summary>
    Catch,

    /// <summary>The surface texture under a climbing hand (<see cref="SurfaceTexture"/>).</summary>
    Texture,

    /// <summary>A vine slide's whirr, haptic and sound.</summary>
    Whirr,

    /// <summary>The lunge's rush of air.</summary>
    Whoosh,
}

/// <summary>
/// The climb-only haptics and sounds (catch, texture, vine whirr, lunge whoosh). Haptics go through
/// <see cref="Haptics"/>, so <c>Interaction.Haptics</c> mutes them; sounds go through the game's <c>SFX_Player</c>
/// (SFX_Player.cs:83) like the cue sounds, so the game's SFX volume applies. Each records when it last reached a hand
/// on the cue clock (<see cref="UI.Affordances.CueLog.Now"/>) for the self-test. Local only; allocation-free after
/// the first sound.
/// </summary>
internal static class ClimbCues
{
    private const int Count = (int)ClimbCue.Whoosh + 1;
    private const int Rate = 44100;
    private static readonly float[] At = NewTimes();
    private static readonly float[] Amplitude = new float[Count * 2];
    private static readonly SFX_Instance[] Sounds = new SFX_Instance[2];
    private static bool soundFailed;

    private static float[] NewTimes()
    {
        var t = new float[Count * 2];
        for (int i = 0; i < t.Length; i++)
            t[i] = float.NegativeInfinity;
        return t;
    }

    /// <summary>A pulse of <paramref name="cue"/> on that hand.</summary>
    public static void Pulse(ClimbCue cue, bool left, float amplitude, float seconds)
    {
        var priority = cue == ClimbCue.Texture ? HapticPriority.Ambient : HapticPriority.Cue;
        if (!Haptics.Pulse(left, amplitude, seconds, priority) && !Haptics.Muted)
            return;
        int i = (int)cue * 2 + (left ? 0 : 1);
        At[i] = UI.Affordances.CueLog.Now;
        Amplitude[i] = amplitude;
    }

    /// <summary>The <paramref name="cue"/>'s sound at <paramref name="position"/>, recorded on both hands' slots.</summary>
    public static void Sound(ClimbCue cue, ClimbSound sound, Vector3 position)
    {
        var player = SFX_Player.instance;
        var sfx = Instance(sound);
        if (player == null || MainCamera.instance == null || sfx == null)
            return;
        player.PlaySFX(sfx, position, null, null, 1f);
        int i = (int)cue * 2;
        At[i] = At[i + 1] = UI.Affordances.CueLog.Now;
    }

    /// <summary>True when <paramref name="cue"/> reached that hand since <paramref name="since"/> (self-test).</summary>
    public static bool Since(ClimbCue cue, bool left, float since, out float amplitude)
    {
        int i = (int)cue * 2 + (left ? 0 : 1);
        amplitude = Amplitude[i];
        return At[i] >= since;
    }

    private static SFX_Instance Instance(ClimbSound sound)
    {
        ref var sfx = ref Sounds[(int)sound];
        if (sfx != null || soundFailed)
            return sfx;
        try
        {
            var samples = ClimbTones.Make(sound, Rate);
            var clip = AudioClip.Create("PeakXR_Climb_" + sound, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            sfx = ScriptableObject.CreateInstance<SFX_Instance>();
            sfx.name = "PeakXR_Climb_" + sound;
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
                cooldown = 0.1f,
            };
            sfx.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (System.Exception e)
        {
            // Sound is a nicety: the haptics still carry it.
            soundFailed = true;
            Plugin.Log.LogWarning($"[interact.climb] climb sounds off: {e.Message}");
            sfx = null;
        }
        return sfx;
    }
}
