using System;
using System.Reflection;
using HarmonyLib;
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
        // The guards block only while armed, in a sandbox run, or while the room is open (SelfTestProgressGuard.Room).
        SelfTestProgressGuard.PatchOnce();
        Patch(typeof(ItemOptimizerNullItemPatch));
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
        // Optional: without it the room's dynamite lights as you come near its table, as in a run.
        try
        {
            ItemRoom.WickRef = GameMember.Field<Dynamite, UnityEngine.Vector3>("dynamitePosition");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[sandbox.room] dynamite lights by its table in the item room: {e.GetBaseException().Message}"
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

/// <summary>
/// An item prefab whose <c>ItemOptimizer.item</c> is unset disables the optimizer in <c>OnEnable</c>
/// (decomp Assembly-CSharp/Peak/ItemOptimizer.cs:9-14), which runs <c>OnDisable</c> (:21-24), and that calls
/// <c>ItemOptimizationManager.DeregisterItem(null)</c>: <c>Dictionary.Remove(null)</c> throws
/// (Peak/ItemOptimizationManager.cs:46-49). Spawning every item in the room (VR run of 3a1b2f9) hit it. An optimizer
/// with no item never registered, so there is nothing to deregister: skipped only then.
/// </summary>
[HarmonyPatch]
static class ItemOptimizerNullItemPatch
{
    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Peak.ItemOptimizer), "OnDisable", Type.EmptyTypes);

    // Only a field never set: an item destroyed after it registered must still be removed from the manager.
    static bool Prefix(Peak.ItemOptimizer __instance) => !ReferenceEquals(__instance.item, null);
}
