using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// One physical inventory anchor on the body. <see cref="Offset"/> is in the body frame (x right, y up,
    /// z forward, metres from the neck pivot), so it follows body yaw and height but never head pitch.
    /// </summary>
    public readonly struct BodySlot
    {
        /// <summary>Game slot index for <c>CharacterItems.EquipSlot</c>: 0-2 hotbar, 3 backpack.</summary>
        public readonly byte GameSlot;
        public readonly Vector3 Offset;
        public readonly float Radius;

        public BodySlot(byte gameSlot, Vector3 offset, float radius)
        {
            GameSlot = gameSlot;
            Offset = offset;
            Radius = radius;
        }
    }

    /// <summary>
    /// Layouts and geometry of the body-anchored hotbar. Every layout has five anchors: the three hotbar slots
    /// (game slots 0-2, left to right) followed by two back anchors over the left and right shoulder that both
    /// map to the backpack slot (3), so either hand can reach the pack.
    /// </summary>
    public static class BodySlotLayout
    {
        public const string Mixed = "Mixed";
        public const string Hips = "Hips";
        public const string Chest = "Chest";

        /// <summary>Layout names for the settings list.</summary>
        public static readonly string[] Names = { Mixed, Hips, Chest };

        /// <summary>Head-local offset from the eyes to the neck pivot the head turns about (down and back).</summary>
        public static readonly Vector3 NeckFromHead = new Vector3(0f, -0.1f, -0.09f);

        /// <summary>Back anchors are larger than the hotbar slots: the hand reaches there blind.</summary>
        public const float BackRadius = 0.16f;

        static readonly Vector3 HipLeft = new Vector3(-0.2f, -0.6f, 0.02f);
        static readonly Vector3 HipRight = new Vector3(0.2f, -0.6f, 0.02f);
        static readonly Vector3 BeltFront = new Vector3(0f, -0.55f, 0.16f);
        static readonly Vector3 ChestLeft = new Vector3(-0.15f, -0.28f, 0.12f);
        static readonly Vector3 ChestMid = new Vector3(0f, -0.3f, 0.15f);
        static readonly Vector3 ChestRight = new Vector3(0.15f, -0.28f, 0.12f);
        static readonly Vector3 BackLeft = new Vector3(-0.15f, 0.05f, -0.18f);
        static readonly Vector3 BackRight = new Vector3(0.15f, 0.05f, -0.18f);

        /// <summary>
        /// Builds a layout by name (unknown names fall back to <see cref="Mixed"/>). <paramref name="heightOffset"/>
        /// raises or lowers the hotbar slots only; the back anchors stay at the shoulders.
        /// </summary>
        public static BodySlot[] Create(string name, float radius, float heightOffset)
        {
            Vector3 a,
                b,
                c;
            switch (name)
            {
                case Hips:
                    a = HipLeft;
                    b = BeltFront;
                    c = HipRight;
                    break;
                case Chest:
                    a = ChestLeft;
                    b = ChestMid;
                    c = ChestRight;
                    break;
                default:
                    a = HipLeft;
                    b = ChestMid;
                    c = HipRight;
                    break;
            }
            var up = new Vector3(0f, heightOffset, 0f);
            return new[]
            {
                new BodySlot(0, a + up, radius),
                new BodySlot(1, b + up, radius),
                new BodySlot(2, c + up, radius),
                new BodySlot(3, BackLeft, BackRadius),
                new BodySlot(3, BackRight, BackRadius),
            };
        }

        /// <summary>Neck pivot from the head pose: the same point whatever the head pitch or roll.</summary>
        public static Vector3 Neck(Vector3 headPosition, Quaternion headRotation) =>
            headPosition + UnityQuat.Rotate(headRotation, NeckFromHead);

        /// <summary>World position of a slot for a neck pivot and a body yaw (degrees, Unity heading).</summary>
        public static Vector3 Anchor(Vector3 neck, float bodyYawDeg, in BodySlot slot)
        {
            float r = bodyYawDeg * UnityQuat.Deg2Rad;
            float s = MathF.Sin(r);
            float c = MathF.Cos(r);
            var o = slot.Offset;
            return neck + new Vector3(o.X * c + o.Z * s, o.Y, o.Z * c - o.X * s);
        }

        /// <summary>
        /// Index of the nearest of the first <paramref name="count"/> anchors whose radius (times
        /// <paramref name="radiusScale"/>) contains <paramref name="point"/>, or -1.
        /// </summary>
        public static int Find(
            Vector3 point,
            Vector3[] anchors,
            BodySlot[] slots,
            int count,
            float radiusScale
        )
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float r = slots[i].Radius * radiusScale;
                float d = Vector3.DistanceSquared(point, anchors[i]);
                if (d <= r * r && d < bestSq)
                {
                    bestSq = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Ghost opacity: 1 inside the slot radius, fading linearly to 0 at <paramref name="fadeDistance"/>.</summary>
        public static float GhostAlpha(float distance, float radius, float fadeDistance)
        {
            if (distance <= radius)
                return 1f;
            if (distance >= fadeDistance || fadeDistance <= radius)
                return 0f;
            return 1f - (distance - radius) / (fadeDistance - radius);
        }

        /// <summary>Heading of the head in degrees, 0 at +Z, positive toward +X; stable when looking straight down.</summary>
        public static float HeadYawDeg(Quaternion headRotation) =>
            UnityQuat.HeadingRadians(headRotation) * UnityQuat.Rad2Deg;

        /// <summary>Head pitch in degrees, positive looking down (Unity's euler x convention).</summary>
        public static float HeadPitchDeg(Quaternion headRotation)
        {
            var f = UnityQuat.Rotate(headRotation, new Vector3(0f, 0f, 1f));
            float flat = MathF.Sqrt(f.X * f.X + f.Z * f.Z);
            return MathF.Atan2(-f.Y, flat) * UnityQuat.Rad2Deg;
        }

        /// <summary>Unity's <c>Mathf.DeltaAngle</c>: shortest signed difference <paramref name="to"/> - <paramref name="from"/> in degrees.</summary>
        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f)
                d -= 360f;
            else if (d < -180f)
                d += 360f;
            return d;
        }
    }

    /// <summary>
    /// Body heading for the slot layout. Looking down at the belt must not swing the slots with the head, so
    /// while the head pitches down the yaw holds, only clamped to stay within <see cref="Deadzone"/> of the head.
    /// Looking level, it follows the head at <see cref="FollowSpeed"/>.
    /// </summary>
    public sealed class BodyYaw
    {
        /// <summary>Largest head-to-body yaw difference, degrees.</summary>
        public float Deadzone = 60f;

        /// <summary>Follow speed while looking level, degrees per second.</summary>
        public float FollowSpeed = 180f;

        /// <summary>Head pitch (degrees, down positive) above which the yaw holds.</summary>
        public float LookDownPitch = 30f;

        bool _started;

        public float Yaw { get; private set; }

        public float Update(float headYawDeg, float headPitchDeg, float dt)
        {
            if (!_started)
            {
                _started = true;
                Yaw = headYawDeg;
                return Yaw;
            }
            float delta = BodySlotLayout.DeltaAngle(Yaw, headYawDeg);
            if (headPitchDeg < LookDownPitch)
            {
                float step = FollowSpeed * dt;
                delta = MathF.Abs(delta) <= step ? 0f : delta - MathF.Sign(delta) * step;
            }
            if (delta > Deadzone)
                Yaw = headYawDeg - Deadzone;
            else if (delta < -Deadzone)
                Yaw = headYawDeg + Deadzone;
            else if (headPitchDeg < LookDownPitch)
                Yaw = headYawDeg - delta;
            return Yaw;
        }

        public void Reset() => _started = false;
    }
}
