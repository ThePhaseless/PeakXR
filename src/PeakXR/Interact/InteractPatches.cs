using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>Harmony patches of <see cref="PhysicalInteractionFeature"/>. All act on the local VR player only.</summary>
internal static class InteractPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the game's once-per-frame input
    /// sampling for the local character. Runs late so it sees other features' injected values.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            var c = Character.localCharacter;
            if (c != null && c.input == __instance)
                PhysicalInteraction.Tick(c, __instance, playerMovementActive);
        }
    }

    internal readonly struct CameraState
    {
        public readonly bool Moved;
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;

        public CameraState(Vector3 localPosition, Quaternion localRotation)
        {
            Moved = true;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }
    }

    /// <summary>
    /// Interaction.DoInteractableRaycasts (Interaction.cs:196): casts from MainCamera's transform. While a hand
    /// points, the camera transform is moved to that hand's aim pose for the duration of the call and restored
    /// after. A hand grab replaces the result with the grabbed interactible, so the game's own DoInteraction
    /// calls Interact on it (item pickup = Item.RequestPickup RPC to the master).
    /// </summary>
    [HarmonyPatch]
    internal static class Raycasts
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteractableRaycasts");

        private static void Prefix(out CameraState __state)
        {
            __state = default;
            int hand = PhysicalInteraction.AimHand;
            var rig = VRRig.Instance;
            var cam = MainCamera.instance;
            if (hand < 0 || !VRSession.Active || rig == null || cam == null)
                return;
            var aim = rig.Aim(hand == 0);
            if (aim == null)
                return;
            var t = cam.transform;
            __state = new CameraState(t.localPosition, t.localRotation);
            t.SetPositionAndRotation(aim.position, aim.rotation);
        }

        private static void Postfix(ref IInteractible interactableResult, ref CameraState __state)
        {
            if (__state.Moved && MainCamera.instance != null)
                MainCamera.instance.transform.SetLocalPositionAndRotation(
                    __state.LocalPosition,
                    __state.LocalRotation
                );

            var c = Character.localCharacter;
            if (c == null || !VRSession.Active)
                return;
            var target = PhysicalInteraction.GrabTarget;
            if (target is Object unityObject && unityObject != null && target.IsInteractible(c))
            {
                interactableResult = target;
                return;
            }
            // One held item at a time: the free hand's grip does not pick up a second item.
            if (
                PhysicalInteraction.GripInteractActive
                && c.data.currentItem != null
                && interactableResult is Item other
                && other != c.data.currentItem
            )
                interactableResult = null;
        }
    }

    /// <summary>CharacterItems.DoDropping (CharacterItems.cs:252, private): on a hand throw, give the held item the
    /// hand's velocity just before vanilla reads it into the DropItemRpc velocity argument (CharacterItems.cs:274),
    /// with zero charge so the master adds no look-direction force. All clients see the throw through that RPC.</summary>
    [HarmonyPatch]
    internal static class Dropping
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "DoDropping");

        private static void Prefix(CharacterItems __instance)
        {
            if (!PhysicalInteraction.ThrowPending)
                return;
            var c = Character.localCharacter;
            if (c == null || c.refs.items != __instance || !c.input.dropWasReleased)
                return;
            PhysicalInteraction.ConsumeThrow();
            var item = c.data.currentItem;
            if (item == null || item.rig == null)
                return;
            item.rig.linearVelocity = PhysicalInteraction.ThrowVelocity;
            __instance.throwChargeLevel = 0f;
        }
    }
}
