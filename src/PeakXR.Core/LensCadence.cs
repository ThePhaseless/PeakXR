using System;

namespace PeakXR.Core;

/// <summary>
/// How often and how large the binocular lens camera renders, a whole second scene render. The eyepiece lens is a
/// 5 cm quad held out at arm's length, so it is drawn every other frame into a half-size texture (the texture keeps
/// the last image in between); the near-eye and panel views fill much of the sight and render every frame at full
/// size. A switch of texture renders at once, so the new texture never shows a stale image.
/// </summary>
public static class LensCadence
{
    /// <summary>Whether the lens camera renders this <paramref name="frame"/>.</summary>
    public static bool Render(bool eyepiece, bool retargeted, int frame) =>
        !eyepiece || retargeted || (frame & 1) == 0;

    /// <summary>Eyepiece texture size: half the lens resolution, at least 64 px. At the closest it shows (the
    /// default 0.12 m near-eye threshold) the 5 cm lens spans about 24 degrees against the near-eye view's 40, so
    /// half the pixels keep about the same density; further out it is still oversampled.</summary>
    public static int EyepieceSize(int lensSize) => Math.Max(64, lensSize / 2);
}
