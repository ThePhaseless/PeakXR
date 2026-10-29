using System;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Interact.Aim;

/// <summary>
/// <c>interact.aim</c>: aimed tools and launchers (rope tie-off, rope and vine shooters, rescue hook, pitons, darts,
/// antizooka, clone staff, placeable items) aim from the holding hand, or along the two-hand axis. The camera pose
/// is swapped only for the duration of each game call that reads it, so hits, previews and RPCs stay vanilla. Adds
/// an aim line while the trigger is touched and recoil haptics on shots.
/// </summary>
public sealed class AimFeature : Feature
{
    public override string Id => "interact.aim";

    // After interact.physical (order 10), whose trigger mapping and LocalHold this builds on.
    public override int Order => 20;

    protected internal override void Install()
    {
        AimConfig.Bind(Plugin.Settings);
        AimPatches.RopeShooterAmmo = GameMember.Delegate<Func<RopeShooter, int>>(
            typeof(RopeShooter),
            "get_Ammo"
        );
        Patch(typeof(AimPatches.RopeTierUpdate));
        Patch(typeof(AimPatches.RopeShooterWillAttach));
        Patch(typeof(AimPatches.RopeShooterFire));
        Patch(typeof(AimPatches.VineShooterWillAttach));
        Patch(typeof(AimPatches.VineShooterFire));
        Patch(typeof(AimPatches.VinePathTry));
        Patch(typeof(AimPatches.RescueHookRay));
        Patch(typeof(AimPatches.RescueHookFire));
        Patch(typeof(OwnGrapple.RescueWall));
        Patch(typeof(OwnGrapple.LetGo));
        Patch(typeof(AimPatches.ConstructableUpdate));
        Patch(typeof(AimPatches.DartFire));
        Patch(typeof(AimPatches.SpawnSomethingAim));
        Patch(typeof(AimPatches.SpawnSomethingFire));
        Patch(typeof(AimPatches.CloneFire));
        Patch(typeof(AimPatches.ClimbingSpike));
        Patch(typeof(AimPatches.PitonPrompt));

        var host = new GameObject("PeakXR.Aim") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<AimReticle>();
    }
}
