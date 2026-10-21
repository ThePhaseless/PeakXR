using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Pass rules of the VR self-test steps. Nobody looks into the headset during that run, so "the menu is in
    /// front of you" has to be decided from poses alone.
    /// </summary>
    public static class SelfTestChecks
    {
        public const float MinPanelDistance = 0.5f;
        public const float MaxPanelDistance = 4f;
        public const float MaxPanelAngle = 60f;

        /// <summary>
        /// A panel is readable when it is 0.5 to 4 m from the head and under 60 degrees off the view direction.
        /// </summary>
        public static bool InFront(
            Vector3 head,
            Vector3 forward,
            Vector3 panel,
            out float distance,
            out float angle
        )
        {
            var to = panel - head;
            distance = to.Length();
            angle = 180f;
            if (distance < 1e-4f || forward.LengthSquared() < 1e-8f)
                return false;
            float cos = Vector3.Dot(to / distance, Vector3.Normalize(forward));
            angle = MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI;
            return distance >= MinPanelDistance
                && distance <= MaxPanelDistance
                && angle < MaxPanelAngle;
        }

        /// <summary>Signed heading change from <paramref name="from"/> to <paramref name="to"/> in degrees, in (-180, 180].</summary>
        public static float YawDelta(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f)
                d -= 360f;
            else if (d <= -180f)
                d += 360f;
            return d;
        }

        /// <summary>
        /// Farthest a throw at <paramref name="speed"/> from <paramref name="height"/> above the landing spot can carry
        /// without drag, at its best angle: v/g * sqrt(v^2 + 2gh). An upper bound for a landed throw.
        /// </summary>
        public static float MaxThrowRange(float speed, float height, float gravity) =>
            speed / gravity * MathF.Sqrt(speed * speed + 2f * gravity * MathF.Max(0f, height));
    }

    /// <summary>
    /// Follows one comfort output (vignette closing, fade level) after a trigger: whether it reacted, its peak, and
    /// when it was back at rest after the last rise, so a stuck vignette or black fade shows up as a number.
    /// </summary>
    public sealed class PulseWatch
    {
        /// <summary>Values at or below this count as rest.</summary>
        public float Rest = 0.001f;

        float riseAt = -1f;
        float settleAt = -1f;

        public float Peak { get; private set; }

        public bool Rose => riseAt >= 0f;

        public bool Settled => settleAt >= 0f;

        /// <summary>Seconds from the first rise to the final return to rest (NaN until settled).</summary>
        public float SettleSeconds => Settled ? settleAt - riseAt : float.NaN;

        public void Sample(float value, float time)
        {
            if (value > Peak)
                Peak = value;
            if (value > Rest)
            {
                if (riseAt < 0f)
                    riseAt = time;
                settleAt = -1f;
            }
            else if (Rose && !Settled)
                settleAt = time;
        }
    }

    /// <summary>
    /// Scripted controller motion for the self-test's hand steps: eased moves between poses and a capped-speed
    /// follow for targets that move (a falling item, a held item steered to the mouth).
    /// </summary>
    public static class HandScript
    {
        /// <summary>Point at time <paramref name="t"/> (0..1, clamped) of a smoothstep move from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Vector3 Along(Vector3 from, Vector3 to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Vector3.Lerp(from, to, t * t * (3f - 2f * t));
        }

        /// <summary>One frame of moving <paramref name="current"/> toward <paramref name="target"/> at no more than <paramref name="speed"/> m/s.</summary>
        public static Vector3 Approach(Vector3 current, Vector3 target, float speed, float dt)
        {
            var to = target - current;
            float step = speed * dt;
            float d = to.Length();
            return d <= step || d < 1e-6f ? target : current + to * (step / d);
        }
    }
}
