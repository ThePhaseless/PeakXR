using System;
using System.Collections.Generic;
using System.Numerics;

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
    /// <summary>Clear floor between two items' footprints on a table, across and between rows.</summary>
    public float ItemGap = 0.15f;

    /// <summary>Table top left clear around its items, on every side.</summary>
    public float EdgePad = 0.08f;

    /// <summary>A table's smallest top, both ways: one small item still stands on a table, not a plank.</summary>
    public float MinTable = 0.6f;

    /// <summary>The footprint an item counts as at least, both ways (an item without a drawn mesh counts as this).</summary>
    public float MinItem = 0.1f;

    /// <summary>Deepest table, reached into from the walkways on both sides; an item deeper than this gets a table of
    /// its own depth. Each area tries depths from <see cref="MinTable"/> up to this, <see cref="DepthStep"/> apart, and
    /// keeps the one that leaves it shallowest.</summary>
    public float TableDepth = 1.4f;
    public float DepthStep = 0.2f;

    /// <summary>An item's near side at most this far in from a table edge: a lean over the table reaches it.</summary>
    public float Reach = 0.7f;

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

    public ShelfSpec Copy() => (ShelfSpec)MemberwiseClone();

    /// <summary>This spec squeezed one step toward the VR minimums (<see cref="ShelfLayout.Plan"/>), step 1 to 4;
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
            // Deeper tables, the middle still within reach of either walkway; items still a hand's width apart.
            s.TableDepth = Math.Max(TableDepth, 1.8f);
            s.ItemGap = Math.Min(ItemGap, 0.1f);
            s.EdgePad = Math.Min(EdgePad, 0.06f);
            s.TableGap = Math.Min(TableGap, 0.3f);
        }
        if (step >= 3)
            s.ZoneGap = Math.Min(ZoneGap, 1.2f);
        if (step >= 4)
        {
            // The last squeeze, for the game's real item sizes in the airport's fixed place: walkways at the minimum
            // everywhere, tables as deep as a reach from both sides, walls a step off.
            s.ZoneGap = Math.Min(ZoneGap, s.Aisle);
            s.FrontWalk = Math.Min(FrontWalk, 1f);
            s.Margin = Math.Min(Margin, 0.2f);
            s.TableDepth = Math.Max(TableDepth, 2.4f);
        }
        return s;
    }
}

/// <summary>Item <see cref="Item"/>'s spot (its index in the footprints given to <see cref="ShelfLayout.Plan"/>): the
/// middle of its footprint, <see cref="X"/> across the room and <see cref="Z"/> away from the player, in meters.
/// <see cref="Turned"/>: it lies a quarter turn round (about up), so its footprint's long side runs across the row.</summary>
public readonly struct ShelfSlot
{
    public readonly int Group;
    public readonly int Item;
    public readonly bool Turned;
    public readonly float X;
    public readonly float Z;

    public ShelfSlot(int group, int item, bool turned, float x, float z)
    {
        Group = group;
        Item = item;
        Turned = turned;
        X = x;
        Z = z;
    }
}

