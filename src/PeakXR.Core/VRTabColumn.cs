using System;

namespace PeakXR.Core;

/// <summary>
/// Layout maths for the VR tab in a headset: the settings rows in one column about <see cref="ColumnDegrees"/> wide at
/// the distance the menu stands, scrolled vertically, and the category rail as its own small panel beside it, turned
/// toward the eye. Sizes are in the settings window's local units, converted with its metres per unit.
/// </summary>
public static class VRTabColumn
{
    /// <summary>Width of the row column as seen from the eye.</summary>
    public const float ColumnDegrees = 30f;

    /// <summary>Widest the VR tab may look (the self-test's bound).</summary>
    public const float MaxColumnDegrees = 35f;

    /// <summary>How far the rail turns toward the eye about its edge next to the column.</summary>
    public const float RailDegrees = 18f;

    /// <summary>Farthest off the view, sideways, a setting's preview card may reach before it moves to the panel's
    /// other side: run 4's card beside the right edge of a menu turned off the view ran out of the frame.</summary>
    public const float CardComfortDegrees = 40f;

    /// <summary>Whether a preview card goes on the panel's right: yes while its far edge
    /// (<paramref name="rightFar"/>) stays within <see cref="CardComfortDegrees"/> of <paramref name="forward"/>, else
    /// on whichever side reaches less far off the view. Yaw only: a low or high row does not move the card.</summary>
    public static bool CardOnRight(
        System.Numerics.Vector3 eye,
        System.Numerics.Vector3 forward,
        System.Numerics.Vector3 rightFar,
        System.Numerics.Vector3 leftFar
    )
    {
        float right = Yaw(rightFar - eye, forward);
        return right <= CardComfortDegrees || right <= Yaw(leftFar - eye, forward);
    }

    static float Yaw(System.Numerics.Vector3 to, System.Numerics.Vector3 forward)
    {
        float a = MathF.Atan2(to.X, to.Z) - MathF.Atan2(forward.X, forward.Z);
        a = MathF.Abs(MathF.IEEERemainder(a, 2f * MathF.PI));
        return a / UnityQuat.Deg2Rad;
    }

    /// <summary>Local units spanning <paramref name="degrees"/> at <paramref name="distance"/> metres, for a panel of
    /// <paramref name="metersPerUnit"/>.</summary>
    public static float Width(float degrees, float distance, float metersPerUnit) =>
        2f
        * distance
        * MathF.Tan(degrees * 0.5f * UnityQuat.Deg2Rad)
        / MathF.Max(metersPerUnit, 1e-6f);

    /// <summary>Scale for a panel whose smallest text is <paramref name="fontUnits"/> tall, so it meets
    /// <see cref="ReadableText.MinDegrees"/> at <paramref name="distance"/>; never below 1, at most 4.</summary>
    public static float TextScale(float fontUnits, float distance, float metersPerUnit) =>
        ReadableText.Grow(fontUnits * metersPerUnit, distance, 4f);

    /// <summary>Distance from the eye to the corner of a panel <paramref name="width"/> by <paramref name="height"/>
    /// metres centred <paramref name="distance"/> ahead: the farthest text on it, so a panel scaled for this distance
    /// meets the floor all over (VR run 56: the far end of a tall rail read 1.09 deg).</summary>
    public static float FarDistance(float distance, float width, float height) =>
        MathF.Sqrt(distance * distance + 0.25f * (width * width + height * height));

    /// <summary>Yaw of the rail about the edge it shares with the column. The window's forward points away from the
    /// eye, so a rail on the left turns by a negative yaw to bring its outer edge closer.</summary>
    public static float RailYaw(bool left) => left ? -RailDegrees : RailDegrees;

    /// <summary>A scroll offset (down from the top of the rows) kept between the first and the last row.</summary>
    public static float ClampScroll(float offset, float content, float viewport) =>
        Math.Clamp(offset, 0f, MathF.Max(0f, content - viewport));

    /// <summary>A row spanning <paramref name="top"/>..<paramref name="bottom"/> (down from the top of the rows) lies
    /// wholly inside the viewport at <paramref name="scroll"/>.</summary>
    public static bool Shows(float top, float bottom, float scroll, float viewport) =>
        top >= scroll - 0.5f && bottom <= scroll + viewport + 0.5f;
}
