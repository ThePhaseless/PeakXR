using System;

namespace PeakXR.Core;

/// <summary>
/// The game's first-person body hiding, as the W/Character shader computes it (its forward and depth fragment
/// programs, PEAK 3.0.1). <c>HideTheBody.SetShowing</c> (HideTheBody.cs:104) only writes <c>_VertexGhost</c>; the shader
/// does the hiding per pixel: within 1 m of the camera, measured flat (x and z), it dithers out every vertex painted
/// red, fading back in by 1.2 m. The scout's outfit is red all over, its skin (hands) and shoes are not, so a ghost
/// of 1 leaves only hands and feet in view. Kept here so the self-test can judge what a ghost value shows.
/// </summary>
public static class BodyGhost
{
    /// <summary>Flat distance (m) from the camera inside which ghosted vertices are gone.</summary>
    public const float Radius = 1f;

    /// <summary>Distance (m) over which they fade back in past <see cref="Radius"/>.</summary>
    public const float Fade = 0.2f;

    /// <summary>
    /// Coverage (0 gone, 1 drawn) of a pixel <paramref name="horizontalDistance"/> metres from the camera with vertex
    /// colour red <paramref name="vertexRed"/>, on a material with <c>_VertexGhost</c> <paramref name="ghost"/>.
    /// <paramref name="rawVertexColor"/> is the material's <c>_UseRawVertexColor</c> (hats): the whole mesh ghosts.
    /// </summary>
    public static float Alpha(
        float ghost,
        float horizontalDistance,
        float vertexRed,
        bool rawVertexColor
    )
    {
        float t = Clamp01((horizontalDistance - Radius) / Fade);
        float near = t * t * (3f - 2f * t);
        float mask = rawVertexColor ? near : Clamp01(near + 1f - vertexRed);
        return Clamp01(1f + ghost * (mask - 1f));
    }

    private static float Clamp01(float v) => MathF.Max(0f, MathF.Min(1f, v));
}
