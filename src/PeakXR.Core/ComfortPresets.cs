using System;
using System.Numerics;

namespace PeakXR.Core;

public enum TurnMode
{
    Snap,
    Smooth,
}

public enum ComfortPreset
{
    Comfortable,
    Moderate,
    Intense,

    /// <summary>The individual settings were edited and match no preset.</summary>
    Custom,
}

/// <summary>The settings a comfort preset controls.</summary>
public struct ComfortProfile
{
    public TurnMode Turn;
    public float SnapAngle;
    public float SmoothTurnSpeed;
    public bool Vignette;
    public float VignetteStrength;

    /// <summary>Seconds for the move stick input to ramp from rest to full (0 = off).</summary>
    public float MoveSmoothing;
}

/// <summary>
/// Comfort presets as bundles of individual settings. Selecting a preset writes the settings; editing a
/// setting afterwards turns the preset into <see cref="ComfortPreset.Custom"/> unless it still matches one.
/// </summary>
public static class ComfortPresets
{
    public static ComfortProfile Get(ComfortPreset preset)
    {
        switch (preset)
        {
            case ComfortPreset.Comfortable:
                // Snap-only turning, strong vignette, instant start and stop (constant speed beats a ramp).
                return new ComfortProfile
                {
                    Turn = TurnMode.Snap,
                    SnapAngle = 30f,
                    SmoothTurnSpeed = 45f,
                    Vignette = true,
                    VignetteStrength = 0.8f,
                    MoveSmoothing = 0f,
                };
            case ComfortPreset.Intense:
                return new ComfortProfile
                {
                    Turn = TurnMode.Smooth,
                    SnapAngle = 30f,
                    SmoothTurnSpeed = 150f,
                    Vignette = false,
                    VignetteStrength = 0.5f,
                    MoveSmoothing = 0f,
                };
            default:
                // Moderate: the design defaults (snap 30 deg, medium vignette, instant moves; smooth turn when
                // chosen at 60 deg/s, the research ceiling).
                return new ComfortProfile
                {
                    Turn = TurnMode.Snap,
                    SnapAngle = 30f,
                    SmoothTurnSpeed = 60f,
                    Vignette = true,
                    VignetteStrength = 0.5f,
                    MoveSmoothing = 0f,
                };
        }
    }

    /// <summary>A preset as it shipped before the vr-practices review (Comfortable ramped 0.3 s and Moderate
    /// 0.15 s; Moderate smooth-turned 120 deg/s, Comfortable 90): what a config still holds if nobody changed
    /// it. Intense did not change.</summary>
    public static ComfortProfile Old(ComfortPreset preset)
    {
        var p = Get(preset);
        switch (preset)
        {
            case ComfortPreset.Comfortable:
                p.SmoothTurnSpeed = 90f;
                p.MoveSmoothing = 0.3f;
                break;
            case ComfortPreset.Moderate:
                p.SmoothTurnSpeed = 120f;
                p.MoveSmoothing = 0.15f;
                break;
        }
        return p;
    }

    /// <summary>
    /// Field by field: each setting of a saved <paramref name="preset"/> config that still holds the preset's old
    /// value was never changed by the player and moves to the preset's current value; every other field is the
    /// player's choice and stays. True when anything moved. Nothing moves for Custom or Intense.
    /// </summary>
    public static bool FromOld(
        ComfortPreset preset,
        ComfortProfile saved,
        out ComfortProfile updated
    )
    {
        updated = saved;
        if (preset != ComfortPreset.Comfortable && preset != ComfortPreset.Moderate)
            return false;
        var old = Old(preset);
        var now = Get(preset);
        bool moved = false;
        if (
            Near(saved.SmoothTurnSpeed, old.SmoothTurnSpeed)
            && !Near(old.SmoothTurnSpeed, now.SmoothTurnSpeed)
        )
        {
            updated.SmoothTurnSpeed = now.SmoothTurnSpeed;
            moved = true;
        }
        if (
            Near(saved.MoveSmoothing, old.MoveSmoothing)
            && !Near(old.MoveSmoothing, now.MoveSmoothing)
        )
        {
            updated.MoveSmoothing = now.MoveSmoothing;
            moved = true;
        }
        return moved;
    }

