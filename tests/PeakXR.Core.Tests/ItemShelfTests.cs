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
        var plan = ShelfLayout.Plan(Typical, Hall, Spec);

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
        var plan = ShelfLayout.Plan(Typical, Hall, Spec);
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
        var plan = ShelfLayout.Plan(Typical, Hall, Spec);
        Assert.Equal(Hall.CenterX, plan.Block.CenterX, 2);
        Assert.True(plan.Block.MinZ >= Hall.MinZ + Spec.FrontWalk - 1e-3f);
    }

    [Fact]
    public void HelpfulNearestDangerousBehindAndWhatGoesOffByItselfFarthest()
    {
        var plan = ShelfLayout.Plan(Typical, Hall, Spec);
        var helpful = plan.Zones[(int)ItemZone.Helpful];
        var dangerous = plan.Zones[(int)ItemZone.Dangerous];
        var volatileZone = plan.Zones[(int)ItemZone.Volatile];
        Assert.True(dangerous.MinZ - helpful.MaxZ >= Spec.ZoneGap - 1e-3f);
        Assert.True(volatileZone.MinZ - dangerous.MaxZ >= Spec.ZoneGap - 1e-3f);
        foreach (var t in plan.Tables)
        {
            var zone = plan.Zones[(int)t.Zone];
            Assert.InRange(t.Footprint.MinZ, zone.MinZ - 1e-3f, zone.MaxZ + 1e-3f);
            Assert.InRange(t.Footprint.MaxZ, zone.MinZ - 1e-3f, zone.MaxZ + 1e-3f);
        }
    }

    [Fact]
    public void WhatGoesOffByItselfKeepsItsClearanceFromThePlayerAndTheOtherTables()
    {
        var spec = new ShelfSpec { VolatileClearance = 5f };
        var plan = ShelfLayout.Plan(Typical, Hall, spec);

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
        var plan = ShelfLayout.Plan(Typical, Hall, Spec);

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
        var plan = ShelfLayout.Plan(Typical, corridor, Spec);

        Assert.True(plan.Fits);
        foreach (var t in plan.Tables)
            AssertInside(corridor, t.Footprint, Spec.Margin);
    }

    [Fact]
    public void ARoomTooSmallSaysSo()
    {
        var closet = new RoomRect(-2f, 2f, 0f, 4f);
        Assert.False(ShelfLayout.Plan(Typical, closet, Spec).Fits);
    }

    // The game's one-of-each count in 0e8ad7e: 139 kinds, 21 dangerous, the dynamite.
    static readonly int[] Game = Counts(
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
    static void AssertSound(int[] counts, RoomRect room, ShelfPlan plan)
    {
        Assert.True(plan.Fits, $"does not fit {room}: {plan.Over:0.00}x");
        var used = plan.Spec;
        Assert.True(
            used.Aisle >= 1f - 1e-3f && used.Margin >= 0.4f - 1e-3f,
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
            // A hall of about 12 x 12 m holds everything, in any proportion near that. 8 x 10 m cannot: 139 items
            // need about 25 m2 of table, a walkway only reaches 0.6 m into a table, and 15 kinds in three areas never
            // share one, so the rows alone need more depth than 8 m of floor past the start walkway leaves.
            { -6f, 6f, 0f, 12f },
            { -5f, 5f, 0f, 14f },
            { -7f, 7f, 0f, 11f },
        };

    [Theory]
    [MemberData(nameof(OpenFloors))]
    public void EveryItemFitsOnAnyOpenFloorOfAboutTwelveByTwelveMetres(
        float minX,
        float maxX,
        float minZ,
        float maxZ
    )
    {
        var room = new RoomRect(minX, maxX, minZ, maxZ);
        AssertSound(Game, room, ShelfLayout.Plan(Game, room, Spec));
    }

    [Fact]
    public void TheMeasuredHallKeepsTheTestWall()
    {
        var plan = ShelfLayout.Plan(Game, new RoomRect(-4.7f, 9.3f, 0f, 18.1f), Spec);
        Assert.True(plan.HasWall);
    }

    [Fact]
    public void ARoomyHallKeepsTheFullMeasures()
    {
        var plan = ShelfLayout.Plan(Game, Hall, Spec);
        Assert.True(plan.Fits);
        Assert.Equal(Spec.Aisle, plan.Spec.Aisle);
        Assert.Equal(Spec.Margin, plan.Spec.Margin);
    }

    [Fact]
    public void ACrampedRoomTightensBeforeDroppingTheWall()
    {
        var plan = ShelfLayout.Plan(Game, new RoomRect(-6.8f, 7.2f, 0f, 20.5f), Spec);
        Assert.True(plan.Fits);
        Assert.True(plan.HasWall);
        Assert.True(plan.Spec.Aisle < Spec.Aisle);
    }

    [Fact]
    public void ASmallerRoomStillRefusesCleanly()
    {
        var room = new RoomRect(-3f, 3f, 0f, 7f);
        var plan = ShelfLayout.Plan(Game, room, Spec);
        Assert.False(plan.Fits);
        Assert.Equal(Game.Sum(), plan.Slots.Length);
        Assert.True(plan.Over > 1f);
    }

    [Fact]
    public void TablesNeverGoPastFourRowsSoEveryItemIsInReachOfAWalkway()
    {
        var plan = ShelfLayout.Plan(Game, new RoomRect(-6f, 6f, 0f, 12f), Spec);
        Assert.Equal(4, plan.Spec.MaxRows);
        foreach (var t in plan.Tables)
        {
            Assert.InRange(t.Rows, 1, 4);
            // The innermost row is at most 0.6 m in from the nearer table edge.
            Assert.True((t.Rows - 1) / 2 * plan.Spec.RowGap + plan.Spec.EdgePad <= 0.6f + 1e-3f);
        }
    }

    [Fact]
    public void TheSpawnElevatorNarrowerThanTwoMarginsSaysItDoesNotFitWithoutThrowing()
    {
        // Measured in game (e7b3cd9): the start orbs' clamp threw "-0.2078318 cannot be greater than -0.2447219".
        var elevator = new RoomRect(-0.2078318f - Spec.Margin, -0.2447219f + Spec.Margin, 0f, 3f);
        var plan = ShelfLayout.Plan(Typical, elevator, Spec);

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
        var plan = ShelfLayout.Plan(Typical, new RoomRect(minX, maxX, minZ, maxZ), Spec);

        Assert.False(plan.Fits);
        Assert.Equal(Typical.Sum(), plan.Slots.Length);
        Assert.NotEmpty(plan.Tables);
    }

    [Fact]
    public void NoItemsInAClosetDoesNotThrow()
    {
        var plan = ShelfLayout.Plan(
            new int[ShelfLayout.Buckets],
            new RoomRect(1f, -1f, 0f, 0f),
            Spec
        );
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
