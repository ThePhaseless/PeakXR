using System;

namespace PeakXR.Core;

/// <summary>
/// How far a lazily following menu stands from the eye while it glides past walls. The glide tests walls 10 times a
/// second, and at a wall's edge the test flips between "wall at 0.4 m" and "open" (the pause menu jumped between
/// 0.60 m and 1.75 m). A wall coming in front pulls the target in at once; the target moves back out only after the
/// way has been clear by <see cref="OutMargin"/> for <see cref="OutSeconds"/>, or on the test that ends the glide. The
/// panel's distance eases toward the target, and it is drawn on top while it stands past the wall (on at once, off
/// after the same clear time).
/// </summary>
public sealed class FollowDistance
{
    /// <summary>Metres the fitted distance must gain before the menu moves back out mid-glide.</summary>
    public const float OutMargin = 0.15f;

    /// <summary>Seconds the way must stay clear before the menu moves back out, or stops drawing on top.</summary>
    public const float OutSeconds = 0.3f;

    // A difference below this ends the ease.
    private const float Arrive = 0.001f;

    /// <summary>Time constant of the ease, seconds.</summary>
    public float SmoothTime = 0.3f;

    private float outFor,
        clearFor;

    /// <summary>Where the panel stands now, metres from the eye.</summary>
    public float Distance { get; private set; }

    /// <summary>Where it eases to, metres.</summary>
    public float Target { get; private set; }

    /// <summary>Drawn on top, with no depth test.</summary>
    public bool OnTop { get; private set; }

    public bool Easing => Distance != Target;

    /// <summary>Places the panel at once (it opened, or a page change re-fitted it).</summary>
    public void Snap(float distance, bool onTop)
    {
        Distance = Target = distance;
        OnTop = onTop;
        outFor = clearFor = 0f;
    }

    /// <summary>
    /// One wall test at the panel's heading: <paramref name="wallDepth"/> as fed to
    /// <see cref="PanelMath.FitBeforeWall"/>, <paramref name="dt"/> seconds since the last one.
    /// <paramref name="settled"/> marks the test that ends a glide: no test follows to confirm a clear way, so it
    /// counts at once.
    /// </summary>
    public void Test(float wanted, float wallDepth, float dt, bool settled)
    {
        var (fit, _) = PanelMath.FitBeforeWall(wanted, wallDepth, PanelMath.NearestMenuDistance);
        if (fit < Target)
        {
            Target = fit;
            outFor = 0f;
        }
        else
        {
            outFor = fit - Target >= OutMargin ? outFor + dt : 0f;
            if (fit > Target && (settled || outFor >= OutSeconds - 1e-4f))
            {
                Target = fit;
                outFor = 0f;
            }
        }

        if (Distance > wallDepth)
        {
            OnTop = true;
            clearFor = 0f;
        }
        else
        {
            clearFor += dt;
            if (settled || clearFor >= OutSeconds - 1e-4f)
                OnTop = false;
        }
    }

    /// <summary>One frame of the ease toward <see cref="Target"/>.</summary>
    public float Step(float dt)
    {
        Distance += (Target - Distance) * ExpSmooth.Factor(1f / MathF.Max(SmoothTime, 1e-4f), dt);
        if (MathF.Abs(Target - Distance) < Arrive)
            Distance = Target;
        return Distance;
    }
}
