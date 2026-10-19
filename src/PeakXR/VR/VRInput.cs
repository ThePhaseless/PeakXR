using UnityEngine;

namespace PeakXR.VR;

/// <summary>One controller's state for the current frame. Poses are in tracking space (relative to
/// <see cref="VRRig.Origin"/>); use the rig transforms for world space.</summary>
public struct HandState
{
    public bool Tracked;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public float Trigger;
    public float Grip;
    public Vector2 Stick;

    public bool TriggerTouch;
    public bool Primary;
    public bool Secondary;
    public bool PrimaryTouch;
    public bool SecondaryTouch;
    public bool StickClick;
    public bool StickTouch;
    public bool Menu;

    /// <summary>Per-finger curls from skeletal input or hand tracking when the runtime offers them,
    /// otherwise null and fingers are derived from the analog values.</summary>
    public float[] SkeletalCurls;
}

/// <summary>Raw XR device state, sampled once per frame before game input is read.</summary>
public static class VRInput
{
    public static HandState Left;
    public static HandState Right;

    public static bool HeadTracked;
    public static Vector3 HeadPosition;
    public static Quaternion HeadRotation = Quaternion.identity;

    public static ref HandState Hand(bool left) => ref left ? ref Left : ref Right;
}
