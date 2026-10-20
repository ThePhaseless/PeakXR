using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>
/// Applies an <see cref="AvatarPose"/> to a character's arms, head and fingers through the game's IK.
/// Attached to any character that has a tracked pose (the local VR player, or a remote peer that
/// sends poses). Writers call <see cref="SetPose"/>; the driver applies it during the character's
/// IK pass and falls back to vanilla animation when the pose goes stale.
/// </summary>
public partial class AvatarPoseDriver : MonoBehaviour
{
    /// <summary>Seconds without a new pose before the driver hands control back to vanilla animation.</summary>
    public const float StaleAfter = 0.5f;

    public Character Character { get; private set; }
    public AvatarPose Pose { get; private set; }
    public float LastPoseTime { get; private set; } = float.NegativeInfinity;

    public bool HasFreshPose => Time.time - LastPoseTime < StaleAfter;

    /// <summary>
    /// While true the arms (and the held item's frame) follow the game's animation instead of the tracked hands;
    /// head look and fingers still apply. Set by the emote feature for full-body emotes.
    /// </summary>
    public bool ArmsSuspended { get; set; }

    public static AvatarPoseDriver For(Character character)
    {
        var driver = character.GetComponent<AvatarPoseDriver>();
        if (driver == null)
        {
            driver = character.gameObject.AddComponent<AvatarPoseDriver>();
            driver.Character = character;
        }
        return driver;
    }

    public void SetPose(in AvatarPose pose)
    {
        Pose = pose;
        LastPoseTime = Time.time;
    }
}
