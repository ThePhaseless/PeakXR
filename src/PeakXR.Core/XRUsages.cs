using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Finds XR input usages by name. Runtimes and controller profiles (Index, Touch, Vive wand, WMR, OpenVR
    /// plugin) expose different subsets and spellings, so each value is looked up in the device's own usage
    /// list once per connect, and missing ones are simply skipped.
    /// </summary>
    public static class XRUsageNames
    {
        static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinky" };

        /// <summary>The first candidate present in <paramref name="available"/> (case-insensitive), spelled as the
        /// device spells it, or null.</summary>
        public static string Find(IReadOnlyList<string> available, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    if (string.Equals(available[i], candidate, StringComparison.OrdinalIgnoreCase))
                        return available[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Finger index (0 thumb .. 4 pinky) of a per-finger curl usage such as "IndexFinger" or "ThumbCurl", or -1.
        /// Touch, button, click, stick and force usages are not curls.
        /// </summary>
        public static int FingerIndex(string usage)
        {
            var name = usage.ToLowerInvariant();
            if (!name.Contains("curl") && !name.Contains("finger"))
                return -1;
            if (
                name.Contains("touch")
                || name.Contains("button")
                || name.Contains("click")
                || name.Contains("stick")
                || name.Contains("force")
            )
                return -1;
            if (name.Contains("little"))
                return 4;
            for (int i = 0; i < Fingers.Length; i++)
            {
                if (name.Contains(Fingers[i]))
                    return i;
            }
            return -1;
        }
    }

    public enum PadZone
    {
        Center,
        Up,
        Down,
    }

    /// <summary>Splits a trackpad click into zones, so controllers without face buttons (Vive wand, WMR touchpad)
    /// still get primary (bottom) and secondary (top) buttons.</summary>
    public static class TrackpadButtons
    {
        public static PadZone Zone(Vector2 pad, float edge = 0.4f)
        {
            if (pad.Y >= edge)
                return PadZone.Up;
            if (pad.Y <= -edge)
                return PadZone.Down;
            return PadZone.Center;
        }
    }

    /// <summary>What a Vive wand's trackpad (the pad is the only stick, and there are no face buttons) gives one hand.</summary>
    public struct WandPadState
    {
        public Vector2 Stick;
        public bool Primary;
        public bool Secondary;
        public bool StickClick;
    }

    /// <summary>
    /// A Vive wand's trackpad reports its position whenever the thumb rests on it, so read as a stick it walks,
    /// turns and flicks the hotbar by itself (review #22). Everything here needs a click. The move hand (the off
    /// hand) walks toward the thumb while clicked and sprints with a click at the rim. The turn hand turns with a
    /// click on either side; its top and bottom clicks are B/Y and A/X and its centre the stick click, and none of
    /// those also flicks or turns. Hotbar and scroll flicks are left to the belt and the wrist gestures.
    /// </summary>
    public static class WandPad
    {
        /// <summary>Pad radius past which a move-hand click sprints.</summary>
        public const float SprintRim = 0.85f;

        /// <summary>Turn-hand clicks closer to the middle than this (sideways) are the stick click, not a turn.</summary>
        public const float TurnSide = 0.5f;

        /// <param name="oneHanded">Accessibility.OneHanded: the one wand's top and bottom clicks walk forward and back
        /// (the stick Y one-handed walking reads) instead of being B and A; the sides still turn and the centre still
        /// clicks.</param>
        public static WandPadState Map(
            Vector2 pad,
            bool clicked,
            bool moveHand,
            bool oneHanded = false
        )
        {
            var s = default(WandPadState);
            if (!clicked)
                return s;
            if (moveHand)
            {
                s.Stick = pad;
                s.StickClick = pad.Length() >= SprintRim;
                return s;
            }
            switch (TrackpadButtons.Zone(pad))
            {
                case PadZone.Up when oneHanded:
                case PadZone.Down when oneHanded:
                    s.Stick = new Vector2(0f, pad.Y);
                    break;
                case PadZone.Up:
                    s.Secondary = true;
                    break;
                case PadZone.Down:
                    s.Primary = true;
                    break;
                default:
                    if (MathF.Abs(pad.X) >= TurnSide)
                        s.Stick = new Vector2(pad.X, 0f);
                    else
                        s.StickClick = true;
                    break;
            }
            return s;
        }
    }

    /// <summary>Finger curl from hand-tracking joint positions: the summed bend between bone segments.</summary>
    public static class JointCurl
    {
        /// <param name="joints">Joint positions from the finger base to the tip.</param>
        /// <param name="count">How many entries of <paramref name="joints"/> are valid.</param>
        /// <param name="fullCurlDegrees">Summed bend that counts as fully curled.</param>
        /// <returns>0 = straight, 1 = fully curled; NaN with fewer than three joints.</returns>
        public static float Curl(Vector3[] joints, int count, float fullCurlDegrees)
        {
            if (count < 3)
                return float.NaN;
            float sum = 0f;
            for (int i = 1; i < count - 1; i++)
            {
                var a = joints[i] - joints[i - 1];
                var b = joints[i + 1] - joints[i];
                float la = a.Length(),
                    lb = b.Length();
                if (la < 1e-6f || lb < 1e-6f)
                    continue;
                float cos = Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f);
                sum += MathF.Acos(cos) * UnityQuat.Rad2Deg;
            }
            return Math.Clamp(sum / fullCurlDegrees, 0f, 1f);
        }
    }
}