/// <summary>A table of one bucket (<see cref="ShelfLayout.Bucket"/>): its front edge at <see cref="Z"/>, centred on
/// <see cref="X"/>, <see cref="Rows"/> deep, holding slots <see cref="First"/> to <see cref="First"/> +
/// <see cref="Count"/> - 1 on <see cref="Footprint"/>.</summary>
public readonly struct ShelfTable
{
    public readonly int Group;
    public readonly ItemZone Zone;
    public readonly float X;
    public readonly float Z;
    public readonly int Rows;
    public readonly int First;
    public readonly int Count;
    public readonly RoomRect Footprint;

    public ShelfTable(
        int group,
        float x,
        float z,
        int rows,
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
/// rows across the room, a walkway between rows and past alternate ends, so one path passes every table. Every table
/// in a band is filled to the band's depth (at most <see cref="ShelfSpec.TableDepth"/>, so every item is in reach of a
/// walkway) and is as narrow as its items allow at that depth: each item gets its own footprint and a gap, deepest
/// first, so rows of tables come out even and little floor is lost. Buckets never share a table. The cooking fire
/// heads the helpful band, the test wall stands past the last band against the left side.
/// </summary>
public static class ShelfLayout
{
    /// <summary>Table buckets: one per zone and group.</summary>
    public const int Buckets = (int)ItemZone.Count * (int)ShelfGroup.Count;

    public static int Bucket(ItemZone zone, ShelfGroup group) =>
        (int)zone * (int)ShelfGroup.Count + (int)group;

    public static ItemZone ZoneOf(int bucket) => (ItemZone)(bucket / (int)ShelfGroup.Count);

    public static ShelfGroup GroupOf(int bucket) => (ShelfGroup)(bucket % (int)ShelfGroup.Count);

    /// <summary>The best plan for <paramref name="counts"/> (items per bucket) of the footprints
    /// <paramref name="sizes"/> (X across, Y along; one per item in bucket order, which each slot's
    /// <see cref="ShelfSlot.Item"/> indexes) in <paramref name="room"/>, giving up comfort before content:
    /// <paramref name="spec"/> as it is, then walkways and wall margins down to the VR minimums, then deeper tables and
    /// smaller gaps, then closer bands, all with the test wall and the full volatile clearance; only then without the
    /// wall, then without the clearance. Nothing fits: the plan that overflows least, with <see cref="ShelfPlan.Fits"/>
    /// false.</summary>
    public static ShelfPlan Plan(
        IReadOnlyList<int> counts,
        IReadOnlyList<Vector2> sizes,
        RoomRect room,
        ShelfSpec spec
    )
    {
        int total = 0;
        foreach (var c in counts)
            total += Math.Max(0, c);
        if (sizes.Count != total)
            throw new ArgumentException(
                $"{sizes.Count} footprints for {total} items",
                nameof(sizes)
            );
        // A footprint the item's meshes could not give counts as the smallest item.
        // Every item laid with its long side across the row: a 2.5 m launcher pointing into the table made its row,
        // and its table, 2.5 m deep.
        var given = new Vector2[total];
        var turned = new bool[total];
        var order = new int[total];
        for (int i = 0; i < total; i++)
        {
            float across = Least(sizes[i].X, spec.MinItem);
            float along = Least(sizes[i].Y, spec.MinItem);
            turned[i] = along > across;
            given[i] = turned[i] ? new Vector2(along, across) : new Vector2(across, along);
            order[i] = i;
        }
        // Each kind deepest first, so a row's items are about as deep as each other (a row is as deep as its deepest).
        var deepest = Comparer<int>.Create(
            (a, b) =>
                given[a].Y != given[b].Y ? given[b].Y.CompareTo(given[a].Y)
                : given[a].X != given[b].X ? given[b].X.CompareTo(given[a].X)
                : a.CompareTo(b)
        );
        for (int b = 0, at = 0; b < counts.Count; b++)
        {
            int count = Math.Max(0, counts[b]);
            Array.Sort(order, at, count, deepest);
            at += count;
        }
        var feet = new Vector2[total];
        for (int k = 0; k < total; k++)
            feet[k] = given[order[k]];

        ShelfPlan fallback = null;
        var tightest = spec.Tighter(4);
        for (int step = 0; step <= 4; step++)
        {
            var pick = Best(
                counts,
                feet,
                order,
                turned,
                room,
                step == 0 ? spec : spec.Tighter(step),
                true,
                true,
                ref fallback
            );
            if (pick != null)
                return pick;
        }
        return Best(counts, feet, order, turned, room, tightest, false, true, ref fallback)
            ?? Best(counts, feet, order, turned, room, tightest, true, false, ref fallback)
            ?? Best(counts, feet, order, turned, room, tightest, false, false, ref fallback)
            ?? fallback;
    }

    static float Least(float size, float least) =>
        float.IsFinite(size) ? Math.Max(least, size) : least;

    static ShelfPlan Best(
        IReadOnlyList<int> counts,
        Vector2[] feet,
        int[] order,
        bool[] turned,
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
        var plan = Pack(counts, feet, order, turned, room, spec, wall, clearance);
        if (fallback == null || plan.Over < fallback.Over)
            fallback = plan;
        return plan.Fits ? plan : null;
    }

    sealed class Strip
    {
        public int Zone;
        public readonly List<Piece> Pieces = new();
        public float Width;
        public float Depth;
    }

    sealed class Piece
    {
        public int Bucket = -1;
        public int First;
        public int Take;
        public int Rows;
        public float Width;
        public float Depth;

        /// <summary>The row length it was filled to, and its rows front to back with the gaps between them.</summary>
        public float Length;
        public float Rowed;
        public float X;
        public float Z;
    }

    /// <summary>One band's strips with every table filled to <paramref name="depth"/>: its pieces side by side across
    /// the room a table gap apart (a walkway beside the fire), the fire first in the helpful band, then the tables in
    /// bucket order, a bucket going on in the next strip when the rest of this one does not hold it. Returns how deep
    /// the band is, its strips and the walkways between them.</summary>
    static float Band(
        IReadOnlyList<int> counts,
        Vector2[] feet,
        int zone,
        int lastZone,
        int at,
        float depth,
        float stripMax,
        ShelfSpec spec,
        Piece fire,
        List<Strip> strips,
        List<Piece> pieces
    )
    {
        var strip = new Strip { Zone = zone };
        void Put(Piece piece, float gap)
        {
            piece.X = strip.Pieces.Count == 0 ? 0f : strip.Width + gap;
            strip.Width = piece.X + piece.Width;
            strip.Depth = Math.Max(strip.Depth, piece.Depth);
            strip.Pieces.Add(piece);
        }
        if (fire != null)
            Put(fire, 0f);
        for (int b = 0; b < counts.Count; b++)
        {
            int count = Math.Max(0, counts[b]);
            int z = (int)ZoneOf(b);
            if (z < zone || z > lastZone)
                continue;
            for (int k = at, end = at + count; k < end; )
            {
                bool fresh = strip.Pieces.Count == 0;
                if (fresh)
                    strip.Zone = z;
                float gap =
                    fresh ? 0f
                    : strip.Pieces[^1] == fire ? spec.Aisle
                    : (int)ZoneOf(strip.Pieces[^1].Bucket) != z ? spec.ZoneGap
                    : spec.TableGap;
                var piece = Table(feet, k, end, depth, stripMax - strip.Width - gap, fresh, spec);
                // The rest of this strip holds none of them: on in a fresh strip, which always takes one.
                if (piece.Take == 0)
                {
                    strips.Add(strip);
                    strip = new Strip { Zone = zone };
                    continue;
                }
                piece.Bucket = b;
                Put(piece, gap);
                pieces.Add(piece);
                k += piece.Take;
            }
            at += count;
        }
        if (strip.Pieces.Count > 0)
            strips.Add(strip);
        float deep = 0f;
        for (int i = 0; i < strips.Count; i++)
            deep += strips[i].Depth + (i > 0 ? spec.Aisle : 0f);
        return deep;
    }

    static ShelfPlan Pack(
        IReadOnlyList<int> counts,
        Vector2[] feet,
        int[] order,
        bool[] turned,
        RoomRect room,
        ShelfSpec spec,
        bool wall,
        float clearance
    )
    {
        float availW = room.Width - 2f * spec.Margin;
        float nearZ = room.MinZ + spec.FrontWalk;
        float availD = room.MaxZ - spec.Margin - nearZ;
        float stripMax = availW - spec.Aisle;
        int zones = (int)ItemZone.Count;
        int total = 0;
        foreach (var c in counts)
            total += Math.Max(0, c);

        // Each band on its own, so each takes the table depth that keeps it shallowest: a band's last strip is often
        // part full. What goes off by itself closes the dangerous band's rows, a zone gap on, when it needs no more
        // clearance than that (its wick moved out of reach): its own band was a strip for one item.
        var strips = new List<Strip>();
        var pieces = new List<Piece>();
        Piece fire = null;
        float step = Math.Max(0.05f, spec.DepthStep);
        bool shared = clearance <= spec.ZoneGap + 1e-3f;
        for (int zone = 0, at = 0; zone < zones; zone++)
        {
            int lastZone =
                shared && zone == (int)ItemZone.Dangerous ? (int)ItemZone.Volatile : zone;
            int inZone = 0;
            for (int b = 0; b < counts.Count; b++)
                if ((int)ZoneOf(b) >= zone && (int)ZoneOf(b) <= lastZone)
                    inZone += Math.Max(0, counts[b]);
            bool hasFire = zone == (int)ItemZone.Helpful && total > 0;
            List<Strip> bestStrips = null;
            List<Piece> bestPieces = null;
            Piece bestFire = null;
            float bestDeep = float.MaxValue;
            for (float depth = spec.MinTable; ; depth += step)
            {
                var bandStrips = new List<Strip>();
                var bandPieces = new List<Piece>();
                var bandFire = hasFire
                    ? new Piece { Width = 2f * spec.FireHalf, Depth = 2f * spec.FireHalf }
                    : null;
                float deep = Band(
                    counts,
                    feet,
                    zone,
                    lastZone,
                    at,
                    depth,
                    stripMax,
                    spec,
                    bandFire,
                    bandStrips,
                    bandPieces
                );
                if (bestStrips == null || deep < bestDeep - 1e-4f)
                {
                    bestStrips = bandStrips;
                    bestPieces = bandPieces;
                    bestFire = bandFire;
                    bestDeep = deep;
                }
                if (inZone == 0 || !(depth + step <= spec.TableDepth + 1e-3f))
                    break;
            }
            strips.AddRange(bestStrips);
            pieces.AddRange(bestPieces);
            fire ??= bestFire;
            at += inZone;
            zone = lastZone;
        }
        fire ??= new Piece { Width = 2f * spec.FireHalf, Depth = 2f * spec.FireHalf };

        float widest = 0f;
        foreach (var s in strips)
            widest = Math.Max(widest, s.Width);
        if (strips.Count > 0)
            widest += spec.Aisle;
        float blockLeft = room.CenterX - widest * 0.5f;
        float z0 = 0f;
        for (int i = 0; i < strips.Count; i++)
        {
            var s = strips[i];
            if (i > 0)
                z0 +=
                    s.Zone == strips[i - 1].Zone ? spec.Aisle
                    : s.Zone == (int)ItemZone.Volatile ? clearance
                    : spec.ZoneGap;
            float left = i % 2 == 0 ? blockLeft : blockLeft + widest - s.Width;
            foreach (var piece in s.Pieces)
            {
                piece.X = left + piece.X + piece.Width * 0.5f;
                piece.Z = z0;
            }
            z0 += s.Depth;
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
        foreach (var piece in pieces)
        {
            int b = piece.Bucket;
            float front = shift + piece.Z;
            var rect = new RoomRect(
                piece.X - piece.Width * 0.5f,
                piece.X + piece.Width * 0.5f,
                front,
                front + piece.Depth
            );
            tables.Add(new ShelfTable(b, piece.X, front, piece.Rows, index, piece.Take, rect));
            // Rows centred on the top front to back, each row centred across it.
            float z = front + (piece.Depth - piece.Rowed) * 0.5f;
            for (int k = piece.First, end = piece.First + piece.Take; k < end; )
            {
                int next = NextRow(
                    feet,
                    k,
                    end,
                    piece.Length,
                    spec.ItemGap,
                    out float w,
                    out float d
                );
                float x = piece.X - w * 0.5f;
                for (; k < next; k++)
                {
                    slots[index++] = new ShelfSlot(
                        b,
                        order[k],
                        turned[order[k]],
                        x + feet[k].X * 0.5f,
                        // Against the edge of its row on the side it is reached from.
                        z - front
                        <= spec.Reach
                            ? z + feet[k].Y * 0.5f
                            : z + d - feet[k].Y * 0.5f
                    );
                    x += feet[k].X + spec.ItemGap;
                }
                z += d + spec.ItemGap;
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
            Spec = spec,
            Fits = fits,
            Over = over,
        };
    }

    /// <summary>A table of items <paramref name="from"/> on (before <paramref name="end"/>) at most
    /// <paramref name="depth"/> deep and <paramref name="width"/> wide: the narrowest that holds them all, else as many
    /// as that width holds (none when not even the first fits; at least one row always, however deep).</summary>
    static Piece Table(
        Vector2[] feet,
        int from,
        int end,
        float depth,
        float width,
        bool fresh,
        ShelfSpec spec
    )
    {
        float inner = width - 2f * spec.EdgePad;
        var piece = new Piece { First = from };
        // A part-filled strip takes the next item only when it fits; a fresh one takes it whatever its width.
        if (!fresh && !(width >= spec.MinTable - 1e-4f && inner >= feet[from].X - 1e-4f))
            return piece;
        float lo = feet[from].X;
        float hi = Math.Max(lo, inner);
        int all = end - from;
        if (Fill(feet, from, end, hi, depth, spec, piece) == all)
        {
            // The narrowest row length that still holds all of them (fewer, longer rows hold more).
            for (int i = 0; i < 12 && hi - lo > 0.01f; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Fill(feet, from, end, mid, depth, spec, piece) == all)
                    hi = mid;
                else
                    lo = mid;
            }
        }
        Fill(feet, from, end, hi, depth, spec, piece);
        piece.Width = Math.Max(spec.MinTable, piece.Width + 2f * spec.EdgePad);
        piece.Depth = Math.Max(spec.MinTable, piece.Rowed + 2f * spec.EdgePad);
        return piece;
    }

    /// <summary>Fills <paramref name="piece"/> with rows of <paramref name="length"/> from item
    /// <paramref name="from"/> while they stay within <paramref name="depth"/> (the first row always): its take, rows,
    /// widest row (in <see cref="Piece.Width"/>, before the pads) and rowed depth. Returns the take.</summary>
    static int Fill(
        Vector2[] feet,
        int from,
        int end,
        float length,
        float depth,
        ShelfSpec spec,
        Piece piece
    )
    {
        piece.Length = length;
        piece.Rows = 0;
        piece.Rowed = 0f;
        piece.Width = 0f;
        // The far side of the first row starting past a reach from the front: the back edge must stay within a reach
        // of it, so that row (and every one behind it) is reached from the back walkway.
        float deepEnd = float.NaN;
        int k = from;
        while (k < end)
        {
            int next = NextRow(feet, k, end, length, spec.ItemGap, out float w, out float d);
            float start = spec.EdgePad + piece.Rowed + (piece.Rows > 0 ? spec.ItemGap : 0f);
            float rowed = start + d - spec.EdgePad;
            float back = rowed + 2f * spec.EdgePad;
            float firstDeep = float.IsNaN(deepEnd) && start > spec.Reach ? start + d : deepEnd;
            if (piece.Rows > 0 && (back > depth + 1e-4f || back - firstDeep > spec.Reach + 1e-4f))
                break;
            deepEnd = firstDeep;
            piece.Rowed = rowed;
            piece.Width = Math.Max(piece.Width, w);
            piece.Rows++;
            k = next;
        }
        piece.Take = k - from;
        return piece.Take;
    }

    /// <summary>The row starting at item <paramref name="from"/>: items side by side a gap apart until the next
    /// would pass <paramref name="length"/> (always at least one), before <paramref name="end"/>. Returns where the
    /// next row starts, with this row's <paramref name="width"/> and its deepest item's <paramref name="depth"/>.</summary>
    static int NextRow(
        Vector2[] feet,
        int from,
        int end,
        float length,
        float gap,
        out float width,
        out float depth
    )
    {
        width = feet[from].X;
        depth = feet[from].Y;
        int k = from + 1;
        for (; k < end && width + gap + feet[k].X <= length + 1e-4f; k++)
        {
            width += gap + feet[k].X;
            depth = Math.Max(depth, feet[k].Y);
        }
        return k;
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

/// <summary>Where the item room stands: its fixed place in the solo airport, or the open floor measured around you.</summary>
public enum RoomPlace : byte
{
    Fixed,
    Measured,
}

/// <summary>
/// The item room's fixed place in the solo airport hall, so it stands in the same known-good spot every time instead
/// of wherever the player happens to measure from. The frame faces world +z, the hall's long axis (the elevator doors
/// and every queue fence run along world x and z); X runs to world +x.
/// <para>Origin: world x -10.0, z 56.75, the start line 3.85 m out from the spawn elevators' doors (Elevator (1-3)/Door,
/// z 52.90) and just past the queue fence posts in front of them (frame x -2.4, 3.4, 9.0, up to world z 57.6). Floor
/// y 0.51: Map/BL_Airport/Main Meshes and the elevator floors (0.55).</para>
/// <para>Bounds: the largest rectangle clear of every active Default/Map/Terrain collider between 0.15 m and 2.2 m above
/// that floor, with every obstacle grown by 0.5 m, from a 0.25 m occupancy map of the Airport scene (level3, game build
/// of 2026-10, UnityPy): frame x -4.0..9.0, z 1.5..16.75 (world x -14..-1, z 58.25..73.5). Left of it the seating
/// rows (world x &lt; -14.5), right the railings (world x &gt; -0.4), ahead the kiosk row and the climbing-wall mats
/// (world z &gt; 74). Below z 1.5 only the start walkway, where no table stands (<see cref="ShelfSpec.FrontWalk"/>).
/// It agrees with the open floor measured in game (VR run of dc78fa3: hall 8.3 x 20.6 m along a heading 15 degrees
/// off +z, 4 m out from the doors).</para>
/// </summary>
public static class AirportRoom
{
    public const float OriginX = -10f;
    public const float OriginZ = 56.75f;
    public const float FloorY = 0.51f;

    public static readonly RoomRect Bounds = new(-4f, 9f, 0f, 16.75f);

    /// <summary>The fixed place in the airport when its plan fits and the level check found nothing in the way
    /// (<paramref name="blocked"/> tables, the fire or the wall meeting level geometry): a game update may have moved
    /// the lobby, and then the measured layout takes over.</summary>
    public static RoomPlace Choose(bool airport, bool fits, int blocked) =>
        airport && fits && blocked == 0 ? RoomPlace.Fixed : RoomPlace.Measured;
}
