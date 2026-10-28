using PeakXR.Core;
using UnityEngine;

namespace PeakXR.VR;

/// <summary>
/// Head-locked fade to black. Any system can request a fade level; the strongest request wins.
/// Used instead of hard cuts whenever the game moves or takes over the camera (comfort).
/// Rendering is done by the UI feature; requesters only call <see cref="Set"/> and <see cref="Clear"/>.
/// A one-shot holder that might never clear passes a safety timeout (<see cref="FadeHolds"/>).
/// </summary>
public static class VRFade
{
    private static readonly FadeHolds holds = new();

    /// <summary>0 = clear, 1 = black. The renderer eases toward this value.</summary>
    public static float Target => holds.Target(Time.unscaledTime);

    public static void Set(object owner, float alpha) => holds.Set(owner, alpha);

    /// <summary>A hold that lapses on its own after <paramref name="timeoutSeconds"/> (real time) unless set
    /// again or cleared first.</summary>
    public static void Set(object owner, float alpha, float timeoutSeconds) =>
        holds.Set(owner, alpha, Time.unscaledTime + timeoutSeconds);

    public static void Clear(object owner) => holds.Clear(owner);
}
