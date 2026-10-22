using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Peak;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Interact.Physical;

/// <summary>
/// <c>interact.world</c>: world props the hands act on beyond the item gestures. Hold the open Book of Bones over a
/// skeleton to revive it; bring an amulet to its altar socket, lay both palms on the soul pillar, push a hand into
/// the summit portal; notices for a cannon shot that left you behind, a checkpoint that saved you and a summoned
/// helicopter, and the summit countdown's last seconds on the wrist; local guards for two vanilla wastes (D11: an amulet put into a full altar, an effigy used
/// with nobody down) that refuse with the usual cue instead; a scene button only the host can press refuses for
/// everyone else; the campfire's "can't light" name list stays opt-in text. Every action still ends in the game's own
/// interact hold or use, and nothing new goes over the network.
/// </summary>
public sealed class WorldHoldsFeature : Feature
{
    public override string Id => "interact.world";

    // After interact.items (15): same input frame, its gesture requests merge first.
    public override int Order => 16;

    internal static ConfigEntry<bool> BookOverBones { get; private set; }
    internal static ConfigEntry<bool> GuardWaste { get; private set; }
    internal static ConfigEntry<bool> AmuletInSocket { get; private set; }
    internal static ConfigEntry<bool> PalmsOnPillar { get; private set; }
    internal static ConfigEntry<bool> HandIntoPortal { get; private set; }

    protected internal override void Install()
    {
        // Book requests reach the game only through interact.physical's input injection.
        if (!FeatureManager.IsActive<PhysicalInteractionFeature>())
            throw new InvalidOperationException("needs interact.physical");
        BookOverBones = Plugin.Settings.Bind(
            "Interaction",
            "BookOverBones",
            true,
            "Hold the Book of Bones open just above a skeleton's hips to revive that scout; the revive runs while the "
                + "book stays there. Gripping the skeleton still works."
        );
        GuardWaste = Plugin.Settings.Bind(
            "Interaction",
            "GuardWastedItems",
            true,
            "Refuse, with the usual cue, two actions the game would waste: putting an amulet into a full altar (it "
                + "is lost) and placing a scout effigy when nobody is down (it does nothing). Only your own action is "
                + "stopped."
        );
        AmuletInSocket = Plugin.Settings.Bind(
            "Interaction",
            "AmuletInSocket",
            true,
            "Put an amulet into the summit altar by bringing it to its socket; the insert runs while it stays there. "
                + "Gripping the statue still works."
        );
        PalmsOnPillar = Plugin.Settings.Bind(
            "Interaction",
            "PalmsOnPillar",
            true,
            "Commune with the soul pillar by laying both open palms on it; the hold runs while they stay there. "
                + "Gripping it still works."
        );
        HandIntoPortal = Plugin.Settings.Bind(
            "Interaction",
            "HandIntoPortal",
            true,
            "Enter the summit portal by pushing a hand into it and keeping it there; pull the hand out to stop. "
                + "Gripping it still works."
        );
        WorldHolds.HasAmulets = GameMember.Field<ScoutStatue, int[]>("hasAmulets");
        WorldHolds.Usability = GameMember.Field<Item, Optionable<bool>>("overrideUsability");
        WorldHolds.CannonEntry = GameMember.Field<ScoutCannon, Transform>("entry");
        Patch(typeof(WorldHolds.PortalSeen));
        Patch(typeof(WorldHolds.CannonLit));
        Patch(typeof(WorldHolds.CannonLaunch));
        Patch(typeof(WorldHolds.CheckpointUsed));
        Patch(typeof(WorldHolds.FlareSummon));
        Patch(typeof(WorldHolds.SummitCountdown));
        Patch(typeof(WorldHolds.Sample));
        Patch(typeof(WorldHolds.SkeletonSeen));
        Patch(typeof(WorldHolds.AltarFull));
        Patch(typeof(WorldHolds.EffigyNobodyDown));
        Patch(typeof(WorldHolds.HostButton));
        Patch(typeof(WorldHolds.CampfireText));
    }
}

