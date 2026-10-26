using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>The item room's tables, in the order the aisle passes them: fire and food first so cooking is one step
/// away, climbing beside the test wall, the odd and the unknown last.</summary>
public enum ShelfGroup : byte
{
    Fire,
    Food,
    Medicine,
    Climbing,
    Throwing,
    Movement,
    Tools,
    Mystic,
    Containers,
    Other,
    Count,
}

/// <summary>
/// Sorts the game's item database into the item room's tables from what each prefab carries (its component type
/// names), so a new item finds a table without a hand-kept list. Names are only a fallback for kinds no component
/// marks (chalk, mushrooms).
/// </summary>
public static class ItemShelf
{
    static readonly string[] Climbing =
    {
        "RopeSpool",
        "RopeShooter",
        "ClimbingSpikeComponent",
        "VineShooter",
        "RescueHook",
        "MagicBean",
    };

    static readonly string[] Fire = { "ItemTorch", "Lantern", "Flare", "Candle", "FireWoodItem" };

    static readonly string[] Movement =
    {
        "Balloon",
        "Glider",
        "ParachuteItem",
        "JetpackItem",
        "Rocketpack",
        "Action_Parasol",
        "Action_LaunchPlayer",
        "Action_ApplySuperJump",
        "Action_WarpRandomly",
        "Action_WarpToBiome",
        "Action_WarpToRandomPlayer",
    };

    static readonly string[] Food =
    {
        "Action_RestoreHunger",
        "Action_GiveExtraStamina",
        "Action_RandomMushroomEffect",
    };

    static readonly string[] Throwing =
    {
        "Breakable",
        "BreakableEgg",
        "Frisbee",
        "Basketball",
        "Snowball",
        "Dynamite",
        "CactusBall",
        "WarpOnThrow",
        "KnockOutPlayerOnImpact",
        "Action_Antizooka",
        "Action_RaycastDart",
        "Stone",
    };

    static readonly string[] Mystic =
    {
        "AmuletBase",
        "Action_StrangeGem",
        "Action_BecomeSkeleton",
        "Action_SacrificeFriend",
        "RitualDaggerFeedBehavior",
        "Action_CallScoutmaster",
        "Action_CloneSelectedItem",
        "Action_AskBingBong",
        "Mandrake",
        "Action_Die",
        "MagicBugle",
    };

    static readonly string[] Medicine =
    {
        "Action_ModifyStatus",
        "Action_ClearAllStatus",
        "Action_HealingGem",
        "Action_AddOrRemoveThorns",
        "Action_ApplyAffliction",
        "Action_ApplyInfiniteStamina",
        "Action_Numb",
    };

    static readonly string[] Tools =
    {
        "Actions_Binoculars",
        "Action_ShowBinocularOverlay",
        "CompassPointer",
        "Guidebook",
        "BugleSFX",
        "Action_Passport",
        "Constructable",
        "Action_OverrideCamera",
    };

    static readonly string[] FoodNames = { "Mushroom", "Shroom", "Berry" };

    /// <summary>Why <paramref name="name"/> is left off the tables, or null to place it. Build previews, props and
    /// constructable templates are not items on their own; creatures belong to the sandbox entity list.</summary>
    public static string SkipReason(string name, ICollection<string> components)
    {
        if (string.IsNullOrEmpty(name))
            return "no name";
        if (name.EndsWith("Preview", StringComparison.Ordinal))
            return "build preview, not an item";
        if (name.Contains("_Prop", StringComparison.Ordinal))
            return "prop, not an item";
        if (
            name.StartsWith("BaseConstructable", StringComparison.Ordinal)
            || name == "TestConstructable"
        )
            return "constructable template";
        if (components.Contains("Mob") || components.Contains("MobItem"))
            return "creature: spawn it from the sandbox entity list";
        return null;
    }

    /// <summary>The table for an item; the first matching rule wins, in the order of the checks below.</summary>
    public static ShelfGroup GroupOf(string name, ICollection<string> components)
    {
        name ??= "";
        if (components.Contains("Backpack"))
            return ShelfGroup.Containers;
        if (Any(components, Climbing) || Has(name, "Chalk"))
            return ShelfGroup.Climbing;
        if (Any(components, Fire))
            return ShelfGroup.Fire;
        if (Any(components, Movement))
            return ShelfGroup.Movement;
        if (
            Any(components, Food)
            || (!components.Contains("Constructable") && AnyName(name, FoodNames))
        )
            return ShelfGroup.Food;
        if (Any(components, Throwing))
            return ShelfGroup.Throwing;
        if (Any(components, Mystic))
            return ShelfGroup.Mystic;
        if (Any(components, Medicine))
            return ShelfGroup.Medicine;
        if (Any(components, Tools))
            return ShelfGroup.Tools;
        return ShelfGroup.Other;
    }

