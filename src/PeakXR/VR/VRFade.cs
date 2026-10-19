using System.Collections.Generic;

namespace PeakXR.VR;

/// <summary>
/// Head-locked fade to black. Any system can request a fade level; the strongest request wins.
/// Used instead of hard cuts whenever the game moves or takes over the camera (comfort).
/// Rendering is done by the UI feature; requesters only call <see cref="Set"/> and <see cref="Clear"/>.
/// </summary>
public static class VRFade
{
    private static readonly Dictionary<object, float> requests = new();

    /// <summary>0 = clear, 1 = black. The renderer eases toward this value.</summary>
    public static float Target { get; private set; }

    public static void Set(object owner, float alpha)
    {
        requests[owner] = alpha;
        Recalculate();
    }

    public static void Clear(object owner)
    {
        if (requests.Remove(owner))
            Recalculate();
    }

    private static void Recalculate()
    {
        var max = 0f;
        foreach (var alpha in requests.Values)
            if (alpha > max)
                max = alpha;
        Target = max;
    }
}