/// <summary>Per-frame state and patch bodies of <see cref="WorldHoldsFeature"/>. Local VR player only. The summit
/// props (altar socket, soul pillar, portal) live in <c>WorldHolds.Summit.cs</c>, the notices (cannon, checkpoint,
/// flare) in <c>WorldHolds.Notices.cs</c>.</summary>
internal static partial class WorldHolds
{
    /// <summary>The book hand above the skeleton's hips (<c>Skelleton.Center</c>, Skelleton.cs:87): radius and height
    /// span. Up to 0.9 m covers a player standing over a body on the ground with the book at waist height.</summary>
    private const float BookRadius = ReadZone.ReviveRadius;
    private const float BookMaxUp = ReadZone.ReviveUp;

    /// <summary>Skeletons farther than this from the book hand are not considered.</summary>
    private const float BookSearch = 1.5f;

    internal static AccessTools.FieldRef<ScoutStatue, int[]> HasAmulets;
    internal static AccessTools.FieldRef<Item, Optionable<bool>> Usability;

    private static readonly ContactDwell Book = new()
    {
        Enter = 0f,
        Exit = 0.15f,
        Dwell = 0.3f,
    };

    /// <summary>The tracked grip of the book hand, not the book: the revive hold opens the book
    /// (Skelleton.Interact, Skelleton.cs:65-71, Action_BookOfBonesAnim.Open), which moves the item's render centre
    /// up out of the zone and ended the hold it had just started (vr32: 0.49 m above, over nothing).</summary>
    private static Vector3 bookPoint;
    private static bool bookOut;
    private static Skelleton seen;
    private static float seenSq;
    private static Skelleton bookTarget;

    /// <summary>The skeleton the book is held over this frame, for the self-test (vr.84).</summary>
    internal static Skelleton BookTarget => Book.Active ? bookTarget : null;

