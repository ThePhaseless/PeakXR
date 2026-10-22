using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// <c>interact.physical</c>: grab-to-pickup, hand-aimed interaction, trigger use, eating at the mouth, hand
/// throws and grip climbing with arm locomotion, plus haptics. Grip climbing also checks the hold under the hand,
/// keeps the climb through hand-over-hand regrips, makes stamina and slides felt, turns an upward arm throw into
/// the game's climb jump and carries a top-out over the ledge. Every action is fed into the game's own input
/// fields so pickups, uses, drops and climbs run through vanilla code and RPCs. Each part has a setting and a
/// button fallback.
/// </summary>
public sealed class PhysicalInteractionFeature : Feature
{
    public override string Id => "interact.physical";

    // After avatar.local (order 0) so LocalHold is consumed by a bound driver; order otherwise irrelevant.
    public override int Order => 10;

    protected internal override void Install()
    {
        InteractConfig.Bind(Plugin.Settings);
        PhysicalInteraction.StartWallClimb = GameMember.Delegate<PhysicalInteraction.TryStartClimb>(
            typeof(CharacterClimbing),
            "TryToStartWallClimb",
            new[] { typeof(bool), typeof(Vector3), typeof(bool), typeof(float) }
        );
        PhysicalInteraction.SinceLastClimbStarted = GameMember.Field<CharacterClimbing, float>(
            "sinceLastClimbStarted"
        );
        Patch(typeof(InteractPatches.Sample));
        Patch(typeof(InteractPatches.Raycasts));
        Patch(typeof(InteractPatches.Dropping));
        Climb.ClimbHands.AnchorTimer = GameMember.Field<BreakableRopeAnchor, float>(
            "willBreakInTime"
        );
        Climb.ClimbHands.AnchorBreaking = GameMember.Field<BreakableRopeAnchor, bool>("isBreaking");
        Climb.ClimbHands.Bind(Plugin.Settings);
        GripHooks.Register(Climb.ClimbHands.Extension);
        GripHooks.Register(Climb.TraversalGrips.Instance);
    }
}
