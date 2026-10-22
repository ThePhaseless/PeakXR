using P = PeakXR.Core.ItemParts;

namespace PeakXR.Core.Tests;

/// <summary>
/// Pins the item use table: each row is one shipped item (or one per mechanic group), with the facts the game's
/// prefab data gives it (prompt, use animation, action components; see the items audit) and the real-life gesture
/// the classifier must pick.
/// </summary>
public class ItemUseTests
{
    private static ItemUseFacts F(string prompt, string anim, P parts, bool main = true) =>
        new(main, prompt, anim, parts);

    public static TheoryData<string, ItemUseFacts, UseGesture> Table =>
        new()
        {
            // F1 Eat
            { "Airline Food", F("eat", "Eat", P.Consume | P.Hunger | P.Acts), UseGesture.Eat },
            { "Red Crispberry", F("eat", "Eat", P.Consume | P.Hunger | P.Acts), UseGesture.Eat },
            { "Berrynana", F("eat", "Eat", P.Consume | P.Hunger | P.Acts), UseGesture.Eat },
            { "Napberry", F("eat", "Eat", P.Consume | P.SelfCare | P.Acts), UseGesture.Eat },
            {
                "Medicinal Root",
                F("eat", "Eat", P.Consume | P.Hunger | P.SelfCare | P.Acts),
                UseGesture.Eat
            },
            { "Scout Cookies", F("eat", "Eat", P.Hunger | P.Acts), UseGesture.Eat },
            {
                "Big Lollipop",
                F("lick", "Eat", P.Consume | P.Hunger | P.SelfCare | P.Acts),
                UseGesture.Eat
            },
            { "Scorpion", F("eat", "Eat", P.Consume | P.Hunger | P.Acts), UseGesture.Eat },
            { "Frog", F("eat", "", P.None), UseGesture.Eat },
            // F2 Drink: Antidote and Cure-All have no Consume (Action_ReduceUses), still drunk.
            {
                "Energy Drink",
                F("drink", "Drink", P.Consume | P.SelfCare | P.Acts),
                UseGesture.Drink
            },
            {
                "Fortified Milk",
                F("drink", "Drink", P.Consume | P.Hunger | P.SelfCare | P.Acts),
                UseGesture.Drink
            },
            { "Antidote", F("drink", "Drink", P.SelfCare | P.Acts), UseGesture.Drink },
            { "Cure-All", F("drink", "Drink", P.SelfCare | P.Acts), UseGesture.Drink },
            // F3 Apply to the body: never eaten.
            {
                "Bandages",
                F("heal injuries", "Heal", P.Consume | P.SelfCare | P.Acts),
                UseGesture.Apply
            },
            {
                "First Aid Kit",
                F("heal injuries", "Heal", P.Consume | P.SelfCare | P.Acts),
                UseGesture.Apply
            },
            // Aloe Vera's animation is Eat, but it restores no hunger and its verb is "use".
            { "Aloe Vera", F("use", "Eat", P.Consume | P.SelfCare | P.Acts), UseGesture.Apply },
            { "Heat Pack", F("heat up", "", P.Consume | P.SelfCare | P.Acts), UseGesture.Apply },
            { "Sunscreen", F("spray", "", P.Acts), UseGesture.Apply },
            // Its prompt says "read", but its only action never fires: chalk goes on the hands.
            { "Climbing Chalk", F("read", "", P.SelfCare), UseGesture.Apply },
            // O1/O2 Open and read
            { "Book of Bones", F("read", "", P.SelfCare | P.Acts), UseGesture.Read },
            { "Pandora's Lunchbox", F("open?", "", P.SelfCare | P.Acts), UseGesture.Open },
            { "Guidebook", F("Open/Close", "", P.Book | P.Acts), UseGesture.Open },
            { "Passport", F("open", "", P.Book | P.Acts), UseGesture.Open },
            { "Parasol", F("open/close", "", P.Parasol | P.Acts), UseGesture.Open },
            { "Binoculars", F("look", "", P.Binoculars | P.Acts), UseGesture.Look },
            // S3 dangerous: commune eye to eye, never on a bare trigger.
            { "Cursed Skull", F("commune", "", P.Lethal | P.Acts), UseGesture.Commune },
            { "ScoutsHonor", F("commune", "", P.Warp | P.Gem | P.Acts), UseGesture.Commune },
            // P1 Construct: they carry Action_Consume, but are never eaten.
            {
                "Checkpoint Flag",
                F("Plant", "", P.Consume | P.Construct | P.Acts),
                UseGesture.Build
            },
            {
                "Portable Stove",
                F("place", "", P.Consume | P.Construct | P.Acts),
                UseGesture.Build
            },
            { "Scout Cannon", F("place", "", P.Consume | P.Construct), UseGesture.Build },
            { "Scout Effigy", F("revive", "", P.Consume | P.Construct | P.Acts), UseGesture.Build },
            // G1 Shooters
            { "rope cannon", F("shoot", "", P.Shooter | P.Acts), UseGesture.Aim },
            { "Chain Launcher", F("shoot", "", P.Shooter), UseGesture.Aim },
            { "Rescue Claw", F("shoot", "", P.Shooter | P.Acts), UseGesture.Aim },
            { "Blowgun", F("shoot", "", P.Shooter | P.Acts), UseGesture.Aim },
            { "VOIDLAUNCHER", F("shoot", "", P.Shooter | P.Acts), UseGesture.Aim },
            // P3 Rope spool
            { "rope spool", F("tie knot", "", P.Spool), UseGesture.Spool },
            // L1/L2 Lights
            { "Lantern", F("light", "", P.Light | P.Acts), UseGesture.Light },
            { "Torch", F("light", "", P.Light | P.Acts), UseGesture.Light },
            { "Candlestick", F("light", "", P.Light | P.Acts), UseGesture.Light },
            { "Flare", F("light", "", P.Light | P.Acts), UseGesture.Light },
            // S1 Blow
            { "Bugle", F("use", "", P.Bugle | P.Acts), UseGesture.Blow },
            { "Scoutmaster's Bugle", F("play", "", P.Bugle | P.Acts), UseGesture.Blow },
            // M1 Balloon: blown up at the mouth on purpose.
            { "Balloon", F("USE", "", P.Balloon | P.Consume | P.Acts), UseGesture.Inflate },
            // A1 Gems, W1 warps
            { "Strange Gem", F("USE", "", P.Gem | P.Acts), UseGesture.Activate },
            { "AMULET_CLONE", F("ACTIVATE", "", P.Gem | P.Acts), UseGesture.Activate },
            { "Warp Compass", F("warp", "", P.Warp | P.Acts), UseGesture.Activate },
            { "WARP COMPASS", F("warp", "", P.Warp | P.Acts), UseGesture.Activate },
            // Button only: no gesture of its own, or no primary use at all.
            { "Bing Bong", F("ask", "", P.Acts), UseGesture.Button },
            { "Megaphone", F("use", "", P.None), UseGesture.Button },
            { "Conch", F("eat", "", P.None, main: false), UseGesture.Button },
            { "Piton", F("", "", P.None, main: false), UseGesture.Button },
            { "Ritual Dagger", F("", "", P.Acts, main: false), UseGesture.Button },
            { "Compass", F("use", "", P.None, main: false), UseGesture.Button },
            { "Dynamite", F("light", "", P.None), UseGesture.Button },
            { "Stick", F("", "", P.None, main: false), UseGesture.Button },
        };

