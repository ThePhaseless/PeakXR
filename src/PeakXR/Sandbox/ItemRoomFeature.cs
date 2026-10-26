using System;
using PeakXR.Framework;
using PeakXR.UI.Settings;

namespace PeakXR.Sandbox;

/// <summary>
/// sandbox.room: the item room (<see cref="ItemRoom"/>), every item of the game laid out in the solo airport so they
/// can be tried one after another from the headset. Part of the sandbox, so it needs <c>Sandbox.Enabled</c>; it opens
/// from the "Item room" button under that switch in the VR tab, or <c>Sandbox.Room</c> in the console.
/// </summary>
public sealed class ItemRoomFeature : Feature
{
    public override string Id => "sandbox.room";

    // After sandbox (0) and the self-test features (1), which may already have patched the progress guard.
    public override int Order => 2;

    public override bool VROnly => false;

    protected internal override void Install()
    {
        if (!FeatureManager.IsActive<SandboxFeature>())
            throw new InvalidOperationException("the sandbox feature is not active");
        // The guards block only while armed or while the room is open (SelfTestProgressGuard.Room).
        if (!SelfTestProgressGuard.Active)
            foreach (var guard in SelfTestProgressGuard.Patches)
                Patch(guard);
        // Optional: without it the room still opens, with the airport's statuses switched off as usual.
        try
        {
            ItemRoom.InAirportRef = GameMember.Field<CharacterAfflictions, bool>("m_inAirport");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[sandbox.room] statuses stay off in the item room: {e.GetBaseException().Message}"
            );
        }
        VRTab.AddButtonAfter(
            SandboxFeature.EnabledEntry,
            "PEAKXR_ITEM_ROOM",
            "Item room",
            "Enter or leave",
            ItemRoom.Toggle
        );
    }
}
