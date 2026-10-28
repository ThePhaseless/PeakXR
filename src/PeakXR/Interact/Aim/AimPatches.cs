using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Aim;

/// <summary>
/// Harmony patches of <see cref="AimFeature"/>. Aimed tools read <c>MainCamera.instance.transform</c> (or
/// <c>Camera.main</c>) as their aim. Each patch moves the camera to the held-item hand aim for exactly the game call
/// that reads it (<see cref="CameraSwap"/>, restored in a finalizer), only for the local player's held tool while VR
/// and the setting are on. Everything downstream (hits, previews, RPC arguments) is vanilla.
/// </summary>
internal static class AimPatches
{
    /// <summary><c>RopeShooter.Ammo</c> getter (RopeShooter.cs:31, private), resolved in Install.</summary>
    internal static Func<RopeShooter, int> RopeShooterAmmo;

    private static Item spikeCacheItem;
    private static bool spikeCacheIsSpike;

    private static bool On => VRSession.Active && AimConfig.Instance.AimFromHand;

    private static CameraSwap Held(Item item) => On ? HandAim.BeginHeld(item) : default;

    /// <summary>Recoil on the holding hand, and softer on the supporting hand of a two-hand hold.</summary>
    private static void Shot(Item item, float amplitude, float seconds)
    {
        if (!VRSession.Active || !AimConfig.Instance.ShotHaptics || !HandAim.IsLocalHeld(item))
            return;
        bool left = LocalHold.ItemInLeftHand;
        Haptics.Pulse(left, amplitude, seconds);
        if (LocalHold.TwoHand)
            Haptics.Pulse(!left, amplitude * 0.6f, seconds);
    }

