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

    public Transform Hand(bool left) => left ? LeftHand : RightHand;
    public Transform Aim(bool left) => left ? LeftAim : RightAim;
}
