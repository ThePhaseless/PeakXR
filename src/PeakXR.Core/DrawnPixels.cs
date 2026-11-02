using System;

namespace PeakXR.Core;

/// <summary>
/// Verdict of self-test vr.02c and vr.04's pixel check: a panel reaches the eye when hiding it changes clearly more of
/// the eye's RGBA8 image than one more frame of the moving scene does. Placement checks compute where a panel would be
/// and cannot see that nothing was drawn there (issue #131).
/// </summary>
public static class DrawnPixels
{
    /// <summary>How far (0-255) a colour channel must move for the pixel to count as changed; past video noise,
    /// water shimmer and dither.</summary>
    public const int ChannelStep = 40;

    /// <summary>Share of an eye's pixels that hiding the panel must change beyond the scene's own change. The smallest
    /// panel, the title's connection popup, covers about 7 % of an eye; a laser dot well under 1 %.</summary>
    public const float MinShare = 0.01f;

    /// <summary>Share of pixels whose red, green or blue moved by at least <see cref="ChannelStep"/>; 0 for empty or
    /// differently sized images.</summary>
    public static float ChangedShare(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length < 4)
            return 0f;
        int changed = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
        {
            if (
                Math.Abs(a[i] - b[i]) >= ChannelStep
                || Math.Abs(a[i + 1] - b[i + 1]) >= ChannelStep
                || Math.Abs(a[i + 2] - b[i + 2]) >= ChannelStep
            )
                changed++;
        }
        return changed / (a.Length / 4f);
    }

    /// <summary>Whether a panel is in the eye: <paramref name="signal"/> is the share changed by hiding it,
    /// <paramref name="noise"/> the share changed by one more frame with it shown.</summary>
    public static bool PanelDrawn(float noise, float signal) => signal - noise >= MinShare;
}
