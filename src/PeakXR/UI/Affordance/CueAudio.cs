using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// The cue sounds through the game's own SFX system: one runtime <c>SFX_Instance</c> per <see cref="CueSound"/>
/// with a clip synthesised once (<see cref="CueTones"/>), played by <c>SFX_Player.PlaySFX</c> (SFX_Player.cs:83),
/// which pools its sources, routes them to its default mixer group (so the game's SFX volume applies) and plays them
/// 3D at the spot. Quiet, no pitch or volume randomness, a short range, and the instance cooldown on top of the
/// service's own rate limit.
/// </summary>
internal static class CueAudio
{
    private const int Rate = 44100;
    private static readonly SFX_Instance[] Instances = new SFX_Instance[3];
    private static bool failed;

    public static void Play(CueSound sound, Vector3 position, float volume)
    {
        var player = SFX_Player.instance;
        // PlaySFX reads MainCamera for its range check (SFX_Player.cs:95).
        if (failed || player == null || MainCamera.instance == null)
            return;
        var sfx = Instance(sound);
        if (sfx != null)
            player.PlaySFX(sfx, position, null, null, volume);
    }

    private static SFX_Instance Instance(CueSound sound)
    {
        ref var sfx = ref Instances[(int)sound];
        if (sfx != null)
            return sfx;
        try
        {
            var samples = CueTones.Make(sound, Rate);
            var clip = AudioClip.Create("PeakXR_Cue_" + sound, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
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
            // Runtime assets, not scene objects: keep them through scene loads and asset unloads.
            sfx.hideFlags = clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (System.Exception e)
        {
            // Sound is a nicety: rings and haptics still carry the cue.
            failed = true;
            Plugin.Log.LogWarning($"[ui.affordance] cue sounds off: {e.Message}");
            sfx = null;
        }
        return sfx;
    }
}
