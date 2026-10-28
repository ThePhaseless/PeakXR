using System;

namespace PeakXR.Core;

/// <summary>
/// Shared maths for the VR settings tab: how many rows fit (the single-column fallback pages by it) and which rail
/// entry a config section belongs to. The headset layout itself is <see cref="VRTabColumn"/>.
/// </summary>
public static class SettingsGrid
{
    /// <summary>Whole items of <paramref name="item"/> with <paramref name="spacing"/> between them that fit in
    /// <paramref name="extent"/>; at least one.</summary>
    public static int Fit(float extent, float item, float spacing)
    {
        float pitch = item + spacing;
        if (pitch <= 0f)
            return 1;
        return Math.Max(1, (int)Math.Floor((extent + spacing) / pitch + 1e-4f));
    }

    /// <summary>Feel-only settings outside the Haptics-named keys: they change nothing but vibration.</summary>
    private static readonly string[] FeelKeys =
    {
        "Heartbeat",
        "Shiver",
        "Heat",
        "HazardZones",
        "WallTouch",
        "WindPressure",
        "CarriedSway",
        "PalJumpFeel",
        "ClimbSurfaceFeel",
        "WarmHandsAtFire",
    };

    /// <summary>Item and world gestures from <c>[Interaction]</c> (docs/controls.md "Items and fires by
    /// hand").</summary>
    private static readonly string[] ItemKeys =
    {
        "CookOverFire",
        "LiftLidToOpen",
        "BugleAtMouth",
        "LightWithFlame",
        "PressToBody",
        "ParasolRunner",
        "ReadAtFace",
        "CommuneAtFace",
        "BookOverBones",
        "GuardWastedItems",
        "TriggerEats",
        "AmuletInSocket",
        "PalmsOnPillar",
        "HandIntoPortal",
    };

    /// <summary>
    /// Where an entry shows in the VR tab, as a section name: every vibration setting under Haptics (headed by the
    /// section it is saved in, so the several "Haptics" switches stay apart), the item and fire gestures under Items,
    /// the rest under their own section. Display only: the config file keeps every section and key.
    /// </summary>
    public static string DisplaySection(string section, string key)
    {
        if (
            key.StartsWith("Haptic", StringComparison.Ordinal)
            || key.EndsWith("Haptics", StringComparison.Ordinal)
            || Array.IndexOf(FeelKeys, key) >= 0
        )
            return "Haptics." + section;
        if (section == "Interaction" && Array.IndexOf(ItemKeys, key) >= 0)
            return "Items";
        return section;
    }

    /// <summary>The rail entry a config section belongs to: the part before the first dot.</summary>
    public static string GroupOf(string section)
    {
        int dot = section.IndexOf('.');
        return dot < 0 ? section : section.Substring(0, dot);
    }

    /// <summary>The section's name inside its group ("UI.HUD" is "HUD"); empty for the group's own section.</summary>
    public static string SubsectionOf(string section)
    {
        int dot = section.IndexOf('.');
        return dot < 0 ? "" : section.Substring(dot + 1);
    }
}
