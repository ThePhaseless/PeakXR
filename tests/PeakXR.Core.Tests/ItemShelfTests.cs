using System.Collections.Generic;
using System.Linq;

namespace PeakXR.Core.Tests;

public class ItemShelfTests
{
    static ShelfGroup Group(string name, params string[] components) =>
        ItemShelf.GroupOf(name, new HashSet<string>(components));

    static string Skip(string name, params string[] components) =>
        ItemShelf.SkipReason(name, new HashSet<string>(components));

    [Theory]
    [InlineData("Torch", "ItemTorch")]
    [InlineData("Lantern", "Lantern")]
    [InlineData("FireWood", "FireWoodItem")]
    public void LightsAndFuelGoToFire(string name, string component) =>
        Assert.Equal(ShelfGroup.Fire, Group(name, component));

    [Fact]
    public void ClimbingBeatsEverythingElse() =>
        // A rope shooter is also launched: it still belongs beside the wall.
        Assert.Equal(ShelfGroup.Climbing, Group("RopeShooter", "RopeShooter", "Breakable"));

    [Fact]
    public void ChalkIsFoundByName() =>
        Assert.Equal(ShelfGroup.Climbing, Group("ClimbingChalk", "Action_ModifyStatus"));

    [Fact]
    public void FoodBeatsBreakable() =>
        // An egg breaks when dropped but is eaten first.
        Assert.Equal(ShelfGroup.Food, Group("Egg", "BreakableEgg", "Action_RestoreHunger"));

    [Fact]
    public void BreakablesAndThrowablesGoToThrowing()
    {
        Assert.Equal(ShelfGroup.Throwing, Group("Coconut", "Breakable"));
        Assert.Equal(ShelfGroup.Throwing, Group("Frisbee", "Frisbee"));
        Assert.Equal(ShelfGroup.Throwing, Group("Dynamite", "Dynamite"));
    }

    [Fact]
    public void MushroomsAreFoodByName() =>
        Assert.Equal(ShelfGroup.Food, Group("Mushroom Glow", "Action_ApplyAffliction"));

    [Fact]
    public void StatusItemsWithoutFoodAreMedicine() =>
        Assert.Equal(ShelfGroup.Medicine, Group("Bandages", "Action_ModifyStatus"));

    [Fact]
    public void MovementAndTools()
    {
        Assert.Equal(ShelfGroup.Movement, Group("Balloon", "Balloon"));
        Assert.Equal(ShelfGroup.Tools, Group("Binoculars", "Actions_Binoculars"));
        Assert.Equal(ShelfGroup.Tools, Group("PortableStovetopItem", "Constructable"));
    }

    [Fact]
    public void BackpackIsAContainer() =>
        Assert.Equal(ShelfGroup.Containers, Group("Backpack", "Backpack"));

    [Fact]
    public void UnknownGoesToOther() => Assert.Equal(ShelfGroup.Other, Group("C_King", "Item"));

    [Theory]
    [InlineData("ShelfShroomPreview")]
    [InlineData("Rope_Prop")]
    [InlineData("BaseConstructable")]
    [InlineData("TestConstructable")]
    public void NonItemsAreSkipped(string name) => Assert.NotNull(Skip(name));

    [Fact]
    public void CreaturesAreSkipped() => Assert.NotNull(Skip("Beetle", "MobItem", "Mob"));

    [Fact]
    public void PlainItemsAreKept() => Assert.Null(Skip("Apple", "Action_RestoreHunger"));

    static string Harm(
        string[] components,
        StatusChange[] statuses = null,
        params string[] afflictions
    ) =>
        ItemShelf.HarmOf(
            new HashSet<string>(components),
            statuses ?? System.Array.Empty<StatusChange>(),
            new HashSet<string>(afflictions)
        );

    [Theory]
    [InlineData("Action_Die")]
    [InlineData("Action_BecomeSkeleton")]
    [InlineData("Action_WarpRandomly")]
    [InlineData("Action_WarpToBiome")]
    [InlineData("Action_WarpToRandomPlayer")]
    [InlineData("Dynamite")]
    [InlineData("KnockOutPlayerOnImpact")]
    [InlineData("Action_InflictPoison")]
    [InlineData("CactusBall")]
    [InlineData("Action_RandomMushroomEffect")]
    [InlineData("Action_CallScoutmaster")]
    public void KillWarpPoisonExplodeComponentsAreDangerous(string component) =>
        Assert.NotNull(Harm(new[] { "Item", component }));

    [Fact]
    public void AStatusAddedIsHarmAndOneTakenAwayIsHelp()
    {
        Assert.Contains(
            "Poison",
            Harm(new[] { "Action_ModifyStatus" }, new[] { new StatusChange("Poison", 0.3f) })
        );
        Assert.Null(
            Harm(new[] { "Action_ModifyStatus" }, new[] { new StatusChange("Injury", -0.4f) })
        );
    }