    /// <summary>The hand whose grip holds the game's interact on <paramref name="target"/>, else
    /// <paramref name="fallback"/> (a flame or book held in that hand).</summary>
    public static bool HandOn(IInteractible target, bool fallback)
    {
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            if (
                PhysicalInteraction.HandAction(left) == GripPressAction.GrabInteractible
                && ReferenceEquals(PhysicalInteraction.HandTarget(left), target)
            )
                return left;
        }
        return fallback;
    }

    private static void Tick(Character c, bool active)
    {
        TickBook(c, active);
        bool on = active && VRSession.Active;
        TickSocket(c, on);
        TickPillar(c, on);
        TickPortal(on);
        TickNotices(c);
    }

    private static void TickBook(Character c, bool active)
    {
        var skeleton = seen;
        seen = null;
        seenSq = BookSearch * BookSearch;
        var item = c.data.currentItem;
        bookOut =
            active
            && VRSession.Active
            && WorldHoldsFeature.BookOverBones.Value
            && item != null
            && item.CompareTag("BookOfBones");
        if (!bookOut)
        {
            Book.Reset();
            bookTarget = null;
            return;
        }
        bool left = LocalHold.ItemInLeftHand;
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        bookPoint = hand != null ? hand.position : item.Center();
        if (skeleton == null || !ReferenceEquals(skeleton, bookTarget))
        {
            Book.Reset();
            bookTarget = skeleton;
        }
        if (skeleton == null)
            return;
        bool was = Book.Active;
        float d = HeatZone.Distance(
            bookPoint.N(),
            skeleton.Center().N(),
            BookRadius,
            0f,
            BookMaxUp
        );
        if (!Book.Update(d, Time.deltaTime))
            return;
        // Skelleton.IsInteractible (Skelleton.cs:58-63): the book with uses left.
        if (!skeleton.IsInteractible(c))
        {
            if (!was)
                Affordance.Deny(left, skeleton.Center(), DenyReason.UsedUp, default, item);
            return;
        }
        GestureRequests.Interact(skeleton);
        var interaction = Interaction.instance;
        if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, skeleton))
            Affordance.Progress(left, Mathf.Clamp01(interaction.constantInteractableProgress));
    }

    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the local character's input frame,
    /// before interact.physical's postfix (Priority.Low) injects the gesture requests.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.High)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            var c = Character.localCharacter;
            if (c != null && c.input == __instance)
                Tick(c, playerMovementActive);
        }
    }

    /// <summary>Skelleton.Update (Skelleton.cs:31, private): each skeleton reports itself when it is near the held
    /// book, so finding one needs no scene search or physics query.</summary>
    [HarmonyPatch]
    internal static class SkeletonSeen
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(Skelleton), "Update");

        private static void Postfix(Skelleton __instance)
        {
            if (!bookOut || __instance.hip == null)
                return;
            float sq = (__instance.Center() - bookPoint).sqrMagnitude;
            if (sq < seenSq)
            {
                seenSq = sq;
                seen = __instance;
            }
        }
    }

    /// <summary>
    /// ScoutStatue.Interact_CastFinished (Peak/ScoutStatue.cs:302-310): sends RPC_InsertAmulet and consumes the
    /// amulet even when every slot is taken, where the master then drops it (:176-185). With the altar full (all
    /// <c>hasAmulets</c> set, :10, GetFreeSlot :213) the local VR player's cast is skipped and refused instead
    /// (D11); the gem (amulet index 99, :167) always goes in.
    /// </summary>
    [HarmonyPatch]
    internal static class AltarFull
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(ScoutStatue),
                "Interact_CastFinished",
                new[] { typeof(Character) }
            );

        private static bool Prefix(ScoutStatue __instance, Character interactor)
        {
            if (!VRSession.Active || !WorldHoldsFeature.GuardWaste.Value || !interactor.IsLocal)
                return true;
            var item = interactor.data.currentItem;
            if (item == null || !item.TryGetComponent(out AmuletBase amulet))
                return true;
            if (amulet.amuletIndex == 99 || !Full(__instance))
                return true;
            Affordance.Deny(
                HandOn(__instance, LocalHold.ItemInLeftHand),
                item.Center(),
                DenyReason.Occupied,
                default,
                __instance
            );
            return false;
        }

        private static bool Full(ScoutStatue statue)
        {
            var slots = HasAmulets(statue);
            if (slots == null)
                return false;
            foreach (int slot in slots)
            {
                if (slot == -1)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// ScoutEffigy.Update (ScoutEffigy.cs:8-21): it marks itself unusable with nobody down, but
    /// <c>Constructable.Update</c> (Constructable.cs:62-69) then overwrites that from the placement check, so the
    /// placement plays out and revives nobody. For the local VR holder the "nobody down" lock is put back afterwards
    /// (D11); the trigger then gets the usual CantUse refusal (UseDenial).
    /// </summary>
    [HarmonyPatch]
    internal static class EffigyNobodyDown
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(ScoutEffigy), "Update");

        private static void Postfix(ScoutEffigy __instance)
        {
            var item = __instance.item;
            if (
                !VRSession.Active
                || !WorldHoldsFeature.GuardWaste.Value
                || item == null
                || item.holderCharacter == null
                || !item.holderCharacter.IsLocal
                || Character.PlayerIsDeadOrDown()
            )
                return;
            Usability(item) = Optionable<bool>.Some(false);
        }
    }

    /// <summary>
    /// TriggerOnInteract.Interact (TriggerOnInteract.cs:23-26): <c>TriggerEvent.TriggerEntered</c> fires only for the
    /// owner of its PhotonView (TriggerEvent.cs:46-53), so for anyone else the press is a silent no-op. Refused with
    /// Blocked (WS-W28).
    /// </summary>
    [HarmonyPatch]
    internal static class HostButton
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(TriggerOnInteract), "Interact", new[] { typeof(Character) });

        private static void Postfix(TriggerOnInteract __instance)
        {
            if (!VRSession.Active || __instance.triggerEvent == null)
                return;
            var view = __instance.triggerEvent.GetComponentInParent<PhotonView>();
            if (view == null || view.IsMine)
                return;
            Affordance.Deny(
                HandOn(__instance, !VRInput.LeftHanded),
                __instance.Center(),
                DenyReason.Blocked,
                default,
                __instance
            );
        }
    }

    /// <summary>
    /// Campfire.GetInteractionText (Campfire.cs:272-287): an unlit fire with friends away lists every missing name and
    /// distance. In VR that list is opt-in text (<c>UI.ActionHintText</c>); the refusal cue shows who is missing.
    /// </summary>
    [HarmonyPatch]
    internal static class CampfireText
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Campfire), "GetInteractionText");

        private static void Postfix(Campfire __instance, ref string __result)
        {
            if (
                !VRSession.Active
                || __instance.Lit
                || AffordanceFeature.ActionHintText == null
                || AffordanceFeature.ActionHintText.Value
                || __result == null
                || __result.IndexOf('\n') < 0
            )
                return;
            __result = LocalizedText.GetText("CANTLIGHT");
        }
    }
}
