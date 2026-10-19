using System;

namespace PeakXR.Core
{
    /// <summary>Curl per finger, 0 = straight, 1 = fully curled.</summary>
    public struct FingerCurls
    {
        public const int Count = 5;

        public float Thumb,
            Index,
            Middle,
            Ring,
            Pinky;

        public FingerCurls(float thumb, float index, float middle, float ring, float pinky)
        {
            Thumb = thumb;
            Index = index;
            Middle = middle;
            Ring = ring;
            Pinky = pinky;
        }

        /// <summary>0 = thumb, 1 = index, 2 = middle, 3 = ring, 4 = pinky.</summary>
        public float this[int finger]
        {
            get
            {
                switch (finger)
                {
                    case 0:
                        return Thumb;
                    case 1:
                        return Index;
                    case 2:
                        return Middle;
                    case 3:
                        return Ring;
                    case 4:
                        return Pinky;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(finger));
                }
            }
            set
            {
                switch (finger)
                {
                    case 0:
                        Thumb = value;
                        break;
                    case 1:
                        Index = value;
                        break;
                    case 2:
                        Middle = value;
                        break;
                    case 3:
                        Ring = value;
                        break;
                    case 4:
                        Pinky = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(finger));
                }
            }
        }
    }

    /// <summary>Maps controller analog input to finger curls.</summary>
    public static class FingerCurlMapper
    {
        /// <summary>Index curl when the finger rests on the trigger without pulling it.</summary>
        public const float TriggerTouchCurl = 0.25f;

        /// <summary>Thumb curl when it rests on a face button or the stick.</summary>
        public const float ThumbTouchCurl = 0.75f;

        /// <summary>
        /// Trigger (+touch) drives the index, grip drives middle/ring/pinky, any thumb touch curls the thumb.
        /// When <paramref name="skeletal"/> has at least 5 entries (thumb..pinky), each non-NaN entry wins.
        /// </summary>
        public static FingerCurls Map(
            float trigger,
            bool triggerTouch,
            float grip,
            bool thumbTouch,
            float[] skeletal = null
        )
        {
            trigger = Clamp01(trigger);
            grip = Clamp01(grip);
            var c = new FingerCurls(
                thumbTouch ? ThumbTouchCurl : 0f,
                triggerTouch ? TriggerTouchCurl + (1f - TriggerTouchCurl) * trigger : trigger,
                grip,
                grip,
                grip
            );

            if (skeletal != null && skeletal.Length >= FingerCurls.Count)
            {
                for (int i = 0; i < FingerCurls.Count; i++)
                {
                    if (!float.IsNaN(skeletal[i]))
                        c[i] = Clamp01(skeletal[i]);
                }
            }
            return c;
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>Exponential smoothing of finger curls toward a target.</summary>
    public sealed class FingerCurlSmoother
    {
        /// <summary>Smoothing rate in 1/s; values &lt;= 0 snap straight to the target.</summary>
        public float Rate = 20f;

        public FingerCurls Current;

        public FingerCurls Update(FingerCurls target, float dt)
        {
            float alpha = Rate > 0f ? 1f - MathF.Exp(-Rate * dt) : 1f;
            Current.Thumb += (target.Thumb - Current.Thumb) * alpha;
            Current.Index += (target.Index - Current.Index) * alpha;
            Current.Middle += (target.Middle - Current.Middle) * alpha;
            Current.Ring += (target.Ring - Current.Ring) * alpha;
            Current.Pinky += (target.Pinky - Current.Pinky) * alpha;
            return Current;
        }

        public void Reset(FingerCurls value) => Current = value;
    }
}