    [Theory]
    [InlineData("PoisonOverTime")]
    [InlineData("Exhausted")]
    [InlineData("Chaos")]
    [InlineData("PreventPoisonHealing")]
    [InlineData("ZombieBite")]
    [InlineData("Blind")]
    public void HarmfulAfflictionsAreDangerous(string affliction) =>
        Assert.NotNull(Harm(new[] { "Action_ApplyAffliction" }, null, affliction));

    [Theory]
    [InlineData("FasterBoi")]
    [InlineData("InfiniteStamina")]
    [InlineData("ClearAllStatus")]
    [InlineData("HealAll")]
    [InlineData("Glowing")]
    public void HelpfulAfflictionsAreNot(string affliction) =>
        Assert.Null(Harm(new[] { "Action_ApplyAffliction" }, null, affliction));

    [Fact]
    public void FoodToolsAndClimbingGearAreHelpful()
    {
        Assert.Null(Harm(new[] { "Action_RestoreHunger" }));
        Assert.Null(Harm(new[] { "Actions_Binoculars" }));
        Assert.Null(Harm(new[] { "RopeSpool" }));
        Assert.Null(Harm(new[] { "Backpack" }));
    }

    [Fact]
    public void ZoneOfItems()
    {
        Assert.Equal(ItemZone.Helpful, ItemShelf.ZoneOf(null, 0f));
        Assert.Equal(ItemZone.Dangerous, ItemShelf.ZoneOf("kills", 0f));
        // Goes off on its own (dynamite lights when you come near the spot it spawned on): kept apart.
        Assert.Equal(ItemZone.Volatile, ItemShelf.ZoneOf("explodes", 4f));
        Assert.Equal(ItemZone.Volatile, ItemShelf.ZoneOf(null, 0f, "releases a bee swarm"));
    }

    [Theory]
    [InlineData("Beehive")]
    [InlineData("BananaPeel")]
    public void ItemsThatActWithoutBeingUsedGoOffByThemselves(string component)
    {
        // VR run of 3a1b2f9: a beehive on its table let out a swarm that chased and poisoned the player.
        var reason = ItemShelf.GoesOff(new HashSet<string> { "Item", component });
        Assert.NotNull(reason);
        Assert.Equal(ItemZone.Volatile, ItemShelf.ZoneOf(null, 0f, reason));
    }

    [Fact]
    public void ItemsThatOnlyActWhenUsedDoNotGoOff()
    {
        Assert.Null(ItemShelf.GoesOff(new HashSet<string> { "Item", "Action_Die", "WarpOnThrow" }));
    }

    [Fact]
    public void OneOfEachItemKeepingTheShortestName()
    {
        // The database holds every chess colour and guidebook page as its own prefab under one display name.
        var names = new[]
        {
            "C_King B",
            "C_King",
            "C_King W",
            "Apple Berry Red",
            "GuidebookPage_1",
            "GuidebookPage",
        };
        var keys = new[] { "King", "King", "King", "Red Crispberry", "Page", "Page" };

        var keep = ItemShelf.Distinct(names, keys);

        Assert.Equal(new[] { false, true, false, true, false, true }, keep);
    }

    [Fact]
    public void DistinctKeysStayApart()
    {
        // Same display name, different effect (the poison mushroom looks like the safe one): separate keys, both kept.
        var keep = ItemShelf.Distinct(
            new[] { "Mushroom Normie", "Mushroom Normie Poison" },
            new[] { "Button Shroom|Helpful", "Button Shroom|Dangerous" }
        );
        Assert.Equal(new[] { true, true }, keep);
    }

    [Theory]
    // Unloaded: loads once you come within the load radius.
    [InlineData(false, 7f, false)]
    [InlineData(false, 5f, true)]
    // Loaded: stays until you are past the unload radius, so walking the edge does not churn spawns.
    [InlineData(true, 7f, true)]
    [InlineData(true, 11f, false)]
    public void TablesLoadNearAndUnloadFar(bool loaded, float distance, bool expected) =>
        Assert.Equal(expected, ShelfLoad.Wanted(loaded, distance, 6f, 10f));
}

public class ShelfLayoutTests
{
    static readonly ShelfSpec Spec = new();

    static int[] Counts(params (ItemZone zone, ShelfGroup group, int count)[] entries)
    {
        var counts = new int[ShelfLayout.Buckets];
        foreach (var (zone, group, count) in entries)
            counts[ShelfLayout.Bucket(zone, group)] = count;
        return counts;
    }

