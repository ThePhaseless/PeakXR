using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>What the environment rumble is feeling. Each has its own texture so the hands can tell them apart.</summary>
internal enum EnvSource
{
    /// <summary>A hand at or in a lit campfire's flames.</summary>
    Fire,

    /// <summary>Wind, a tornado's pull: the force the game pushes the body with.</summary>
    Wind,

    /// <summary>The game raising Hot: desert sun, lava heat.</summary>
    Heat,

    /// <summary>The game raising Cold: night, fog, snowstorm.</summary>
    Cold,

    /// <summary>A hazard zone raising another status: spore clouds and other <c>StatusEmitter</c> zones.</summary>
    Hazard,
}

/// <summary>
/// The environment as one unbroken rumble per hand. Features feed each source's level every frame
/// (<see cref="Feed"/>: 0 to 1, felt strength at the default slider); here each level eases toward what was fed
/// (a source not fed for <see cref="Hold"/> eases to 0), gets its source's noise texture, and all are mixed with
/// <see cref="EnvironmentFeel.Mix"/> so the sum never passes full. The mix goes out every frame as an impulse longer
/// than a frame (<see cref="SteadyRumble.Seconds"/>), so there are no gaps, at
/// <see cref="HapticPriority.Environment"/>: any other pulse (a cue, a heartbeat) takes the hand and the rumble
/// carries on after it. <c>Interaction.HapticStrength</c> scales it like every pulse.
/// </summary>
internal sealed class EnvironmentRumble : MonoBehaviour
{
    private const int Sources = 5;

    /// <summary>Seconds a fed level holds; wind is fed from FixedUpdate, which can skip render frames.</summary>
    private const float Hold = 0.15f;

    /// <summary>Time constant (s) of each level's easing: onsets and fades are smooth, never a step.</summary>
    private const float EaseSeconds = 0.2f;

    /// <summary>Below this felt strength nothing is sent (the last impulse runs out unfelt).</summary>
    private const float MinFelt = 0.005f;

    // Texture per source (index = EnvSource): noise rate (Hz) and how deep it dips. Fire crackles, wind gusts,
    // heat swells slowly, cold shivers finely, a hazard throbs.
    private static readonly float[] Frequency = { 6f, 0.7f, 0.35f, 9f, 2.5f };
    private static readonly float[] Depth = { 0.35f, 0.45f, 0.2f, 0.4f, 0.6f };

    private static readonly float[] Target = new float[Sources * 2];
    private static readonly float[] FedAt = NewFedAt();
    private static readonly int[] FedFrame = new int[Sources * 2];
    private static readonly float[] Levels = new float[Sources * 2];
    private static readonly float[] Mixed = new float[2];

    private static EnvironmentRumble instance;

    private static float[] NewFedAt()
    {
        var t = new float[Sources * 2];
        for (int i = 0; i < t.Length; i++)
            t[i] = float.NegativeInfinity;
        return t;
    }

    /// <summary>This frame's level (0 to 1) of <paramref name="source"/> on one hand; the strongest feed of a frame
    /// wins.</summary>
    public static void Feed(EnvSource source, bool left, float level)
    {
        if (!VRSession.Active)
            return;
        if (instance == null)
        {
            var host = new GameObject("PeakXR.EnvironmentRumble")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EnvironmentRumble>();
        }
        int i = Index(source, left);
        float v = Mathf.Clamp01(level);
        if (FedFrame[i] == Time.frameCount)
            v = Mathf.Max(v, Target[i]);
        FedFrame[i] = Time.frameCount;
        Target[i] = v;
        FedAt[i] = Time.unscaledTime;
    }

    /// <summary>The eased level of one source on one hand (self-test).</summary>
    public static float Level(EnvSource source, bool left) => Levels[Index(source, left)];

    /// <summary>The felt strength last sent to one hand, all sources mixed (self-test).</summary>
    public static float Felt(bool left) => Mixed[left ? 0 : 1];

    private static int Index(EnvSource source, bool left) => (int)source * 2 + (left ? 0 : 1);

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        bool active = VRSession.Active;
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            float mix = 0f;
            for (int s = 0; s < Sources; s++)
            {
                int i = s * 2 + h;
                float target = active && now - FedAt[i] <= Hold ? Target[i] : 0f;
                Levels[i] = EnvironmentFeel.Ease(Levels[i], target, dt, EaseSeconds);
                mix = EnvironmentFeel.Mix(
                    mix,
                    EnvironmentFeel.Textured(
                        Levels[i],
                        now,
                        Frequency[s],
                        Depth[s],
                        (uint)(i * 7919 + 13)
                    )
                );
            }
            Mixed[h] = mix;
            // Felt strength is mix x slider: the pulse API applies the gameplay scale, so undo it here.
            if (mix > MinFelt)
                Haptics.Pulse(
                    left,
                    mix / HapticScale.Cue,
                    SteadyRumble.Seconds,
                    HapticPriority.Environment
                );
        }
    }
}
