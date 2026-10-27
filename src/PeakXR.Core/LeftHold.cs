using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>The item plane a left-primary hold mirrors the game's right hold across.</summary>
    public enum LeftPlane
    {
        /// <summary>The item's own x = 0: the plain mirror (<see cref="GripMath.MirrorX"/>).</summary>
        Origin,

        /// <summary>Across x through the drawn centre: an item drawn off its pivot sideways (a big shell).</summary>
        CentreX,

        /// <summary>Across z through the drawn centre, the item turned half round its up axis: an item lopsided
        /// sideways but even front to back (a flag on its pole, a skewered marshmallow).</summary>
        CentreZ,
    }

    /// <summary>
    /// The left-primary hold of an item the game only authored for the right hand. A rigid item cannot be mirrored, so
    /// the left hand holds the mirror image of the right hold about one of the item's own planes: the plain x = 0 mirror
    /// while that keeps the palm on the item, else the plane the item is most nearly symmetric about, and then a shift
    /// that puts the palm back as near the item's surface as the right hold has it. On the game's 193 items with grips
    /// the plain mirror left the hand 2-14 cm off the model on 11 of them (docs/research/item-grab-shapes.md).
    /// </summary>
    public static class LeftHold
    {
        /// <summary>A left palm this much farther from the item than the right one is still on it, metres.</summary>
        public const float Tolerance = 0.01f;

        /// <summary>A left palm this much nearer the item than the right one (the palm reference sunk into it, where
        /// <c>Collider.ClosestPoint</c> reads 0) is a different hold, metres: the CactusBall, drawn 0.1 m off its pivot,
        /// put the plain mirror's palm inside the ball (vr.244: 0.000 m left, 0.121 m right).</summary>
        public const float DeepTolerance = 0.06f;

        /// <summary>A plane earlier in the order wins while its asymmetry is within this of the best (share of the
        /// item's longest side).</summary>
        public const float Margin = 0.02f;

        /// <summary>Probe points <see cref="Probes"/> writes.</summary>
        public const int ProbeCount = 26;

        /// <summary>Where a right-hold point of the item is in the left hold: reflected across the plane.</summary>
        public static Vector3 Reflect(Vector3 p, LeftPlane plane, Vector3 centre) =>
            plane switch
            {
                LeftPlane.CentreX => new Vector3(2f * centre.X - p.X, p.Y, p.Z),
                LeftPlane.CentreZ => new Vector3(p.X, p.Y, 2f * centre.Z - p.Z),
                _ => new Vector3(-p.X, p.Y, p.Z),
            };

        /// <summary>
        /// The item-frame pose that follows the plain mirrored pose (<see cref="GripMath.MirrorX"/> of the right hold)
        /// so the left hand holds the item at <c>Reflect(grip) + shift</c>: item point q is drawn where the plain
        /// mirror draws <c>M·S(q - shift)</c>, S the reflection across the plane and M the item's x mirror.
        /// </summary>
        public static void Correction(
            LeftPlane plane,
            Vector3 centre,
            Vector3 shift,
            out Vector3 pos,
            out Quaternion rot
        )
        {
            // E(q) = M(S(q)): a translation for the x planes, a half turn about y for the z plane.
            Vector3 t;
            switch (plane)
            {
                case LeftPlane.CentreX:
                    rot = Quaternion.Identity;
                    t = new Vector3(-2f * centre.X, 0f, 0f);
                    break;
                case LeftPlane.CentreZ:
                    rot = new Quaternion(0f, 1f, 0f, 0f);
                    t = new Vector3(0f, 0f, 2f * centre.Z);
                    break;
                default:
                    rot = Quaternion.Identity;
                    t = Vector3.Zero;
                    break;
            }
            pos = t - UnityQuat.Rotate(rot, shift);
        }

        /// <summary>The shift that brings a left palm off the item back to the right palm's gap
        /// <paramref name="rightGap"/>: toward <paramref name="closest"/>, the item's surface point nearest it.
        /// Zero while it is within <see cref="Tolerance"/> of that gap.</summary>
        public static Vector3 Shift(float rightGap, Vector3 leftPalm, Vector3 closest)
        {
            var toward = closest - leftPalm;
            float gap = toward.Length();
            if (gap <= rightGap + Tolerance || gap < 1e-6f)
                return Vector3.Zero;
            return toward * ((gap - rightGap) / gap);
        }

        /// <summary>
        /// The plane for an item: the plain mirror while its palm stays on the item (<paramref name="plainGap"/>
        /// within <see cref="Tolerance"/> above and <see cref="DeepTolerance"/> below <paramref name="rightGap"/>) and it
        /// keeps the far end's side, else the plane it is most symmetric about
        /// (asymmetries from <see cref="Asymmetry"/>), in the order Origin, CentreX, CentreZ within
        /// <see cref="Margin"/>. <paramref name="keepForward"/> (a launcher's barrel, an item with a front: <see cref="HasFront"/>) rules out the z plane, and a plane
        /// that turns the item's far end round (<see cref="TurnsEnd"/>) is out unless the item is even across it
        /// (asymmetry within <see cref="Margin"/>). With none left, the plain mirror.
        /// </summary>
        public static LeftPlane Choose(
            float rightGap,
            float plainGap,
            float asymOrigin,
            float asymX,
            float asymZ,
            bool keepForward,
            bool originTurns = false,
            bool xTurns = false,
            bool zTurns = false
        )
        {
            bool origin = !originTurns || asymOrigin <= Margin;
            if (origin && plainGap <= rightGap + Tolerance && plainGap >= rightGap - DeepTolerance)
                return LeftPlane.Origin;
            bool x = !xTurns || asymX <= Margin;
            bool z = !keepForward && (!zTurns || asymZ <= Margin);
            float best = float.PositiveInfinity;
            if (origin)
                best = asymOrigin;
            if (x)
                best = Math.Min(best, asymX);
            if (z)
                best = Math.Min(best, asymZ);
            if (origin && asymOrigin <= best + Margin)
                return LeftPlane.Origin;
            if (x && asymX <= best + Margin)
                return LeftPlane.CentreX;
            return z ? LeftPlane.CentreZ : LeftPlane.Origin;
        }

        /// <summary>The longest side must be this many times the next for the item to have a far end worth keeping.</summary>
        public const float LongAxisRatio = 1.15f;

        /// <summary>Asymmetry across z (<see cref="Asymmetry"/>) above which the item has a front: its back is not its
        /// front, so a half turn (<see cref="LeftPlane.CentreZ"/>) would show the player the wrong side.</summary>
        public const float FrontAsymmetry = 0.06f;

        /// <summary>The item has a far end: its longest side clearly leads the next (<see cref="LongAxisRatio"/>). A
        /// ball or a plush as wide as it is tall has none, so turning it round changes nothing the player sees.</summary>
        public static bool HasLongAxis(Vector3 size)
        {
            float a = Math.Abs(size.X),
                b = Math.Abs(size.Y),
                c = Math.Abs(size.Z);
            float longest = Math.Max(a, Math.Max(b, c));
            float second =
                longest == a ? Math.Max(b, c)
                : longest == b ? Math.Max(a, c)
                : Math.Max(a, b);
            return longest >= LongAxisRatio * second;
        }

        /// <summary>The item's front and back differ (<paramref name="asymZ"/> over <see cref="FrontAsymmetry"/>): keep
        /// the side the right hold shows the player facing them, so no half turn.</summary>
        public static bool HasFront(float asymZ) => asymZ > FrontAsymmetry;

        /// <summary>Whether mirroring across <paramref name="plane"/> puts the item's far end (from the grip, along
        /// its longest side) on the grip's other side: the item would lie the other way round in the left hand, a
        /// flag's pole pointing back past the fist.</summary>
        public static bool TurnsEnd(LeftPlane plane, Vector3 centre, Vector3 grip, Vector3 end) =>
            Vector3.Dot(Reflect(end, plane, centre) - grip, end - grip) <= 0f;

        /// <summary>How unlike its reflection an item is: the mean difference of the surface distances at the probes
        /// and at their reflections, over the item's longest side.</summary>
        public static float Asymmetry(float[] distances, float[] reflected, float size)
        {
            float sum = 0f;
            int n = Math.Min(distances.Length, reflected.Length);
            for (int i = 0; i < n; i++)
                sum += Math.Abs(distances[i] - reflected[i]);
            return n > 0 && size > 1e-6f ? sum / n / size : 0f;
        }

        /// <summary>The 26 points of a 3x3x3 grid over the drawn box grown by a fifth, its centre left out.</summary>
        public static void Probes(Vector3 min, Vector3 max, Vector3[] into)
        {
            var centre = (min + max) * 0.5f;
            var half = (max - min) * 0.6f;
            int n = 0;
            for (int a = -1; a <= 1; a++)
            for (int b = -1; b <= 1; b++)
            for (int c = -1; c <= 1; c++)
            {
                if (a == 0 && b == 0 && c == 0)
                    continue;
                into[n++] = centre + half * new Vector3(a, b, c);
            }
        }
    }
}