    /// <summary>RopeTier.Update (RopeTier.cs:57): ghost anchor line from the camera at RopeTier.cs:97-106,
    /// so the anchor preview follows the hand.</summary>
    [HarmonyPatch]
    internal static class RopeTierUpdate
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(RopeTier), "Update");

        private static void Prefix(RopeTier __instance, out CameraSwap __state) =>
            __state = Held(__instance.item);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>RopeShooter.WillAttach (RopeShooter.cs:122-138): camera ray at :133. Also drives the usability
    /// check in RopeShooter.Update (:67-70).</summary>
    [HarmonyPatch]
    internal static class RopeShooterWillAttach
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(RopeShooter),
                "WillAttach",
                new[] { typeof(RaycastHit).MakeByRefType() }
            );

        private static void Prefix(RopeShooter __instance, out CameraSwap __state) =>
            __state = Held(__instance.item);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>RopeShooter.OnPrimaryFinishedCast (RopeShooter.cs:72, private): fire ray along the camera at
    /// :87-88 and the GetShot direction -Camera.main.forward at :113. A shot spends ammo (:112): pulse then.</summary>
    [HarmonyPatch]
    internal static class RopeShooterFire
    {
        // Ammo before the call; the game calls this on the main thread only, never re-entrantly.
        private static int ammoBefore = -1;

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(RopeShooter), "OnPrimaryFinishedCast");

        private static void Prefix(RopeShooter __instance, out CameraSwap __state)
        {
            ammoBefore = HandAim.IsLocalHeld(__instance.item) ? RopeShooterAmmo(__instance) : -1;
            __state = Held(__instance.item);
        }

        private static void Postfix(RopeShooter __instance)
        {
            if (ammoBefore > 0 && RopeShooterAmmo(__instance) < ammoBefore)
                Shot(__instance.item, 0.8f, 0.15f);
        }

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>VineShooter.WillAttach (VineShooter.cs:71-92): camera ray at :78, pitch gates from the camera
    /// position at :82,87. Also drives VineShooter.Update (:31-34).</summary>
    [HarmonyPatch]
    internal static class VineShooterWillAttach
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(VineShooter),
                "WillAttach",
                new[] { typeof(RaycastHit).MakeByRefType() }
            );

        private static void Prefix(VineShooter __instance, out CameraSwap __state) =>
            __state = Held(__instance.item);

        // Inside the fire below, a passing aim check means the game goes on to its path tries.
        private static void Postfix(bool __result)
        {
            if (__result)
                VineWatch.Aimed();
        }

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>The local player's vine or chain shot, from cast end to its last path try (<see cref="CastWatch"/>).</summary>
    internal static readonly CastWatch VineWatch = new();

    /// <summary>Self-test only: every path try of the next local shot fails, as when no hang finds open air.</summary>
    internal static bool SelfTestNoPath;

    /// <summary>VineShooter.OnPrimaryFinishedCast (VineShooter.cs:36, private): vine start 1 m ahead of the
    /// camera at :52-57. When all ten path tries fail (:49-69) the cast just ends; the local holder gets NoPath at
    /// the aimed spot with the shooter red.</summary>
    [HarmonyPatch]
    internal static class VineShooterFire
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(VineShooter), "OnPrimaryFinishedCast");

        private static void Prefix(VineShooter __instance, out CameraSwap __state)
        {
            if (HandAim.IsLocalHeld(__instance.item))
                VineWatch.Begin();
            __state = Held(__instance.item);
        }

        private static void Postfix(VineShooter __instance)
        {
            if (!VineWatch.Open)
                return;
            var reason = VineWatch.End(DenyReason.NoPath);
            SelfTestNoPath = false;
            if (reason == DenyReason.None)
                return;
            var item = __instance.item;
            var spot = AimSpot(item);
            Affordance.Deny(LocalHold.ItemInLeftHand, spot, reason, default, item);
        }

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);

        /// <summary>Where the hand aim meets terrain within the shooter's range, else the item.</summary>
        private static Vector3 AimSpot(Item item)
        {
            if (
                HandAim.TryHeld(out var origin, out var rotation)
                && Physics.Raycast(
                    origin,
                    rotation * Vector3.forward,
                    out var hit,
                    item.GetComponent<VineShooter>().maxLength,
                    HelperFunctions.LayerType.TerrainMap.ToLayerMask()
                )
            )
                return hit.point;
            return item.Center();
        }
    }

    /// <summary>JungleVine.CheckVinePath (JungleVine.cs:234, static): one path try of a vine shot.</summary>
    [HarmonyPatch]
    internal static class VinePathTry
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(JungleVine),
                "CheckVinePath",
                new[]
                {
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(float),
                    typeof(Vector3).MakeByRefType(),
                    typeof(float),
                }
            );

        private static void Postfix(ref bool __result)
        {
            if (!VineWatch.Open)
                return;
            if (SelfTestNoPath)
                __result = false;
            if (__result)
                VineWatch.Built();
        }
    }

    /// <summary>RescueHook.GetMiddleScreenRay (RescueHook.cs:261-269): ScreenPointToRay(screen centre) is the only
    /// camera read of the hook, used by GetHit (:271) from both Update (:108) and Fire (:231). Screen centre has
    /// no meaning for a stereo camera, so the result is replaced with the held-item hand ray.</summary>
    [HarmonyPatch]
    internal static class RescueHookRay
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(RescueHook), "GetMiddleScreenRay");

        private static bool Prefix(RescueHook __instance, ref Ray __result)
        {
            if (
                !On
                || !HandAim.IsLocalHeld(__instance.item)
                || !HandAim.TryHeld(out var origin, out var rotation)
            )
                return true;
            __result = new Ray(origin, rotation * Vector3.forward);
            return false;
        }
    }

    /// <summary>RescueHook.Fire (RescueHook.cs:231, private): every call shoots the claw.</summary>
    [HarmonyPatch]
    internal static class RescueHookFire
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(RescueHook), "Fire");

        private static void Postfix(RescueHook __instance) => Shot(__instance.item, 0.6f, 0.12f);
    }

    /// <summary>Constructable.Update (Constructable.cs:44, protected virtual): range check from the camera at :52,
    /// and TryUpdatePreview (:77, ray at :79) with CreateOrMovePreview (:100, yaw from camera forward at
    /// :113,118), which are only called from here. CheckpointConstructable (CheckpointConstructable.cs:3) only
    /// overrides FinishConstruction; ScoutEffigy.Update (ScoutEffigy.cs:8) calls base.Update. Every range check
    /// (:52, :79, :139) reads <c>maxConstructDistance</c> (:13) from the camera, now at the hand: it is shortened by
    /// the hand's offset from the head for the call, so nothing is placed farther from the head than vanilla allows
    /// (as InteractPatches.Raycasts does for interaction).</summary>
    [HarmonyPatch]
    internal static class ConstructableUpdate
    {
        internal readonly struct State
        {
            public readonly CameraSwap Swap;
            public readonly float Distance;

            public State(CameraSwap swap, float distance)
            {
                Swap = swap;
                Distance = distance;
            }

            /// <summary>Finalizer body, static for the same reason as <see cref="CameraSwap.Restore"/>.</summary>
            public static Exception Restore(Constructable target, Exception exception, State state)
            {
                if (state.Distance >= 0f)
                    target.maxConstructDistance = state.Distance;
                return CameraSwap.Restore(exception, state.Swap);
            }
        }

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Constructable), "Update");

        private static void Prefix(Constructable __instance, out State __state)
        {
            var mc = MainCamera.instance;
            var head = mc != null ? mc.transform.position : Vector3.zero;
            var swap = Held(__instance.item);
            float reach = __instance.maxConstructDistance;
            if (swap.Active)
                __instance.maxConstructDistance = Mathf.Max(
                    0f,
                    reach - Vector3.Distance(head, mc.transform.position)
                );
            __state = new State(swap, swap.Active ? reach : -1f);
        }

        private static Exception Finalizer(
            Constructable __instance,
            Exception __exception,
            State __state
        ) => State.Restore(__instance, __exception, __state);
    }

    /// <summary>Action_RaycastDart.FireDart (Action_RaycastDart.cs:37, private): ray and sphere cast from the
    /// muzzle along the camera forward at :43,47,49. Every call fires.</summary>
    [HarmonyPatch]
    internal static class DartFire
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Action_RaycastDart), "FireDart");

        private static void Prefix(Item ___item, out CameraSwap __state) => __state = Held(___item);

        private static void Postfix(Item ___item) => Shot(___item, 0.4f, 0.06f);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>Peak.Action_RaycastSpawnSomething.FixedUpdate (Peak/Action_RaycastSpawnSomething.cs:26, private):
    /// antizooka aim ray along the camera forward at :28.</summary>
    [HarmonyPatch]
    internal static class SpawnSomethingAim
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(global::Peak.Action_RaycastSpawnSomething), "FixedUpdate");

        private static void Prefix(Item ___item, out CameraSwap __state) => __state = Held(___item);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>Peak.Action_RaycastSpawnSomething.RunAction (Peak/Action_RaycastSpawnSomething.cs:35): spawn point
    /// backed off along the camera forward at :37. Every call fires.</summary>
    [HarmonyPatch]
    internal static class SpawnSomethingFire
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(global::Peak.Action_RaycastSpawnSomething), "RunAction");

        private static void Prefix(Item ___item, out CameraSwap __state) => __state = Held(___item);

        private static void Postfix(Item ___item) => Shot(___item, 0.9f, 0.2f);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>Peak.Action_CloneSelectedItem.RunAction (Peak/Action_CloneSelectedItem.cs:22): target pick through
    /// Interaction.DoInteractableRaycasts (:28, camera ray at Interaction.cs:196), beam end along the camera at
    /// :42 and :48-49. The interact.physical pointing-hand swap nests inside this one.</summary>
    [HarmonyPatch]
    internal static class CloneFire
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(global::Peak.Action_CloneSelectedItem), "RunAction");

        private static void Prefix(Item ___item, out CameraSwap __state) => __state = Held(___item);

        private static void Postfix(Item ___item) => Shot(___item, 0.5f, 0.1f);

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);
    }

    /// <summary>
    /// CharacterItems.UpdateClimbingSpikeUse (CharacterItems.cs:981, private): the piton ray
    /// RaycastClimbingSpikeStart (:1072-1076, camera at :1075) and the preview range check
    /// WithinClimbingSpikePreviewRange (:971-979, camera at :976) are only called from here. A held spike aims
    /// from the holding hand; a pocketed spike (secondary use while climbing, :1019) aims from the hand whose
    /// trigger is secondary use in interact.physical: the free hand, or the left hand when empty-handed.
    /// </summary>
    [HarmonyPatch]
    internal static class ClimbingSpike
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "UpdateClimbingSpikeUse");

        private static void Prefix(Character ___character, out CameraSwap __state)
        {
            __state = default;
            if (
                !On
                || ___character == null
                || !___character.IsLocal
                || ___character.data.climbingSpikeCount <= 0
            )
                return;
            var held = ___character.data.currentItem;
            if (held != null && IsSpike(held))
                __state = HandAim.BeginHeld(held);
            else
                __state = HandAim.BeginHand(
                    PhysicalInteraction.UseHandLeft(___character, secondary: true)
                );
        }

        private static Exception Finalizer(Exception __exception, CameraSwap __state) =>
            CameraSwap.Restore(__exception, __state);

        private static bool IsSpike(Item item)
        {
            if (!ReferenceEquals(item, spikeCacheItem))
            {
                spikeCacheItem = item;
                spikeCacheIsSpike = item.TryGetComponent<ClimbingSpikeComponent>(out _);
            }
            return spikeCacheIsSpike;
        }
    }
}
