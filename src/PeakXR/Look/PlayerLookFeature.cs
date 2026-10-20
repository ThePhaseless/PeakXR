using System;
using PeakXR.Framework;
using PeakXR.Locomotion;

namespace PeakXR.Look;

/// <summary>
/// <c>player.look</c>: the headset is the character's look direction, so vanilla peers see where the VR player
/// looks and every look-relative system (movement, aiming, interaction) follows the head. Snap or smooth turning
/// rotates the rig, never the look values directly. Scripted look writes (frog tongue, rescue hook, scoutmaster)
/// win while they run and blend back to the headset after.
/// </summary>
public sealed class PlayerLookFeature : Feature
{
    public override string Id => "player.look";

    protected internal override void Install()
    {
        ComfortConfig.Bind(Plugin.Settings);
        LookPatches.Movement = GameMember.Field<CharacterMovement, Character>("character");
        LookPatches.Recalculate = GameMember.Delegate<Action<Character>>(
            typeof(Character),
            "RecalculateLookDirections"
        );
        Patch(typeof(LookPatches.CameraLook));
    }
}
