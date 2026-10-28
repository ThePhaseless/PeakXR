namespace PeakXR.Core;

/// <summary>What lies within reach of a hand when its grip closes.</summary>
public enum GrabCandidate
{
    None,

    /// <summary>A world item that would go through the pickup path.</summary>
    Item,

    /// <summary>Any other interactible: rope, vine, piton handle, luggage...</summary>
    Interactible,
}

public enum GripPressAction
{
    None,
    GrabItem,
    GrabInteractible,

    /// <summary>The free hand joins the held item: two-hand hold.</summary>
    SecondHand,
    Climb,

    /// <summary>The holding hand closes again on its own item (ready to throw).</summary>
    Regrip,

    /// <summary>Fallback: the game's normal interact on whatever the head or pointing hand aims at.</summary>
    Interact,

    /// <summary>
    /// Another feature (body slots, social reach) claimed this grip through the extension hook; it runs the
    /// action itself. Only the free hand joining the held item outranks a claim.
    /// </summary>
    Extension,
}

public enum GripReleaseAction
{
    Keep,
    Throw,
    Drop,

    /// <summary>The release belongs to an extension, e.g. holstering the held item into a body slot.</summary>
    Extension,
}

/// <summary>What the game did with an accepted pickup, so the hand that asked gets the cue that says so
/// (<see cref="GripIntent.PickupAnswer"/>).</summary>
public enum PickupCue
{
    /// <summary>In a pocket, or in the hand that asked: the success tick.</summary>
    Success,

    /// <summary>Equipped over the item that was in hand: a notice, the item in hand changed.</summary>
    Swapped,

    /// <summary>Dropped behind the hip (pockets full while climbing): the refusal.</summary>
    Dropped,
}

/// <summary>State of one hand and its surroundings at the moment its grip closes.</summary>
public struct GripContext
{
    public bool PhysicalGrab;
    public bool PhysicalClimb;
    public GrabCandidate Candidate;
    public bool NearClimbable;

    /// <summary>The character holds an item (in either hand).</summary>
    public bool HoldingItem;

    /// <summary>This hand is the one holding it.</summary>
    public bool ThisHandHolds;

    /// <summary>This hand is on or right by the held item's body (its colliders' surface).</summary>
    public bool NearHeldItem;

    /// <summary>This hand is at the held item's off-hand grip point (its <c>Hand_L</c>/<c>Hand_R</c>): a join
    /// even beside rock or a line.</summary>
    public bool AtSecondGrip;

    /// <summary>This hand is right at the held item's second grip point, within the grab radius (tighter than
    /// <see cref="AtSecondGrip"/>'s reach): the one place a pointing hand still joins the item.</summary>
    public bool AtSecondGripTight;

    /// <summary>The hand's grab candidate is the target its pointed aim hits.</summary>
    public bool CandidateIsAimed;

    /// <summary>The interactible candidate is a climb handle (a piton or pickaxe), held by the free hand only.</summary>
    public bool CandidateIsHandle;

    /// <summary>Metres from the hand to that handle, and to the probed rock (infinity when none was probed): the
    /// hand closes on whichever is nearer.</summary>
    public float HandleDistance,
        RockDistance;

    /// <summary>One-handed play (<c>Accessibility.OneHanded</c>): no free hand, so the item hand takes a climb
    /// handle and keeps the item, as vanilla hangs.</summary>
    public bool OneHanded;
    public Handedness HeldHandedness;

    /// <summary>The game holds the held item in both hands (it is not <c>rightHandOnly</c>), so a free hand may
    /// join it whatever <see cref="HeldHandedness"/> says.</summary>
    public bool HeldBothHandsInGame;

    /// <summary>
    /// An extension (inventory slot, social target) wants this press. Independent of the physical toggles,
    /// since extensions have their own settings.
    /// </summary>
    public bool ExtensionClaim;

