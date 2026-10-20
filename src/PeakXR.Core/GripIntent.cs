namespace PeakXR.Core
{
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

        /// <summary>This hand is within reach of the held item.</summary>
        public bool NearHeldItem;
        public Handedness HeldHandedness;

        /// <summary>
        /// An extension (inventory slot, social target) wants this press. Independent of the physical toggles,
        /// since extensions have their own settings.
        /// </summary>
        public bool ExtensionClaim;
    }

    /// <summary>
    /// Decides what a grip press or release means. The rules keep hands independent and allow only one held
    /// item: a free hand may join the held item, climb or interact, but never pick up a second item.
    /// </summary>
    public static class GripIntent
    {
        public static GripPressAction OnPress(in GripContext c)
        {
            if (
                c.HoldingItem
                && !c.ThisHandHolds
                && c.NearHeldItem
                && ItemHoldTable.AcceptsSecondHand(c.HeldHandedness)
            )
                return GripPressAction.SecondHand;

            if (c.ExtensionClaim)
                return GripPressAction.Extension;

            if (c.PhysicalGrab)
            {
                if (c.Candidate == GrabCandidate.Item && !c.HoldingItem)
                    return GripPressAction.GrabItem;
                if (c.Candidate == GrabCandidate.Interactible)
                    return GripPressAction.GrabInteractible;
            }

            if (c.PhysicalClimb && c.NearClimbable)
                return GripPressAction.Climb;
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
            bool extensionClaims = false
        )
        {
            if (!thisHandHolds)
                return GripReleaseAction.Keep;
            if (extensionClaims)
                return GripReleaseAction.Extension;
            if (!canDrop)
                return GripReleaseAction.Keep;
            if (throwEnabled && speed >= throwSpeed)
                return GripReleaseAction.Throw;
            return releaseToDrop ? GripReleaseAction.Drop : GripReleaseAction.Keep;
        }

        /// <summary>Index straight, the other fingers closed: the hand aims instead of the head.</summary>
        public static bool IsPointing(in FingerCurls c) =>
            c.Index < 0.35f && c.Middle > 0.6f && c.Ring > 0.5f;
    }
}
