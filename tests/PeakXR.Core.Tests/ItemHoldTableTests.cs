namespace PeakXR.Core.Tests;

public class ItemHoldTableTests
{
    [Fact]
    public void NameOverride_WinsOverTraits()
    {
        var p = ItemHoldTable.Classify("Rope Cannon", ItemTraits.RightHandOnly);
        Assert.Equal(Handedness.OptionalTwoHand, p.Handedness);
    }

    [Fact]
    public void NameMatch_IgnoresCaseSpacesAndCloneSuffix()
    {
        var a = ItemHoldTable.Classify("ropecannon(Clone)", ItemTraits.None);
        var b = ItemHoldTable.Classify("Rope Cannon", ItemTraits.None);
        Assert.Equal(b, a);
    }

    [Fact]
    public void CheckpointFlag_PlantsWithOneHand()
    {
        var p = ItemHoldTable.Classify(
            "Checkpoint Flag Flag_Plantable_Checkpoint",
            ItemTraits.Elongated
        );
        Assert.Equal(Handedness.OptionalTwoHand, p.Handedness);
    }

    [Fact]
    public void UnknownName_RightHandOnly_IsOneHand()
    {
        var p = ItemHoldTable.Classify("Some Future Gadget", ItemTraits.RightHandOnly);
        Assert.Equal(Handedness.OneHand, p.Handedness);
    }

    [Fact]
    public void UnknownName_Long_IsAPoleUsableWithOneHand()
    {
        // External review #19: the game needs no second hand for any item (it only joins one when the item is not
        // rightHandOnly, CharacterItems.cs:647-651), so an unknown long item takes both hands but works with one.
        var p = ItemHoldTable.Classify("Mystery Stick", ItemTraits.Elongated);
        Assert.Equal(new HoldProfile(Handedness.OptionalTwoHand, GripPreset.TwoHandPole), p);
    }

    [Fact]
    public void RequireTwoHandsOff_LetsTwoHandedItemsWorkWithOneHand()
    {
        Assert.False(ItemHoldTable.CanUse(Handedness.TwoHanded, false, requireTwoHands: true));
        Assert.True(ItemHoldTable.CanUse(Handedness.TwoHanded, false, requireTwoHands: false));
    }

    [Fact]
    public void UnknownName_SmallFood_IsOneHandPinch()
    {
        var p = ItemHoldTable.Classify("Strange Fruit", ItemTraits.Food | ItemTraits.Small);
        Assert.Equal(new HoldProfile(Handedness.OneHand, GripPreset.Pinch), p);
    }

    [Fact]
    public void UnknownName_Flat_IsFlatPalm()
    {
        var p = ItemHoldTable.Classify("Strange Map", ItemTraits.Flat);
        Assert.Equal(GripPreset.FlatPalm, p.Grip);
    }

    [Fact]
    public void UnknownName_NoTraits_DefaultsToOptionalTwoHandCylinder()
    {
        var p = ItemHoldTable.Classify("Thing", ItemTraits.None);
        Assert.Equal(new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder), p);
    }

    [Fact]
    public void NullName_UsesTraits()
    {
        var p = ItemHoldTable.Classify(null, ItemTraits.RightHandOnly);
        Assert.Equal(Handedness.OneHand, p.Handedness);
    }

    [Fact]
    public void Normalize_StripsCloneSpacesAndCase()
    {
        Assert.Equal("ropecannon", ItemHoldTable.Normalize(" Rope Cannon (Clone)"));
        Assert.Equal("", ItemHoldTable.Normalize(null));
    }

    [Fact]
    public void UseAllowed_TwoHandedNeedsSecondHand()
    {
        Assert.False(ItemHoldTable.CanUse(Handedness.TwoHanded, secondHandOnItem: false));
        Assert.True(ItemHoldTable.CanUse(Handedness.TwoHanded, secondHandOnItem: true));
        Assert.True(ItemHoldTable.CanUse(Handedness.OptionalTwoHand, secondHandOnItem: false));
        Assert.True(ItemHoldTable.CanUse(Handedness.OneHand, secondHandOnItem: false));
    }

    [Fact]
    public void SecondHand_OnlyForTwoHandCapableItems()
    {
        Assert.False(ItemHoldTable.AcceptsSecondHand(Handedness.OneHand));
        Assert.True(ItemHoldTable.AcceptsSecondHand(Handedness.TwoHanded));
        Assert.True(ItemHoldTable.AcceptsSecondHand(Handedness.OptionalTwoHand));
    }

    [Fact]
    public void SecondHand_AlsoForAnyItemTheGameHoldsWithBothHands()
    {
        // Vanilla joins the left hand to every item that is not rightHandOnly (CharacterItems.cs:647-651).
        Assert.True(ItemHoldTable.AcceptsSecondHand(Handedness.OneHand, vanillaBothHands: true));
        Assert.False(ItemHoldTable.AcceptsSecondHand(Handedness.OneHand, vanillaBothHands: false));
    }

    [Fact]
    public void BingBong_TakesASecondHand() =>
        Assert.True(
            ItemHoldTable.AcceptsSecondHand(
                ItemHoldTable.Classify("BingBong(Clone)", ItemTraits.None).Handedness
            )
        );
}
