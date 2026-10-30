using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// A held item let go into a backpack on the ground or on a friend's back goes into the free pocket nearest the hand,
/// through the game's own stash (the backpack wheel's <c>TryStash</c>). Otherwise the release is refused with the
/// reason, and the item stays in hand.
/// </summary>
public static class PackDrop
{
    /// <summary>The game opens a worn pack's wheel only to a look within this of the pack's facing
    /// (<c>BackpackOnBackVisuals.IsInteractible</c>, BackpackOnBackVisuals.cs:99-104).</summary>
    public const float MaxBackAngle = 110f;

    /// <summary>Why the item cannot go in, or None. A busy hand says why first (the switch gates), then a worn pack
    /// reached from its wearer's front (<paramref name="fromBehind"/> false), then an item the wheel refuses
    /// (<c>canBackpack</c>), then a pack with no free pocket (<paramref name="freePocket"/> below 0).</summary>
    public static DenyReason Refusal(
        bool switchBlocked,
        DenyReason blockReason,
        bool canBackpack,
        int freePocket,
        bool fromBehind
    ) =>
        switchBlocked ? blockReason
        : !fromBehind ? DenyReason.WrongAngle
        : !canBackpack ? DenyReason.CantHolster
        : freePocket < 0 ? DenyReason.PackFull
        : DenyReason.None;

    /// <summary>The game's from-behind rule for a worn pack, with the look taken from the eye to the pack (as the
    /// hand grip on a friend's pack does): on the level, within <see cref="MaxBackAngle"/> of the pack's facing.</summary>
    public static bool FromBehind(Vector3 eye, Vector3 pack, Vector3 packForward)
    {
        var look = new Vector3(pack.X - eye.X, 0f, pack.Z - eye.Z);
        var facing = new Vector3(packForward.X, 0f, packForward.Z);
        float lengths = look.Length() * facing.Length();
        if (lengths < 1e-6f)
            return true;
        float cos = Math.Clamp(Vector3.Dot(look, facing) / lengths, -1f, 1f);
        return MathF.Acos(cos) * (180f / MathF.PI) < MaxBackAngle;
    }

    /// <summary>The free pocket (<paramref name="free"/>) whose spot (<paramref name="pockets"/>) is nearest the hand,
    /// so the hand picks the pocket; -1 when none is free.</summary>
    public static int NearestFree(
        Vector3 hand,
        ReadOnlySpan<Vector3> pockets,
        ReadOnlySpan<bool> free
    )
    {
        int best = -1;
        float bestSq = float.MaxValue;
        int count = Math.Min(pockets.Length, free.Length);
        for (int i = 0; i < count; i++)
        {
            float sq = Vector3.DistanceSquared(hand, pockets[i]);
            if (free[i] && sq < bestSq)
            {
                best = i;
                bestSq = sq;
            }
        }
        return best;
    }
}
