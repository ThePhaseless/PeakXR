using System.Collections.Generic;

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
    }
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
                Assert.True(
                    Gap(plan.Tables[i].Footprint, plan.Tables[j].Footprint) >= Spec.Aisle - 1e-3f,
                    $"tables {i} and {j} leave no walkway"
                );
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

    [Fact]
    public void BucketsRoundTrip()
    {
        int b = ShelfLayout.Bucket(ItemZone.Dangerous, ShelfGroup.Food);
        Assert.Equal(ItemZone.Dangerous, ShelfLayout.ZoneOf(b));
        Assert.Equal(ShelfGroup.Food, ShelfLayout.GroupOf(b));
    }
}
