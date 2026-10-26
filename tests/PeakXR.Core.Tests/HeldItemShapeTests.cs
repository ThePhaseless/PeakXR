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
        Assert.Equal(new Vector3(0f, -0.4f, 0f), HeldItemShape.FarEnd(min, max, new Vector3(0f, 0.25f, 0f)));
        Assert.Equal(new Vector3(0f, 0.3f, 0f), HeldItemShape.FarEnd(min, max, new Vector3(0f, -0.3f, 0f)));
    }

    [Fact]
    public void Closest_IsThePointItselfInside_AndOnTheSurfaceOutside()
    {
        var min = new Vector3(-1f, -1f, -1f);
        var max = new Vector3(1f, 1f, 1f);
        var inside = new Vector3(0.2f, -0.3f, 0.5f);
        Assert.Equal(inside, HeldItemShape.Closest(min, max, inside));
        Assert.Equal(new Vector3(1f, 0.5f, -1f), HeldItemShape.Closest(min, max, new Vector3(3f, 0.5f, -2f)));
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
}
