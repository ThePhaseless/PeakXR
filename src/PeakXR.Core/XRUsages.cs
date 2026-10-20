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