    // Roughly the game's database: about 120 items, most helpful, a few dangerous, one that goes off by itself.
    static readonly int[] Typical = Counts(
        (ItemZone.Helpful, ShelfGroup.Fire, 6),
        (ItemZone.Helpful, ShelfGroup.Food, 30),
        (ItemZone.Helpful, ShelfGroup.Medicine, 10),
        (ItemZone.Helpful, ShelfGroup.Climbing, 10),
        (ItemZone.Helpful, ShelfGroup.Throwing, 6),
        (ItemZone.Helpful, ShelfGroup.Movement, 8),
        (ItemZone.Helpful, ShelfGroup.Tools, 10),
        (ItemZone.Helpful, ShelfGroup.Mystic, 6),
        (ItemZone.Helpful, ShelfGroup.Containers, 2),
        (ItemZone.Dangerous, ShelfGroup.Food, 12),
        (ItemZone.Dangerous, ShelfGroup.Throwing, 3),
        (ItemZone.Dangerous, ShelfGroup.Movement, 3),
        (ItemZone.Dangerous, ShelfGroup.Mystic, 4),
        (ItemZone.Volatile, ShelfGroup.Throwing, 1)
    );

    // Footprints (across, along) like the game's items: berries to backpacks and long tools, in a fixed cycle.
    static readonly Vector2[] Shapes =
    {
        new(0.12f, 0.12f),
        new(0.25f, 0.15f),
        new(0.2f, 0.2f),
        new(0.35f, 0.3f),
        new(0.15f, 0.45f),
        new(0.5f, 0.35f),
        new(0.1f, 0.1f),
        new(0.3f, 0.2f),
    };

    internal static Vector2[] Sizes(int[] counts)
    {
        var sizes = new Vector2[counts.Sum()];
        for (int i = 0; i < sizes.Length; i++)
            sizes[i] = Shapes[i % Shapes.Length];
        return sizes;
    }

    internal static ShelfPlan Plan(int[] counts, RoomRect room, ShelfSpec spec) =>
        ShelfLayout.Plan(counts, Sizes(counts), room, spec);

    /// <summary>The floor slot <paramref name="k"/>'s item covers where the plan put it.</summary>
    internal static RoomRect Spot(ShelfPlan plan, IReadOnlyList<Vector2> sizes, int k)
    {
        var s = plan.Slots[k];
        var size = s.Turned ? new Vector2(sizes[s.Item].Y, sizes[s.Item].X) : sizes[s.Item];
        return new(
            s.X - size.X * 0.5f,
            s.X + size.X * 0.5f,
            s.Z - size.Y * 0.5f,
            s.Z + size.Y * 0.5f
        );
    }

    [Fact]
    public void ALongItemLiesAcrossItsRowNotIntoTheTable()
    {
        // A launcher 2.46 m long pointing away from the walkway made its row (and its table) 2.46 m deep.
        var counts = Counts((ItemZone.Helpful, ShelfGroup.Throwing, 2));
        var sizes = new[] { new Vector2(0.49f, 2.46f), new Vector2(0.4f, 0.3f) };
        var plan = ShelfLayout.Plan(counts, sizes, Hall, Spec);

        var launcher = plan.Slots.Single(s => s.Item == 0);
        Assert.True(launcher.Turned);
        Assert.False(plan.Slots.Single(s => s.Item == 1).Turned);
        Assert.True(plan.Tables[0].Footprint.Depth < 1f, $"{plan.Tables[0].Footprint}");
        AssertDisplayed(plan, sizes, Spec.ItemGap);
    }

    [Fact]
    public void EveryItemHasExactlyOneSpotOnATableOfItsKind()
    {
        var plan = Plan(Game, Hall, Spec);
        var bucket = Game.SelectMany((count, b) => Enumerable.Repeat(b, count)).ToArray();
        var seen = new bool[bucket.Length];
        foreach (var s in plan.Slots)
        {
            Assert.False(seen[s.Item], $"item {s.Item} placed twice");
            seen[s.Item] = true;
            Assert.Equal(bucket[s.Item], s.Group);
        }
        Assert.All(seen, Assert.True);
    }

    /// <summary>Every two items on a table at least <paramref name="gap"/> apart, footprint to footprint, and each
    /// item an edge margin in from every side of its table.</summary>
    internal static void AssertDisplayed(ShelfPlan plan, IReadOnlyList<Vector2> sizes, float gap)
    {
        foreach (var t in plan.Tables)
            for (int i = t.First; i < t.First + t.Count; i++)
            {
                var a = Spot(plan, sizes, i);
                var f = t.Footprint;
                float edge = plan.Spec.EdgePad - 1e-3f;
                Assert.True(
                    a.MinX - f.MinX >= edge
                        && f.MaxX - a.MaxX >= edge
                        && a.MinZ - f.MinZ >= edge
                        && f.MaxZ - a.MaxZ >= edge,
                    $"item {i} ({a}) off the edge of its table ({f})"
                );
                for (int j = i + 1; j < t.First + t.Count; j++)
                    Assert.True(
                        Gap(a, Spot(plan, sizes, j)) >= gap - 1e-3f,
                        $"items {i} ({a}) and {j} ({Spot(plan, sizes, j)}) closer than {gap}"
                    );
            }
    }

