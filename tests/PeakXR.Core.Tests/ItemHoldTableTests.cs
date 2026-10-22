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
    public void CheckpointFlag_PlantsOneHanded()
    {
        var p = ItemHoldTable.Classify(
            "Checkpoint Flag Flag_Plantable_Checkpoint",
            ItemTraits.Elongated
        );
        Assert.Equal(Handedness.OptionalTwoHand, p.Handedness);
    }

    [Fact]
    public void UnknownName_RightHandOnly_IsOneHanded()
    {
        var p = ItemHoldTable.Classify("Some Future Gadget", ItemTraits.RightHandOnly);
        Assert.Equal(Handedness.OneHanded, p.Handedness);
    }

    [Fact]
    public void UnknownName_Long_IsTwoHandedPole()
    {
        var p = ItemHoldTable.Classify("Mystery Stick", ItemTraits.Elongated);
        Assert.Equal(new HoldProfile(Handedness.TwoHanded, GripPreset.TwoHandPole), p);
    }

    [Fact]
    public void UnknownName_SmallFood_IsOneHandedPinch()
    {
        var p = ItemHoldTable.Classify("Strange Fruit", ItemTraits.Food | ItemTraits.Small);
        Assert.Equal(new HoldProfile(Handedness.OneHanded, GripPreset.Pinch), p);
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
        Assert.Equal(Handedness.OneHanded, p.Handedness);
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
        Assert.True(ItemHoldTable.CanUse(Handedness.OneHanded, secondHandOnItem: false));
    }

    [Fact]
    public void SecondHand_OnlyForTwoHandCapableItems()
    {
        Assert.False(ItemHoldTable.AcceptsSecondHand(Handedness.OneHanded));
        Assert.True(ItemHoldTable.AcceptsSecondHand(Handedness.TwoHanded));
        Assert.True(ItemHoldTable.AcceptsSecondHand(Handedness.OptionalTwoHand));
    }
}
