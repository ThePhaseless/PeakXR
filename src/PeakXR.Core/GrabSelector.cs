using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Picks the best grab candidate for a hand. No allocations.</summary>
    public static class GrabSelector
    {
        /// <summary>Default angle weight in metres: a candidate straight behind the hand scores this much worse.</summary>
        public const float DefaultAngleWeight = 0.05f;

        /// <summary>How many points <see cref="SightPoints"/> writes.</summary>
        public const int SightPointCount = 3;

        /// <summary>
        /// Where the eye may see a grab candidate: the point of its bounds nearest the hand, its centre and the top of
        /// its bounds. An item partly sunk into uneven ground hides the first two below the surface while its top still
        /// shows, so the grab checks line of sight to each and takes the item when any is clear; a wall between hides
        /// all three.
        /// </summary>
        /// <returns>The number of points written (<see cref="SightPointCount"/>).</returns>
        public static int SightPoints(
            Vector3 closest,
            Vector3 center,
            Vector3 extents,
            Vector3[] into
        )
        {
            into[0] = closest;
            into[1] = center;
            into[2] = center + new Vector3(0f, extents.Y, 0f);
            return SightPointCount;
        }

        /// <summary>
        /// Returns the index of the best of the first <paramref name="count"/> candidates, or -1 if none is within
        /// <paramref name="grabRadius"/>. Distance is measured to the candidate's surface when
        /// <paramref name="radii"/> is given. Score = distance + angleWeight * (1 - cos(angle to hand forward)) / 2;
        /// lowest wins.
        /// </summary>
        public static int Select(
            Vector3 handPos,
            Vector3 handForward,
            Vector3[] positions,
            float[] radii,
            int count,
            float grabRadius,
            float angleWeight = DefaultAngleWeight
        )
        {
            if (positions == null)
                return -1;
            if (count > positions.Length)
                count = positions.Length;

            float fwdLen = handForward.Length();
            Vector3 fwd = fwdLen > 1e-6f ? handForward / fwdLen : Vector3.Zero;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 to = positions[i] - handPos;
                float centerDist = to.Length();
                float radius = radii != null && i < radii.Length ? radii[i] : 0f;
                float dist = MathF.Max(0f, centerDist - radius);
                if (dist > grabRadius)
                    continue;

                float score = dist;
                if (centerDist > 1e-6f)
                    score += angleWeight * (1f - Vector3.Dot(fwd, to / centerDist)) * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }
    }
}
