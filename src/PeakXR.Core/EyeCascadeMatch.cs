using System;

namespace PeakXR.Core;

/// <summary>
/// Counts frames whose MultiPass eyes fitted the same main-light shadow cascades (self-test vr.470, issue #5). Each eye
/// pass culls on its own and URP fits the cascades to that cull, so with culling shared both eyes must give the same
/// cascade values. Per frame the first XR cull's cascades are kept and every later cull of the frame must repeat them,
/// cascade for cascade. Allocation-free after construction.
/// </summary>
public sealed class EyeCascadeMatch
{
    /// <summary>URP's cascade limit.</summary>
    public const int MaxCascades = 4;

    /// <summary>Floats per cascade: the culling sphere (4) and the world-to-shadow matrix (16).</summary>
    public const int Values = 20;

    private const float Tolerance = 1e-5f;

    private readonly float[] first = new float[MaxCascades * Values];
    private int frame = int.MinValue;
    private int culls;
    private int firstMask;
    private int cullMask;
    private bool mismatch;

    /// <summary>Frames with two or more XR culls that fitted cascades.</summary>
    public int StereoFrames { get; private set; }

    /// <summary>Of those, frames whose culls all fitted the same cascades.</summary>
    public int MatchedFrames { get; private set; }

    /// <summary>An XR cull of <paramref name="frameNumber"/> starts; a new frame number closes the previous frame.</summary>
    public void BeginCull(int frameNumber)
    {
        if (culls > 1 && cullMask != firstMask)
            mismatch = true;
        if (frameNumber != frame)
        {
            if (culls > 1 && firstMask != 0)
            {
                StereoFrames++;
                if (!mismatch)
                    MatchedFrames++;
            }
            frame = frameNumber;
            culls = 0;
            firstMask = 0;
            mismatch = false;
        }
        culls++;
        cullMask = 0;
    }

    /// <summary>The current cull fitted cascade <paramref name="index"/> to <paramref name="values"/>
    /// (<see cref="Values"/> floats); anything else is ignored.</summary>
    public void Cascade(int index, ReadOnlySpan<float> values)
    {
        if (culls == 0 || index < 0 || index >= MaxCascades || values.Length != Values)
            return;
        int bit = 1 << index;
        cullMask |= bit;
        var kept = first.AsSpan(index * Values, Values);
        if (culls == 1)
        {
            values.CopyTo(kept);
            firstMask |= bit;
            return;
        }
        if ((firstMask & bit) == 0)
        {
            mismatch = true;
            return;
        }
        for (int i = 0; i < Values; i++)
        {
            if (Math.Abs(values[i] - kept[i]) > Tolerance * Math.Max(1f, Math.Abs(kept[i])))
            {
                mismatch = true;
                return;
            }
        }
    }
}
