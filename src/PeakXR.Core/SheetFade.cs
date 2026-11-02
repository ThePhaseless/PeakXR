using System;

namespace PeakXR.Core;

/// <summary>
/// How much of a big view-aligned smoke sheet to keep near the eyes, and how much haze stands in for what was taken
/// (issue #5). A 7 m flat sheet half a metre away shows the two eyes very different parts of itself, correct stereo for
/// something no one can fuse. With only the fade, a player inside the cloud would see clearly, where flat shows a wall.
/// </summary>
public static class SheetFade
{
    /// <summary>View-plane distance at and under which a sheet is gone: 0.126 tan apart between the eyes here.</summary>
    public const float FadeStart = 0.5f;

    /// <summary>View-plane distance at and past which a sheet is untouched.</summary>
    public const float FadeEnd = 1.5f;

    /// <summary>The haze's strongest alpha, so hands and what is ahead stay visible.</summary>
    public const float MaxHaze = 0.7f;

    /// <summary>The widest eye tangent read on the test headset (vr.483): past it a sheet is out of view.</summary>
    public const float ViewTangent = 1.43f;

    /// <summary>How much of a sheet stays at view-plane distance <paramref name="z"/>, on <c>|z|</c>, so a sheet just
    /// behind the head is gone before a turn brings it round.</summary>
    public static float Alpha(float z, float start, float end)
    {
        float t = Saturate((MathF.Abs(z) - start) / (end - start));
        return t * t * (3f - (2f * t));
    }

    /// <summary>The haze for this much lost opacity: under <paramref name="lost"/> everywhere. <see cref="Lost"/> counts
    /// sheets at full texture, so where a texture cuts a sheet the haze can still be stronger than the smoke was. That
    /// is why thin dense smoke stays out of the fade.</summary>
    public static float Haze(float lost)
    {
        float s = Saturate(lost);
        return MaxHaze * s * s * (3f - (2f * s));
    }

    /// <summary>The smoke shaders' last alpha step at full texture: <c>saturate((a - x) / (y - x))</c>, with
    /// <paramref name="a"/> the vertex alpha times <c>_Opacity</c> and x, y the material's <c>_FinalAlphaRemap</c>.</summary>
    public static float Opacity(float a, float x, float y) => Saturate((a - x) / (y - x));

    /// <summary>One step of <c>lost = 1 - product(1 - o)</c>, how sheets over each other add up.</summary>
    public static float Lost(float running, float opacityLost) =>
        1f - ((1f - running) * (1f - Saturate(opacityLost)));

    /// <summary>How much of the view a fading sheet stands in. 1 when it crosses the view's axis, falling to 0 where its
    /// near edge, <paramref name="lateral"/> minus half of <paramref name="size"/> off the axis, leaves the view. A
    /// sheet off to the side fades too and nobody sees it, so it must lift no haze.</summary>
    public static float ViewShare(float lateral, float size, float z)
    {
        float outside = lateral - (size * 0.5f);
        if (outside <= 0f)
            return 1f;
        return z <= 0f ? 0f : Saturate(1f - (outside / (ViewTangent * z)));
    }

    /// <summary>How wide a sheet draws at <paramref name="z"/>: the renderer caps it at <paramref name="maxSize"/> of the
    /// view's height, however big the particle says it is.</summary>
    public static float DrawnSize(float size, float maxSize, float z) =>
        MathF.Min(size, maxSize * 2f * z * ViewTangent);

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
