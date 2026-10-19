using BepInEx.Configuration;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Net;

/// <summary>
/// Multiplayer pose sync between PeakXR clients. Peers find each other through the <c>PeakXR</c>
/// Photon player property; VR players stream head, hands and fingers with <c>RaiseEvent</c> to those
/// peers only, and every modded client (VR or flat) drives remote characters through
/// <see cref="Avatar.AvatarPoseDriver"/>. Vanilla clients see nothing new: they ignore the property
/// and are never targeted by the event. What vanilla does see (head direction via <c>lookValues</c>)
/// is owned by <c>player.look</c>.
/// No Harmony patches: everything goes through public Photon callbacks and public game members
/// (Character.AllCharacters Character.cs:128, Character.localCharacter :118, data.lookValues
/// CharacterData.cs:77, refs.hip Character.cs:41 + Bodypart.Rig Bodypart.cs:72, and the ragdoll
/// state fields passedOut/fullyPassedOut :18-20, fallSeconds :230, isCarried :265, dead :335).
/// </summary>
public sealed class NetPoseFeature : Feature
{
    public override string Id => "net.pose";

    /// <summary>Flat players still receive and show VR peers' poses.</summary>
    public override bool VROnly => false;

    internal static ConfigEntry<bool> SendPose;
    internal static ConfigEntry<bool> ShowRemotePoses;
    internal static ConfigEntry<int> SendRate;
    internal static ConfigEntry<int> InterpolationDelayMs;

    protected internal override void Install()
    {
        SendPose = Plugin.Settings.Bind(
            "Multiplayer",
            "SendPose",
            true,
            new ConfigDescription(
                "Send your tracked head, hands and fingers to other PeakXR players while in VR. Vanilla players never receive it."
            )
        );
        ShowRemotePoses = Plugin.Settings.Bind(
            "Multiplayer",
            "ShowRemotePoses",
            true,
            new ConfigDescription(
                "Show the tracked heads, hands and fingers of other PeakXR VR players."
            )
        );
        SendRate = Plugin.Settings.Bind(
            "Multiplayer",
            "PoseSendRate",
            25,
            new ConfigDescription(
                "Pose packets per second. Only changed poses are sent; lower values save bandwidth.",
                new AcceptableValueRange<int>(20, 30)
            )
        );
        InterpolationDelayMs = Plugin.Settings.Bind(
            "Multiplayer",
            "PoseInterpolationDelayMs",
            100,
            new ConfigDescription(
                "How far behind real time remote hands are shown, in milliseconds. Higher is smoother on bad connections.",
                new AcceptableValueRange<int>(50, 250)
            )
        );

        var host = new GameObject("PeakXR.NetPose") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(host);
        host.AddComponent<NetPoseSync>();
    }
}
