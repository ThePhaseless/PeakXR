using System;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.VR;

/// <summary>
/// The player's tracking space in the world. Lives on its own root GameObject (never under the
/// game's MainCamera, which cutscenes deactivate). Origin is placed by the camera feature each frame;
/// Head and the hand transforms follow tracked poses inside it.
/// </summary>
public class VRRig : MonoBehaviour
{
    public static VRRig Instance { get; internal set; }

    /// <summary>Tracking-space root. Moving or rotating it moves the player's view.</summary>
    public Transform Origin;

    /// <summary>HMD pose. The stereo camera renders from here.</summary>
    public Transform Head;
    public Camera HeadCamera;

    /// <summary>While true, the camera feature leaves <see cref="Origin"/> alone (title stage places it).</summary>
    public bool OriginOverride;

    /// <summary>Grip poses: where the palm holds things.</summary>
    public Transform LeftHand;
    public Transform RightHand;

    /// <summary>Aim poses: pointer rays for UI and aimed tools.</summary>
    public Transform LeftAim;
    public Transform RightAim;

    /// <summary>
    /// Tracking-space (x, z) point the camera feature keeps over the character's body when it places
    /// <see cref="Origin"/>. Room-scale walking (player.locomotion) slides it toward the head as the body catches
    /// up, so the view stays world-fixed; leaning moves the view away from the body.
    /// </summary>
    public System.Numerics.Vector2 RoomAnchor;

    /// <summary>Metres added to tracking-space Y so the calibrated eye height matches the avatar's. The camera
    /// feature calibrates it (on recenter, honouring <see cref="SeatedMode"/>) and applies it when placing
    /// <see cref="Origin"/>.</summary>
    public float HeightOffset;

    /// <summary>The player plays seated (setting <c>Comfort.SeatedMode</c>, written by player.locomotion): height
    /// calibration maps the seated head height to the avatar's standing eye height.</summary>
    public bool SeatedMode;

    /// <summary>Seated downward reach gain (setting <c>Comfort.SeatedReachGain</c>, written by player.locomotion); the
    /// camera feature applies it to the hand transforms (<see cref="SeatedReach"/>). 1 = off.</summary>
    public float SeatedReachGain = 1f;

    /// <summary>World units per real metre of tracking space (<c>Camera.WorldScale</c>, applied by the camera feature as
    /// <see cref="Origin"/>'s scale behind a fade): head motion, hand reach and the eye separation grow together, so
    /// the world looks smaller. Tracking-space values (<see cref="VRInput"/>, <see cref="RoomAnchor"/>,
    /// <see cref="HeightOffset"/>) stay in real metres; world positions read through <see cref="Origin"/> are scaled.
    /// 1 at the title stage.</summary>
    public float Scale = 1f;

    /// <summary>Metres the first-person view rises above the body's eye point. player.locomotion sets it to the torso's
    /// drop while a physical crouch lowers the body, so the view stays at the real head; 0 otherwise.</summary>
    public float CrouchLift;

    /// <summary>Raised after a recenter (first frame, menu long-press, <see cref="Recenter"/>).</summary>
    public event Action Recentered;

    /// <summary>Set by <see cref="Recenter"/>; the camera feature performs it under a short fade.</summary>
    internal bool RecenterRequested;

    /// <summary>
    /// Turns the player's view around the head by <paramref name="degrees"/> (positive = right). Used by snap and
    /// smooth turning. Rotates <see cref="Origin"/> around the head and moves <see cref="RoomAnchor"/> so the head
    /// stays where it is when the camera feature re-places the origin.
    /// </summary>
    public void RotateYaw(float degrees)
    {
        if (Origin == null || degrees == 0f)
            return;
        var headLocal =
            VRInput.HeadTracked ? VRInput.HeadPosition
            : Head != null ? Head.localPosition
            : Vector3.zero;
        Origin.RotateAround(Origin.TransformPoint(headLocal), Vector3.up, degrees);
        RoomAnchor = RoomScaleFollow.AnchorAfterYaw(
            RoomAnchor,
            new System.Numerics.Vector2(headLocal.x, headLocal.z),
            degrees
        );
    }

    /// <summary>
    /// Re-centres the player: the current head spot becomes <see cref="RoomAnchor"/>, the headset's heading
    /// turns to the body's heading and the eye height is recalibrated. Performed by the camera feature on its
    /// next frame, behind a short fade.
    /// </summary>
    public void Recenter() => RecenterRequested = true;

    internal void RaiseRecentered() => Recentered?.Invoke();

    public Transform Hand(bool left) => left ? LeftHand : RightHand;

    public Transform Aim(bool left) => left ? LeftAim : RightAim;
}
