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

    // Scratch for one evaluation: every reader loads them first (Find or PointVerdictNow).
    private Backpack ground;
    private Character wearer;
    private Vector3 packAt;
    private bool wasOver;

    /// <summary>A pack the free hand can point at: on the ground, or worn by <see cref="Wearer"/>. <see cref="Id"/> is
    /// its PhotonView id (the item's, or the wearer's), unique in a room; -1 none.</summary>
    private struct Pick
    {
        public Backpack Ground;
        public Character Wearer;
        public Vector3 At;
        public int Id;
    }

    private static readonly Pick NoPick = new() { Id = -1 };

    // Point to open (#71), per hand (0 left, 1 right).
    private readonly ReachOutTracker[] point = { NewPoint(), NewPoint() };
    private readonly Pick[] pointPick = { NoPick, NoPick };
    private readonly bool[] pointArmed = new bool[2];
    private bool pullPrevDown,
        pullPrevLeft,
        pullClaimed;
    private int pullHand = -1;

    // The walk's smallest-angle pack, and the tracker's current pick (which hysteresis may keep over a nearer angle).
    private Pick walkBest,
        walkHeld;
    private float walkBestAngle;
    private Collider rayCollider;
    private IInteractible rayTarget;

    private static ReachOutTracker NewPoint() =>
        new(PackDrop.PointAcquire, PackDrop.PointHold, PackDrop.PointDwell, ReachOut.Grace);

    /// <summary>The hand whose trigger a pointed-at pack owns: an armed pick, or the claimed pull until release; -1
    /// none. The reach-out yields on it (SocialHands).</summary>
    public int PointHand { get; private set; } = -1;

    /// <summary>The armed pick's verdict this frame (self-test), None when the trigger would open it.</summary>
    public DenyReason PointVerdict { get; private set; }

    /// <summary>The pack <see cref="PointHand"/> points at (self-test): the ground <c>Backpack</c>, or the wearer's
    /// <c>BackpackOnBackVisuals</c>; null none.</summary>
    public Object PointPack =>
        PointHand < 0 ? null
        : pointPick[PointHand].Ground != null ? pointPick[PointHand].Ground
        : pointPick[PointHand].Wearer != null
            ? pointPick[PointHand].Wearer.refs.backpackHandler.activeBackpackVisuals
        : null;

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
        TickPoint(c, Over);
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

    // ------------------------------------------------------------------ point to open (#71)
    //
    // With an item in one hand, the other hand points at a ground pack or another player's worn pack and pulls its
    // trigger: the pack's wheel opens through its own entry point (Backpack.Interact, Backpack.cs:32;
    // BackpackOnBackVisuals.Interact_CastFinished, BackpackOnBackVisuals.cs:162, without the worn pack's 0.25 s cast,
    // as the shoulder grip), which is local UI (GUIManager.OpenBackpackWheel, GUIManager.cs:546), so the stash that
    // follows is the game's own (BackpackWheel.Choose/TryStash, BackpackWheel.cs:122-176). The trigger hand drives the
    // wheel and its release commits (BodyInventory.HoldWheelByTrigger; BackpackWheel.cs:96). The pick is the pack the
    // aim ray hits, else the smallest angle off it (Core ReachOutTracker, PackDrop.Point*), whatever its distance: past
    // the game's 2 m interact reach (Interaction.cs:7) it shows OutOfReach. Occlusion as vanilla's (Interaction.cs:257).

    /// <summary>Per frame, before <see cref="Tick"/>'s early return: the free hand's pick and its cue.</summary>
    private void TickPoint(Character c, bool over)
    {
        // A pull whose release Inject never saw (a menu or the wheel was open) ends with the raw trigger.
        if (VRInput.Hand(pullPrevLeft).Trigger < 0.4f)
        {
            pullPrevDown = pullClaimed = false;
            pullHand = -1;
        }
        bool free = !LocalHold.ItemInLeftHand;
        int h = free ? 0 : 1;
        point[1 - h].Reset();
        pointArmed[1 - h] = false;
        var rig = VRRig.Instance;
        var aim = rig != null ? rig.Aim(free) : null;
        if (over || aim == null || !FreeHand(c, free))
        {
            point[h].Reset();
            pointArmed[h] = false;
            PointHand = pullHand;
            return;
        }
        var t = point[h];
        t.Begin();
        WalkPoint(c, t, aim.position, aim.forward);
        t.Step(1f, 0f, true, Time.time);

        var pick =
            t.Friend >= 0 && t.Friend == walkBest.Id ? walkBest
            : t.Friend >= 0 && t.Friend == walkHeld.Id ? walkHeld
            : NoPick;
        // Armed now, not in the lost pick's grace: the ring and the claim follow one rule.
        bool armed = pick.Id >= 0 && t.ArmedFriend == t.Friend;
        pointArmed[h] = armed;
        if (armed)
        {
            pointPick[h] = pick;
            PointVerdict = PointVerdictNow(c, h);
            if (PointVerdict == DenyReason.None)
            {
                if (Affordance.Hints)
                    Affordance.Mark(
                        free,
                        packAt,
                        DenyReason.HappenedHere,
                        DenyHint.At(packAt),
                        "Trigger to open"
                    );
            }
            else
                Affordance.Hover(free, packAt, PointVerdict, PointHint(c, PointVerdict));
            // With hints off the tick is the only sign the pick is armed.
            if (t.Acquired)
                Haptics.Pulse(free, 0.15f, 0.02f);
        }
        PointHand =
            pullHand >= 0 ? pullHand
            : armed ? h
            : -1;
    }

    /// <summary>The free hand may point: it is tracked and open (it holds nothing), holds no wall, rope or the item,
    /// the other hand holds an item a pack can take, and nothing blocks input. Needs <c>Inventory.BodySlots</c>, whose
    /// wheel hold (<see cref="BodyInventory.HoldWheelByTrigger"/>) commits the wheel.</summary>
    private static bool FreeHand(Character c, bool left)
    {
        var item = c.data.currentItem;
        ref var s = ref VRInput.Hand(left);
        return InventoryConfig.Instance.PointToOpenPack
            && VRSession.Active
            && BodyInventory.Instance.Valid
            && c == Character.localCharacter
            && item != null
            && item is not Backpack
            && LocalHold.ItemInLeftHand != left
            && s.Tracked
            && s.Grip < ReachOut.OpenGrip
            && !PhysicalInteraction.WorldGripHolds(left)
            && !c.data.usingWheel
            && BodyInventory.CanDoInput != null
            && BodyInventory.CanDoInput(c);
    }

    /// <summary>Offers every pack within <see cref="PackDrop.PointSearch"/> of the eye and the hold angle of the aim,
    /// not hidden behind terrain, to <paramref name="t"/>; keeps the smallest-angle one and the tracker's current
    /// pick. Same packs as <see cref="Find"/>, plus the game's own interact gate for a ground pack.</summary>
    private void WalkPoint(Character c, ReachOutTracker t, Vector3 from, Vector3 dir)
    {
        walkBest = walkHeld = NoPick;
        walkBestAngle = float.MaxValue;
        // shortcut: one ray against solid colliders; a worn pack with only trigger colliders falls back to its angle.
        if (
            Physics.Raycast(
                from,
                dir,
                out var hit,
                PackDrop.PointSearch + 1f,
                HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                QueryTriggerInteraction.Ignore
            )
        )
        {
            if (!ReferenceEquals(hit.collider, rayCollider))
            {
                rayCollider = hit.collider;
                rayTarget = hit.collider.GetComponentInParent<IInteractible>();
            }
        }
        else
        {
            rayCollider = null;
            rayTarget = null;
        }
        var eye = c.Head;
        var items = Item.ALL_ITEMS;
        for (int i = 0; i < items.Count; i++)
        {
            if (
                items[i] is not Backpack pack
                || pack == null
                || pack.itemState != ItemState.Ground
                || pack.backpackType == BackpackSlot.BackpackType.Rocketpack
                || !pack.IsInteractible(c)
            )
                continue;
            Offer(
                t,
                from,
                dir,
                eye,
                pack,
                new Pick
                {
                    Ground = pack,
                    At = pack.Center(),
                    Id = pack.photonView.ViewID,
                }
            );
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
            Offer(
                t,
                from,
                dir,
                eye,
                visuals,
                new Pick
                {
                    Wearer = ch,
                    At = visuals.transform.position,
                    Id = ch.refs.view.ViewID,
                }
            );
        }
    }

    private void Offer(
        ReachOutTracker t,
        Vector3 from,
        Vector3 dir,
        Vector3 eye,
        IInteractible target,
        Pick pick
    )
    {
        if ((pick.At - eye).sqrMagnitude > PackDrop.PointSearch * PackDrop.PointSearch)
            return;
        // The pack the ray hits is the pick whatever its centre's angle (a big pack seen close).
        bool onRay = ReferenceEquals(rayTarget, target);
        float angle = onRay ? 0f : ReachOut.AimDegrees(from.N(), (from + dir).N(), pick.At.N());
        if (angle > PackDrop.PointHold || (!onRay && Occluded(from, pick.At, target)))
            return;
        // All in range: the aim picks, never the distance (OutOfReach shows on the aimed pack).
        t.Offer(pick.Id, angle, true);
        if (pick.Id == t.Friend)
            walkHeld = pick;
        if (angle < walkBestAngle)
        {
            walkBestAngle = angle;
            walkBest = pick;
        }
    }

    /// <summary>Vanilla's occlusion test (Interaction.cs:257): terrain or map between, unless the hit is the pack.</summary>
    private static bool Occluded(Vector3 from, Vector3 to, IInteractible target)
    {
        var hit = HelperFunctions.LineCheck(
            from,
            to,
            HelperFunctions.LayerType.TerrainMap,
            0f,
            QueryTriggerInteraction.Collide
        );
        return hit.collider != null
            && !ReferenceEquals(hit.collider.GetComponentInParent<IInteractible>(), target);
    }

    /// <summary>The verdict for hand <paramref name="h"/>'s stored pick now, out of reach first.</summary>
    private DenyReason PointVerdictNow(Character c, int h)
    {
        var p = pointPick[h];
        ground = p.Ground;
        wearer = p.Wearer;
        packAt = p.At;
        return PackDrop.PointRefusal(
            Vector3.Distance(c.Head, packAt),
            Refusal(c, c.data.currentItem, packAt, out _)
        );
    }

    private DenyHint PointHint(Character c, DenyReason r) =>
        r == DenyReason.OutOfReach ? DenyHint.Range(c.Head, packAt, PackDrop.PointReach) : Hint(r);

    /// <summary>The stored pick is still a pack to open: a ground pack still lying there, a wearer still wearing one.</summary>
    private static bool Live(Pick p) =>
        p.Ground != null
            ? p.Ground.itemState == ItemState.Ground
            : p.Wearer != null
                && p.Wearer.refs.backpackHandler != null
                && SocialHands.WearsPack(p.Wearer)
                && p.Wearer.refs.backpackHandler.activeBackpackVisuals != null;

    /// <summary>
    /// From <c>PhysicalInteraction.Inject</c> every frame it runs, with the free hand's trigger: true while this pull
    /// belongs to the pack, so the held item's secondary use does not see it. Only a pull that starts on an armed pick
    /// is taken, and it stays taken until release whatever happens to the wheel. Reads the pick <see cref="TickPoint"/>
    /// armed in the previous frame's Inventory postfix, one frame behind Inject; fine at 90 Hz.
    /// </summary>
    public bool ClaimTrigger(Character c, bool left, bool down, bool holding)
    {
        bool active = holding && down;
        bool edge = active && !pullPrevDown && left == pullPrevLeft;
        if (left != pullPrevLeft)
            pullClaimed = false;
        pullPrevDown = active;
        pullPrevLeft = left;
        if (!active)
        {
            pullClaimed = false;
            pullHand = -1;
            return false;
        }
        if (edge)
        {
            pullClaimed = TryOpen(c, left);
            pullHand = pullClaimed ? (left ? 0 : 1) : -1;
        }
        return pullClaimed;
    }

    /// <summary>The press on an armed pick: open its wheel, or say why not. False when nothing is armed on that hand,
    /// so the pull stays the item's secondary use.</summary>
    private bool TryOpen(Character c, bool left)
    {
        int h = left ? 0 : 1;
        var p = pointPick[h];
        if (!pointArmed[h] || !FreeHand(c, left) || !Live(p))
            return false;
        var reason = PointVerdictNow(c, h);
        Component tint =
            p.Ground != null ? p.Ground : p.Wearer.refs.backpackHandler.activeBackpackVisuals;
        if (reason != DenyReason.None)
        {
            Affordance.DenyAtHand(left, reason, PointHint(c, reason), tint);
            return true;
        }
        if (p.Ground != null)
            p.Ground.Interact(c);
        else
            p.Wearer.refs.backpackHandler.activeBackpackVisuals.Interact_CastFinished(c);
        if (c.data.usingBackpackWheel)
        {
            BodyInventory.Instance.HoldWheelByTrigger(left);
            Haptics.Pulse(left, 0.5f, 0.05f);
        }
        else
            Affordance.DenyAtHand(left, DenyReason.CantSwitch, default, tint);
        return true;
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
