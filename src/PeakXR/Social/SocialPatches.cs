using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Social;

/// <summary>Harmony patches of <see cref="SocialFeature"/>. All act on the local VR player only.</summary>
internal static class SocialPatches
{
    private static MethodBase SampleMethod() =>
        GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

    private static bool IsLocal(CharacterInput input, out Character c)
    {
        c = Character.localCharacter;
        return c != null && c.input == input && VRSession.Active;
    }

    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal), first postfix: the game's own
    /// interact state, before any hand adds to it (for the give-up guard).</summary>
    [HarmonyPatch]
    internal static class SampleFirst
    {
        private static MethodBase TargetMethod() => SampleMethod();

        [HarmonyPriority(Priority.First)]
        private static void Postfix(CharacterInput __instance)
        {
            if (IsLocal(__instance, out _))
                SocialHands.Instance.CaptureVanilla(__instance);
        }
    }

    /// <summary>CharacterInput.Sample(bool), after <c>interact.physical</c> (priority Low), whose grip edges reach
    /// <see cref="SocialHands"/> first: hold interact or reach for the gripped target.</summary>
    [HarmonyPatch]
    internal static class SampleTick
    {
        private static MethodBase TargetMethod() => SampleMethod();

        [HarmonyPriority(Priority.VeryLow)]
        private static void Postfix(CharacterInput __instance)
        {
            if (IsLocal(__instance, out var c))
                SocialHands.Instance.Tick(c, __instance, Time.deltaTime);
        }
    }

    /// <summary>CharacterInput.Sample(bool), last postfix: the give-up guard sees every injected interact.</summary>
    [HarmonyPatch]
    internal static class SampleLast
    {
        private static MethodBase TargetMethod() => SampleMethod();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CharacterInput __instance)
        {
            if (IsLocal(__instance, out var c))
                SocialHands.Instance.GuardGiveUp(c, __instance);
        }
    }

    /// <summary>
    /// Interaction.DoInteractableRaycasts (Interaction.cs:196): Interaction.LateUpdate derives the prompt,
    /// <c>bestCharacter</c> (feeding, Item.cs:986/1050) and <c>DoInteraction</c> from its result. Runs after
    /// <c>interact.physical</c>'s postfix; a hand-chosen social target replaces the head ray's pick. Other callers
    /// (Action_CloneSelectedItem.cs:28) pass an override distance and are left alone.
    /// </summary>
    [HarmonyPatch]
    internal static class Raycasts
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteractableRaycasts");

        [HarmonyPriority(Priority.LowerThanNormal)]
        private static void Postfix(ref IInteractible interactableResult, float overrideDistance)
        {
            if (overrideDistance != -1f || !VRSession.Active)
                return;
            var target = SocialHands.Instance.RaycastTarget;
            if (target is Object unityObject && unityObject != null)
                interactableResult = target;
        }
    }

    /// <summary>CharacterInteractible.IsSecondaryInteractible (CharacterInteractible.cs:156): vanilla requires both
    /// players' look directions to face each other (:164). When a VR hand holds the item at this friend's mouth, the
    /// hand replaces that check. Items with their own feed angle (overrideFeedAngle) keep vanilla's rule.</summary>
    [HarmonyPatch]
    internal static class FeedAngle
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterInteractible),
                "IsSecondaryInteractible",
                new[] { typeof(Character) }
            );

        private static void Postfix(
            CharacterInteractible __instance,
            Character interactor,
            ref bool __result
        )
        {
            if (__result || !VRSession.Active || interactor != Character.localCharacter)
                return;
            if (SocialHands.Instance.FeedTarget != __instance || __instance.character == interactor)
                return;
            var item = interactor.data.currentItem;
            if (
                item != null
                && item.canUseOnFriend
                && !item.overrideFeedAngle
                && __instance.character != null
                && !__instance.character.data.dead
            )
                __result = true;
        }
    }

    /// <summary>GUIManager.Grasp (GUIManager.cs:754): called when the local reach catches a friend
    /// (CharacterGrabbing.cs:247) or a helping hand catches us (CharacterGrabbing.cs:282). Adds a haptic cue.</summary>
    [HarmonyPatch]
    internal static class Grasp
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(GUIManager), "Grasp");

        private static void Postfix() => SocialHands.Instance.OnGrasp();
    }
}
