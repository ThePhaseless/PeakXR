using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// A trigger press on a held item the game will not use (<c>Item.CanUsePrimary</c>, Item.cs:797-817:
/// <c>overrideUsability</c>, no main interact, <c>ItemUses</c> at 0). The flat game greys its prompt; in VR the
/// trigger just clicks. Says why, and for the shooters, which write <c>overrideUsability</c> from
/// <c>WillAttach</c> every frame (RopeShooter.cs:69, :122-138; VineShooter.cs:33, :71-92), which of their rules
/// failed and where an accepted aim would point.
/// </summary>
internal static class UseDenial
{
    /// <summary>Just inside the vine shooter's elevation window (VineShooter.cs:83-90: refused above 0.5 and below
    /// -0.7 of Dot(up, aim)).</summary>
    private const float VineMaxUp = 0.45f;
    private const float VineMinUp = -0.65f;

    public static void Deny(Character c, Item item, bool left)
    {
        var reason = Why(c, item, out var hint);
        Affordance.DenyAtHand(left, reason, hint);
    }

    /// <summary>A held rope spool's press that cannot tie off: <c>RopeTier.Update</c> (RopeTier.cs:97-111) only takes
    /// terrain its aim meets within <c>maxAnchorGhostDistance</c> that lies within <c>maxAnchorDistance</c> of the
    /// body, and otherwise holding the trigger does nothing at all. For terrain too far from the body, says how much
    /// closer.</summary>
    public static bool SpoolRefused(Character c, Item item, out DenyHint hint)
    {
        hint = default;
        if (
            !item.TryGetComponent<RopeTier>(out var tier)
            || !HandAim.TryHeld(out var origin, out var rotation)
        )
            return false;
        var dir = rotation * Vector3.forward;
        if (
            !Physics.Raycast(
                origin,
                dir,
                out var hit,
                tier.maxAnchorGhostDistance,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap),
                QueryTriggerInteraction.Ignore
            )
        )
            return true;
        if (Vector3.Distance(hit.point, c.Center) < tier.maxAnchorDistance)
            return false;
        hint = DenyHint.Range(c.Center, hit.point, tier.maxAnchorDistance);
        return true;
    }

    /// <summary>The reason and, for an aim the vine shooter refuses, the aim it would take. Also read every frame by the
    /// aim line while it is red.</summary>
    public static DenyReason Why(Character c, Item item, out DenyHint hint)
    {
        hint = default;
        if (c.data.petrifyAmount >= 100 && item.TryGetComponent<global::Peak.AmuletBase>(out _))
            return DenyReason.Petrified;
        bool rope = item.TryGetComponent<RopeShooter>(out var ropeShooter);
        bool vine = !rope && item.TryGetComponent<VineShooter>(out _);
        if ((rope || vine) && !c.data.isGrounded)
            return DenyReason.NeedsGround;
        if (rope && !ropeShooter.HasAmmo)
            return DenyReason.UsedUp;
        // TryGetDataEntry, not GetData: GetData registers a missing entry (Item.cs:1223-1239).
        if (
            item.data != null
            && item.data.TryGetDataEntry<OptionableIntItemData>(DataEntryKey.ItemUses, out var uses)
            && uses.HasData
            && uses.Value == 0
        )
            return DenyReason.UsedUp;
        if (!(rope || vine))
            return Other(c, item, out hint);
        if (!HandAim.TryHeld(out var origin, out var rotation))
            return DenyReason.CantUse;
        var dir = rotation * Vector3.forward;
        float max = rope ? ropeShooter.maxLength : item.GetComponent<VineShooter>().maxLength;
        if (
            !Physics.Raycast(
                origin,
                dir,
                out var hit,
                max,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
            )
        )
            return DenyReason.NoWall;
        if (!vine)
            return DenyReason.CantUse;
        float up = Vector3.Dot(Vector3.up, (hit.point - origin).normalized);
        if (up <= 0.5f && up >= -0.7f)
            return DenyReason.CantUse;
        // An aim the window accepts, same heading: the ring goes where that aim would attach, and only if it would
        // (terrain within range, still inside the window measured to the new hit).
        var ok = DenyRange.ClampElevation(dir.N(), VineMinUp, VineMaxUp).U();
        if (
            Physics.Raycast(
                origin,
                ok,
                out var okHit,
                max,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
            )
        )
        {
            float okUp = Vector3.Dot(Vector3.up, (okHit.point - origin).normalized);
            if (okUp <= 0.5f && okUp >= -0.7f)
                hint = DenyHint.At(okHit.point);
        }
        return up > 0.5f ? DenyReason.AimLower : DenyReason.AimHigher;
    }

    /// <summary>The reason for the items that are neither shooters nor used up: wrecked food, an effigy with nobody
    /// down, a build spot the game refuses, a claw with nothing in reach.</summary>
    private static DenyReason Other(Character c, Item item, out DenyHint hint)
    {
        hint = default;
        // ItemCooking.Wreck (ItemCooking.cs:195-210) strips the actions and blocks use.
        if (
            item.TryGetComponent<ItemCooking>(out var cooking)
            && cooking.wreckWhenCooked
            && item.data != null
            && item.data.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cooked)
            && cooked.Value > 0
        )
            return DenyReason.Spoiled;
        // ScoutEffigy.Update (ScoutEffigy.cs:8-19): unusable while nobody is dead or passed out.
        if (item.TryGetComponent<ScoutEffigy>(out _) && !Character.PlayerIsDeadOrDown())
            return DenyReason.NotInTrouble;
        if (item.TryGetComponent<Constructable>(out var build))
            return BuildRefusal(build, out hint);
        // RescueHook.Update (RescueHook.cs:108-116): usable only with a hit within range.
        if (item.TryGetComponent<RescueHook>(out _))
            return DenyReason.OutOfReach;
        if (item.TryGetComponent<global::Peak.Action_CloneSelectedItem>(out var clone))
            return CloneRefused(item, clone, out _) ? DenyReason.CantClone : DenyReason.CantUse;
        return DenyReason.CantUse;
    }

    /// <summary>Which of <c>Constructable.CurrentHitIsValid</c> (Constructable.cs:136-147) and the preview's
    /// <c>CollisionValid</c> (ConstructablePreview.cs:38-48) failed, measured along the hand aim as interact.aim
    /// makes the game measure it.</summary>
    private static DenyReason BuildRefusal(Constructable build, out DenyHint hint)
    {
        hint = default;
        if (!HandAim.TryHeld(out var origin, out var rotation))
            return DenyReason.OutOfReach;
        var dir = rotation * Vector3.forward;
        if (
            !Physics.Raycast(
                origin,
                dir,
                out var hit,
                build.maxConstructDistance,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            hint = DenyHint.At(origin + dir * build.maxConstructDistance);
            return DenyReason.OutOfReach;
        }
        if (
            build.maxConstructVerticalAngle > 0f
            && Vector3.Angle(Vector3.up, hit.normal) > build.maxConstructVerticalAngle
        )
            return DenyReason.TooSteep;
        return DenyReason.Blocked;
    }

    /// <summary>The clone amulet's own pick (Peak/Action_CloneSelectedItem.cs:28-37) from the hand aim: refused
    /// when it finds no item or one tagged ScoutAmulet or NonCloneable, which the game wastes silently.</summary>
    public static bool CloneRefused(
        Item held,
        global::Peak.Action_CloneSelectedItem clone,
        out Item target
    )
    {
        target = null;
        var interaction = Interaction.instance;
        if (interaction == null)
            return true;
        var swap = HandAim.BeginHeld(held);
        try
        {
            interaction.DoInteractableRaycasts(out var result, clone.range);
            target = result as Item;
        }
        finally
        {
            CameraSwap.Restore(null, swap);
        }
        return target == null
            || target.itemTags.HasFlag(Item.ItemTags.ScoutAmulet)
            || target.itemTags.HasFlag(Item.ItemTags.NonCloneable);
    }

    /// <summary>
    /// An off-hand trigger the game will not act on (<c>Item.CanUseSecondary</c>, Item.cs:819-845): a feed with no
    /// friend in front, or uses run out. Items with no secondary use at all stay silent, since the off-hand trigger
    /// on them never meant anything.
    /// </summary>
    public static void DenySecondary(Item item, bool left)
    {
        if (!item.UIData.hasSecondInteract && !item.canUseOnFriend)
            return;
        var reason =
            item.data != null
            && item.data.TryGetDataEntry<OptionableIntItemData>(DataEntryKey.ItemUses, out var uses)
            && uses.HasData
            && uses.Value == 0
                ? DenyReason.UsedUp
            : item.canUseOnFriend && item.mustUseOnFriend ? DenyReason.NeedsFriend
            : DenyReason.CantUse;
        Affordance.DenyAtHand(left, reason, default, item);
    }

    /// <summary>A press the game accepts but that does nothing: a Bugle of Friendship below its toot cost
    /// (MagicBugle.cs:88-105). True when it was cued.</summary>
    public static bool DenyEmptyPress(Item item, bool left)
    {
        if (
            !item.TryGetComponent<MagicBugle>(out var bugle)
            || bugle.currentFuel >= bugle.initialTootCost
        )
            return false;
        Affordance.DenyAtHand(left, DenyReason.UsedUp, default, item);
        return true;
    }
}
