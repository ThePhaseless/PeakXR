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
            try
            {
                var c = Character.localCharacter;
                if (c != null && c.input == __instance)
                    PhysicalInteraction.Tick(c, __instance, playerMovementActive);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<PhysicalInteractionFeature>(e);
            }
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

            private static readonly HoldGrace PointedHold = new();

            /// <summary>
            /// A hand-aimed hold keeps its target through a brief ray dropout (<see cref="HoldGrace"/>): the game drops a
            /// held interact the frame its target leaves the ray and never restarts it while interact stays pressed
            /// (Interaction.cs:128-132), and a jittering hand grazes off edges. For the grace this frame's pick stays the
            /// held target, the same hold on the same object, so nothing changes on the wire; past it the game drops the
            /// hold as before and the aiming hand feels the cancel.
            /// </summary>
            public static void KeepPointedHold(
                Interaction interaction,
                Character c,
                ref IInteractible picked,
                State state
            )
            {
                var held = interaction.currentHeldInteractible;
                bool holding =
                    state.Swap.Active
                    && c.input.interactIsPressed
                    && held is UnityEngine.Object live
                    && live != null;
                switch (PointedHold.Update(holding, holding && picked == held, Time.time))
                {
                    case HoldGraceStep.Keep:
                        picked = held;
                        break;
                    case HoldGraceStep.Drop:
                        Affordance.Cancel(state.Left);
                        break;
                }
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
            State.KeepPointedHold(__instance, c, ref interactableResult, __state);
            // A fallback grip's press that DoInteraction turns into Item.Interact this frame (Interaction.cs:108-125:
            // interact held and readyToInteract, not only its press edge, so a grip already holding interact counts):
            // the pickup belongs to that grip's hand.
            if (
                c.input.interactIsPressed
                && __instance.readyToInteract
                && PhysicalInteraction.GripInteractActive
                && interactableResult is Item picked
            )
                PhysicalInteraction.InteractPicking(picked);
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
            // A throw here would skip the drop itself.
            try
            {
                Apply(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<PhysicalInteractionFeature>(e);
            }
        }

        private static void Apply(CharacterItems __instance)
        {
            var c = Character.localCharacter;
            if (c == null || c.refs.items != __instance || !c.input.dropWasReleased)
                return;
            var sticky = c.data.currentStickyItem;
            // currentStickyItem follows the currentItem setter (CharacterData.cs:418-437); read the held item itself
            // too, so no path that equips a sticky item can miss the cue.
            var held = c.data.currentItem;
            if (sticky == null && held != null)
                held.TryGetComponent(out sticky);
            // One line per drop release (run 34's vr.63 had no sticky line: did the release reach DoDropping at all?).
            if (VRSession.Active)
                Plugin.Log.LogInfo(
                    $"[interact] drop release: {(held != null ? held.name : "nothing")}, sticky {sticky != null} (currentStickyItem {c.data.currentStickyItem != null}), charge {__instance.throwChargeLevel:0.00} of {(sticky != null ? sticky.throwChargeRequirement : 0f):0.00}, hand throw {PhysicalInteraction.ThrowPending}"
                );
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
            // The swing's speed mapped onto the vanilla throw range (HandThrow.LaunchSpeed): a hard swing throws as
            // hard as a full charge, so breakables (Breakable.cs:61-67, minBreakVelocity) and bonks fire, and never
            // harder (CharacterItems.cs:394, :428: half the max throw force times the item's multiplier), along the hand.
            var body = c.refs.hip != null ? c.refs.hip.Rig.linearVelocity : Vector3.zero;
            float mult = item.throwForceMultiplier;
            var cfg = InteractConfig.Instance;
            var hand = PhysicalInteraction.ThrowHandVelocity;
            float speed = HandThrow.LaunchSpeed(
                hand.magnitude,
                cfg.ThrowSpeed,
                cfg.ThrowFullSpeed,
                0.5f * __instance.maxThrowForce * mult
            );
            var velocity =
                body + (hand.sqrMagnitude > 1e-6f ? hand.normalized * speed : Vector3.zero);
            float charge = HandThrow.Charge(
                speed,
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

    /// <summary>
    /// CharacterItems.OnPickupAccepted (CharacterItems.cs:440-465, the master's answer to a pickup): vanilla equips
    /// the new slot (:457), which would swap a VR player's held item out of the hand holding it when the other, free
    /// hand only pocketed something. While such a pocketing pickup is pending
    /// (<see cref="PhysicalInteraction.TakePocketing"/>) that equip is skipped: the item stays in the pocket slot
    /// <c>Player.AddItem</c> gave it (Item.cs:617) and the held item stays in hand. Never for the temporary slot
    /// (250, Player.cs:137-141): vanilla fills it only with the item it equips, so that equip runs. Into empty hands
    /// that is a pickup that worked (the success tick); over a held item it swaps what is in hand, so the hand that
    /// asked gets a notice that says so, not the tick. A climber's temporary-slot item is dropped behind the hip
    /// instead (CharacterItems.cs:445-452): that hand gets the pockets-full cue (the grip refuses both up front; this
    /// covers pockets filled while the answer was on its way). Core <see cref="GripIntent.PickupAnswer"/> picks the
    /// cue; the success tick is <see cref="PhysicalInteraction.PickupAccepted"/>. Local only: the equip is the
    /// owner's own call.
    /// </summary>
    [HarmonyPatch]
    internal static class KeepHeldOnPocket
    {
        /// <summary>The pocketing pickup's answer is running: <see cref="SkipEquip"/> drops its equip.</summary>
        internal static bool Active;

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "OnPickupAccepted");

        private static void Prefix(CharacterItems __instance, byte slotID)
        {
            var c = Character.localCharacter;
            if (c == null || __instance != c.refs.items)
                return;
            try
            {
                bool pocketing = PhysicalInteraction.TakePocketing();
                Active = pocketing && slotID != GripIntent.TempSlot;
                var cue = GripIntent.PickupAnswer(
                    slotID,
                    c.data.isClimbingAnything,
                    c.data.currentItem != null
                );
                bool? asked = PhysicalInteraction.PickupAccepted(cue: cue == PickupCue.Success);
                bool left = asked ?? !LocalHold.ItemInLeftHand;
                if (cue == PickupCue.Dropped)
                    Affordance.DenyAtHand(left, DenyReason.PocketsFull);
                else if (cue == PickupCue.Swapped)
                    PhysicalInteraction.PickupSwapped(left);
            }
            catch (Exception e)
            {
                // A throw here would skip the pickup's answer and lose the item: let vanilla equip it.
                Active = false;
                PatchGuard.Fail<PhysicalInteractionFeature>(e);
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            Active = false;
            return __exception;
        }
    }

    /// <summary>Item.DenyPickupRPC (Item.cs:598-602): the master refused a pickup and the item is back in the world.
    /// The hand that asked gets the refusal cue (<see cref="PhysicalInteraction.PickupDenied"/>).</summary>
    [HarmonyPatch]
    internal static class PickupDenied
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Item), "DenyPickupRPC");

        // An RPC: a throw here would reach Photon's dispatch.
        private static void Postfix(Item __instance)
        {
            try
            {
                PhysicalInteraction.PickupDenied(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<PhysicalInteractionFeature>(e);
            }
        }
    }

    /// <summary>CharacterItems.EquipSlot (CharacterItems.cs:482): skipped only inside a pocketing pickup's answer
    /// (<see cref="KeepHeldOnPocket"/>).</summary>
    [HarmonyPatch]
    internal static class SkipEquip
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "EquipSlot");

        private static bool Prefix() => !KeepHeldOnPocket.Active;
    }
}
