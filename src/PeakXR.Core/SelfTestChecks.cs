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

        /// <summary>Farthest the VR eye may sit from the avatar's own eyes (m).</summary>
        public const float MaxEyeError = 0.08f;

        /// <summary>Degrees between the view direction <paramref name="forward"/> and the direction to
        /// <paramref name="point"/>; 180 for degenerate input.</summary>
        public static float AngleOff(Vector3 eye, Vector3 forward, Vector3 point)
        {
            var to = point - eye;
            if (to.LengthSquared() < 1e-8f || forward.LengthSquared() < 1e-8f)
                return 180f;
            float cos = Vector3.Dot(Vector3.Normalize(to), Vector3.Normalize(forward));
            return MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI;
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

        /// <summary>Fixed slack on every game duration (s): frame hitches, a network round trip, the step's own polls.</summary>
        public const float GameMargin = 1f;

        /// <summary>
        /// Seconds a self-test wait gets for a game duration of <paramref name="gameSeconds"/> (an item's use time,
        /// an interact hold, a fuse) after <paramref name="leadIn"/> seconds of scripted setup (the hand reaching
        /// the spot): a quarter on top plus <see cref="GameMargin"/>, so a slow item never runs out of a budget
        /// sized for a fast one. A negative or NaN duration counts as instant.
        /// </summary>
        public static float WaitBudget(float gameSeconds, float leadIn = 0f) =>
            (gameSeconds > 0f ? gameSeconds * 1.25f : 0f) + MathF.Max(0f, leadIn) + GameMargin;

        /// <summary>
        /// When a settle wait gives up: at <paramref name="deadline"/>, unless the body has been still for
        /// <paramref name="stillFor"/> seconds at <paramref name="now"/>; then no earlier than that stillness reaching
        /// <paramref name="hold"/> plus <paramref name="margin"/>, so a settle in progress always finishes. Stillness
        /// that breaks after the deadline ends the wait.
        /// </summary>
        public static float SettleDeadline(
            float deadline,
            float now,
            float stillFor,
            float hold,
            float margin
        ) => stillFor > 0f ? MathF.Max(deadline, now - stillFor + hold + margin) : deadline;

        /// <summary>Horizontal carry of a throw at <paramref name="horizontal"/> m/s and <paramref name="vertical"/> m/s up
        /// from <paramref name="height"/> above flat ground, without drag.</summary>
        public static float FlatRange(float horizontal, float vertical, float height, float gravity)
        {
            float t =
                (vertical + MathF.Sqrt(vertical * vertical + 2f * gravity * MathF.Max(0f, height)))
                / gravity;
            return horizontal * t;
        }

        /// <summary>Launch pitch (deg) below which a throw counts as aimed down.</summary>
        public const float AimedDownPitch = -5f;

        /// <summary>
        /// Why a throw landed short of <paramref name="minDistance"/>, in order: its first contact was the thrower's
        /// own arm or body; it left the hand pointing down; its first contact was steep (over 45 deg from up) or came
        /// before 60 % of its <paramref name="flatRange"/> (something in the way); else it fell short on open ground.
        /// </summary>
        public static ThrowVerdict JudgeThrow(
            float distance,
            float minDistance,
            float pitchDeg,
            bool firstContactOwnBody,
            bool hadContact,
            float contactSlopeDeg,
            float contactDistance,
            float flatRange
        )
        {
            if (distance >= minDistance)
                return ThrowVerdict.Ok;
            if (firstContactOwnBody)
                return ThrowVerdict.OwnBody;
            if (pitchDeg < AimedDownPitch)
                return ThrowVerdict.AimedDown;
            if (hadContact && (contactSlopeDeg > 45f || contactDistance < 0.6f * flatRange))
                return ThrowVerdict.HitOnTheWay;
            return ThrowVerdict.ShortOnFlat;
        }

        /// <summary>Seconds to fall <paramref name="height"/> from rest: free fall up to
        /// <paramref name="terminalSpeed"/>, then at that speed.</summary>
        public static float FallSeconds(float height, float gravity, float terminalSpeed)
        {
            if (height <= 0f)
                return 0f;
            float speedUpDistance = terminalSpeed * terminalSpeed / (2f * gravity);
            return height <= speedUpDistance
                ? MathF.Sqrt(2f * height / gravity)
                : terminalSpeed / gravity + (height - speedUpDistance) / terminalSpeed;
        }
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

    /// <summary>vr.13's verdict on a throw (<see cref="SelfTestChecks.JudgeThrow"/>).</summary>
    public enum ThrowVerdict
    {
        Ok,
        OwnBody,
        AimedDown,
        HitOnTheWay,
        ShortOnFlat,
    }
}
