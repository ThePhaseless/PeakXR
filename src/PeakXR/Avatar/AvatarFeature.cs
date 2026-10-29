using PeakXR.Framework;

namespace PeakXR.Avatar;

/// <summary>
/// <c>avatar.local</c>: tracked arms, head and fingers for any character with a fresh <see cref="AvatarPose"/>
/// (the local VR player and modded remote players), held items in the real hand, and the VR player's own body
/// visible. Installed in flat play too, because flat modded clients render remote VR players with the same
/// driver; the local-only parts check <see cref="VR.VRSession.Active"/>.
/// </summary>
public sealed class AvatarFeature : Feature
{
    public override string Id => "avatar.local";

    public override bool VROnly => false;

    protected internal override void Install()
    {
        AvatarConfig.Bind(Plugin.Settings);
        AvatarPatches.Resolve();
        Patch(typeof(AvatarPatches.ConfigureIK));
        Patch(typeof(AvatarPatches.HandleIK));
        Patch(typeof(AvatarPatches.SaveAdditional));
        Patch(typeof(AvatarPatches.HoldForward));
        Patch(typeof(AvatarPatches.HoldUp));
        Patch(typeof(AvatarPatches.AttachItem));
        Patch(typeof(AvatarPatches.UnAttachItem));
        Patch(typeof(AvatarPatches.HoldItem));
        Patch(typeof(AvatarPatches.ItemWalkForce));
        Patch(typeof(AvatarPatches.ItemDrag));
        Patch(typeof(AvatarPatches.HideBody));
        Patch(typeof(AvatarPatches.LevelLook));
        Patch(typeof(AvatarPatches.ShowBodyWhenDetached));
        Patch(typeof(AvatarPatches.CharacterStart));
        ReachGhost.Create();
        HeldItemScale.Install();
    }
}