    /// <summary>The body is already on a wall or in the air: a wall in reach wins over a loose item, so a
    /// hand reaching for a hold never picks up an item lying on the ledge instead.</summary>
    public bool BodyClimbing;

    /// <summary>The hold probe found rock the game would let this hand grab, not just something in the grip
    /// sphere. A hand that holds an item climbs only on such a hold.</summary>
    public bool HoldConfirmed;

    /// <summary>The hand is in the pointing pose (<see cref="GripIntent.IsPointing"/>). Any grip with the finger off
    /// the trigger reads as this, so on its own it decides nothing.</summary>
    public bool Pointing;

    /// <summary>What the hand's aim ray hits within the game's interact reach.</summary>
    public AimedTarget Aimed;

    /// <summary>Pointing at an item taker: a deliberate aim with the item it takes in hand, which beats a
    /// climbable in reach and a body-slot claim (only those).</summary>
    public readonly bool PointedAtTaker => Pointing && Aimed == AimedTarget.ItemTaker;
}

/// <summary>What a pointing hand's aim ray hits within the game's interact reach.</summary>
public enum AimedTarget
{
    None,

    /// <summary>Something interactible that does not depend on the held item.</summary>
    Other,

    /// <summary>A target that takes the interact only because of the held item (the scout statue with an
    /// amulet, a skeleton with the Book of Bones): nobody climbs while offering it.</summary>
    ItemTaker,
}

/// <summary>
/// Whether a grip's release may drop or throw the item in that hand. Only an item the grip closed on, or one the
/// hand already held when it closed: an item that arrives in a hand whose grip is already closed (a hotbar or
/// wheel select, a friend's hand-over, a game equip) needs a fresh close before a release lets it go, so a player
/// who happens to be gripping never loses it.
/// </summary>
public static class ReleaseArming
{
    /// <summary>The grip closes: whatever the hand holds, or then grabs, is the grip's.</summary>
    public static bool OnPress() => true;

    /// <summary>An item arrived in this hand.</summary>
    /// <param name="gripClosed">The hand's grip was closed when it arrived.</param>
    /// <param name="action">What that grip is doing.</param>
    /// <param name="isTheGrabbedItem">It is the item that grip's pickup reached for: its own grab, or the item
    /// the game's interact picked up for a grip that fell back to it.</param>
    public static bool OnArrive(bool gripClosed, GripPressAction action, bool isTheGrabbedItem) =>
        !gripClosed
        || action == GripPressAction.Extension
        || ((action is GripPressAction.GrabItem or GripPressAction.Interact) && isTheGrabbedItem);
}

