using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Adaptive XR viewport scale (Unity <c>XRSettings.renderViewportScale</c>): steps down quickly when
    /// the GPU frame time runs over budget and back up slowly when there is headroom. The hysteresis
    /// band in between holds the scale, so it does not oscillate and blur in and out.
    /// </summary>
    public sealed class ViewportScaler
    {
        public float Min = 0.7f;
        public float Max = 1f;
        public float Step = 0.05f;

        /// <summary>Load (frame time / budget) above which the scale steps down.</summary>
        public float DownThreshold = 0.9f;

        /// <summary>Load below which the scale steps up.</summary>
        public float UpThreshold = 0.7f;

        public float DownSeconds = 0.25f;
        public float UpSeconds = 2f;

        float _over;
        float _under;

        public float Scale { get; private set; } = 1f;

        public void SetScale(float scale)
        {
            Scale = Math.Clamp(scale, Min, Max);
            _over = _under = 0f;
        }

        /// <summary>Feeds one frame's GPU time and the frame budget (both ms); returns the new scale.</summary>
        public float Update(float frameMs, float budgetMs, float dt)
        {
            if (!(frameMs > 0f) || !(budgetMs > 0f) || float.IsInfinity(frameMs))
                return Scale;

            float load = frameMs / budgetMs;
            if (load > DownThreshold)
            {
                _under = 0f;
                _over += dt;
                if (_over >= DownSeconds)
                {
                    Scale = Math.Max(Min, Scale - Step);
                    _over = 0f;
                }
            }
            else if (load < UpThreshold)
            {
                _over = 0f;
                _under += dt;
                if (_under >= UpSeconds)
                {
                    Scale = Math.Min(Max, Scale + Step);
                    _under = 0f;
                }
            }
            else
            {
                _over = _under = 0f;
            }
            return Scale;
        }
    }

    /// <summary>Layout math for the desktop mirror of one eye.</summary>
    public static class MirrorMath
    {
        /// <summary>
        /// UV rect (x, y, width, height) that fills a window of aspect <paramref name="dstAspect"/> with an
        /// image of aspect <paramref name="srcAspect"/> without distortion, cropping the longer side evenly.
        /// </summary>
        public static Vector4 EnvelopeUv(float srcAspect, float dstAspect)
        {
            if (!(srcAspect > 0f) || !(dstAspect > 0f))
                return new Vector4(0f, 0f, 1f, 1f);
            if (srcAspect > dstAspect)
            {
                float w = dstAspect / srcAspect;
                return new Vector4((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = srcAspect / dstAspect;
            return new Vector4(0f, (1f - h) * 0.5f, 1f, h);
        }
    }
}