    [Fact]
    public void ItemsOnATableStandTheirOwnFootprintPlusTheGapApartWellOnTheTable()
    {
        var sizes = Sizes(Game);
        var plan = ShelfLayout.Plan(Game, sizes, Hall, Spec);

        Assert.True(plan.Fits);
        AssertDisplayed(plan, sizes, Spec.ItemGap);
    }

    [Fact]
    public void EvenTheTightestLayoutKeepsTenCentimetresBetweenItems()
    {
        var sizes = Sizes(Game);
        var plan = ShelfLayout.Plan(Game, sizes, new RoomRect(-6f, 6f, 0f, 12f), Spec);

        Assert.True(plan.Fits);
        Assert.True(plan.Spec.ItemGap >= 0.1f - 1e-3f);
        AssertDisplayed(plan, sizes, 0.1f);
    }

    [Fact]
    public void ABigItemGetsTheRoomItNeeds()
    {
        // Four backpack-sized items: the old fixed 0.55 m pitch let them touch.
        var counts = Counts((ItemZone.Helpful, ShelfGroup.Containers, 4));
        var sizes = Enumerable.Repeat(new Vector2(0.6f, 0.5f), 4).ToArray();
        var plan = ShelfLayout.Plan(counts, sizes, Hall, Spec);

        AssertDisplayed(plan, sizes, Spec.ItemGap);
    }

    [Fact]
    public void TablesAreNoSmallerThanASturdyMinimum()
    {
        var plan = Plan(Game, Hall, Spec);
        foreach (var t in plan.Tables)
        {
            Assert.True(t.Footprint.Width >= Spec.MinTable - 1e-3f, $"{t.Footprint} too narrow");
            Assert.True(t.Footprint.Depth >= Spec.MinTable - 1e-3f, $"{t.Footprint} too shallow");
        }
    }

    [Fact]
    public void ABadFootprintCountsAsTheSmallestItem()
    {
        var counts = Counts((ItemZone.Helpful, ShelfGroup.Food, 4));
        var sizes = new[]
        {
            new Vector2(float.NaN, 0.2f),
            new Vector2(-1f, 0f),
            new Vector2(float.PositiveInfinity, 0.2f),
            new Vector2(0.2f, 0.2f),
        };
        var plan = ShelfLayout.Plan(counts, sizes, Hall, Spec);

        Assert.True(plan.Fits);
        var smallest = new Vector2(Spec.MinItem, Spec.MinItem);
        AssertDisplayed(
            plan,
            new[] { smallest, smallest, new(Spec.MinItem, 0.2f), sizes[3] },
            Spec.ItemGap
        );
        Assert.All(plan.Slots, s => Assert.True(float.IsFinite(s.X) && float.IsFinite(s.Z)));
    }

    [Fact]
    public void FootprintsMustMatchTheItems() =>
        Assert.Throws<System.ArgumentException>(() =>
            ShelfLayout.Plan(Typical, new Vector2[3], Hall, Spec)
        );

    // The player stands at z = 0, x = 0; the hall is 24 m wide and 30 m deep.
    static readonly RoomRect Hall = new(-12f, 12f, 0f, 30f);