/// <summary>
/// Decides what a grip press or release means. The rules keep hands independent and allow only one held
/// item: a free hand may join the held item, climb or interact, and a second item it grabs goes to the pockets.
/// </summary>
public static class GripIntent
{
    public static GripPressAction OnPress(in GripContext c)
    {
        // The free hand joins the held item at its second grip, or on its body when nothing else is at the hand:
        // a chain, rope, ledge or item there wins over brushing the item (VR run 51's chain grab joined the
        // launcher instead).
        // A pointing hand whose aim finds an interactible is aiming at it, not joining the item, unless it sits
        // right on the item's second grip (VR run 66: a hand pointing at the statue joined the held amulet).
        bool aimsElsewhere = c.Pointing && c.Aimed != AimedTarget.None && !c.AtSecondGripTight;
        if (
            c.HoldingItem
            && !c.ThisHandHolds
            && ItemHoldTable.AcceptsSecondHand(c.HeldHandedness, c.HeldBothHandsInGame)
            && (
                (c.AtSecondGrip && !aimsElsewhere)
                // A hand closing on the item's body joins it unless it points at a taker: a scripted or relaxed
                // grip reads as pointing, and whatever lies past the item must not steal the join (vr.14).
                || (
                    c.NearHeldItem
                    && c.Candidate == GrabCandidate.None
                    && !c.NearClimbable
                    && !(c.PointedAtTaker && !c.AtSecondGripTight)
                )
            )
        )
            return GripPressAction.SecondHand;

        // Nobody climbs with a pointed finger: a pointed aim at a target that takes it beats a climb or a body-slot
        // claim, but not the hand's own grab candidate, whose path (a world hold, its progress, its refusals)
        // is the interact itself (the airport kiosk, run 52).
        if (c.PointedAtTaker)
        {
            // The hand already on the taker grabs it there; some other interactible at the hand is not what
            // it points at (vr66: an unrelated candidate at the pointing hand).
            if (c.PhysicalGrab && c.Candidate == GrabCandidate.Interactible && c.CandidateIsAimed)
                return GripPressAction.GrabInteractible;
            if (c.PhysicalGrab && c.Candidate == GrabCandidate.Item && !c.HoldingItem)
                return GripPressAction.GrabItem;
            return GripPressAction.Interact;
        }

        if (c.ExtensionClaim)
            return GripPressAction.Extension;
        // The item hand on a piton or handle keeps its item: no hang, and no wall catch on the rock behind it
        // either, which would put the item away (VR run 59). The press is a Regrip, refused with HandsFull.
        // Only when the hand is closing on the handle itself: on rock nearer than the handle the climb wins as in
        // vanilla, which puts the item away (CharacterClimbing.cs:569).
        if (
            c.ThisHandHolds
            && c.CandidateIsHandle
            && !c.OneHanded
            && c.HandleDistance <= c.RockDistance + HandleDepth
        )
            return GripPressAction.Regrip;
        // Tightening the grip on a held item is never a climb attempt (climbing puts the item away, and a sticky one
        // falls, CharacterItems.cs:498): the holding hand climbs only when it is on a real hold, as a falling
        // player catching the wall.
        bool mayClimb = c.PhysicalClimb && c.NearClimbable && (!c.ThisHandHolds || c.HoldConfirmed);
        if (c.BodyClimbing && mayClimb && c.Candidate != GrabCandidate.Interactible)
            return GripPressAction.Climb;

        if (c.PhysicalGrab)
        {
            if (c.Candidate == GrabCandidate.Item && !c.HoldingItem)
                return GripPressAction.GrabItem;
            // The hand that holds an item never hangs from a climb handle: the free hand does (vanilla keeps the
            // item while hanging, ClimbHandle.cs:54-67, CharacterClimbing.cs:640-647).
            if (
                c.Candidate == GrabCandidate.Interactible
                && !(c.ThisHandHolds && c.CandidateIsHandle && !c.OneHanded)
            )
                return GripPressAction.GrabInteractible;
        }

        if (mayClimb)
            return GripPressAction.Climb;
        // A free hand on a second item: the game pockets it (Item.Interact, Item.cs:578-591), so the hand takes the
        // item it touched rather than whatever the head aims at.
        if (c.PhysicalGrab && c.Candidate == GrabCandidate.Item && !c.ThisHandHolds)
            return GripPressAction.GrabItem;
        if (c.ThisHandHolds)
            return GripPressAction.Regrip;
        return GripPressAction.Interact;
    }

    /// <summary>
    /// Throw when the holding hand lets go fast, otherwise keep the item (or drop it when configured).
    /// An extension claim (holstering over a slot) wins over throw and drop for the holding hand.
    /// </summary>
    public static GripReleaseAction OnRelease(
        bool thisHandHolds,
        bool canDrop,
        float speed,
        float throwSpeed,
        bool throwEnabled,
        bool releaseToDrop,
        bool extensionClaims = false,
        bool armed = true
    )
    {
        // An item that reached the hand while its grip was already closed (ReleaseArming) is kept: the grip
        // never closed on it, so letting go of it is no drop or throw.
        if (!thisHandHolds || !armed)
            return GripReleaseAction.Keep;
        if (extensionClaims)
            return GripReleaseAction.Extension;
        if (!canDrop)
            return GripReleaseAction.Keep;
        if (throwEnabled && speed >= throwSpeed)
            return GripReleaseAction.Throw;
        return releaseToDrop ? GripReleaseAction.Drop : GripReleaseAction.Keep;
    }

