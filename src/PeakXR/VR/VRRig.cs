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

    /// <summary>Metres added to tracking-space Y so the calibrated eye height matches the avatar's
    /// (height calibration and seated mode, player.locomotion). The camera feature applies it when placing
    /// <see cref="Origin"/>.</summary>
    public float HeightOffset;

    /// <summary>
    /// Turns the player's view around the head by <paramref name="degrees"/> (positive = right). Used by snap and
    /// smooth turning. Rotates <see cref="Origin"/> around the head and moves <see cref="RoomAnchor"/> so the head
    /// stays where it is when the camera feature re-places the origin.
    /// </summary>
    public void RotateYaw(float degrees)
    {
        if (Origin == null || degrees == 0f)
            return;
        var headLocal = VRInput.HeadTracked ? VRInput.HeadPosition
            : Head != null ? Head.localPosition
            : Vector3.zero;
        Origin.RotateAround(Origin.TransformPoint(headLocal), Vector3.up, degrees);
        RoomAnchor = RoomScaleFollow.AnchorAfterYaw(
            RoomAnchor,
            new System.Numerics.Vector2(headLocal.x, headLocal.z),
            degrees
        );
    }

    public Transform Hand(bool left) => left ? LeftHand : RightHand;

    public Transform Aim(bool left) => left ? LeftAim : RightAim;
}
