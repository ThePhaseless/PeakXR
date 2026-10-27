using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// An oriented box in an item's own frame (unit axes, half extents along them): one drawn part of the item, so a
/// grab can be measured to what the player sees where the game's collider is smaller, larger or elsewhere.
/// </summary>
public readonly struct ReachBox
{
    public readonly Vector3 Center;
    public readonly Vector3 AxisX;
    public readonly Vector3 AxisY;
    public readonly Vector3 AxisZ;
    public readonly Vector3 Half;

    public ReachBox(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 half)
    {
        Center = center;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
        Half = half;
    }

    /// <summary>The axis-aligned box from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static ReachBox Aligned(Vector3 min, Vector3 max) =>
        new((min + max) * 0.5f, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (max - min) * 0.5f);

    /// <summary>The point of the box nearest <paramref name="p"/>; <paramref name="p"/> itself when inside.</summary>
    public Vector3 Closest(Vector3 p)
    {
        var d = p - Center;
        return Center
            + AxisX * Math.Clamp(Vector3.Dot(d, AxisX), -Half.X, Half.X)
            + AxisY * Math.Clamp(Vector3.Dot(d, AxisY), -Half.Y, Half.Y)
            + AxisZ * Math.Clamp(Vector3.Dot(d, AxisZ), -Half.Z, Half.Z);
    }

    /// <summary>Grows the frame-aligned box <paramref name="min"/>..<paramref name="max"/> to hold this one.</summary>
    public void Encapsulate(ref Vector3 min, ref Vector3 max)
    {
        var e =
            Vector3.Abs(AxisX * Half.X) + Vector3.Abs(AxisY * Half.Y) + Vector3.Abs(AxisZ * Half.Z);
        min = Vector3.Min(min, Center - e);
        max = Vector3.Max(max, Center + e);
    }
}

/// <summary>
/// Which of an item's drawn parts a grab measures to, from boxes in the item's own frame. The audit of every
/// ItemDatabase prefab (docs/research/item-grab-shapes.csv) found drawn parts far outside the colliders (a rescue
/// hook's claw 0.63 m, a scorpion's claws, a coconut's top) and colliders well past the mesh (a glow mushroom's
/// 0.17 m, an early worm's 0.68 m ground disc). A part past the colliders by more than <see cref="Tolerance"/>
/// extends the reach; colliders past every drawn part by more than it are clipped to what is drawn.
/// </summary>
public static class ItemReach
{
    /// <summary>Metres a drawn part may differ from the colliders before the grab measures to it: half the grab
    /// radius, so leaves a hand on the fruit's collider already reaches stay out.</summary>
    public const float Tolerance = 0.06f;

    /// <summary>Flesh around a skinned part's bones, metres: their span stands in for its pose (its own bounds are
    /// up to twice the posed mesh, the frog's 2 m tall), and stays inside the mesh, so it only adds reach.</summary>
    public const float BonePad = 0.05f;

    /// <summary>How far the box <paramref name="min"/>..<paramref name="max"/> reaches past
    /// <paramref name="boxMin"/>..<paramref name="boxMax"/> on its farthest side; negative when inside.</summary>
    public static float Beyond(Vector3 min, Vector3 max, Vector3 boxMin, Vector3 boxMax)
    {
        var out1 = Vector3.Max(boxMin - min, max - boxMax);
        return Math.Max(out1.X, Math.Max(out1.Y, out1.Z));
    }

    /// <summary>The drawn part reaches past the colliders' box: a hand on it may be out of the colliders' reach.</summary>
    public static bool Extends(Vector3 colMin, Vector3 colMax, Vector3 partMin, Vector3 partMax) =>
        Beyond(partMin, partMax, colMin, colMax) > Tolerance;

    /// <summary>The colliders reach past everything drawn: a hand there grabs from empty air.</summary>
    public static bool Clips(Vector3 colMin, Vector3 colMax, Vector3 visMin, Vector3 visMax) =>
        Beyond(colMin, colMax, visMin, visMax) > Tolerance;

    /// <summary>The grab distance: to the colliders (and, clipped, also to the drawn parts), or to an extending
    /// part when nearer. Pass <see cref="float.PositiveInfinity"/> for a measure the item does not use.</summary>
    public static float Distance(float collider, float visual, float extra, bool clip) =>
        Math.Min(clip ? Math.Max(collider, visual) : collider, extra);
}
