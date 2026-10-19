using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Picks the best grab candidate for a hand. No allocations.</summary>
    public static class GrabSelector
    {
        /// <summary>Default angle weight in metres: a candidate straight behind the hand scores this much worse.</summary>
        public const float DefaultAngleWeight = 0.05f;

        /// <summary>
        /// Returns the index of the best of the first <paramref name="count"/> candidates, or -1 if none is within
        /// <paramref name="grabRadius"/>. Distance is measured to the candidate's surface when
        /// <paramref name="radii"/> is given. Score = distance + angleWeight * (1 - cos(angle to hand forward)) / 2;
        /// lowest wins.
        /// </summary>
        public static int Select(Vector3 handPos, Vector3 handForward, Vector3[] positions, float[] radii, int count,
            float grabRadius, float angleWeight = DefaultAngleWeight)
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
