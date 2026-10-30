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
/// pack and open the grip. It goes into the free pocket nearest the hand (the pack's pocket spots,
/// <c>BackpackVisuals.backpackSlots</c>), through the stash the game's backpack wheel makes
/// (BackpackWheel.TryStash, BackpackWheel.cs:162-172): <c>Backpack.Stash</c> (Backpack.cs:119-145) for a ground pack,
/// <c>CharacterBackpackHandler.StashInBackpack</c> (CharacterBackpackHandler.cs:127-136) for a worn one, so peers see
/// the game's own RPCs. A worn pack takes it only from behind its wearer, by the wheel's own angle
/// (<see cref="PackDrop.FromBehind"/>); from the front it is WrongAngle with a ring behind them, where to stand. While
/// the hand is in a pack, a teal pin marks the pocket when the item would go in, a red ring with the reason when not;
/// a release there is refused with that reason and the item stays in hand (Core <see cref="PackDrop"/>). Your own worn
/// pack is <see cref="BodyInventory"/>'s shoulder slot.
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

    /// <summary>The pocket the item would go into and its spot (the pack's middle when it has no pocket spots), or
    /// for WrongAngle the place behind the wearer to stand; set by <see cref="Refusal"/>.</summary>
    private Vector3 cueAt;

    /// <summary>The ground pack whose visuals are cached in <see cref="groundVisuals"/>.</summary>
    private Backpack visualsOf;
    private BackpackVisuals groundVisuals;

    private static readonly Vector3[] PocketSpots = new Vector3[MaxPockets];
    private static readonly System.Numerics.Vector3[] PocketPoints = new System.Numerics.Vector3[
        MaxPockets
    ];
    private static readonly bool[] PocketFree = new bool[MaxPockets];
    private const int MaxPockets = 16;

    /// <summary>The pocket the last verdict chose (self-test), -1 for none.</summary>
    public int Pocket { get; private set; } = -1;

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
        var reason = Refusal(c, ctx.HeldItem, ctx.HandPosition, out int pocket);
        if (reason != DenyReason.None)
        {
            Affordance.Deny(ctx.Left, packAt, reason, Hint(reason), ctx.HeldItem);
            return true;
        }
        if (ground != null)
            ground.Stash(c, (byte)pocket);
        else
            wearer.refs.backpackHandler.StashInBackpack(c, (byte)pocket);
        Haptics.Pulse(ctx.Left, 0.5f, 0.05f);
        Affordance.Play(CueSound.WouldWork, cueAt, 0.6f);
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
        Verdict = Refusal(c, item, hand.position, out int pocket);
        Pocket = pocket;
        if (Verdict == DenyReason.None)
            Affordance.Mark(left, cueAt, DenyReason.HappenedHere, DenyHint.At(cueAt));
        else
            Affordance.Hover(left, packAt, Verdict, Hint(Verdict));
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

    /// <summary>Why the found pack refuses the held item (Core <see cref="PackDrop"/>), with the free pocket nearest
    /// the hand. The wheel offers a pocket only to an item it may take (BackpackWheelSlice.cs:84) and only when one is
    /// free; a worn pack only from behind (BackpackOnBackVisuals.cs:99-104).</summary>
    private DenyReason Refusal(Character c, Item held, Vector3 hand, out int pocket)
    {
        var selected = c.refs.items.currentSelectedSlot;
        bool blocked = BodyInventory.SwitchBlocked(c);
        BackpackData data;
        BackpackVisuals visuals;
        int slotCount;
        if (ground != null)
        {
            if (!ReferenceEquals(visualsOf, ground))
            {
                visualsOf = ground;
                groundVisuals = ground.GetComponent<BackpackVisuals>();
            }
            visuals = groundVisuals;
            data = ground.GetData<BackpackData>(DataEntryKey.BackpackData);
            slotCount = ground.slotCount;
        }
        else
        {
            var worn = wearer.refs.backpackHandler.activeBackpackVisuals;
            visuals = worn;
            data = BackpackReference.GetFromEquippedBackpack(wearer).GetData();
            slotCount = worn != null ? worn.slotCount : 0;
        }
        pocket = PickPocket(data, slotCount, visuals, hand, out bool spotted);
        bool fromBehind =
            ground != null
            || visuals == null
            || PackDrop.FromBehind(c.Head.N(), packAt.N(), visuals.transform.forward.N());
        cueAt =
            !fromBehind ? Behind(visuals.transform)
            : spotted && pocket >= 0 ? PocketSpots[pocket]
            : packAt;
        return PackDrop.Refusal(
            blocked,
            blocked ? BodyInventory.BlockReason(c) : DenyReason.None,
            held.UIData.canBackpack && selected.IsSome,
            pocket,
            fromBehind
        );
    }

    /// <summary>Where to stand for a worn pack: behind it on its facing, at its height.</summary>
    private Vector3 Behind(Transform pack)
    {
        var back = pack.forward;
        back.y = 0f;
        return back.sqrMagnitude > 1e-6f ? packAt - back.normalized * BehindStand : packAt;
    }

    /// <summary>How far behind a worn pack the WrongAngle ring stands, metres.</summary>
    private const float BehindStand = 0.6f;

    private DenyHint Hint(DenyReason reason) =>
        reason == DenyReason.WrongAngle ? DenyHint.At(cueAt) : default;

    /// <summary>The free pocket nearest the hand among the pack's pocket spots; the first free one when the pack has
    /// no spot for every pocket. -1 when none is free.</summary>
    private static int PickPocket(
        BackpackData data,
        int slotCount,
        BackpackVisuals visuals,
        Vector3 hand,
        out bool spotted
    )
    {
        var spots = visuals != null ? visuals.backpackSlots : null;
        spotted = false;
        if (data == null)
            return -1;
        int count = Mathf.Min(Mathf.Min(slotCount, data.itemSlots.Length), MaxPockets);
        if (spots == null || spots.Length < count)
            return FreePocket(data, slotCount);
        for (int i = 0; i < count; i++)
        {
            if (spots[i] == null)
                return FreePocket(data, slotCount);
            PocketSpots[i] = spots[i].position;
            PocketPoints[i] = PocketSpots[i].N();
            PocketFree[i] = data.itemSlots[i] != null && data.itemSlots[i].IsEmpty();
        }
        spotted = true;
        return PackDrop.NearestFree(
            hand.N(),
            new System.ReadOnlySpan<System.Numerics.Vector3>(PocketPoints, 0, count),
            new System.ReadOnlySpan<bool>(PocketFree, 0, count)
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
