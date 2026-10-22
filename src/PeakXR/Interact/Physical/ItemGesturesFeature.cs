using System;
using System.Collections.Generic;
using PeakXR.Framework;

namespace PeakXR.Interact.Physical;

/// <summary>
/// <c>interact.items</c>: physical gestures for items and fires in VR. Hold food over a campfire to cook, lift a
/// suitcase lid to open it, raise the bugle to your mouth and tilt the bell for the note, touch a flame to a fuse or
/// fire to light it, press a bandage to your body or a friend's, warm your palms at the fire and slide a parasol's
/// runner. Every gesture ends in input the game already reads (interact hold, primary use, the bugle's own pitch
/// argument), so unmodded players see the normal result. Each has a setting; the buttons keep working.
/// </summary>
public sealed class ItemGesturesFeature : Feature
{
    public override string Id => "interact.items";

    // After interact.physical (10): its hand state and grip hooks exist; before interact.tools (25).
    public override int Order => 15;

    protected internal override void Install()
    {
        // Gestures reach the game only through interact.physical's input injection and grip hooks; without it
        // they would buzz the controllers for holds that never happen.
        if (!FeatureManager.IsActive<PhysicalInteractionFeature>())
            throw new InvalidOperationException("needs interact.physical");
        ItemGesturesConfig.Bind(Plugin.Settings);
        ItemGestures.CampfireList = GameMember.StaticField<Campfire, List<Campfire>>(
            "ALL_CAMPFIRES"
        );
        Patch(typeof(ItemGesturesPatches.Sample));
        Patch(typeof(ItemGesturesPatches.Tooting));
        Patch(typeof(ItemGesturesPatches.DoInteraction));
        Patch(typeof(ItemGesturesPatches.CancelUse));
        Patch(typeof(ItemGesturesPatches.DoUsing));
        Patch(typeof(ItemGesturesPatches.PickupCooldown));
        Patch(typeof(ItemGesturesPatches.PickupDenied));
        Patch(typeof(ItemGesturesPatches.SkeletonNear));
        Patch(typeof(ItemGesturesPatches.WarpRefund));
        GripHooks.Register(ParasolRunner.Instance);
        ItemGestures.Installed = true;
    }
}
