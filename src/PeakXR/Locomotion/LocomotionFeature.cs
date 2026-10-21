using PeakXR.Framework;
using PeakXR.Interact;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// <c>player.locomotion</c>: controllers to <see cref="CharacterInput"/> (move, jump, sprint, crouch, hotbar,
/// backpack, ping, emote, pause, plus interact/use/drop fallbacks when interact.physical is off), room-scale walking,
/// the comfort vignette, move smoothing, comfort presets and the seated flag. Layout: docs/controls.md.
/// </summary>
public sealed class LocomotionFeature : Feature
{
    public override string Id => "player.locomotion";

    // After interact.physical (10), so its install result decides who owns grip and trigger.
    public override int Order => 20;

    protected internal override void Install()
    {
        ComfortConfig.Bind(Plugin.Settings);
        LocomotionConfig.Bind(Plugin.Settings);
        BodyGestures.Bind(Plugin.Settings);
        LocomotionInput.PhysicalOwnsHands = FeatureManager.IsActive<PhysicalInteractionFeature>();
        Patch(typeof(LocomotionPatches.Sample));
        Patch(typeof(LocomotionPatches.HotbarKey));

        var host = new GameObject("PeakXR.Locomotion") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(host);
        host.AddComponent<LocomotionDriver>();
    }
}
