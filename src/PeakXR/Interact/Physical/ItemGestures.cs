using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
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

    /// <summary>The held item's real-life use gesture (<see cref="ItemUse"/>).</summary>
    public static UseGesture Use { get; private set; }

    /// <summary>Applied to the body (bandage, heat pack): also where social feeding puts it on a friend.</summary>
    public static bool StatusItem => Use == UseGesture.Apply;

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
        // Skeletons report from their own Update; take last frame's nearest and start a new search.
        NearHips = hipsSeen ? seenHips.N() : null;
        hipsSeen = false;
        seenSq = NearSkeleton * NearSkeleton;
        var cfg = ItemGesturesConfig.Instance;
        float dt = Time.deltaTime;
        FireGestures.Tick(c, rig, cfg, dt);
        LidGesture.Tick(cfg);
        HeldGestures.Tick(c, rig, cfg, dt);
        ParasolRunner.Instance.Tick(c, input, cfg, dt);
        CookWarning(c);
    }

    /// <summary>Patched before Item.CancelUsePrimary/CancelUseSecondary: a cast of the local VR player's held
    /// item that was under way and not finished is being called off. A primary the game called off because the item
    /// became unusable also says why.</summary>
    public static void BeforeCancel(Item item, bool secondary)
    {
        if (
            !VRSession.Active
            || item == null
            || item.finishedCast
            || item.castProgress <= 0f
            || !(secondary ? item.isUsingSecondary : item.isUsingPrimary)
        )
            return;
        var c = Character.localCharacter;
        if (c == null || !ReferenceEquals(item.holderCharacter, c))
            return;
        bool left = LocalHold.ItemInLeftHand;
        Affordance.Cancel(left);
        if (!secondary && !item.CanUsePrimary())
            UseDenial.Deny(c, item, left);
    }

    /// <summary>Patched before CharacterItems.DoUsing for the local VR player: the off-hand trigger the game will
    /// refuse (<see cref="UseDenial.DenySecondary"/>) and a primary press that would do nothing.</summary>
    public static void BeforeUsing(Character c)
    {
        if (
            !VRSession.Active
            || c == null
            || !c.IsLocal
            || c.data.passedOut
            || c.data.fullyPassedOut
        )
            return;
        var item = c.data.currentItem;
        if (item == null)
            return;
        var input = c.input;
        bool left = LocalHold.ItemInLeftHand;
        // While climbing, the off-hand trigger sets a pocketed piton (CharacterItems.cs:1019), not the held item.
        if (
            input.useSecondaryWasPressed
            && !item.isUsingPrimary
            && !(c.data.isClimbingAnything && c.data.climbingSpikeCount > 0)
            && !item.CanUseSecondary()
        )
            UseDenial.DenySecondary(item, !left);
        if (input.usePrimaryWasPressed && item.CanUsePrimary())
            UseDenial.DenyEmptyPress(item, left);
        // A build aimed at nothing: Constructable.CurrentHitIsValid (Constructable.cs:136-147) accepts the empty
        // hit, the cast runs, FinishConstruction (:158-166) builds nothing and Action_Consume still eats the item.
        // Refused here instead, and the local press withheld so the item is kept.
        if (
            (input.usePrimaryWasPressed || input.usePrimaryIsPressed)
            && !item.isUsingPrimary
            && item.TryGetComponent<Constructable>(out var build)
            && build.currentPreview == null
        )
        {
            if (input.usePrimaryWasPressed)
                UseDenial.Deny(c, item, left);
            input.usePrimaryWasPressed = input.usePrimaryIsPressed = false;
        }
    }

    /// <summary>Patched before Item.Interact: the game's equip cooldown (Item.cs:580) would drop this pickup
    /// silently.</summary>
    public static void BeforePickup(Item item, Character interactor)
    {
        if (
            interactor == null
            || !interactor.IsLocal
            || interactor.player == null
            || !interactor.player.HasEmptySlot(item.itemID)
            || interactor.refs.items.lastEquippedSlotTime + 0.25f <= Time.time
        )
            return;
        PickupRefused(item, DenyReason.TooSoon);
    }

    /// <summary>A pickup of <paramref name="item"/> by the local VR player that did not happen: refused at the
    /// item from the hand nearest it, which is the one that reached for it.</summary>
    public static void PickupRefused(Item item, DenyReason reason)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || item == null)
            return;
        var at = item.Center();
        var left = rig.Hand(true);
        var right = rig.Hand(false);
        bool useLeft =
            left != null
            && (
                right == null
                || (left.position - at).sqrMagnitude < (right.position - at).sqrMagnitude
            );
        Affordance.Deny(useLeft, at, reason, default, item);
    }

    /// <summary>The warp compass in the local player's hand found nobody to warp to and got its use back: without
    /// this the warp sound plays and nothing happens.</summary>
    public static void WarpRefunded(Item item)
    {
        if (item != null && HandAim.IsLocalHeld(item))
            Affordance.DenyAtHand(LocalHold.ItemInLeftHand, DenyReason.NeedsFriend, default, item);
    }

    private static ItemCooking cooking;
    private static int explodeAt;

    /// <summary>
    /// Danger while the held item cooks toward harm (<see cref="CookRisk"/>): the campfire hold (cook gesture or grip,
    /// Campfire.cs:300-337) is under way and finishing it would burn, wreck or blow up the item. Urgency follows the
    /// hold, so the warning speeds up as the cook nears done.
    /// </summary>
    private static void CookWarning(Character c)
    {
        var interaction = Interaction.instance;
        if (
            cooking == null
            || !cooking.canBeCooked
            || interaction == null
            || interaction.currentHeldInteractible is not Campfire
            || interaction.currentConstantInteractableTime <= 0f
        )
            return;
        if (
            Held.data == null
            || !Held.data.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cooked)
            || !CookRisk.NextHarms(
                cooked.Value,
                cooking.wreckWhenCooked,
                explodeAt,
                !cooking.ignoreDefaultCookBehavior
            )
        )
            return;
        Affordance.Danger(
            HoldLeft,
            Held.transform.position,
            Mathf.Clamp01(interaction.constantInteractableProgress)
        );
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
        Use = UseOf(item);
        cooking = item != null ? item.GetComponent<ItemCooking>() : null;
        explodeAt = 0;
        if (cooking != null)
        {
            foreach (var behaviour in cooking.additionalCookingBehaviors)
            {
                if (
                    behaviour is CookingBehavior_Explode
                    && (explodeAt == 0 || behaviour.cookedAmountToTrigger < explodeAt)
                )
                    explodeAt = behaviour.cookedAmountToTrigger;
            }
        }
    }

    /// <summary>Skeletons farther than this from the held book are ignored, metres.</summary>
    private const float NearSkeleton = 2f;

    private static Item classifiedItem;
    private static UseGesture classifiedUse;

    /// <summary>The use gesture of <paramref name="item"/> (Button for null), cached for the last item asked, so
    /// interact.physical can route the mouth with it every frame even when this feature does not tick.</summary>
    public static UseGesture UseOf(Item item)
    {
        if (item == null)
            return UseGesture.Button;
        if (!ReferenceEquals(item, classifiedItem))
        {
            classifiedItem = item;
            classifiedUse = ItemUse.Classify(Facts(item));
        }
        return classifiedUse;
    }

    /// <summary>Set by <see cref="ItemGesturesFeature"/>: the face gesture exists, so the trigger may be refused.</summary>
    public static bool Installed;

    /// <summary>The bare trigger must not use <paramref name="item"/>: it kills or warps you, and the eye-to-eye
    /// gesture (<see cref="HeldGestures"/>) is how it is used in VR.</summary>
    public static bool TriggerRefused(Item item)
    {
        if (!Installed || !VRSession.Active)
            return false;
        var use = UseOf(item);
        var cfg = ItemGesturesConfig.Instance;
        return ItemUse.NeedsGesture(use)
            && (use == UseGesture.Read ? cfg.ReadAtFace : cfg.CommuneAtFace);
    }

    private static bool hipsSeen;
    private static Vector3 seenHips;
    private static float seenSq;

    /// <summary>The hips of the skeleton nearest the book hand last frame, or null (<see cref="ReadZone"/>).</summary>
    public static System.Numerics.Vector3? NearHips { get; private set; }

    /// <summary>Patched after Skelleton.Update: while a read item is held, keeps the nearest skeleton's hips.</summary>
    public static void SeeSkeleton(Skelleton skeleton)
    {
        if (Use != UseGesture.Read || Held == null)
            return;
        var hips = skeleton.Center();
        float d = (hips - Held.transform.position).sqrMagnitude;
        if (d >= seenSq)
            return;
        seenSq = d;
        seenHips = hips;
        hipsSeen = true;
    }

    /// <summary>The held item goes to the mouth to be used (eat, drink, blow up): interact.physical's eat route.</summary>
    public static bool AtMouth(Item item) => ItemUse.AtMouth(UseOf(item));

    /// <summary>The game data <see cref="ItemUse.Classify"/> reads: Item.cs:39-75 (UIData), ItemUseFeedback.cs:5,
    /// and the item's action and behaviour components.</summary>
    public static ItemUseFacts Facts(Item item)
    {
        var parts = ItemParts.None;
        foreach (var action in item.GetComponents<ItemAction>())
        {
            if (
                action.OnPressed
                || action.OnHeld
                || action.OnReleased
                || action.OnCastFinished
                || action.OnConsumed
            )
                parts |= ItemParts.Acts;
            parts |= action switch
            {
                Action_Consume or Action_ConsumeAndSpawn => ItemParts.Consume,
                Action_RestoreHunger => ItemParts.Hunger,
                Action_ModifyStatus m when m.changeAmount < 0f => ItemParts.SelfCare,
                Action_ClearAllStatus or Action_ApplyAffliction or Action_AddOrRemoveThorns =>
                    ItemParts.SelfCare,
                Action_Die => ItemParts.Lethal,
                Action_LightLantern or Action_LightCandle or Action_Flare => ItemParts.Light,
                Action_Balloon => ItemParts.Balloon,
                Action_WarpToBiome
                or Action_WarpToRandomPlayer
                or global::Peak.Action_WarpToShadowRealm => ItemParts.Warp,
                Action_RaycastDart
                or global::Peak.Action_Antizooka
                or global::Peak.Action_RaycastSpawnSomething => ItemParts.Shooter,
                Action_ShowBinocularOverlay => ItemParts.Binoculars,
                Action_Parasol => ItemParts.Parasol,
                Action_Passport or Action_Guidebook => ItemParts.Book,
                Action_StrangeGem => ItemParts.Gem,
                _ => ItemParts.None,
            };
        }
        if (item.GetComponent<Constructable>() != null)
            parts |= ItemParts.Construct;
        if (
            item.GetComponent<RopeShooter>() != null
            || item.GetComponent<VineShooter>() != null
            || item.GetComponent<RescueHook>() != null
        )
            parts |= ItemParts.Shooter;
        if (item.GetComponent<RopeSpool>() != null)
            parts |= ItemParts.Spool;
        if (item.GetComponent<BugleSFX>() != null)
            parts |= ItemParts.Bugle;
        if (item.GetComponent<Parasol>() != null)
            parts |= ItemParts.Parasol;
        if (item is Guidebook)
            parts |= ItemParts.Book;
        if (item.GetComponent<global::Peak.AmuletBase>() != null)
            parts |= ItemParts.Gem;
        var feedback = item.GetComponent<ItemUseFeedback>();
        return new ItemUseFacts(
            item.UIData != null && item.UIData.hasMainInteract,
            item.UIData?.mainInteractPrompt,
            feedback != null ? feedback.useAnimation : null,
            parts
        );
    }

    /// <summary>The nearest campfire whose <see cref="FireBase"/> is within <paramref name="range"/>, or null; with
    /// <paramref name="unlitOnly"/>, the nearest one still unlit (<c>FireState.Off</c>), so a lit or spent fire
    /// nearby cannot hide it.</summary>
    public static Campfire NearestCampfire(
        Vector3 point,
        float range,
        bool litOnly,
        bool unlitOnly = false
    )
    {
        var all = Campfires;
        if (all == null)
            return null;
        Campfire best = null;
        float bestSq = range * range;
        for (int i = 0; i < all.Count; i++)
        {
            var fire = all[i];
            if (
                fire == null
                || (litOnly && !fire.Lit)
                || (unlitOnly && fire.state != Campfire.FireState.Off)
            )
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
