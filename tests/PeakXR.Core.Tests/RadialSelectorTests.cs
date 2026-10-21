namespace PeakXR.Core.Tests;

public class RadialSelectorTests
{
    // Four slices: up, right, down, left.
    private static readonly Vector2[] Dirs =
    {
        new(0f, 1f),
        new(1f, 0f),
        new(0f, -1f),
        new(-1f, 0f),
    };
    private static readonly bool[] AllOn = { true, true, true, true };

    [Fact]
    public void Nearest_PicksSmallestAngle()
    {
        Assert.Equal(1, RadialSelector.Nearest(new Vector2(0.9f, 0.2f), Dirs, AllOn, 4));
        Assert.Equal(3, RadialSelector.Nearest(new Vector2(-0.5f, -0.4f), Dirs, AllOn, 4));
    }

    [Fact]
    public void Nearest_SkipsDisabledSlices()
    {
        var enabled = new[] { true, false, true, true };
        Assert.Equal(0, RadialSelector.Nearest(new Vector2(0.9f, 0.5f), Dirs, enabled, 4));
    }

    [Fact]
    public void Nearest_NoneEnabled_ReturnsMinusOne()
    {
        Assert.Equal(-1, RadialSelector.Nearest(new Vector2(1f, 0f), Dirs, new bool[4], 4));
    }

    [Fact]
    public void HandInsideFlickDistance_GivesNoDirection()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        Assert.False(s.Update(new Vector2(0.03f, 0f), Vector2.Zero, out var dir));
        Assert.Equal(Vector2.Zero, dir);
    }

    [Fact]
    public void HandFlick_GivesUnitDirectionThatPicksThatSlice()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        Assert.True(s.Update(new Vector2(0.01f, -0.07f), Vector2.Zero, out var dir));
        TestUtil.Near(1f, dir.Length());
        Assert.True(dir.LengthSquared() >= RadialSelector.StickThresholdSq);
        Assert.Equal(2, RadialSelector.Nearest(dir, Dirs, AllOn, 4));
    }

    [Fact]
    public void Selection_IsKeptWhileHandDriftsSlightlyBack()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        s.Update(new Vector2(0.06f, 0f), Vector2.Zero, out _);
        Assert.True(s.Update(new Vector2(0.04f, 0f), Vector2.Zero, out _));
        Assert.False(s.Update(new Vector2(0.01f, 0f), Vector2.Zero, out _));
        Assert.False(s.Active);
    }

    [Fact]
    public void Stick_UsesVanillaThresholdAndWinsOverHand()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        // sqrMagnitude 0.49 < 0.5: ignored, like EmoteWheel.TestSelectSliceGamepad.
        Assert.False(s.Update(Vector2.Zero, new Vector2(0.7f, 0f), out _));
        Assert.True(s.Update(new Vector2(0.1f, 0f), new Vector2(-0.8f, 0f), out var dir));
        Assert.Equal(new Vector2(-0.8f, 0f), dir);
    }

    [Fact]
    public void HandPastOuterEdge_SelectsNothing()
    {
        var s = new RadialSelector { FlickDistance = 0.05f, MaxDistance = 0.15f };
        // Straight past the drawn ring: empty space, no slice.
        Assert.False(s.Update(new Vector2(0f, 0.2f), Vector2.Zero, out var dir));
        Assert.Equal(Vector2.Zero, dir);
        Assert.True(s.Update(new Vector2(0f, 0.1f), Vector2.Zero, out _));
        // A small overshoot keeps the choice (tremor), a clear one drops it.
        Assert.True(s.Update(new Vector2(0f, 0.155f), Vector2.Zero, out _));
        Assert.False(s.Update(new Vector2(0f, 0.2f), Vector2.Zero, out _));
    }

    [Fact]
    public void Stick_IgnoresMaxDistance()
    {
        var s = new RadialSelector { FlickDistance = 0.05f, MaxDistance = 0.15f };
        Assert.True(s.Update(new Vector2(0f, 0.5f), new Vector2(0.9f, 0f), out var dir));
        Assert.Equal(new Vector2(0.9f, 0f), dir);
    }

    [Fact]
    public void Reset_ClearsActive()
    {
        var s = new RadialSelector();
        s.Update(new Vector2(0f, 0.2f), Vector2.Zero, out _);
        s.Reset();
        Assert.False(s.Active);
    }
}
