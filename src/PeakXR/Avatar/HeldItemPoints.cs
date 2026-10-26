using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Gesture trigger points on the local VR player's held item, carried by the tracked holding hand (not the physics
/// item, which lags the arm and meets the holder's own colliders) at its drawn size (<see cref="HeldItemScale"/>). A
/// left-hand hold is the mirror image of the right's (AvatarPoseDriver.ItemInController), so these points follow
/// whichever hand holds.
/// </summary>
internal static class HeldItemPoints
{
    private static readonly ItemHoldInfo info = new();

    /// <summary>The held item's shape facts (bounds, grip), bound to <paramref name="item"/>.</summary>
    public static ItemHoldInfo Info(Item item) => info.Bind(item) ? info : null;

    /// <summary>World position of <paramref name="local"/> (item frame, game size) on the item held in
    /// <paramref name="hand"/>, the <paramref name="left"/> controller's grip transform, where it is drawn.</summary>
    public static bool World(Item item, Transform hand, bool left, Vector3 local, out Vector3 world)
    {
        world = default;
        if (!Frame(item, hand, left, out var origin, out var rot))
            return false;
        local = HeldBookSize
            .ScaleAbout(local.N(), info.GripLocal.N(), HeldItemScale.Factor(item))
            .U();
        world = origin + rot * local;
        return true;
    }

    /// <summary>The point of the held item's drawn bounds nearest <paramref name="target"/>: where it meets a mouth,
    /// the eyes or the body. The hand itself when the item is not carried by it.</summary>
    public static Vector3 Nearest(Item item, Transform hand, bool left, Vector3 target)
    {
        if (
            !Frame(item, hand, left, out var origin, out var rot)
            || info.BoundsMax == info.BoundsMin
        )
            return hand != null ? hand.position : target;
        var min = info.BoundsMin.N();
        var max = info.BoundsMax.N();
        HeldItemShape.ScaleAbout(ref min, ref max, info.GripLocal.N(), HeldItemScale.Factor(item));
        var local = (Quaternion.Inverse(rot) * (target - origin)).N();
        return origin + rot * HeldItemShape.Closest(min, max, local).U();
    }

    /// <summary>Where a held item with no flame transform burns or is struck (a flare): the end of its longest side
    /// away from the grip, on the item as it lies and as it is drawn. Its centre when it has no shape.</summary>
    public static Vector3 Tip(Item item)
    {
        if (!info.Bind(item) || info.BoundsMax == info.BoundsMin)
            return item.Center();
        var local = HeldItemShape
            .FarEnd(info.BoundsMin.N(), info.BoundsMax.N(), info.GripLocal.N())
            .U();
        return HeldItemScale.ToVisual(item, item.transform.TransformPoint(local));
    }

    /// <summary>The held item's frame (game size) on the tracked hand.</summary>
    private static bool Frame(
        Item item,
        Transform hand,
        bool left,
        out Vector3 origin,
        out Quaternion rot
    )
    {
        origin = default;
        rot = Quaternion.identity;
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (
            item == null
            || hand == null
            || driver == null
            || !info.Bind(item)
            || !driver.HeldItemInController(left, out var xPos, out var xRot)
        )
            return false;
        origin = hand.position + hand.rotation * xPos;
        rot = hand.rotation * xRot;
        return true;
    }
}
