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

    /// <summary>Components that act on whoever is near while the item just lies there, with what they do: a beehive
    /// releases its swarm at Start (Beehive.cs:26-55), a banana peel slips a player moving within 1 m
    /// (BananaPeel.cs:17-27). Dynamite's proximity wick comes as its ignite radius instead.</summary>
    static readonly KeyValuePair<string, string>[] GoesOffComponents =
    {
        new("Beehive", "releases a bee swarm"),
        new("BananaPeel", "slips you within 1 m"),
    };

    /// <summary>What the item does on its own, unused, or null when it waits to be used.</summary>
    public static string GoesOff(ICollection<string> components)
    {
        foreach (var pair in GoesOffComponents)
            if (components.Contains(pair.Key))
                return pair.Value;
        return null;
    }

    /// <summary>The item room area: <paramref name="igniteRadius"/> &gt; 0 (dynamite's <c>lightFuseRadius</c>) or
    /// anything it does on its own (<see cref="GoesOff"/>) goes off by itself, any <paramref name="harm"/> is
    /// dangerous, the rest helpful.</summary>
    public static ItemZone ZoneOf(string harm, float igniteRadius, string goesOff = null) =>
        igniteRadius > 0f || goesOff != null ? ItemZone.Volatile
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

    /// <summary>Which of <paramref name="names"/> to place: one per <paramref name="keys"/> entry (the game's display
    /// name with the zone), the shortest prefab name of each, the first on a tie. The database lists every chess colour
    /// and guidebook page as its own prefab.</summary>
    public static bool[] Distinct(IReadOnlyList<string> names, IReadOnlyList<string> keys)
    {
        var keep = new bool[names.Count];
        var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
        {
            string key = keys[i] ?? names[i] ?? "";
            if (best.TryGetValue(key, out int b))
            {
                if ((names[b] ?? "").Length <= (names[i] ?? "").Length)
                    continue;
                keep[b] = false;
            }
            best[key] = i;
            keep[i] = true;
        }
        return keep;
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

    /// <summary>Walking room between rows of tables, and past the end of each row.</summary>
    public float Aisle = 1.6f;

    /// <summary>Between tables side by side in a row: a different kind starts there.</summary>
    public float TableGap = 0.5f;

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

    /// <summary>Rows a table may hold. Two face a walkway each; more put inner rows a reach in from an edge.</summary>
    public int MaxRows = 2;

    public ShelfSpec Copy() => (ShelfSpec)MemberwiseClone();

    /// <summary>This spec squeezed one step toward the VR minimums (<see cref="ShelfLayout.Plan"/>), step 1 to 3;
    /// never looser than it was.</summary>
    public ShelfSpec Tighter(int step)
    {
        var s = Copy();
        // Walkways a person passes in, walls an arm's length off.
        s.Aisle = Math.Min(Aisle, 1f);
        s.Margin = Math.Min(Margin, 0.4f);
        s.FrontWalk = Math.Min(FrontWalk, 1.5f);
        if (step >= 2)
        {
            // Four rows, the inner ones 0.55 m in from an edge: within reach of either walkway.
            s.MaxRows = Math.Max(MaxRows, 4);
            s.RowGap = Math.Min(RowGap, 0.4f);
            s.EdgePad = Math.Min(EdgePad, 0.15f);
            s.TableGap = Math.Min(TableGap, 0.3f);
            s.Spacing = Math.Min(Spacing, 0.45f);
            s.MaxPerRow = Math.Max(MaxPerRow, 16);
        }
        if (step >= 3)
            s.ZoneGap = Math.Min(ZoneGap, 1.2f);
        return s;
    }
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

    /// <summary>The measures it was laid out with: the given ones, or tightened to fit (<see cref="ShelfSpec.Tighter"/>).</summary>
    public ShelfSpec Spec;

    /// <summary>Everything stands inside the room, clear of its walls, with every clearance kept.</summary>
    public bool Fits;

    /// <summary>The larger of width and depth used over what the room offers (1 = exactly full).</summary>
    public float Over;
}

/// <summary>
/// Lays the item room out in the middle of the measured floor: a band of tables per <see cref="ItemZone"/>, helpful
/// nearest the player, dangerous behind it, what goes off by itself farthest. A band puts its tables side by side in
/// rows across the room, a walkway between rows and past alternate ends, so one path passes every table; a table is at
/// most <see cref="ShelfSpec.MaxRows"/> deep so every item is in reach of a walkway; buckets never share a table. The cooking fire heads the helpful band, the test wall stands past the last band against the left side.
/// </summary>
public static class ShelfLayout
{
    /// <summary>Table buckets: one per zone and group.</summary>
    public const int Buckets = (int)ItemZone.Count * (int)ShelfGroup.Count;

    public static int Bucket(ItemZone zone, ShelfGroup group) =>
        (int)zone * (int)ShelfGroup.Count + (int)group;

    public static ItemZone ZoneOf(int bucket) => (ItemZone)(bucket / (int)ShelfGroup.Count);

    public static ShelfGroup GroupOf(int bucket) => (ShelfGroup)(bucket % (int)ShelfGroup.Count);

