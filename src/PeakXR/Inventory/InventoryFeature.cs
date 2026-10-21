using System;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Inventory;

/// <summary>
/// <c>inventory.vr</c>: body-anchored hotbar (hips, chest, shoulders) with grip draw and release holster, the worn
/// backpack over either shoulder (open, take, stash, take off), wheel pocket selection by hand movement and
/// hand-lock haptics when the game refuses a switch. In VR the hotbar is drawn on the body and the game's 2D hotbar
/// is hidden. Every switch goes through the game's hotbar input, so
/// vanilla gates, <c>EquipSlot</c> and its RPCs apply. Hotbar buttons keep working as the fallback.
/// </summary>
public sealed class InventoryFeature : Feature
{
    public override string Id => "inventory.vr";

    // After interact.physical (10): grips reach the extension through its GripHooks.
    public override int Order => 20;

    protected internal override void Install()
    {
        InventoryConfig.Bind(Plugin.Settings);
        BodyInventory.LockedFromSwitching = GameMember.Delegate<Func<CharacterItems, bool>>(
            typeof(CharacterItems),
            "get_lockedFromSwitching"
        );
        BodyInventory.CanDoInput = GameMember.Delegate<Func<Character, bool>>(
            typeof(Character),
            "CanDoInput"
        );
        BodyInventory.SelectWheelSlice = GameMember.Delegate<Action<BackpackWheel, Vector2>>(
            typeof(BackpackWheel),
            "TestSelectSliceGamepad",
            new[] { typeof(Vector2) }
        );
        Patch(typeof(InventoryPatches.Sample));
        Patch(typeof(InventoryPatches.SelectSlot));
        GripHooks.Register(BodyInventory.Instance);
        // The 2D hotbar comes back as soon as VR stops; the Sample postfix only runs in VR.
        VRSession.Stopped += HotbarHider.Restore;
    }
}