    /// <summary>Camera.ThirdPersonFollow a preset sets: Comfortable stills the detached view (0, re-placed behind a
    /// fade); the others keep the slow follow (0.8/s). NaN for Custom.</summary>
    public static float ChaseFollow(ComfortPreset preset) =>
        preset switch
        {
            ComfortPreset.Comfortable => 0f,
            ComfortPreset.Custom => float.NaN,
            _ => 0.8f,
        };

    /// <summary>The chase follow after switching <paramref name="from"/> → <paramref name="to"/>: the new
    /// preset's value while <paramref name="current"/> still holds the old preset's, else the player's own.</summary>
    public static float ChaseFollowAfter(ComfortPreset from, ComfortPreset to, float current)
    {
        float was = ChaseFollow(from),
            next = ChaseFollow(to);
        return !float.IsNaN(was) && !float.IsNaN(next) && Near(current, was) ? next : current;
    }

    /// <summary>The preset <paramref name="profile"/> matches, ignoring settings its turn and vignette modes do
    /// not use; <see cref="ComfortPreset.Custom"/> when none matches.</summary>
    public static ComfortPreset Identify(ComfortProfile profile)
    {
        for (var p = ComfortPreset.Comfortable; p < ComfortPreset.Custom; p++)
        {
            if (Matches(Get(p), profile))
                return p;
        }
        return ComfortPreset.Custom;
    }

    static bool Matches(ComfortProfile a, ComfortProfile b)
    {
        if (a.Turn != b.Turn || a.Vignette != b.Vignette || !Near(a.MoveSmoothing, b.MoveSmoothing))
            return false;
        if (
            a.Turn == TurnMode.Snap
                ? !Near(a.SnapAngle, b.SnapAngle)
                : !Near(a.SmoothTurnSpeed, b.SmoothTurnSpeed)
        )
            return false;
        return !a.Vignette || Near(a.VignetteStrength, b.VignetteStrength);
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;
}

/// <summary>
/// Reduced-acceleration smoothing for the move stick: the input vector moves toward the stick at a limited
/// rate, so the force-driven body speeds up gently. Stopping ramps twice as fast so the player does not
/// slide on after letting go.
/// </summary>
public sealed class InputRamp
{
    /// <summary>Seconds from rest to full input; 0 or less passes the stick through.</summary>
    public float RiseTime;

    public Vector2 Current { get; private set; }

    public Vector2 Update(Vector2 target, float dt)
    {
        if (RiseTime <= 0f)
            return Current = target;
        bool speedingUp = target.Length() > Current.Length();
        float maxStep = dt / (speedingUp ? RiseTime : RiseTime * 0.5f);
        var delta = target - Current;
        float len = delta.Length();
        Current = len <= maxStep ? target : Current + delta * (maxStep / len);
        return Current;
    }

    public void Reset() => Current = Vector2.Zero;
}

/// <summary>Opening of the comfort vignette ring, as half-angles from the view centre in degrees.</summary>
public static class VignetteShape
{
    /// <summary>Clear-view half-angle at intensity 0: just at a typical headset's edge (50-55 degrees to each
    /// side), so the ring shows from the first bit of motion (vr-practices-review P0-1).</summary>
    public const float OpenAngle = 55f;

    /// <summary>Clear-view half-angle at full intensity.</summary>
    public const float ClosedAngle = 22f;

    /// <summary>Width of the soft edge between clear and black.</summary>
    public const float Feather = 18f;

    public static float InnerAngle(float intensity)
    {
        float t = intensity < 0f ? 0f : (intensity > 1f ? 1f : intensity);
        return OpenAngle + (ClosedAngle - OpenAngle) * t;
    }
}
