using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;

namespace PeakXR.Interact.Physical;

/// <summary>Harmony patches of <see cref="ItemGesturesFeature"/>. All act on the local VR player only.</summary>
internal static class ItemGesturesPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the game's once-per-frame input
    /// sampling for the local character. Runs before interact.physical's postfix (Priority.Low), so the gesture
    /// requests it makes are injected by that postfix in the same frame.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.High)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            try
            {
                var c = Character.localCharacter;
                if (c != null && c.input == __instance)
                    ItemGestures.Tick(c, __instance, playerMovementActive);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<ItemGesturesFeature>(e);
            }
        }
    }

    /// <summary>BugleSFX.UpdateTooting (BugleSFX.cs:43, private): sends RPC_StartToot with a pitch from the holder's
    /// lookDirection (BugleSFX.cs:59-61). The look is swapped to the bell for the call and restored in a finalizer.</summary>
    [HarmonyPatch]
    internal static class Tooting
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(BugleSFX), "UpdateTooting");

        private static void Prefix(BugleSFX __instance) => HeldGestures.BeforeTooting(__instance);

        private static Exception Finalizer(Exception __exception)
        {
            HeldGestures.AfterTooting();
            return __exception;
        }
    }

    /// <summary>Interaction.DoInteraction (Interaction.cs:99, private): adds the frame time to
    /// currentInteractableHeldTime and finishes the cast at currentConstantInteractableTime (Interaction.cs:136-139).
    /// The prefix caps a lifted lid's held time first.</summary>
    [HarmonyPatch]
    internal static class DoInteraction
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteraction");

        private static void Prefix(Interaction __instance) =>
            LidGesture.BeforeInteraction(__instance);
    }

    /// <summary>Item.CancelUsePrimary (Item.cs:918) and Item.CancelUseSecondary (Item.cs:1063): every way a use stops
    /// early runs through them (released, pulled away from the mouth or body, the build spot left behind at
    /// Constructable.cs:52-56, unusable mid-cast at CharacterItems.cs:209-215, stashed at Item.cs:1124-1129). The
    /// prefix still sees the cast in progress and gives the cancel cue.</summary>
    [HarmonyPatch]
    internal static class CancelUse
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return GameMember.Method(typeof(Item), "CancelUsePrimary");
            yield return GameMember.Method(typeof(Item), "CancelUseSecondary");
        }

        private static void Prefix(Item __instance, MethodBase __originalMethod) =>
            ItemGestures.BeforeCancel(__instance, __originalMethod.Name == "CancelUseSecondary");
    }

    /// <summary>CharacterItems.DoUsing (CharacterItems.cs:197, private): where the game reads this frame's use
    /// input, after every feature injected its own. Off-hand presses the game refuses, and presses it accepts that do
    /// nothing, get their refusal cue here.</summary>
    [HarmonyPatch]
    internal static class DoUsing
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "DoUsing");

        private static void Prefix(Character ___character) =>
            ItemGestures.BeforeUsing(___character);
    }

    /// <summary>Item.Interact (Item.cs:578-597): a pickup within 0.25 s of an equip (:580) is ignored without a
    /// word. The prefix refuses it at the item instead.</summary>
    [HarmonyPatch]
    internal static class PickupCooldown
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(Item), "Interact");

        private static void Prefix(Item __instance, Character interactor) =>
            ItemGestures.BeforePickup(__instance, interactor);
    }

    /// <summary>Item.DenyPickupRPC (Item.cs:599-603): the master refused this player's pickup (someone else got it,
    /// or no slot by the time it arrived, :627, :639); the item reappears where it was.</summary>
    [HarmonyPatch]
    internal static class PickupDenied
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Item), "DenyPickupRPC");

        private static void Postfix(Item __instance) =>
            ItemGestures.PickupRefused(__instance, DenyReason.Occupied);
    }

    /// <summary>Action_WarpToRandomPlayer.IncreaseUsesRPC (Action_WarpToRandomPlayer.cs:35-46): RunAction (:13-31)
    /// found nobody alive farther than <c>minimumDistance</c> and gave the use back after the warp sound. Runs on every
    /// client; only the local holder gets NeedsFriend at the hand with the compass red.</summary>
    [HarmonyPatch]
    internal static class WarpRefund
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Action_WarpToRandomPlayer), "IncreaseUsesRPC");

        private static void Postfix(Action_WarpToRandomPlayer __instance) =>
            ItemGestures.WarpRefunded(__instance.GetComponent<Item>());
    }

    /// <summary>Skelleton.Update (Skelleton.cs:31, private): each skeleton, every frame. Reports its hips
    /// (Skelleton.Center, :80) so the book's read zone stays off where the book revives instead.</summary>
    [HarmonyPatch]
    internal static class SkeletonNear
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(Skelleton), "Update");

        private static void Postfix(Skelleton __instance) => ItemGestures.SeeSkeleton(__instance);
    }
}
