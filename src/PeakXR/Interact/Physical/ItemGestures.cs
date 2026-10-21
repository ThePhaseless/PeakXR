using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Per-frame entry of <see cref="ItemGesturesFeature"/> for the local VR player, run from the
/// <c>CharacterInput.Sample</c> postfix just before <c>interact.physical</c> injects its inputs, so the gesture
/// requests (<see cref="GestureRequests"/>) land in the same frame. Holds what every gesture shares: the held item's
/// kind, the holding and free hand, and the campfire list.
/// </summary>
internal static class ItemGestures
{
    /// <summary><c>Campfire.ALL_CAMPFIRES</c> (Campfire.cs:31, private static), resolved in Install.</summary>
    public static AccessTools.FieldRef<List<Campfire>> CampfireList;

    private static Item boundItem;

    /// <summary>Held item this frame (null when none or destroyed).</summary>
    public static Item Held { get; private set; }

    public static BugleSFX Bugle { get; private set; }
    public static Parasol Parasol { get; private set; }

    /// <summary>A non-food item whose use heals or clears a status (bandage, cure-all, antidote).</summary>
    public static bool StatusItem { get; private set; }

    /// <summary>The held item changed this frame.</summary>
    public static bool ItemChanged { get; private set; }

    public static bool HoldLeft => LocalHold.ItemInLeftHand;

    public static List<Campfire> Campfires => CampfireList?.Invoke();

    public static void Tick(Character c, CharacterInput input, bool active)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || c != Character.localCharacter || !active)
        {
            Bind(null);
            FireGestures.Reset();
            LidGesture.Reset();
            HeldGestures.Reset();
            ParasolRunner.Instance.Reset();
            return;
        }
        var item = c.data.currentItem;
        Bind(item != null ? item : null);
        var cfg = ItemGesturesConfig.Instance;
        float dt = Time.deltaTime;
        FireGestures.Tick(c, rig, cfg, dt);
        LidGesture.Tick(cfg);
        HeldGestures.Tick(c, rig, cfg, dt);
        ParasolRunner.Instance.Tick(c, input, cfg, dt);
    }

    private static void Bind(Item item)
    {
        ItemChanged = !ReferenceEquals(item, boundItem);
        Held = item;
        if (!ItemChanged)
            return;
        boundItem = item;
        Bugle = item != null ? item.GetComponent<BugleSFX>() : null;
        Parasol = item != null ? item.GetComponent<Parasol>() : null;
        StatusItem = item != null && IsStatusItem(item);
    }

    /// <summary>Heals or clears a status on use (Action_ModifyStatus.cs:4 with a negative amount,
    /// Action_ClearAllStatus.cs:6) and restores no hunger (Action_RestoreHunger), so it is not food.</summary>
    private static bool IsStatusItem(Item item)
    {
        if (item.GetComponent<Action_RestoreHunger>() != null)
            return false;
        if (item.GetComponent<Action_ClearAllStatus>() != null)
            return true;
        foreach (var modify in item.GetComponents<Action_ModifyStatus>())
        {
            if (modify.changeAmount < 0f)
                return true;
        }
        return false;
    }

    /// <summary>The nearest campfire whose <see cref="Campfire.Center"/> is within <paramref name="range"/>, or null.</summary>
    public static Campfire NearestCampfire(Vector3 point, float range, bool litOnly)
    {
        var all = Campfires;
        if (all == null)
            return null;
        Campfire best = null;
        float bestSq = range * range;
        for (int i = 0; i < all.Count; i++)
        {
            var fire = all[i];
            if (fire == null || (litOnly && !fire.Lit))
                continue;
            float d = (FireBase(fire) - point).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = fire;
            }
        }
        return best;
    }

    /// <summary>Where the flames start: the fire particle system (Campfire.cs:51), else the campfire's root.</summary>
    public static Vector3 FireBase(Campfire fire) =>
        fire.fireParticles != null
            ? fire.fireParticles.transform.position
            : fire.transform.position;

    /// <summary>World pose of the holding hand's grip, or null when untracked.</summary>
    public static Transform HoldingHand(VRRig rig) =>
        VRInput.Hand(HoldLeft).Tracked ? rig.Hand(HoldLeft) : null;
}
