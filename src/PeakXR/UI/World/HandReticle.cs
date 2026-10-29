using PeakXR.Interact;
using PeakXR.Interact.Aim;
using PeakXR.Interact.Ping;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// Where <c>UI.HUD.Reticle</c> Hand puts the game's reticle: the point the aiming hand's ray meets the world. The flat
/// game's crosshair is hidden in VR, and testers missed what it told them (the default aim point, spike, rope, vine,
/// ladder, reach, grasp, boost, kick, climb jump, a shootable item's aim). The aiming hand is the one that uses: the
/// held item's (along its barrel or two-hand axis, <see cref="HandAim.TryHeld"/>), else the primary use hand
/// (<see cref="PhysicalInteraction.UseHandLeft"/>). It stands aside while another VR aim cue shows, so the hand
/// never carries two.
/// </summary>
internal static class HandReticle
{
    /// <summary>As far as an aimed tool's default reach (AimReticle); past it the dot rests at this distance.</summary>
    private const float Range = 40f;

    private static readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>The menu laser, an aimed tool's line, a throw arc or the ping preview is drawn now.</summary>
    public static bool OtherCue =>
        LaserPointer.Showing
        || AimReticle.Showing
        || ThrowAimCue.Dots > 0
        || PingPreview.DotShown
        || PingPreview.RayDots > 0;

    /// <summary>The aimed point this frame. False with no local character, an untracked aiming hand or another aim
    /// cue on show. Allocation-free.</summary>
    public static bool TryPoint(out Vector3 point)
    {
        point = default;
        var c = Character.localCharacter;
        if (c == null || OtherCue)
            return false;
        var held = c.data.currentItem;
        Vector3 origin;
        Quaternion rotation;
        bool aimed =
            held != null
                ? HandAim.TryHeld(out origin, out rotation)
                : HandAim.TryHand(
                    PhysicalInteraction.UseHandLeft(c, secondary: false),
                    out origin,
                    out rotation
                );
        if (!aimed)
            return false;
        var dir = rotation * Vector3.forward;
        int n = Physics.RaycastNonAlloc(
            origin,
            dir,
            hits,
            Range,
            HelperFunctions.AllPhysical,
            QueryTriggerInteraction.Ignore
        );
        float nearest = Range;
        var body = c.transform;
        var item = held != null ? held.transform : null;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            // The ray leaves from inside the player's own hand and held item.
            var t = h.collider.transform;
            if (h.distance < nearest && !t.IsChildOf(body) && (item == null || !t.IsChildOf(item)))
                nearest = h.distance;
        }
        point = origin + dir * nearest;
        return true;
    }
}
