using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Social;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Inventory;

/// <summary>
/// Put the held item into a backpack lying on the ground or worn by another player: bring the holding hand into the
/// pack and open the grip. It goes into the first free pocket through the stash the game's backpack wheel makes
/// (BackpackWheel.TryStash, BackpackWheel.cs:162-172): <c>Backpack.Stash</c> (Backpack.cs:119-145) for a ground pack,
/// <c>CharacterBackpackHandler.StashInBackpack</c> (CharacterBackpackHandler.cs:127-136) for a worn one, so peers see
/// the game's own RPCs. While the hand is in a pack, a teal pin marks it when the item would go in, a red ring with
/// the reason when not; a release there is refused with that reason and the item stays in hand (Core
/// <see cref="PackDrop"/>). Your own worn pack is <see cref="BodyInventory"/>'s shoulder slot.
/// </summary>
internal sealed class PackStash : IGripExtension
{
    public static readonly PackStash Instance = new();

    /// <summary>How near (m) the holding hand must come to a pack's middle: inside a pack about 0.5 m tall.</summary>
    private const float Reach = 0.3f;

    private Backpack ground;
    private Character wearer;
    private Vector3 packAt;
    private bool wasOver;

    /// <summary>This frame's verdict for the pack the hand is in (self-test), None when it would go in.</summary>
    public DenyReason Verdict { get; private set; }

    /// <summary>The holding hand is in a pack this frame (self-test).</summary>
    public bool Over { get; private set; }

    public bool ClaimPress(in GripHookContext ctx) => false;

    public void BeginPress(in GripHookContext ctx) { }

    public bool EndPress(in GripHookContext ctx) => false;

    public bool TryHeldRelease(in GripHookContext ctx)
    {
        var c = ctx.Character;
        if (!ctx.ThisHandHolds || !Find(c, ctx.HeldItem, ctx.HandPosition))
            return false;
        var reason = Refusal(c, ctx.HeldItem, out int pocket);
        if (reason != DenyReason.None)
        {
            Affordance.Deny(ctx.Left, packAt, reason, default, ctx.HeldItem);
            return true;
        }
        if (ground != null)
            ground.Stash(c, (byte)pocket);
        else
            wearer.refs.backpackHandler.StashInBackpack(c, (byte)pocket);
        Haptics.Pulse(ctx.Left, 0.5f, 0.05f);
        Affordance.Play(CueSound.WouldWork, packAt, 0.6f);
        return true;
    }

    /// <summary>Per frame from the <c>CharacterInput.Sample</c> postfix: the cue at the pack the hand is in.</summary>
    public void Tick(Character c)
    {
        var item = c.data.currentItem;
        var rig = VRRig.Instance;
        bool left = LocalHold.ItemInLeftHand;
        var hand = rig != null ? rig.Hand(left) : null;
        Over =
            InventoryConfig.Instance.StashIntoPacks
            && item != null
            && hand != null
            && !c.data.usingBackpackWheel
            && Find(c, item, hand.position);
        if (!Over)
        {
            wasOver = false;
            return;
        }
        Verdict = Refusal(c, item, out _);
        if (Verdict == DenyReason.None)
            Affordance.Mark(left, packAt, DenyReason.HappenedHere, DenyHint.At(packAt));
        else
            Affordance.Hover(left, packAt, Verdict);
        if (!wasOver)
            Haptics.Pulse(left, 0.15f, 0.02f);
        wasOver = true;
    }

    /// <summary>The nearest ground pack or other player's worn pack within <see cref="Reach"/> of the hand. A held
    /// pack is no item to stash, and a rocket pack has no pockets (its interact lights it).</summary>
    private bool Find(Character c, Item held, Vector3 hand)
    {
        ground = null;
        wearer = null;
        if (
            !VRSession.Active
            || !InventoryConfig.Instance.StashIntoPacks
            || held == null
            || held is Backpack
            || c != Character.localCharacter
        )
            return false;
        float best = Reach * Reach;
        var items = Item.ALL_ITEMS;
        for (int i = 0; i < items.Count; i++)
        {
            if (
                items[i] is not Backpack pack
                || pack == null
                || pack.itemState != ItemState.Ground
                || pack.backpackType == BackpackSlot.BackpackType.Rocketpack
            )
                continue;
            var at = pack.Center();
            float sq = (at - hand).sqrMagnitude;
            if (sq < best)
            {
                best = sq;
                ground = pack;
                packAt = at;
            }
        }
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (
                ch == null
                || ch == c
                || ch.refs.backpackHandler == null
                || !SocialHands.WearsPack(ch)
                || ch.player.backpackSlot.backpackType == BackpackSlot.BackpackType.Rocketpack
            )
                continue;
            var visuals = ch.refs.backpackHandler.activeBackpackVisuals;
            if (visuals == null || !visuals.isActiveAndEnabled)
                continue;
            var at = visuals.transform.position;
            float sq = (at - hand).sqrMagnitude;
            if (sq < best)
            {
                best = sq;
                ground = null;
                wearer = ch;
                packAt = at;
            }
        }
        return ground != null || wearer != null;
    }

    /// <summary>Why the found pack refuses the held item (Core <see cref="PackDrop"/>), with its first free pocket.
    /// The wheel offers a pocket only to an item it may take (BackpackWheelSlice.cs:84) and only when one is free.</summary>
    private DenyReason Refusal(Character c, Item held, out int pocket)
    {
        var selected = c.refs.items.currentSelectedSlot;
        bool blocked = BodyInventory.SwitchBlocked(c);
        if (ground != null)
            pocket = FreePocket(
                ground.GetData<BackpackData>(DataEntryKey.BackpackData),
                ground.slotCount
            );
        else
        {
            var visuals = wearer.refs.backpackHandler.activeBackpackVisuals;
            pocket = FreePocket(
                BackpackReference.GetFromEquippedBackpack(wearer).GetData(),
                visuals != null ? visuals.slotCount : 0
            );
        }
        return PackDrop.Refusal(
            blocked,
            blocked ? BodyInventory.BlockReason(c) : DenyReason.None,
            held.UIData.canBackpack && selected.IsSome,
            pocket
        );
    }

    /// <summary>First empty pocket among the pack's first <paramref name="slotCount"/>, or -1.</summary>
    internal static int FreePocket(BackpackData data, int slotCount)
    {
        if (data == null)
            return -1;
        int count = Mathf.Min(slotCount, data.itemSlots.Length);
        for (int i = 0; i < count; i++)
        {
            if (data.itemSlots[i] != null && data.itemSlots[i].IsEmpty())
                return i;
        }
        return -1;
    }
}
