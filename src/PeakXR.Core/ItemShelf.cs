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

/// <summary>The item room's areas, near to far from where the player starts.</summary>
public enum ItemZone : byte
{
    /// <summary>Food, drink, healing, tools, climbing gear: nothing that hurts you.</summary>
    Helpful,

    /// <summary>Hurts, kills, warps, poisons or explodes once used (<see cref="ItemShelf.HarmOf"/>).</summary>
    Dangerous,

    /// <summary>Goes off without being used (dynamite lights when you come near it): kept apart from everything.</summary>
    Volatile,
    Count,
}

/// <summary>A status an item adds (positive) or takes away (negative) when used, by the game's status name.</summary>
public readonly struct StatusChange
{
    public readonly string Status;
    public readonly float Amount;

    public StatusChange(string status, float amount)
    {
        Status = status;
        Amount = amount;
    }
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

    /// <summary>Components whose whole job hurts the user, with what they do (decompile: Action_Die,
    /// Action_BecomeSkeleton, Action_Warp*, Dynamite, KnockOutPlayerOnImpact, Action_InflictPoison, CactusBall,
    /// Action_RandomMushroomEffect's bad half, Action_CallScoutmaster's chase).</summary>
    static readonly KeyValuePair<string, string>[] HarmfulComponents =
    {
        new("Action_Die", "kills"),
        new("Action_BecomeSkeleton", "kills (skeleton)"),
        new("Action_WarpRandomly", "warps"),
        new("Action_WarpToBiome", "warps"),
        new("Action_WarpToRandomPlayer", "warps"),
        new("Dynamite", "explodes"),
        new("KnockOutPlayerOnImpact", "knocks out"),
        new("Action_InflictPoison", "poisons"),
        new("CactusBall", "thorns"),
        new("Action_RandomMushroomEffect", "random effect, half of them bad"),
        new("Action_CallScoutmaster", "sets the Scoutmaster on you"),
    };

    /// <summary>Afflictions (Peak.Afflictions.Affliction.AfflictionType) that only hurt. Status ones
    /// (AdjustStatus, the over-time ones) arrive as <see cref="StatusChange"/> with their sign instead.</summary>
    static readonly string[] HarmfulAfflictions =
    {
        "PoisonOverTime",
        "Exhausted",
        "Chaos",
        "PreventPoisonHealing",
        "ZombieBite",
        "Blind",
    };

    /// <summary>What an item does to its user that hurts, or null when nothing does: a harmful component, a status
    /// it adds (every status of the game's bar is bad, so any positive change is harm), or a harmful affliction.</summary>
    public static string HarmOf(
        ICollection<string> components,
        IReadOnlyList<StatusChange> statuses,
        ICollection<string> afflictions
    )
    {
        foreach (var pair in HarmfulComponents)
            if (components.Contains(pair.Key))
                return pair.Value;
        foreach (var change in statuses)
            if (change.Amount > 0f)
                return "adds " + change.Status;
        foreach (var name in HarmfulAfflictions)
            if (afflictions.Contains(name))
                return name;
        return null;
    }

    /// <summary>The item room area: <paramref name="igniteRadius"/> &gt; 0 (dynamite's <c>lightFuseRadius</c>) goes
    /// off by itself, any <paramref name="harm"/> is dangerous, the rest helpful.</summary>
    public static ItemZone ZoneOf(string harm, float igniteRadius) =>
        igniteRadius > 0f ? ItemZone.Volatile
        : harm != null ? ItemZone.Dangerous
        : ItemZone.Helpful;

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

/// <summary>An axis-aligned floor area in the item room's frame: X across (right positive), Z away from the player.</summary>
public readonly struct RoomRect
{
    public readonly float MinX;
    public readonly float MaxX;
    public readonly float MinZ;
    public readonly float MaxZ;

    public RoomRect(float minX, float maxX, float minZ, float maxZ)
    {
        MinX = minX;
        MaxX = maxX;
        MinZ = minZ;
        MaxZ = maxZ;
    }

    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public float CenterX => (MinX + MaxX) * 0.5f;
    public float CenterZ => (MinZ + MaxZ) * 0.5f;

    /// <summary>Shortest distance between two areas; 0 when they touch or overlap.</summary>
    public float Gap(RoomRect other)
    {
        float dx = Math.Max(0f, Math.Max(MinX - other.MaxX, other.MinX - MaxX));
        float dz = Math.Max(0f, Math.Max(MinZ - other.MaxZ, other.MinZ - MaxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override string ToString() => $"x {MinX:0.0}..{MaxX:0.0} z {MinZ:0.0}..{MaxZ:0.0}";
}

/// <summary>The item room's measures, in meters.</summary>
public sealed class ShelfSpec
{
    /// <summary>Between items in a row.</summary>
    public float Spacing = 0.55f;

    /// <summary>Between a table's front and back row.</summary>
    public float RowGap = 0.6f;

    /// <summary>Plank beyond the outer rows, front and back.</summary>
    public float EdgePad = 0.225f;

    /// <summary>Walking room between tables.</summary>
    public float Aisle = 1.6f;

    /// <summary>Between the helpful and the dangerous area.</summary>
    public float ZoneGap = 2.5f;

    /// <summary>Kept from the room's walls.</summary>
    public float Margin = 0.8f;

    /// <summary>Clear floor between where the player starts and the first table (the start orbs stand in it).</summary>
    public float FrontWalk = 2f;

    /// <summary>Distance from items that go off by themselves to everything else, at least <see cref="ZoneGap"/>
    /// (the room sets it from the largest <c>lightFuseRadius</c>).</summary>
    public float VolatileClearance;

    /// <summary>The test wall's footprint and the climbing room in front of it.</summary>
    public float WallWidth = 4f;
    public float WallDepth = 0.5f;
    public float WallRoom = 2.5f;

    /// <summary>Half the cooking fire's footprint.</summary>
    public float FireHalf = 0.6f;

    public int MinPerRow = 4;
    public int MaxPerRow = 12;
}

/// <summary>One item's spot: <see cref="X"/> across the room, <see cref="Z"/> away from the player, in meters.</summary>
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

/// <summary>A table of one bucket (<see cref="ShelfLayout.Bucket"/>): its front row at <see cref="Z"/>, centred on
/// <see cref="X"/>, <see cref="Rows"/> deep, holding slots <see cref="First"/> to <see cref="First"/> +
/// <see cref="Count"/> - 1 on <see cref="Footprint"/>.</summary>
public readonly struct ShelfTable
{
    public readonly int Group;
    public readonly ItemZone Zone;
    public readonly float X;
    public readonly float Z;
    public readonly int Rows;
    public readonly float HalfWidth;
    public readonly int First;
    public readonly int Count;
    public readonly RoomRect Footprint;

    public ShelfTable(
        int group,
        float x,
        float z,
        int rows,
        float halfWidth,
        int first,
        int count,
        RoomRect footprint
    )
    {
        Group = group;
        Zone = ShelfLayout.ZoneOf(group);
        X = x;
        Z = z;
        Rows = rows;
        HalfWidth = halfWidth;
        First = first;
        Count = count;
        Footprint = footprint;
    }
}

public sealed class ShelfPlan
{
    public ShelfSlot[] Slots;
    public ShelfTable[] Tables;

    /// <summary>All tables together.</summary>
    public RoomRect Block;

    /// <summary>Each <see cref="ItemZone"/>'s tables together; zero size when it has none.</summary>
    public RoomRect[] Zones;

    /// <summary>Across position of the start orbs, in front of the player.</summary>
    public float StartX;

    public RoomRect Fire;
    public bool HasWall;
    public RoomRect Wall;
    public int PerRow;

    /// <summary>Everything stands inside the room, clear of its walls, with every clearance kept.</summary>
    public bool Fits;

    /// <summary>The larger of width and depth used over what the room offers (1 = exactly full).</summary>
    public float Over;
}

/// <summary>
/// Lays the item room out in the middle of the measured floor: a band of tables per <see cref="ItemZone"/>, helpful
/// nearest the player, dangerous behind it, what goes off by itself farthest. A band wraps its tables into as many
/// columns as the room is wide; a table is at most two rows deep so every item faces a walkway; buckets never share a
/// table. The cooking fire heads the helpful band, the test wall stands past the last band against the left side.
/// </summary>
public static class ShelfLayout
{
    /// <summary>Table buckets: one per zone and group.</summary>
    public const int Buckets = (int)ItemZone.Count * (int)ShelfGroup.Count;

    public static int Bucket(ItemZone zone, ShelfGroup group) =>
        (int)zone * (int)ShelfGroup.Count + (int)group;

    public static ItemZone ZoneOf(int bucket) => (ItemZone)(bucket / (int)ShelfGroup.Count);

    public static ShelfGroup GroupOf(int bucket) => (ShelfGroup)(bucket % (int)ShelfGroup.Count);

    /// <summary>The best plan for <paramref name="counts"/> (items per bucket) in <paramref name="room"/>: the full
    /// volatile clearance and the wall if they fit, the table width that leaves the most room, else the plan that
    /// overflows least, with <see cref="ShelfPlan.Fits"/> false.</summary>
    public static ShelfPlan Plan(IReadOnlyList<int> counts, RoomRect room, ShelfSpec spec)
    {
        float full = Math.Max(spec.ZoneGap, spec.VolatileClearance);
        ShelfPlan fallback = null;
        foreach (float clearance in new[] { full, spec.ZoneGap })
        foreach (bool wall in new[] { true, false })
        {
            ShelfPlan pick = null;
            for (int p = spec.MinPerRow; p <= spec.MaxPerRow; p++)
            {
                var plan = Pack(counts, p, room, spec, wall, clearance);
                if (plan.Fits && (pick == null || plan.Over < pick.Over))
                    pick = plan;
                if (fallback == null || plan.Over < fallback.Over)
                    fallback = plan;
            }
            if (pick != null)
                return pick;
        }
        return fallback;
    }

    sealed class Piece
    {
        public int Bucket = -1;
        public int Take;
        public int Rows;
        public float Width;
        public float Depth;
        public float X;
        public float Z;
    }

    static ShelfPlan Pack(
        IReadOnlyList<int> counts,
        int perRow,
        RoomRect room,
        ShelfSpec spec,
        bool wall,
        float clearance
    )
    {
        perRow = Math.Max(1, perRow);
        float colW = perRow * spec.Spacing;
        float availW = room.Width - 2f * spec.Margin;
        float nearZ = room.MinZ + spec.FrontWalk;
        float availD = room.MaxZ - spec.Margin - nearZ;
        int zones = (int)ItemZone.Count;

        // Pieces per zone: the fire first in the helpful band, then the tables in bucket order.
        var bands = new List<Piece>[zones];
        for (int z = 0; z < zones; z++)
            bands[z] = new List<Piece>();
        var fire = new Piece { Width = 2f * spec.FireHalf, Depth = 2f * spec.FireHalf };
        bands[(int)ItemZone.Helpful].Add(fire);
        int total = 0;
        for (int b = 0; b < counts.Count; b++)
        {
            int count = Math.Max(0, counts[b]);
            total += count;
            for (int done = 0; done < count; )
            {
                int take = Math.Min(2 * perRow, count - done);
                int rows = take > perRow ? 2 : 1;
                bands[(int)ZoneOf(b)]
                    .Add(
                        new Piece
                        {
                            Bucket = b,
                            Take = take,
                            Rows = rows,
                            Width = Math.Min(take, perRow) * spec.Spacing,
                            Depth = (rows - 1) * spec.RowGap + 2f * spec.EdgePad,
                        }
                    );
                done += take;
            }
        }

        // Each band: as many columns as fit across, the pieces dealt evenly into them; bands stacked away from the player.
        int maxCols = Math.Max(1, (int)MathF.Floor((availW + spec.Aisle) / (colW + spec.Aisle)));
        float z0 = 0f,
            widest = 0f;
        int previous = -1;
        for (int zone = 0; zone < zones; zone++)
        {
            var band = bands[zone];
            if (band.Count == 0 || (band.Count == 1 && band[0] == fire && total == 0))
                continue;
            if (previous >= 0)
                z0 += zone == (int)ItemZone.Volatile ? clearance : spec.ZoneGap;
            previous = zone;
            int cols = Math.Min(maxCols, band.Count);
            int perCol = (band.Count + cols - 1) / cols;
            cols = (band.Count + perCol - 1) / perCol;
            float bandW = cols * colW + (cols - 1) * spec.Aisle;
            widest = Math.Max(widest, bandW);
            float depth = 0f;
            for (int c = 0; c < cols; c++)
            {
                float x = room.CenterX - bandW * 0.5f + c * (colW + spec.Aisle) + colW * 0.5f;
                float z = z0;
                for (int k = c * perCol; k < Math.Min(band.Count, (c + 1) * perCol); k++)
                {
                    band[k].X = x;
                    band[k].Z = z;
                    z += band[k].Depth + spec.Aisle;
                }
                depth = Math.Max(depth, z - spec.Aisle - z0);
            }
            z0 += depth;
        }
        float used = z0 + (wall ? spec.WallRoom + spec.WallDepth : 0f);
        // Centred in what the room offers past the walkway.
        float shift = nearZ + Math.Max(0f, (availD - used) * 0.5f);

        var slots = new ShelfSlot[total];
        var tables = new List<ShelfTable>();
        var zoneRects = new RoomRect[zones];
        var span = new bool[zones];
        int index = 0;
        float minX = float.MaxValue,
            maxX = float.MinValue;
        for (int b = 0; b < counts.Count; b++)
            foreach (var piece in bands[(int)ZoneOf(b)])
            {
                if (piece.Bucket != b)
                    continue;
                float front = shift + piece.Z;
                var rect = new RoomRect(
                    piece.X - piece.Width * 0.5f,
                    piece.X + piece.Width * 0.5f,
                    front,
                    front + piece.Depth
                );
                int widestRow = Math.Min(piece.Take, perRow);
                tables.Add(
                    new ShelfTable(
                        b,
                        piece.X,
                        front + spec.EdgePad,
                        piece.Rows,
                        (widestRow - 1) * spec.Spacing * 0.5f,
                        index,
                        piece.Take,
                        rect
                    )
                );
                for (int k = 0; k < piece.Take; k++)
                {
                    int row = k / perRow;
                    int inRow = Math.Min(perRow, piece.Take - row * perRow);
                    float x = piece.X + (k % perRow - (inRow - 1) * 0.5f) * spec.Spacing;
                    slots[index++] = new ShelfSlot(b, x, front + spec.EdgePad + row * spec.RowGap);
                }
                int zone = (int)ZoneOf(b);
                zoneRects[zone] = span[zone]
                    ? new RoomRect(
                        Math.Min(zoneRects[zone].MinX, rect.MinX),
                        Math.Max(zoneRects[zone].MaxX, rect.MaxX),
                        Math.Min(zoneRects[zone].MinZ, rect.MinZ),
                        Math.Max(zoneRects[zone].MaxZ, rect.MaxZ)
                    )
                    : rect;
                span[zone] = true;
                minX = Math.Min(minX, rect.MinX);
                maxX = Math.Max(maxX, rect.MaxX);
            }

        var fireRect = new RoomRect(
            fire.X - spec.FireHalf,
            fire.X + spec.FireHalf,
            shift + fire.Z,
            shift + fire.Z + fire.Depth
        );
        float last = shift + z0;
        var wallRect = new RoomRect(
            room.MinX + spec.Margin,
            room.MinX + spec.Margin + spec.WallWidth,
            last + spec.WallRoom,
            last + spec.WallRoom + spec.WallDepth
        );
        float startX = Math.Clamp(0f, room.MinX + spec.Margin, room.MaxX - spec.Margin);
        var start = new RoomRect(startX, startX, room.MinZ, room.MinZ);

        bool fits =
            widest <= availW + 1e-3f
            && used <= availD + 1e-3f
            && (!wall || spec.WallWidth <= availW + 1e-3f);
        if (span[(int)ItemZone.Volatile])
        {
            var v = zoneRects[(int)ItemZone.Volatile];
            fits &= v.Gap(start) >= clearance - 1e-3f && v.Gap(fireRect) >= clearance - 1e-3f;
            if (wall)
                fits &= v.Gap(wallRect) >= clearance - 1e-3f;
        }
        float over =
            availW <= 0f || availD <= 0f
                ? float.MaxValue
                : Math.Max(widest / availW, used / availD);
        return new ShelfPlan
        {
            Slots = slots,
            Tables = tables.ToArray(),
            Block =
                tables.Count > 0
                    ? new RoomRect(minX, maxX, shift, last)
                    : new RoomRect(room.CenterX, room.CenterX, nearZ, nearZ),
            Zones = zoneRects,
            StartX = startX,
            Fire = fireRect,
            HasWall = wall,
            Wall = wallRect,
            PerRow = perRow,
            Fits = fits,
            Over = over,
        };
    }
}
