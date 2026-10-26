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
}

public class ShelfLayoutTests
{
    [Fact]
    public void SmallGroupIsOneCenteredRow()
    {
        var plan = ShelfLayout.Plan(new[] { 3 }, perRow: 8, spacing: 1f, rowGap: 0.5f, aisle: 2f);

        Assert.Equal(3, plan.Slots.Length);
        Assert.Equal(-1f, plan.Slots[0].X, 3);
        Assert.Equal(0f, plan.Slots[1].X, 3);
        Assert.Equal(1f, plan.Slots[2].X, 3);
        Assert.All(plan.Slots, s => Assert.Equal(0f, s.Z, 3));
        var table = Assert.Single(plan.Tables);
        Assert.Equal(1, table.Rows);
        Assert.Equal(0, table.First);
        Assert.Equal(3, table.Count);
    }

    [Fact]
    public void TablesHoldTwoRowsSoEveryItemFacesAnAisle()
    {
        // 20 items, 8 a row: a table of 16 (two rows), then one of 4.
        var plan = ShelfLayout.Plan(
            new[] { 20 },
            perRow: 8,
            spacing: 0.5f,
            rowGap: 0.6f,
            aisle: 1.5f
        );

        Assert.Equal(2, plan.Tables.Length);
        Assert.Equal(2, plan.Tables[0].Rows);
        Assert.Equal(16, plan.Tables[0].Count);
        Assert.Equal(0f, plan.Slots[7].Z, 3);
        Assert.Equal(0.6f, plan.Slots[8].Z, 3);
        // The next table starts an aisle past the back row.
        Assert.Equal(2.1f, plan.Tables[1].Z, 3);
        Assert.Equal(2.1f, plan.Slots[16].Z, 3);
        Assert.Equal(2.1f, plan.Depth, 3);
    }

    [Fact]
    public void EachGroupStartsItsOwnTableAndEmptyGroupsTakeNoSpace()
    {
        var plan = ShelfLayout.Plan(
            new[] { 2, 0, 1 },
            perRow: 8,
            spacing: 1f,
            rowGap: 0.5f,
            aisle: 2f
        );

        Assert.Equal(2, plan.Tables.Length);
        Assert.Equal(0, plan.Tables[0].Group);
        Assert.Equal(2, plan.Tables[1].Group);
        Assert.Equal(2f, plan.Tables[1].Z, 3);
        Assert.Equal(2, plan.Slots[2].Group);
        Assert.Equal(0f, plan.Slots[2].X, 3);
    }

    [Fact]
    public void HalfWidthCoversTheWidestRow()
    {
        var plan = ShelfLayout.Plan(new[] { 5 }, perRow: 4, spacing: 1f, rowGap: 0.5f, aisle: 2f);

        Assert.Equal(1.5f, plan.Tables[0].HalfWidth, 3);
    }

    [Fact]
    public void ChoosePerRowTakesTheNarrowestThatFits()
    {
        var counts = new[] { 30, 30 };
        int narrow = ShelfLayout.ChoosePerRow(counts, 100f, 6, 16, 0.5f, 0.6f, 1.5f);
        Assert.Equal(6, narrow);

        // 15 a row puts each group on one two-row table: 2.7 m deep. 14 needs a third table.
        int fit = ShelfLayout.ChoosePerRow(counts, 3f, 6, 16, 0.5f, 0.6f, 1.5f);
        Assert.Equal(15, fit);
        Assert.True(ShelfLayout.Plan(counts, 14, 0.5f, 0.6f, 1.5f).Depth > 3f);
    }

    [Fact]
    public void ChoosePerRowFallsBackToTheWidest() =>
        Assert.Equal(16, ShelfLayout.ChoosePerRow(new[] { 500 }, 1f, 6, 16, 0.5f, 0.6f, 1.5f));
}