    /// <summary>The best plan for <paramref name="counts"/> (items per bucket) in <paramref name="room"/>, giving up
    /// comfort before content: <paramref name="spec"/> as it is, then walkways and wall margins down to the VR
    /// minimums, then fuller tables (up to four rows), then closer bands, all with the test wall and the full volatile
    /// clearance; only then without the wall, then without the clearance. Within a step, the table width that leaves
    /// the most room. Nothing fits: the plan that overflows least, with <see cref="ShelfPlan.Fits"/> false.</summary>
    public static ShelfPlan Plan(IReadOnlyList<int> counts, RoomRect room, ShelfSpec spec)
    {
        ShelfPlan fallback = null;
        var tightest = spec.Tighter(3);
        for (int step = 0; step <= 3; step++)
        {
            var pick = Best(
                counts,
                room,
                step == 0 ? spec : spec.Tighter(step),
                true,
                true,
                ref fallback
            );
            if (pick != null)
                return pick;
        }
        return Best(counts, room, tightest, false, true, ref fallback)
            ?? Best(counts, room, tightest, true, false, ref fallback)
            ?? Best(counts, room, tightest, false, false, ref fallback)
            ?? fallback;
    }

    static ShelfPlan Best(
        IReadOnlyList<int> counts,
        RoomRect room,
        ShelfSpec spec,
        bool wall,
        bool fullClearance,
        ref ShelfPlan fallback
    )
    {
        float clearance = fullClearance
            ? Math.Max(spec.ZoneGap, spec.VolatileClearance)
            : spec.ZoneGap;
        ShelfPlan pick = null;
        for (int p = spec.MinPerRow; p <= spec.MaxPerRow; p++)
        {
            var plan = Pack(counts, p, room, spec, wall, clearance);
            if (plan.Fits && (pick == null || plan.Over < pick.Over))
                pick = plan;
            if (fallback == null || plan.Over < fallback.Over)
                fallback = plan;
        }
        return pick;
    }

    sealed class Strip
    {
        public int Zone;
        public int First;
        public int End;
        public float Width;
        public float Depth;
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
            int maxRows = Math.Max(1, spec.MaxRows);
            for (int done = 0; done < count; )
            {
                int take = Math.Min(maxRows * perRow, count - done);
                int rows = (take + perRow - 1) / perRow;
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

        // Each band: its pieces side by side in strips across the room, a table gap apart (a walkway beside the
        // fire); strips a walkway apart, each leaving a walkway at alternate ends so the aisles join into one path.
        float stripMax = availW - spec.Aisle;
        var strips = new List<Strip>();
        for (int zone = 0; zone < zones; zone++)
        {
            var band = bands[zone];
            if (band.Count == 0 || (band.Count == 1 && band[0] == fire && total == 0))
                continue;
            for (int k = 0; k < band.Count; )
            {
                var strip = new Strip
                {
                    Zone = zone,
                    First = k,
                    End = k + 1,
                };
                band[k].X = 0f;
                strip.Width = band[k].Width;
                strip.Depth = band[k].Depth;
                for (; strip.End < band.Count; strip.End++)
                {
                    var next = band[strip.End];
                    float gap = band[strip.End - 1] == fire ? spec.Aisle : spec.TableGap;
                    if (strip.Width + gap + next.Width > stripMax + 1e-3f)
                        break;
                    next.X = strip.Width + gap;
                    strip.Width += gap + next.Width;
                    strip.Depth = Math.Max(strip.Depth, next.Depth);
                }
                strips.Add(strip);
                k = strip.End;
            }
        }
        float widest = 0f;
        foreach (var strip in strips)
            widest = Math.Max(widest, strip.Width);
        if (strips.Count > 0)
            widest += spec.Aisle;
        float blockLeft = room.CenterX - widest * 0.5f;
        float z0 = 0f;
        for (int i = 0; i < strips.Count; i++)
        {
            var strip = strips[i];
            if (i > 0)
                z0 +=
                    strip.Zone == strips[i - 1].Zone ? spec.Aisle
                    : strip.Zone == (int)ItemZone.Volatile ? clearance
                    : spec.ZoneGap;
            float left = i % 2 == 0 ? blockLeft : blockLeft + widest - strip.Width;
            var band = bands[strip.Zone];
            for (int k = strip.First; k < strip.End; k++)
            {
                band[k].X = left + band[k].X + band[k].Width * 0.5f;
                band[k].Z = z0;
            }
            z0 += strip.Depth;
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
        // Not Math.Clamp: it throws when the floor is narrower than two margins (the spawn elevator).
        float startX = Math.Min(Math.Max(0f, room.MinX + spec.Margin), room.MaxX - spec.Margin);
        if (!(startX >= room.MinX && startX <= room.MaxX))
            startX = room.CenterX;
        var start = new RoomRect(startX, startX, room.MinZ, room.MinZ);

        bool fits =
            Finite(room)
            && widest <= availW + 1e-3f
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
            Spec = spec,
            Fits = fits,
            Over = over,
        };
    }

    static bool Finite(RoomRect r) =>
        float.IsFinite(r.MinX)
        && float.IsFinite(r.MaxX)
        && float.IsFinite(r.MinZ)
        && float.IsFinite(r.MaxZ);
}

/// <summary>Whether a table's items should be out: spawned within the load radius of the player, removed past the
/// unload radius, unchanged between, so walking along the edge does not spawn and remove them in turn.</summary>
public static class ShelfLoad
{
    public static bool Wanted(bool loaded, float distance, float load, float unload) =>
        loaded ? distance <= unload : distance <= load;
}
