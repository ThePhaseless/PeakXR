using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Common;

/// <summary>
/// The eased fade level actually drawn, shared by every fade sphere (the rig head and the loading
/// space camera) so they always agree. Ticked once per frame by whichever UI runtime runs first.
/// </summary>
internal static class FadeView
{
    private static readonly FadeEaser easer = new();
    private static int lastFrame = -1;

    /// <summary>Seconds for a full black-to-clear sweep. Set from the UI settings each frame.</summary>
    public static float Seconds = 0.3f;

    /// <summary>0 = clear, 1 = black.</summary>
    public static float Alpha => easer.Alpha;

    /// <summary>Go black at once, for a scene cut that happened without warning.</summary>
    public static void Snap(float alpha) => easer.Snap(alpha);

    public static void Tick()
    {
        if (lastFrame == Time.frameCount)
            return;
        lastFrame = Time.frameCount;
        easer.Seconds = Seconds;
        easer.Update(VRFade.Target, Time.unscaledDeltaTime);
    }
}
