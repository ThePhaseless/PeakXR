using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>Harmony patches of <see cref="PingFeature"/>. Input and aim act on the local VR player only; the
/// marker size applies to every marker the local VR viewer sees.</summary>
internal static class PingPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal; ping read at :258): injects the
    /// finger-gun or button ping into <c>pingWasPressed</c>, which PointPinger reads (PointPinger.cs:32). Runs
    /// after interact.physical so it can hold off the use press of the trigger that fired the finger gun.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.VeryLow)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            try
            {
                if (!VRSession.Active)
                    return;
                var c = Character.localCharacter;
                if (c != null && c.input == __instance)
                    PingInput.Tick(c, __instance, playerMovementActive);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<PingFeature>(e);
            }
        }
    }

    /// <summary>PointPinger.TryGetPingHit(out RaycastHit, Vector3) (PointPinger.cs:56, private static): vanilla
    /// casts <c>Camera.main.ScreenPointToRay</c> through the mouse or screen centre (PointPinger.cs:58), which has
    /// no meaning for a stereo headset camera. In VR the ray starts at the pinging hand's aim pose (the head when
    /// that hand is untracked) with the same TerrainMap mask, so DoPing sends ReceivePoint_Rpc with the hand-aimed
    /// point (PointPinger.cs:71). Only the local player's DoPing calls this. A miss (the sky) is dropped silently by
    /// vanilla (PointPinger.cs:68); in VR it is refused as NoWall at the ray end, with the preview's red dot left there
    /// as the refused marker. A hit gets a contact tick on the pinging hand.</summary>
    [HarmonyPatch]
    internal static class PingHit
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(PointPinger),
                "TryGetPingHit",
                new[] { typeof(RaycastHit).MakeByRefType(), typeof(Vector3) }
            );

        private static bool Prefix(ref bool __result, out RaycastHit hit)
        {
            hit = default;
            if (!VRSession.Active || !TryAim(out var origin, out var direction))
                return true;
            __result = Physics.Raycast(
                origin,
                direction,
                out hit,
                float.PositiveInfinity,
                HelperFunctions.LayerType.TerrainMap.ToLayerMask()
            );
            bool left = PingInput.PingLeft;
            if (__result)
                Affordance.Contact(left);
            else
            {
                var end = origin + direction * PingPreview.SkyMeters;
                var marker = PingPreview.Refused(end);
                Affordance.Deny(left, end, DenyReason.NoWall, default, marker);
            }
            return false;
        }

        private static bool TryAim(out Vector3 origin, out Vector3 direction)
        {
            if (HandAim.TryHand(PingInput.PingLeft, out origin, out var rotation))
            {
                direction = rotation * Vector3.forward;
                return true;
            }
            var rig = VRRig.Instance;
            if (rig != null && rig.Head != null)
            {
                origin = rig.Head.position;
                direction = rig.Head.forward;
                return true;
            }
            direction = default;
            return false;
        }
    }

    /// <summary>PointPing.Go (PointPing.cs:56, private; called from Start and Update): vanilla scales the marker
    /// by the camera frustum height at the marker's distance (PointPing.cs:58-61), so its size follows the camera
    /// FOV, which in XR is the wide eye FOV. In VR the scale is recomputed from a fixed apparent angle at the
    /// distance from the head, clamped to the prefab's own <c>minMaxScale</c> range. Orientation and the pinging
    /// character's point animation target stay vanilla.</summary>
    [HarmonyPatch]
    internal static class MarkerScale
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(PointPing), "Go");

        private static void Postfix(PointPing __instance)
        {
            if (!VRSession.Active)
                return;
            var rig = VRRig.Instance;
            if (rig == null || rig.Head == null)
                return;
            var t = __instance.transform;
            float distance = Vector3.Distance(rig.Head.position, t.position);
            float frustumShare = __instance.sizeOfFrustum;
            var minMax = __instance.minMaxScale;
            float scale = AimMath.PingMarkerScale(
                distance,
                PingConfig.Instance.MarkerAngle,
                minMax.x * frustumShare,
                minMax.y * frustumShare
            );
            t.localScale = new Vector3(scale, scale, scale);
        }
    }
}
