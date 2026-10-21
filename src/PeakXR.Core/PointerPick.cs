using System;

namespace PeakXR.Core;

/// <summary>
/// What the VR laser shows for one ray through world-space UI. Events still go to the front hit, as in vanilla,
/// so an invisible graphic that blocks clicks (a modal fading in) keeps blocking them. The beam, though, ends on
/// the first hit you can see or use, passing through empty transparent areas. When that is a control hidden
/// behind such an invisible blocker, the click cannot reach it, and the laser says so instead of seeming to work.
/// </summary>
public static class PointerPick
{
    [Flags]
    public enum Hit : byte
    {
        /// <summary>Invisible and not part of any control: an empty area or a blocker.</summary>
        None = 0,

        /// <summary>Visibly drawn.</summary>
        Visible = 1,

        /// <summary>Part of something you can click, hover, drag or scroll.</summary>
        Usable = 2,
    }

    public readonly struct Result
    {
        public Result(int visual, bool blocked)
        {
            Visual = visual;
            Blocked = blocked;
        }

        /// <summary>Index of the hit the beam ends on, or -1 for a miss.</summary>
        public int Visual { get; }

        /// <summary>The beam ends on a control that the front (event) hit keeps from getting the click.</summary>
        public bool Blocked { get; }
    }

    /// <param name="hits">The ray's hits, front first; the first one gets the events.</param>
    /// <param name="count">How many entries of <paramref name="hits"/> are used.</param>
    public static Result Resolve(Hit[] hits, int count)
    {
        for (int i = 0; i < count; i++)
            if (hits[i] != Hit.None)
                return new Result(i, i > 0 && (hits[i] & Hit.Usable) != 0);
        return new Result(-1, false);
    }
}
