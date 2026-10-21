using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
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

    /// <summary>
    /// Interaction.DoInteractableRaycasts (Interaction.cs:196): casts from MainCamera's transform. While a hand
    /// points, the camera transform is moved to that hand's aim pose for the duration of the call and restored
    /// in a finalizer. A hand grab replaces the result with the grabbed interactible, so the game's own DoInteraction
    /// calls Interact on it (item pickup = Item.RequestPickup RPC to the master). Other callers
    /// (Action_CloneSelectedItem.cs:28) pass an override distance and are left alone. The ray from the hand is
    /// shortened by the hand's offset from the head, so nothing it picks lies farther from the head than vanilla's
    /// <c>Interaction.distance</c> (2 m, Interaction.cs:7, read at :204).
    /// </summary>
    [HarmonyPatch]
    internal static class Raycasts
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Interaction), "DoInteractableRaycasts");

        private readonly struct State
        {
            public readonly CameraSwap Swap;
            public readonly float Distance;
            public readonly bool Left;

            public State(CameraSwap swap, float distance, bool left)
            {
                Swap = swap;
                Distance = distance;
                Left = left;
            }

            /// <summary>Finalizer body, static for the same reason as <see cref="CameraSwap.Restore"/>.</summary>
            public static Exception Restore(
                Interaction interaction,
                Exception exception,
                State state
            )
            {
                if (state.Distance > 0f)
                    interaction.distance = state.Distance;
                return CameraSwap.Restore(exception, state.Swap);
            }

            /// <summary>Interact pressed with nothing picked: if the full-length hand ray would have found an
            /// interactible beyond the shortened one, say "Too far" instead of failing silently.</summary>
            public static void ReportTooFar(Interaction interaction, Character c, State state)
            {
                var mc = MainCamera.instance;
                if (state.Distance <= 0f || !state.Swap.Active || mc == null)
                    return;
                var t = mc.transform; // still at the hand: the finalizer restores it
                if (
                    !Physics.Raycast(
                        t.position,
                        t.forward,
                        out var hit,
                        state.Distance,
                        HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                        QueryTriggerInteraction.Collide
                    )
                    || hit.distance <= interaction.distance
                )
                    return;
                var target = hit.collider.GetComponentInParent<IInteractible>();
                if (target != null && target.IsInteractible(c))
                    ParityFeedback.Denied(state.Left, hit.point, ParityFeedback.TooFar);
            }
        }

        private static void Prefix(
            Interaction __instance,
            float overrideDistance,
            out State __state
        )
        {
            __state = default;
            int hand = PhysicalInteraction.AimHand;
            var rig = VRRig.Instance;
            var mc = MainCamera.instance;
            if (
                overrideDistance != -1f
                || hand < 0
                || !VRSession.Active
                || rig == null
                || mc == null
            )
                return;
            var aim = rig.Aim(hand == 0);
            if (aim == null)
                return;
            float reach = __instance.distance;
            float offset = Vector3.Distance(mc.transform.position, aim.position);
            __instance.distance = Mathf.Max(0f, reach - offset);
            __state = new State(CameraSwap.Begin(aim.position, aim.rotation), reach, hand == 0);
        }

        private static void Postfix(
            Interaction __instance,
            ref IInteractible interactableResult,
            float overrideDistance,
            State __state
        )
        {
            var c = Character.localCharacter;
            if (overrideDistance != -1f || c == null || !VRSession.Active)
                return;
            var target = PhysicalInteraction.GrabTarget;
            if (
                target is UnityEngine.Object unityObject
                && unityObject != null
                && target.IsInteractible(c)
            )
            {
                interactableResult = target;
                return;
            }
            if (interactableResult == null && c.input.interactWasPressed)
                State.ReportTooFar(__instance, c, __state);
            // One held item at a time: the free hand's grip does not pick up a second item.
            if (
                PhysicalInteraction.GripInteractActive
                && c.data.currentItem != null
                && interactableResult is Item other
                && other != c.data.currentItem
            )
                interactableResult = null;
        }

        private static Exception Finalizer(
            Interaction __instance,
            Exception __exception,
            State __state
        ) => State.Restore(__instance, __exception, __state);
    }

    /// <summary>CharacterItems.DoDropping (CharacterItems.cs:252, private): on a hand throw, give the held item the
    /// hand's velocity just before vanilla reads it into the DropItemRpc velocity argument (CharacterItems.cs:274),
    /// and send the charge a vanilla throw would need for that speed (<see cref="HandThrow"/>), never below a sticky
    /// item's <c>throwChargeRequirement</c> (:277-289, which otherwise aborts the drop). The master adds that charge's
    /// look-direction force (:391-395, :426), so the item's velocity is reduced by it first and leaves along the
    /// hand. All clients see the throw through that RPC. A gentle release of a sticky item stays refused, as in
    /// vanilla, with a ui.affordance cue.</summary>
    [HarmonyPatch]
    internal static class Dropping
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "DoDropping");

        private static void Prefix(CharacterItems __instance)
        {
            var c = Character.localCharacter;
            if (c == null || c.refs.items != __instance || !c.input.dropWasReleased)
                return;
            var sticky = c.data.currentStickyItem;
            if (!PhysicalInteraction.ThrowPending)
            {
                if (
                    sticky != null
                    && VRSession.Active
                    && __instance.throwChargeLevel < sticky.throwChargeRequirement
                )
                    Affordance.DenyAtHand(LocalHold.ItemInLeftHand, DenyReason.StuckToHand);
                return;
            }
            PhysicalInteraction.ConsumeThrow();
            var item = c.data.currentItem;
            if (item == null || item.rig == null)
                return;
            // No faster than a full-charge vanilla throw (CharacterItems.cs:394, :428): body speed plus half the
            // max throw force times the item's multiplier.
            var body = c.refs.hip != null ? c.refs.hip.Rig.linearVelocity : Vector3.zero;
            float mult = item.throwForceMultiplier;
            var velocity = Vector3.ClampMagnitude(
                PhysicalInteraction.ThrowVelocity,
                body.magnitude + 0.5f * __instance.maxThrowForce * mult
            );
            float charge = HandThrow.Charge(
                (velocity - body).magnitude,
                __instance.minThrowForce,
                __instance.maxThrowForce,
                mult,
                sticky != null ? sticky.throwChargeRequirement : 0f
            );
            // The master's look direction: HelperFunctions.LookToDirection(lookValues, forward) (HelperFunctions.cs:183-195).
            var look = c.data.lookValues;
            var lookDir = Quaternion.Euler(-look.y, look.x, 0f) * Vector3.forward;
            item.rig.linearVelocity =
                velocity
                - lookDir
                    * HandThrow.LookSpeed(
                        charge,
                        __instance.minThrowForce,
                        __instance.maxThrowForce,
                        mult
                    );
            __instance.throwChargeLevel = charge;
        }
    }
}