    /// <summary>The table's label.</summary>
    public static string Title(ShelfGroup group) =>
        group switch
        {
            ShelfGroup.Fire => "Fire and light",
            ShelfGroup.Food => "Food",
            ShelfGroup.Medicine => "Medicine",
            ShelfGroup.Climbing => "Climbing",
            ShelfGroup.Throwing => "Throw and launch",
            ShelfGroup.Movement => "Movement",
            ShelfGroup.Tools => "Tools",
            ShelfGroup.Mystic => "Mystic",
            ShelfGroup.Containers => "Bags",
            _ => "Other",
        };

    static bool Any(ICollection<string> components, string[] names)
    {
        foreach (var n in names)
            if (components.Contains(n))
                return true;
        return false;
    }

    static bool AnyName(string name, string[] parts)
    {
        foreach (var p in parts)
            if (Has(name, p))
                return true;
        return false;
    }

    static bool Has(string name, string part) =>
        name.Contains(part, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One item's spot: <see cref="X"/> across the aisle, <see cref="Z"/> along it, in meters.</summary>
public readonly struct ShelfSlot
{
    public readonly int Group;
    public readonly float X;
    public readonly float Z;

    public ShelfSlot(int group, float x, float z)
    {
        Group = group;
        X = x;
        Z = z;
    }
}

/// <summary>A table of one group: its front row at <see cref="Z"/>, <see cref="Rows"/> deep, and the slots
/// <see cref="First"/> to <see cref="First"/> + <see cref="Count"/> - 1.</summary>
public readonly struct ShelfTable
{
    public readonly int Group;
    public readonly float Z;
    public readonly int Rows;
    public readonly float HalfWidth;
    public readonly int First;
    public readonly int Count;

    public ShelfTable(int group, float z, int rows, float halfWidth, int first, int count)
    {
        Group = group;
        Z = z;
        Rows = rows;
        HalfWidth = halfWidth;
        First = first;
        Count = count;
    }
}

public sealed class ShelfPlan
{
    public ShelfSlot[] Slots;
    public ShelfTable[] Tables;

    /// <summary>Distance from the first row to the last, in meters.</summary>
    public float Depth;
}

/// <summary>
/// Lays the item room out as tables across an aisle. A table is at most two rows deep, so every item faces a walkway:
/// the front row from the aisle before the table, the back row from the one after it. Groups never share a table.
/// </summary>
public static class ShelfLayout
{
    public static ShelfPlan Plan(
        IReadOnlyList<int> counts,
        int perRow,
        float spacing,
        float rowGap,
        float aisle
    )
    {
        perRow = Math.Max(1, perRow);
        int total = 0;
        foreach (var c in counts)
            total += Math.Max(0, c);
        var slots = new ShelfSlot[total];
        var tables = new List<ShelfTable>();
        float z = 0f;
        int index = 0;
        for (int g = 0; g < counts.Count; g++)
        {
            for (int done = 0; done < counts[g]; )
            {
                if (tables.Count > 0)
                    z += aisle;
                int take = Math.Min(2 * perRow, counts[g] - done);
                int rows = take > perRow ? 2 : 1;
                int widest = Math.Min(take, perRow);
                tables.Add(new ShelfTable(g, z, rows, (widest - 1) * spacing * 0.5f, index, take));
                for (int k = 0; k < take; k++)
                {
                    int row = k / perRow;
                    int inRow = Math.Min(perRow, take - row * perRow);
                    float x = (k % perRow - (inRow - 1) * 0.5f) * spacing;
                    slots[index++] = new ShelfSlot(g, x, z + row * rowGap);
                }
                z += (rows - 1) * rowGap;
                done += take;
            }
        }
        return new ShelfPlan
        {
            Slots = slots,
            Tables = tables.ToArray(),
            Depth = z,
        };
    }

    /// <summary>The fewest items a row (the narrowest room) whose layout fits <paramref name="maxDepth"/>; the widest
    /// allowed when none does.</summary>
    public static int ChoosePerRow(
        IReadOnlyList<int> counts,
        float maxDepth,
        int minPerRow,
        int maxPerRow,
        float spacing,
        float rowGap,
        float aisle
    )
    {
        for (int p = minPerRow; p < maxPerRow; p++)
            if (Plan(counts, p, spacing, rowGap, aisle).Depth <= maxDepth)
                return p;
        return maxPerRow;
    }
}