    [Theory]
    [MemberData(nameof(Table))]
    public void ClassifiesEveryGroup(string item, ItemUseFacts facts, UseGesture expected) =>
        Assert.True(
            ItemUse.Classify(facts) == expected,
            $"{item}: {ItemUse.Classify(facts)}, expected {expected}"
        );

    [Theory]
    [InlineData(UseGesture.Eat, true)]
    [InlineData(UseGesture.Drink, true)]
    [InlineData(UseGesture.Inflate, true)]
    [InlineData(UseGesture.Build, false)]
    [InlineData(UseGesture.Apply, false)]
    [InlineData(UseGesture.Read, false)]
    [InlineData(UseGesture.Commune, false)]
    [InlineData(UseGesture.Button, false)]
    public void OnlyFoodDrinkAndBalloonGoToTheMouth(UseGesture g, bool mouth) =>
        Assert.Equal(mouth, ItemUse.AtMouth(g));

    [Fact]
    public void OnlyCommuneRefusesTheBareTrigger()
    {
        for (var g = UseGesture.Button; g < UseGesture.Count; g++)
            Assert.Equal(g == UseGesture.Commune, ItemUse.NeedsGesture(g));
    }

    [Fact]
    public void PromptsCompareIgnoringCaseAndSpace() =>
        Assert.Equal(UseGesture.Drink, ItemUse.Classify(F("  DRINK ", "", P.SelfCare)));

    [Fact]
    public void UnknownFoodFallsBackToEating() =>
        Assert.Equal(UseGesture.Eat, ItemUse.Classify(F("munch", "", P.Consume | P.Hunger)));

    [Fact]
    public void NullStringsAreButton() =>
        Assert.Equal(
            UseGesture.Button,
            ItemUse.Classify(new ItemUseFacts(true, null, null, P.None))
        );
}

public class LimitStopTests
{
    [Fact]
    public void FiresOnceWhenPushedIntoTheLimit()
    {
        var s = new LimitStop();
        Assert.False(s.Update(5f, 0f, 10f, 1));
        Assert.True(s.Update(10f, 0f, 10f, 1));
        // Still pushing: a dead knob, no repeat.
        Assert.False(s.Update(10f, 0f, 10f, 1));
        Assert.True(s.Pressing);
    }

    [Fact]
    public void PushingAwayOrStoppingRearms()
    {
        var s = new LimitStop();
        Assert.True(s.Update(0f, 0f, 10f, -1));
        Assert.False(s.Update(0f, 0f, 10f, 0));
        Assert.True(s.Update(0f, 0f, 10f, -1));
        // At the low end, pushing up is free travel.
        Assert.False(s.Update(0f, 0f, 10f, 1));
    }

    [Fact]
    public void ToleratesFloatClampNoise() =>
        Assert.True(new LimitStop().Update(9.9995f, 0f, 10f, 1));
}

public class CookRiskTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void BurnsPastLevelTwo(int cooked, bool harms) =>
        Assert.Equal(harms, CookRisk.NextHarms(cooked, wreck: false, explodeAt: 0, defaults: true));

    [Fact]
    public void WreckOrExplodeHarmsAtOnce()
    {
        Assert.True(CookRisk.NextHarms(0, wreck: true, explodeAt: 0, defaults: true));
        Assert.True(CookRisk.NextHarms(0, wreck: false, explodeAt: 1, defaults: false));
    }

    [Fact]
    public void ItemsWithoutDefaultCookingNeverBurn() =>
        Assert.False(CookRisk.NextHarms(6, wreck: false, explodeAt: 0, defaults: false));
}