    /// <summary>The temporary slot the game fills when the pockets are full (<c>Player.tempFullSlot</c>).</summary>
    public const byte TempSlot = 250;

    /// <summary>
    /// The cue for the game's answer to a pickup (<c>CharacterItems.OnPickupAccepted</c>, CharacterItems.cs:440-465)
    /// into <paramref name="slot"/>. A pocket slot is a pickup that worked. The temporary slot (pockets full) is
    /// dropped behind the hip while climbing (:445-452), and otherwise equipped (:457): over the held item when
    /// something was <paramref name="holding"/>, which swaps what is in hand, so that is a notice, not a success.
    /// </summary>
    public static PickupCue PickupAnswer(byte slot, bool climbing, bool holding) =>
        slot != TempSlot ? PickupCue.Success
        : climbing ? PickupCue.Dropped
        : holding ? PickupCue.Swapped
        : PickupCue.Success;

    /// <summary>
    /// Why a release that <see cref="OnRelease"/> made a throw or drop must keep the item instead, or None. The
    /// game ignores both while the item casts (progress above 0, CharacterItems.DoDropping); a throw also needs
    /// <c>canThrow</c>.
    /// </summary>
    public static DenyReason ReleaseRefusal(
        GripReleaseAction result,
        bool canThrow,
        float castProgress
    )
    {
        if (result is not (GripReleaseAction.Throw or GripReleaseAction.Drop))
            return DenyReason.None;
        if (castProgress > 0f)
            return DenyReason.TooSoon;
        return result == GripReleaseAction.Throw && !canThrow
            ? DenyReason.CantThrow
            : DenyReason.None;
    }

    /// <summary>
    /// When a pointing grip's interact comes due. While the hand keeps pointing it is aiming a finger-gun ping, so
    /// the interact keeps waiting; it comes due <paramref name="window"/> seconds after the index curls with no
    /// trigger squeeze (the squeeze itself cancels it). Pinging never picks anything up.
    /// </summary>
    public static float DeferPointedInteract(
        float interactAt,
        bool pointing,
        float now,
        float window
    ) => pointing && now + window > interactAt ? now + window : interactAt;

    /// <summary>How far a piton or handle stands out of the rock it is set in (metres): rock up to this much nearer
    /// than the handle's surface is the face it sits on, not a separate hold (VR run 60).</summary>
    public const float HandleDepth = 0.1f;

    /// <summary>Index straight, the other fingers closed: the hand aims instead of the head. A finger resting on the
    /// trigger (<see cref="FingerCurlMapper.TriggerTouchCurl"/>) is a fist, not a point: at 0.35 a touch-sensing
    /// controller's plain grip read as a finger gun and its interact waited forever (VR run 59, vr.81).</summary>
    public static bool IsPointing(in FingerCurls c) =>
        c.Index < PointedIndex && c.Middle > 0.6f && c.Ring > 0.5f;

    /// <summary>Index curl below which the finger counts as straight, under a finger resting on the trigger.</summary>
    public const float PointedIndex = 0.2f;

    /// <summary>
    /// The ping's finger gun: pointing with the thumb lifted off the stick and face buttons. A grip with the index
    /// straight and the thumb resting is a common way to hold Index and Touch controllers, so that alone is a
    /// pointed interact, never a ping aim (external review #13).
    /// </summary>
    public static bool IsFingerGun(in FingerCurls c) => IsPointing(c) && c.Thumb < 0.5f;

    /// <summary>A pointing grip's interact waits for a possible ping only for a finger gun while pinging is
    /// live; any other pointing grip interacts at once, so neither steals the other.</summary>
    public static bool PointedInteractWaits(in FingerCurls c, bool pingLive) =>
        pingLive && IsFingerGun(c);
}
