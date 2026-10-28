namespace PeakXR.Core.Tests;

public class HeldItemShapeTests
{
    // The game's Bugle prefab: render bounds in its own frame, the bell's particles and its Hand_R grip.
    static readonly Vector3 BugleMin = new(-0.2f, -0.537f, -0.067f);
    static readonly Vector3 BugleMax = new(0.131f, 0.096f, 0.797f);
    static readonly Vector3 BugleBell = new(-0.05f, -0.073f, 0.646f);
    static readonly Vector3 BugleMouthpiece = new(0.056f, -0.15f, -0.01f); // its mesh's far end

    [Fact]
    public void FarEnd_OfTheBugle_FromItsBell_IsNearItsMouthpiece()
    {
        var end = HeldItemShape.FarEnd(BugleMin, BugleMax, BugleBell);
        Assert.Equal(BugleMin.Z, end.Z, 3);
        // Inside the bugle's own mouth radius (HeldGestures: 0.16 m), where its grip is not.
        Assert.True(Vector3.Distance(end, BugleMouthpiece) < 0.16f);
        var grip = new Vector3(0.081f, -0.362f, 0.077f);
        Assert.True(Vector3.Distance(grip, BugleMouthpiece) > 0.2f);
    }

    [Fact]
    public void FarEnd_PicksTheLongestSide_AndTheEndAwayFromThePoint()
    {
        var min = new Vector3(-0.05f, -0.4f, -0.05f);
        var max = new Vector3(0.05f, 0.3f, 0.05f);
        Assert.Equal(
            new Vector3(0f, -0.4f, 0f),
            HeldItemShape.FarEnd(min, max, new Vector3(0f, 0.25f, 0f))
        );
        Assert.Equal(
            new Vector3(0f, 0.3f, 0f),
            HeldItemShape.FarEnd(min, max, new Vector3(0f, -0.3f, 0f))
        );
    }

    [Fact]
    public void Closest_IsThePointItselfInside_AndOnTheSurfaceOutside()
    {
        var min = new Vector3(-1f, -1f, -1f);
        var max = new Vector3(1f, 1f, 1f);
        var inside = new Vector3(0.2f, -0.3f, 0.5f);
        Assert.Equal(inside, HeldItemShape.Closest(min, max, inside));
        Assert.Equal(
            new Vector3(1f, 0.5f, -1f),
            HeldItemShape.Closest(min, max, new Vector3(3f, 0.5f, -2f))
        );
    }

    [Fact]
    public void ScaleAbout_KeepsThePivot_AndShrinksTheBox()
    {
        var min = new Vector3(0f, -0.2f, -0.2f);
        var max = new Vector3(0.4f, 0.2f, 0.2f);
        var pivot = new Vector3(0.4f, 0f, 0f);
        HeldItemShape.ScaleAbout(ref min, ref max, pivot, 0.5f);
        Assert.Equal(new Vector3(0.2f, -0.1f, -0.1f), min);
        Assert.Equal(new Vector3(0.4f, 0.1f, 0.1f), max);
    }

    // Every item whose use animation is Drink (ItemUseFeedback.useAnimation, resources.assets): drawn bounds in the
    // item's frame and the middle of its mesh's top 5 % (the neck, cap or carton ridge), read with UnityPy.
    [Theory]
    [InlineData("Antidote", -0.163f, -0.164f, 0.074f, 0.163f, 0.464f, 0.404f, 0f, 0.451f, 0.241f)]
    [InlineData("Cure-All", -0.216f, -0.346f, -0.036f, 0.216f, 0.281f, 0.396f, 0f, 0.277f, 0.18f)]
    [InlineData("Cure-Some", -0.25f, -0.401f, -0.07f, 0.25f, 0.326f, 0.43f, 0f, 0.321f, 0.18f)]
    [InlineData(
        "Energy Drink",
        -0.081f,
        -0.093f,
        -0.058f,
        0.075f,
        0.304f,
        0.087f,
        0.012f,
        0.295f,
        0.016f
    )]
    [InlineData(
        "Energy Elixir",
        -0.081f,
        -0.093f,
        -0.058f,
        0.075f,
        0.304f,
        0.087f,
        0.012f,
        0.295f,
        0.016f
    )]
    [InlineData(
        "FortifiedMilk",
        -0.162f,
        -0.129f,
        -0.136f,
        0.162f,
        0.405f,
        0.188f,
        0f,
        0.399f,
        0.026f
    )]
    [InlineData("Sports Drink", -0.1f, -0.24f, -0.1f, 0.1f, 0.287f, 0.1f, 0f, 0.282f, 0f)]
    public void Opening_OfEachGameDrink_IsItsNeck(
        string item,
        float loX,
        float loY,
        float loZ,
        float hiX,
        float hiY,
        float hiZ,
        float neckX,
        float neckY,
        float neckZ
    )
    {
        var min = new Vector3(loX, loY, loZ);
        var max = new Vector3(hiX, hiY, hiZ);
        var opening = HeldItemShape.Opening(min, max);
        Assert.True(
            Vector3.Distance(opening, new Vector3(neckX, neckY, neckZ)) < 0.02f,
            $"{item}: opening {opening}"
        );
        // The bottom is the far end of the item from it, not a second way in.
        Assert.True(Vector3.Distance(opening, new Vector3(opening.X, loY, opening.Z)) > 0.35f);
    }

    [Fact]
    public void FarEnd_AlongZ_WhenZIsTheLongestSide()
    {
        var min = new Vector3(-0.05f, -0.1f, -0.2f);
        var max = new Vector3(0.05f, 0.1f, 0.6f);
        Assert.Equal(
            new Vector3(0f, 0f, -0.2f),
            HeldItemShape.FarEnd(min, max, new Vector3(0f, 0f, 0.5f))
        );
        Assert.Equal(
            new Vector3(0f, 0f, 0.6f),
            HeldItemShape.FarEnd(min, max, new Vector3(0f, 0f, -0.1f))
        );
    }
}