    static float Gap(RoomRect a, RoomRect b)
    {
        float dx = System.Math.Max(0f, System.Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
        float dz = System.Math.Max(0f, System.Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
        return System.MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Tables side by side in a row (one front line) a table gap apart; rows a walkway apart.</summary>
    static void AssertApart(ShelfPlan plan, int i, int j)
    {
        var a = plan.Tables[i].Footprint;
        var b = plan.Tables[j].Footprint;
        bool row = Math.Abs(a.MinZ - b.MinZ) < 1e-3f;
        Assert.True(
            Gap(a, b) >= (row ? plan.Spec.TableGap : plan.Spec.Aisle) - 1e-3f,
            $"tables {i} ({a}) and {j} ({b}) too close"
        );
    }

    static void AssertInside(RoomRect room, RoomRect r, float margin)
    {
        Assert.True(r.MinX >= room.MinX + margin - 1e-3f, $"{r} past the left wall");
        Assert.True(r.MaxX <= room.MaxX - margin + 1e-3f, $"{r} past the right wall");
        Assert.True(r.MinZ >= room.MinZ - 1e-3f, $"{r} behind the player");
        Assert.True(r.MaxZ <= room.MaxZ - margin + 1e-3f, $"{r} past the far wall");
    }

    [Fact]
    public void EveryTableStandsInsideTheRoomClearOfWallsAndOfEachOther()
    {
        var plan = Plan(Typical, Hall, Spec);

        Assert.True(plan.Fits);
        int items = 0;
        foreach (var c in Typical)
            items += c;
        Assert.Equal(items, plan.Slots.Length);
        for (int i = 0; i < plan.Tables.Length; i++)
        {
            AssertInside(Hall, plan.Tables[i].Footprint, Spec.Margin);
            for (int j = i + 1; j < plan.Tables.Length; j++)
                AssertApart(plan, i, j);
        }
    }

    [Fact]
    public void ItemsLieOnTheirTables()
    {
        var plan = Plan(Typical, Hall, Spec);
        foreach (var t in plan.Tables)
            for (int k = t.First; k < t.First + t.Count; k++)
            {
                var s = plan.Slots[k];
                Assert.Equal(t.Group, s.Group);
                Assert.InRange(s.X, t.Footprint.MinX, t.Footprint.MaxX);
                Assert.InRange(s.Z, t.Footprint.MinZ, t.Footprint.MaxZ);
            }
    }

    [Fact]
    public void TheTablesStandInTheMiddleOfTheRoom()
    {
        var plan = Plan(Typical, Hall, Spec);
        Assert.Equal(Hall.CenterX, plan.Block.CenterX, 2);
        Assert.True(plan.Block.MinZ >= Hall.MinZ + Spec.FrontWalk - 1e-3f);
    }

    [Fact]
    public void HelpfulNearestDangerousBehindAndWhatGoesOffByItselfApart()
    {
        var plan = Plan(Typical, Hall, Spec);
        var helpful = plan.Zones[(int)ItemZone.Helpful];
        var dangerous = plan.Zones[(int)ItemZone.Dangerous];
        var volatileZone = plan.Zones[(int)ItemZone.Volatile];
        Assert.True(dangerous.MinZ - helpful.MaxZ >= Spec.ZoneGap - 1e-3f);
        // Its wick moved out of reach, it needs no more than a zone gap: it may close the dangerous row, never nearer.
        Assert.True(volatileZone.MinZ >= dangerous.MinZ - 1e-3f);
        foreach (var t in plan.Tables)
        {
            var zone = plan.Zones[(int)t.Zone];
            Assert.InRange(t.Footprint.MinZ, zone.MinZ - 1e-3f, zone.MaxZ + 1e-3f);
            Assert.InRange(t.Footprint.MaxZ, zone.MinZ - 1e-3f, zone.MaxZ + 1e-3f);
            if (t.Zone != ItemZone.Volatile)
                Assert.True(
                    Gap(t.Footprint, volatileZone) >= Spec.ZoneGap - 1e-3f,
                    $"{t.Footprint} at {volatileZone}"
                );
        }
    }

    [Fact]
    public void WhatGoesOffByItselfKeepsItsClearanceFromThePlayerAndTheOtherTables()
    {
        var spec = new ShelfSpec { VolatileClearance = 5f };
        var plan = Plan(Typical, Hall, spec);

        Assert.True(plan.Fits);
        var start = new RoomRect(plan.StartX, plan.StartX, Hall.MinZ, Hall.MinZ);
        foreach (var v in plan.Tables)
        {
            if (v.Zone != ItemZone.Volatile)
                continue;
            Assert.True(Gap(v.Footprint, start) >= 5f - 1e-3f);
            foreach (var t in plan.Tables)
                if (t.Zone != ItemZone.Volatile)
                    Assert.True(Gap(v.Footprint, t.Footprint) >= 5f - 1e-3f);
            Assert.True(Gap(v.Footprint, plan.Fire) >= 5f - 1e-3f);
            if (plan.HasWall)
                Assert.True(Gap(v.Footprint, plan.Wall) >= 5f - 1e-3f);
        }
    }

    [Fact]
    public void CookingFireAndTestWallStandInsideTheRoomOffTheTables()
    {
        var plan = Plan(Typical, Hall, Spec);

        Assert.True(plan.HasWall);
        AssertInside(Hall, plan.Wall, Spec.Margin);
        AssertInside(Hall, plan.Fire, Spec.Margin);
        foreach (var t in plan.Tables)
        {
            Assert.True(Gap(t.Footprint, plan.Wall) >= Spec.Aisle - 1e-3f);
            Assert.True(Gap(t.Footprint, plan.Fire) >= Spec.Aisle - 1e-3f);
        }
    }

    [Fact]
    public void ANarrowHallWrapsTheTablesIntoMoreColumnsInsteadOfThroughTheWalls()
    {
        // The old layout ran one row of up to 14 items (7.7 m) across, never measuring the side walls.
        var corridor = new RoomRect(-5f, 5f, 0f, 45f);
        var plan = Plan(Typical, corridor, Spec);

        Assert.True(plan.Fits);
        foreach (var t in plan.Tables)
            AssertInside(corridor, t.Footprint, Spec.Margin);
    }

    [Fact]
    public void ARoomTooSmallSaysSo()
    {
        var closet = new RoomRect(-2f, 2f, 0f, 4f);
        Assert.False(Plan(Typical, closet, Spec).Fits);
    }

    // The game's one-of-each count in 0e8ad7e: 139 kinds, 21 dangerous, the dynamite.
    internal static readonly int[] Game = Counts(
        (ItemZone.Helpful, ShelfGroup.Fire, 7),
        (ItemZone.Helpful, ShelfGroup.Food, 45),
        (ItemZone.Helpful, ShelfGroup.Medicine, 12),
        (ItemZone.Helpful, ShelfGroup.Climbing, 12),
        (ItemZone.Helpful, ShelfGroup.Throwing, 8),
        (ItemZone.Helpful, ShelfGroup.Movement, 10),
        (ItemZone.Helpful, ShelfGroup.Tools, 12),
        (ItemZone.Helpful, ShelfGroup.Mystic, 6),
        (ItemZone.Helpful, ShelfGroup.Containers, 2),
        (ItemZone.Helpful, ShelfGroup.Other, 3),
        (ItemZone.Dangerous, ShelfGroup.Food, 15),
        (ItemZone.Dangerous, ShelfGroup.Throwing, 1),
        (ItemZone.Dangerous, ShelfGroup.Movement, 1),
        (ItemZone.Dangerous, ShelfGroup.Mystic, 4),
        (ItemZone.Volatile, ShelfGroup.Throwing, 1)
    );

    /// <summary>Everything placed, inside the room clear of its walls, and every table, the fire and the wall a
    /// walkway apart, by the measures the plan was laid out with.</summary>
    internal static void AssertSound(int[] counts, RoomRect room, ShelfPlan plan)
    {
        Assert.True(plan.Fits, $"does not fit {room}: {plan.Over:0.00}x");
        var used = plan.Spec;
        Assert.True(
            used.Aisle >= 1f - 1e-3f && used.Margin >= 0.2f - 1e-3f && used.ItemGap >= 0.1f - 1e-3f,
            "below the VR minimums"
        );
        Assert.Equal(counts.Sum(), plan.Slots.Length);
        AssertInside(room, plan.Fire, used.Margin);
        if (plan.HasWall)
            AssertInside(room, plan.Wall, used.Margin);
        for (int i = 0; i < plan.Tables.Length; i++)
        {
            var t = plan.Tables[i].Footprint;
            AssertInside(room, t, used.Margin);
            Assert.True(Gap(t, plan.Fire) >= used.Aisle - 1e-3f, $"table {i} at the fire");
            if (plan.HasWall)
                Assert.True(Gap(t, plan.Wall) >= used.Aisle - 1e-3f, $"table {i} at the wall");
            for (int j = i + 1; j < plan.Tables.Length; j++)
                AssertApart(plan, i, j);
        }
    }

    public static TheoryData<float, float, float, float> OpenFloors() =>
        new()
        {
            // Measured in the solo airport (0e8ad7e): from the spawn elevator, then from the open hall.
            { -6.8f, 7.2f, 0f, 20.5f },
            { -4.7f, 9.3f, 0f, 18.1f },
            // A hall of about 15 x 15 m holds everything at game size: 139 kinds, a median half a metre across, need
            // about 66 m2 of table top with 10 cm between them, and walkways a metre wide between rows of tables.
            { -7.5f, 7.5f, 0f, 15f },
            { -8f, 8f, 0f, 16f },
            { -6.5f, 6.5f, 0f, 16.75f },
        };

    [Theory]
    [MemberData(nameof(OpenFloors))]
    public void EveryItemFitsOnAnyOpenFloorOfAboutFifteenByFifteenMetres(
        float minX,
        float maxX,
        float minZ,
        float maxZ
    )
    {
        var room = new RoomRect(minX, maxX, minZ, maxZ);
        var feet = AirportRoomTests.GameFeet();
        var plan = ShelfLayout.Plan(Game, feet, room, Spec);
        AssertSound(Game, room, plan);
        AssertDisplayed(plan, feet, 0.1f);
    }

    [Fact]
    public void AnOpenFloorOfTwelveByTwelveMetresIsTooSmallAtGameSize() =>
        Assert.False(
            ShelfLayout
                .Plan(Game, AirportRoomTests.GameFeet(), new RoomRect(-6f, 6f, 0f, 12f), Spec)
                .Fits
        );

    [Fact]
    public void TheMeasuredHallKeepsTheTestWall()
    {
        var plan = ShelfLayout.Plan(
            Game,
            AirportRoomTests.GameFeet(),
            new RoomRect(-4.7f, 9.3f, 0f, 18.1f),
            Spec
        );
        Assert.True(plan.HasWall);
    }

    [Fact]
    public void ARoomyHallKeepsTheFullMeasures()
    {
        var plan = ShelfLayout.Plan(Game, AirportRoomTests.GameFeet(), Hall, Spec);
        Assert.True(plan.Fits);
        Assert.Equal(Spec.Aisle, plan.Spec.Aisle);
        Assert.Equal(Spec.Margin, plan.Spec.Margin);
    }

    [Fact]
    public void ACrampedRoomTightensBeforeDroppingTheWall()
    {
        var plan = ShelfLayout.Plan(
            Game,
            AirportRoomTests.GameFeet(),
            new RoomRect(-6.8f, 7.2f, 0f, 20.5f),
            Spec
        );
        Assert.True(plan.Fits);
        Assert.True(plan.HasWall);
        Assert.True(plan.Spec.Aisle < Spec.Aisle);
    }

    [Fact]
    public void ASmallerRoomStillRefusesCleanly()
    {
        var room = new RoomRect(-3f, 3f, 0f, 7f);
        var plan = Plan(Game, room, Spec);
        Assert.False(plan.Fits);
        Assert.Equal(Game.Sum(), plan.Slots.Length);
        Assert.True(plan.Over > 1f);
    }

    [Fact]
    public void TablesAreNeverSoDeepThatAnItemIsOutOfReachOfAWalkway()
    {
        // The tightest layout, the game's real items in the airport's fixed place.
        var sizes = AirportRoomTests.GameFeet();
        var plan = ShelfLayout.Plan(Game, sizes, AirportRoom.Bounds, Spec);
        Assert.Equal(2.4f, plan.Spec.TableDepth);
        foreach (var t in plan.Tables)
        {
            // Every item's near side at most 0.7 m in from the nearer table edge: a lean over the table reaches it.
            for (int k = t.First; k < t.First + t.Count; k++)
            {
                var s = Spot(plan, sizes, k);
                float inFromEdge = System.Math.Min(
                    s.MinZ - t.Footprint.MinZ,
                    t.Footprint.MaxZ - s.MaxZ
                );
                Assert.True(inFromEdge <= 0.7f + 1e-3f, $"item {k} {inFromEdge:0.00} m in");
            }
        }
    }

    [Fact]
    public void TheSpawnElevatorNarrowerThanTwoMarginsSaysItDoesNotFitWithoutThrowing()
    {
        // Measured in game (e7b3cd9): the start orbs' clamp threw "-0.2078318 cannot be greater than -0.2447219".
        var elevator = new RoomRect(-0.2078318f - Spec.Margin, -0.2447219f + Spec.Margin, 0f, 3f);
        var plan = Plan(Typical, elevator, Spec);

        Assert.False(plan.Fits);
        Assert.InRange(plan.StartX, elevator.MinX, elevator.MaxX);
    }

    public static TheoryData<float, float, float, float> BadRooms() =>
        new()
        {
            { 0f, 0f, 0f, 0f },
            { -0.5f, 0.5f, 0f, 0.5f },
            { 5f, -5f, 0f, 30f },
            { -12f, 12f, 30f, 0f },
            { 5f, -5f, 30f, 0f },
            { float.NaN, 12f, 0f, 30f },
            { -12f, 12f, 0f, float.NaN },
            { float.NaN, float.NaN, float.NaN, float.NaN },
            { float.NegativeInfinity, float.PositiveInfinity, 0f, 30f },
            { -12f, 12f, 0f, float.PositiveInfinity },
            { float.PositiveInfinity, float.NegativeInfinity, 0f, 30f },
        };

    [Theory]
    [MemberData(nameof(BadRooms))]
    public void AnyDegenerateInvertedOrNonFiniteRoomGivesTheDoesNotFitPlan(
        float minX,
        float maxX,
        float minZ,
        float maxZ
    )
    {
        var plan = Plan(Typical, new RoomRect(minX, maxX, minZ, maxZ), Spec);

        Assert.False(plan.Fits);
        Assert.Equal(Typical.Sum(), plan.Slots.Length);
        Assert.NotEmpty(plan.Tables);
    }

    [Fact]
    public void NoItemsInAClosetDoesNotThrow()
    {
        var plan = Plan(new int[ShelfLayout.Buckets], new RoomRect(1f, -1f, 0f, 0f), Spec);
        Assert.False(plan.Fits);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void BucketsRoundTrip()
    {
        int b = ShelfLayout.Bucket(ItemZone.Dangerous, ShelfGroup.Food);
        Assert.Equal(ItemZone.Dangerous, ShelfLayout.ZoneOf(b));
        Assert.Equal(ShelfGroup.Food, ShelfLayout.GroupOf(b));
    }
}

public class AirportRoomTests
{
    // Prefabs the room leaves out of the database's 194 (props, and one prefab per display name), leaving the game's
    // 139 kinds: display names read with UnityPy from the ItemDatabase (game build of 2026-10).
    static readonly HashSet<string> NotShown = new(System.StringComparer.Ordinal)
    {
        "Berrynana Peel Blue Variant",
        "Berrynana Peel Brown Variant",
        "Berrynana Peel Pink Variant",
        "BingBong_Prop Variant",
        "Binoculars_Prop",
        "Bugle_Prop Variant",
        "C_Bishop W",
        "C_Bishop_f",
        "C_Bishop_f Variant",
        "C_Bishop_m",
        "C_Bishop_m Variant",
        "C_King Variant",
        "C_King W",
        "C_Knight",
        "C_Knight Variant",
        "C_Knight W",
        "C_Pawn W",
        "C_Pawn_f",
        "C_Pawn_f Variant",
        "C_Pawn_m",
        "C_Pawn_m Variant",
        "C_Queen",
        "C_Queen Variant",
        "C_Queen W",
        "C_Rook W",
        "C_Rook_f",
        "C_Rook_f Variant",
        "C_Rook_m",
        "C_Rook_m Variant",
        "Cheat Compass 1",
        "EggRaven",
        "Glizzy_CattailVariant",
        "GuidebookPage_0_Intro",
        "GuidebookPage_10_Sleepy",
        "GuidebookPage_11_Awake",
        "GuidebookPage_12_Crashout",
        "GuidebookPage_13_FirstTeams",
        "GuidebookPage_1_Mushrooms",
        "GuidebookPage_2_Campfire",
        "GuidebookPage_3_Revival",
        "GuidebookPage_4_Poison",
        "GuidebookPage_5_Zombies",
        "GuidebookPage_6_BodyHeat",
        "GuidebookPage_7_BurningSun",
        "GuidebookPage_8_Magma",
        "GuidebookPage_9_Gloom",
        "Kingberry Green",
        "Lollipop_Prop",
        "Mandrake_Hidden",
        "Mushroom Cluster Poison",
        "Mushroom Lace Poison",
        "Mushroom Normie Poison",
        "Parasol_Roots Variant",
        "RescueHook_Infinite",
        "ScoutCookies_Vanilla",
    };

    /// <summary>The game's 139 kinds' real footprints (<see cref="ItemDrawnBox"/>), dealt into the buckets of
    /// <see cref="ShelfLayoutTests.Game"/> in name order: game-sized items, a median half a metre across.</summary>
    internal static Vector2[] GameFeet()
    {
        var feet = ItemDrawnBoxTests
            .Rows()
            .Where(r => !NotShown.Contains(r.Name))
            .Select(r => new Vector2(r.Hi.X - r.Lo.X, r.Hi.Z - r.Lo.Z))
            .ToArray();
        Assert.Equal(ShelfLayoutTests.Game.Sum(), feet.Length);
        return feet;
    }

    [Fact]
    public void TheFixedAirportPlaceHoldsTheGamesRealItemsWithEveryGapKept()
    {
        var feet = GameFeet();
        var plan = ShelfLayout.Plan(
            ShelfLayoutTests.Game,
            feet,
            AirportRoom.Bounds,
            new ShelfSpec()
        );

        ShelfLayoutTests.AssertSound(ShelfLayoutTests.Game, AirportRoom.Bounds, plan);
        ShelfLayoutTests.AssertDisplayed(plan, feet, 0.1f);
        // Every item a hand's width apart takes the floor the test wall stood on: it is left out here.
        Assert.False(plan.HasWall);
    }

    [Fact]
    public void TheFixedPlaceIsUsedOnlyInTheAirportWhenItFitsAndNothingStandsInIt()
    {
        Assert.Equal(RoomPlace.Fixed, AirportRoom.Choose(airport: true, fits: true, blocked: 0));
        // A game update moved the lobby: something now stands where a table would.
        Assert.Equal(RoomPlace.Measured, AirportRoom.Choose(airport: true, fits: true, blocked: 1));
        Assert.Equal(
            RoomPlace.Measured,
            AirportRoom.Choose(airport: true, fits: false, blocked: 0)
        );
        Assert.Equal(
            RoomPlace.Measured,
            AirportRoom.Choose(airport: false, fits: true, blocked: 0)
        );
    }
}
