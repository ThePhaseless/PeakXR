using System;
using UnityEngine;

namespace PeakXR.VR;

/// <summary>One controller's state for the current frame. Poses are in tracking space (relative to
/// <see cref="VRRig.Origin"/>); use the rig transforms for world space.</summary>
public struct HandState
{
    public bool Tracked;

    /// <summary>The runtime has no device for this hand (switched off, flat, unpaired). Default false, so a scripted
    /// or blanked hand counts as present.</summary>
    public bool Disconnected;
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

    /// <summary>A thumb rests on the Touch thumbrest or the Index/WMR trackpad: a resting thumb, never a finger
    /// gun's lifted one.</summary>
    public bool ThumbrestTouch;
    public bool Menu;

    /// <summary>The controller cannot sense a finger on the trigger (Vive wand, WMR, Reverb G2): a finger off the
    /// trigger is unknown, so the hand never reads as pointing (<c>ControllerButtons.NoTriggerTouch</c>).</summary>
    public bool NoTriggerTouch;

    /// <summary>Per-finger curls from skeletal input or hand tracking when the runtime offers them,
    /// otherwise null and fingers are derived from the analog values.</summary>
    public float[] SkeletalCurls;

    /// <summary>A Vive wand: its pad is the stick and it has no A or B (one-handed play picks
    /// <c>BindingTable.OneHandedWand</c> for it).</summary>
    public bool WandPad;

    /// <summary>This frame a Vive wand's centre click (its B/Y) slid out of the centre: B/Y ended without a tap, so
    /// the layout must not read it as a ping (<c>WandPadLatch</c>).</summary>
    public bool SecondaryCancelled;

    /// <summary>The controller accepts haptic impulses (<c>InputDevice.SendHapticImpulse</c>).</summary>
    public bool Haptics;
}

/// <summary>Raw XR device state, sampled once per frame before game input is read.</summary>
public static class VRInput
{
    public static HandState Left;
    public static HandState Right;

    public static bool HeadTracked;
    public static Vector3 HeadPosition;
    public static Quaternion HeadRotation = Quaternion.identity;

    /// <summary>The player chose the left hand as dominant (setting <c>Input.DominantHand</c>). Layouts mirror.</summary>
    public static bool LeftHanded;

    /// <summary>One-handed play (setting <c>Accessibility.OneHanded</c>): only the <see cref="LeftHanded"/> controller
    /// is read, the other is left untracked, and every two-hand action takes its one-hand path.</summary>
    public static bool OneHanded;

    /// <summary>The off-hand controller for cues and HUD, or the one controller in one-handed play.</summary>
    public static bool OffHandLeft => OneHanded ? LeftHanded : !LeftHanded;

    /// <summary>Self-test only: runs right after the sampler fills this frame's state, so scripted hands can stand in
    /// for the controllers (tracking-space poses, buttons, sticks). Null outside the self-test.</summary>
    internal static Action TestOverride;

    public static ref HandState Hand(bool left) => ref left ? ref Left : ref Right;
}
