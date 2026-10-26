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

    /// <summary>Aim of the local player's held item. A launcher fires along its own barrel as the player sees it
    /// (<see cref="TryBarrel"/>). Anything else aims from the holding hand, along the two-hand axis while the
    /// free hand supports it (<see cref="LocalHold.TwoHand"/>, which feeds <c>AvatarPose.TwoHandHold</c>).</summary>
    public static bool TryHeld(out Vector3 origin, out Quaternion rotation)
    {
        bool left = LocalHold.ItemInLeftHand;
        if (!TryHand(left, out origin, out rotation))
            return false;
        if (TryBarrel(ref origin, ref rotation))
            return true;
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

    private static Item barrelItem;
    private static bool barrelIsLauncher;
    private static Transform barrelMuzzle;

    /// <summary>
    /// The held launcher's barrel line: the item's +Z, through its muzzle. Every launcher prefab has its muzzle on
    /// its +Z axis (RopeShooter SpawnPoint z 1.05, ChainShooter barrel z 0.54, HealingDart DartSpawnPos z 0.81,
    /// AntiZooka rayOrigin z 0.46, RescueHook firePoint z 0.43). The controller's aim pose and the model's barrel
    /// differ by the item's hold angle, so a shot along the controller left the visible barrel at an angle
    /// (tester: the rope cannon "doesn't go where I aim"). The ray starts on that line level with the item pivot,
    /// near the hand, so a muzzle poking into a wall still finds it.
    /// </summary>
    private static bool TryBarrel(ref Vector3 origin, ref Quaternion rotation)
    {
        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        if (item == null)
            return false;
        if (!ReferenceEquals(item, barrelItem))
        {
            barrelItem = item;
            barrelMuzzle = Muzzle(item, out barrelIsLauncher);
        }
        if (!barrelIsLauncher)
            return false;
        var t = item.transform;
        var forward = t.forward;
        var pivot = t.position;
        var muzzle = barrelMuzzle != null ? barrelMuzzle.position : pivot;
        origin = muzzle - forward * Vector3.Dot(muzzle - pivot, forward);
        rotation = t.rotation;
        return true;
    }

    /// <summary>The launcher's muzzle (RopeShooter.cs:15, Action_RaycastDart.cs:14,
    /// Peak/Action_RaycastSpawnSomething.cs:8, RescueHook.cs:79); VineShooter has none and uses its pivot.</summary>
    private static Transform Muzzle(Item item, out bool launcher)
    {
        launcher = true;
        if (item.TryGetComponent<RopeShooter>(out var rope))
            return rope.spawnPoint;
        if (item.TryGetComponent<Action_RaycastDart>(out var dart))
            return dart.spawnTransform;
        if (item.TryGetComponent<global::Peak.Action_RaycastSpawnSomething>(out var spawner))
            return spawner.rayOrigin;
        if (item.TryGetComponent<RescueHook>(out var hook))
            return hook.firePoint;
        launcher = item.TryGetComponent<VineShooter>(out _);
        return null;
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
