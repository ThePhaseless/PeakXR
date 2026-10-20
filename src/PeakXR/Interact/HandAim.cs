using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// World-space aim rays from the tracked hands, for game code that aims from the camera (tools, launchers,
/// placement previews, ping). The held item aims from the hand that holds it; when the free hand also grips a
/// two-hand item, it aims along the line between the hands, which is steadier and matches how the item is held.
/// </summary>
internal static class HandAim
{
    /// <summary>Hands closer than this (metres) fall back to the holding hand's own aim.</summary>
    public static float TwoHandMinSeparation = 0.15f;

    /// <summary>The aim pose of one tracked hand. False when VR is off or the hand is not tracked.</summary>
    public static bool TryHand(bool left, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = Quaternion.identity;
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || !VRInput.Hand(left).Tracked)
            return false;
        var aim = rig.Aim(left);
        if (aim == null)
            return false;
        origin = aim.position;
        rotation = aim.rotation;
        return true;
    }

    /// <summary>Aim of the local player's held item: from the holding hand, along the two-hand axis while the
    /// free hand supports it (<see cref="LocalHold.TwoHand"/>, which feeds <c>AvatarPose.TwoHandHold</c>).</summary>
    public static bool TryHeld(out Vector3 origin, out Quaternion rotation)
    {
        bool left = LocalHold.ItemInLeftHand;
        if (!TryHand(left, out origin, out rotation))
            return false;
        if (!LocalHold.TwoHand || !VRInput.Hand(!left).Tracked)
            return true;
        var rig = VRRig.Instance;
        var hold = rig.Hand(left);
        var support = rig.Hand(!left);
        if (hold == null || support == null)
            return true;
        var dir = AimMath
            .TwoHandDirection(
                hold.position.N(),
                support.position.N(),
                (rotation * Vector3.forward).N(),
                TwoHandMinSeparation
            )
            .U();
        rotation = Quaternion.LookRotation(dir, rotation * Vector3.up);
        return true;
    }

    /// <summary>True when <paramref name="item"/> is the item the local player currently holds.</summary>
    public static bool IsLocalHeld(Item item)
    {
        var c = Character.localCharacter;
        return item != null && c != null && c.data.currentItem == item;
    }

    /// <summary>Moves the game camera to the held-item aim for one game call when <paramref name="item"/> is the
    /// local player's held item and VR is active; otherwise an inactive swap. Pair with
    /// <see cref="CameraSwap.End"/> in a finalizer.</summary>
    public static CameraSwap BeginHeld(Item item) =>
        IsLocalHeld(item) && TryHeld(out var origin, out var rotation)
            ? CameraSwap.Begin(origin, rotation)
            : default;

    /// <summary>Moves the game camera to one hand's aim for one game call (inactive when that hand is untracked).</summary>
    public static CameraSwap BeginHand(bool left) =>
        TryHand(left, out var origin, out var rotation)
            ? CameraSwap.Begin(origin, rotation)
            : default;
}
