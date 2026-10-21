using System;

namespace PeakXR.Core;

/// <summary>
/// Size and height of a loading screen floating in the VR loading room. With its backing card gone a screen's
/// art can reach far past the flat screen edge (the plane screen's globe and polar grid hang ~2800 units below
/// its centre), so the art is scaled to fit a comfortable view and lifted to stay above the floor.
/// </summary>
public readonly struct LoadingLayout
{
    /// <summary>Vertical half angle the art may span, seen from the panel distance.</summary>
    public const float MaxHalfAngleV = 24f;

    /// <summary>Horizontal half angle the art may span.</summary>
    public const float MaxHalfAngleH = 35f;

    /// <summary>Lowest the art may come to the room floor, in metres.</summary>
    public const float FloorMargin = 0.25f;

    /// <summary>Panel scale: metres per canvas unit.</summary>
    public readonly float MetersPerUnit;

    /// <summary>Height of the canvas centre above the floor, in metres.</summary>
    public readonly float CenterHeight;

    /// <summary>Height of the art's lowest point above the floor, in metres.</summary>
    public readonly float ContentBottom;

    public LoadingLayout(float metersPerUnit, float centerHeight, float contentBottom)
    {
        MetersPerUnit = metersPerUnit;
        CenterHeight = centerHeight;
        ContentBottom = contentBottom;
    }

    /// <summary>
    /// Fits art spanning <paramref name="minX"/>..<paramref name="maxX"/>, <paramref name="minY"/>..
    /// <paramref name="maxY"/> (canvas units around the canvas centre). Never larger than the default scale
    /// (<paramref name="defaultWidth"/> metres for <paramref name="canvasWidth"/> units); the art's centre goes to
    /// eye height, then up if its bottom would sink below <see cref="FloorMargin"/>.
    /// </summary>
    public static LoadingLayout Fit(
        float minX,
        float maxX,
        float minY,
        float maxY,
        float canvasWidth,
        float defaultWidth,
        float distance,
        float eyeHeight
    )
    {
        var mpu = canvasWidth > 0f ? defaultWidth / canvasWidth : 0.001f;
        var height = maxY - minY;
        // The canvas centre stays straight ahead, so the wider side decides.
        var halfWidth = MathF.Max(MathF.Abs(minX), MathF.Abs(maxX));
        if (height <= 0f && halfWidth <= 0f)
            return new LoadingLayout(mpu, eyeHeight, eyeHeight);

        var maxHeight = 2f * distance * MathF.Tan(MaxHalfAngleV * MathF.PI / 180f);
        var maxHalfWidth = distance * MathF.Tan(MaxHalfAngleH * MathF.PI / 180f);
        if (height * mpu > maxHeight)
            mpu = maxHeight / height;
        if (halfWidth * mpu > maxHalfWidth)
            mpu = maxHalfWidth / halfWidth;

        var center = eyeHeight - (minY + maxY) * 0.5f * mpu;
        var bottom = center + minY * mpu;
        if (bottom < FloorMargin)
        {
            center += FloorMargin - bottom;
            bottom = FloorMargin;
        }
        return new LoadingLayout(mpu, center, bottom);
    }
}

/// <summary>
/// Hold a button to confirm (skip the boot splash): fills over <see cref="HoldSeconds"/>, drains twice as fast
/// when released, fires once per hold. A button already down when armed must be released first, so picking a
/// controller up with a finger on the trigger never skips.
/// </summary>
public sealed class HoldToConfirm
{
    public float HoldSeconds = 0.6f;

    private bool waitRelease;
    private bool fired;

    public float Progress { get; private set; }

    public void Arm(bool held)
    {
        Progress = 0f;
        fired = false;
        waitRelease = held;
    }

    /// <summary>True on the frame the hold completes.</summary>
    public bool Update(float dt, bool held)
    {
        if (!held)
        {
            waitRelease = false;
            fired = false;
            Progress = MathF.Max(0f, Progress - dt * 2f / HoldSeconds);
            return false;
        }
        if (waitRelease || fired)
            return false;
        Progress = MathF.Min(1f, Progress + dt / HoldSeconds);
        fired = Progress >= 1f;
        return fired;
    }
}
