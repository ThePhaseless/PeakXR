using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using Photon.Pun;
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
    /// (Action_CloneSelectedItem.cs:28) pass an override distance and are left alone. The target must pass the
    /// game's own <c>IsInteractible</c> (CharacterInteractible.cs:126), as the ray's pick does (Interaction.cs:233),
    /// so an item with <c>overrideFeedAngle</c> (CharacterInteractible.cs:167-172) keeps its vanilla feed window. The
    /// head-aim parts of that check for thorns, ticks and packs follow the hand (<see cref="HandPick"/>,
    /// <see cref="PackPick"/>).
    /// </summary>
    [HarmonyPatch]
    internal static class Raycasts
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteractableRaycasts");

        [HarmonyPriority(Priority.LowerThanNormal)]
        private static void Postfix(ref IInteractible interactableResult, float overrideDistance)
        {
            var c = Character.localCharacter;
            if (overrideDistance != -1f || c == null || !VRSession.Active)
                return;
            var hands = SocialHands.Instance;
            var target = hands.RaycastTarget;
            if (target is not Object unityObject || unityObject == null)
                return;
            if (target.IsInteractible(c))
                interactableResult = target;
            else if (
                hands.FeedReady
                && target is CharacterInteractible friend
                && friend == hands.FeedTarget
                && c.data.currentItem != null
                && c.data.currentItem.overrideFeedAngle
            )
                ParityFeedback.Denied(
                    LocalHold.ItemInLeftHand,
                    target.Center(),
                    ParityFeedback.WrongAngle
                );
        }
    }

    /// <summary>
    /// ThornOnMe.IsInteractible (ThornOnMe.cs:121-138) and Bugfix.IsInteractible (Bugfix.cs:123-132) also demand the
    /// camera aim within 5° of a thorn, 2° of a tick (+15° solo). For the target a hand picked, the camera looks from
    /// the eye at it for that one call, so the pick follows the hand; a flat player could turn to look there too.
    /// Restored in a finalizer, so an exception cannot leave the camera turned.
    /// </summary>
    [HarmonyPatch]
    internal static class HandPick
    {
        private static MethodBase[] TargetMethods() =>
            [
                GameMember.Method(typeof(ThornOnMe), "IsInteractible", [typeof(Character)]),
                GameMember.Method(typeof(Bugfix), "IsInteractible", [typeof(Character)]),
            ];

        private static void Prefix(
            Component __instance,
            Character interactor,
            out CameraSwap __state
        )
        {
            __state = default;
            var mc = MainCamera.instance;
            if (
                !VRSession.Active
                || mc == null
                || interactor != Character.localCharacter
                || !ReferenceEquals(SocialHands.Instance.RaycastTarget, __instance)
            )
                return;
            var eye = mc.transform.position;
            var to = __instance.transform.position - eye;
            if (to.sqrMagnitude > 1e-6f)
                __state = CameraSwap.Begin(eye, Quaternion.LookRotation(to));
        }

        private static System.Exception Finalizer(
            System.Exception __exception,
            CameraSwap __state
        ) => CameraSwap.Restore(__exception, __state);
    }

    /// <summary>
    /// ThornOnMe.HoverEnter / HoverExit (ThornOnMe.cs:155-165) write its highlight through <c>mpb</c>, which only
    /// <c>AddPropertyBlock</c> (:144) creates, and <c>OnEnable</c> (:54-60) calls that only when the serialized
    /// <c>mainRenderer</c> is unset; on the thorn prefabs it is set, so <c>mpb</c> stays null. Vanilla's head cone almost
    /// never hovers a thorn, but a hand pick does: GUIManager.OnInteractChange (GUIManager.cs:1031-1043) then throws in
    /// HoverExit before it takes the new hover, and repeats every frame, which stops the rest of GUIManager.LateUpdate
    /// (prompts, wheels, wrist readouts). Creates the block the way <c>AddPropertyBlock</c> does before either runs;
    /// with no renderer at all there is nothing to highlight, so the call is skipped.
    /// </summary>
    [HarmonyPatch]
    internal static class ThornHover
    {
        private static readonly AccessTools.FieldRef<ThornOnMe, MaterialPropertyBlock> Block =
            GameMember.Field<ThornOnMe, MaterialPropertyBlock>("mpb");

        private static MethodBase[] TargetMethods() =>
            [
                GameMember.Method(typeof(ThornOnMe), "HoverEnter"),
                GameMember.Method(typeof(ThornOnMe), "HoverExit"),
            ];

        private static bool Prefix(ThornOnMe __instance)
        {
            if (Block(__instance) != null)
                return true;
            if (__instance.mainRenderer == null)
                __instance.mainRenderer = __instance.GetComponentInChildren<MeshRenderer>(true);
            if (__instance.mainRenderer == null)
                __instance.mainRenderer = __instance.GetComponentInChildren<SkinnedMeshRenderer>(
                    true
                );
            if (__instance.mainRenderer == null)
                return false;
            var block = new MaterialPropertyBlock();
            __instance.mainRenderer.GetPropertyBlock(block);
            Block(__instance) = block;
            return true;
        }
    }

    /// <summary>BackpackOnBackVisuals.IsInteractible (BackpackOnBackVisuals.cs:99-104) wants the interactor's look
    /// within 110° of the pack's facing; it also gates the hold's start (IsConstantlyInteractable, :152, read at
    /// Interaction.cs:119). For a pack a hand grips, the look points from the head at the pack for that call.</summary>
    [HarmonyPatch]
    internal static class PackPick
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(BackpackOnBackVisuals), "IsInteractible", [typeof(Character)]);

        private static void Prefix(
            BackpackOnBackVisuals __instance,
            Character interactor,
            out Vector3 __state
        )
        {
            __state = Vector3.zero;
            if (
                !VRSession.Active
                || interactor != Character.localCharacter
                || !ReferenceEquals(SocialHands.Instance.RaycastTarget, __instance)
            )
                return;
            var to = __instance.transform.position - interactor.Head;
            if (to.sqrMagnitude < 1e-6f)
                return;
            __state = interactor.data.lookDirection;
            interactor.data.lookDirection = to.normalized;
        }

        private static System.Exception Finalizer(
            System.Exception __exception,
            Character interactor,
            Vector3 __state
        )
        {
            if (__state != Vector3.zero)
                interactor.data.lookDirection = __state;
            return __exception;
        }
    }

    /// <summary>CharacterCarrying.RPCA_StartCarry (CharacterCarrying.cs:56-84) drops the carrier's worn pack at the
    /// lifted friend's torso (:59-67). For the local VR player, a notice marks where it lies.</summary>
    [HarmonyPatch]
    internal static class CarryDropsPack
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterCarrying), "RPCA_StartCarry", [typeof(PhotonView)]);

        private static bool IsLocal(CharacterCarrying carrying)
        {
            var c = Character.localCharacter;
            return VRSession.Active
                && c != null
                && c.refs.carriying == carrying
                && c.player != null;
        }

        private static void Prefix(CharacterCarrying __instance, out bool __state)
        {
            bool local = IsLocal(__instance);
            if (local)
                SocialHands.CarryStarts++;
            __state = local && !Character.localCharacter.player.backpackSlot.IsEmpty();
        }

        private static void Postfix(
            CharacterCarrying __instance,
            PhotonView targetView,
            bool __state
        )
        {
            if (!__state || !IsLocal(__instance) || targetView == null)
                return;
            var friend = targetView.GetComponent<Character>();
            if (friend == null || !Character.localCharacter.player.backpackSlot.IsEmpty())
                return;
            var at =
                friend.refs.ragdoll != null
                && friend.refs.ragdoll.partDict.TryGetValue(BodypartType.Torso, out var torso)
                    ? torso.transform.position
                    : friend.Center;
            Affordance.Inform(
                SocialHands.Instance.CarryLeft,
                at,
                DenyReason.Dropped,
                DenyHint.At(at)
            );
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
