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
    public void HandInsideFlickDistance_SelectsNothing()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        Assert.Equal(-1, s.Update(new Vector2(0.03f, 0f), Vector2.Zero, Dirs, AllOn, 4));
        Assert.False(s.Changed);
    }

    [Fact]
    public void HandFlick_SelectsSliceInThatDirection()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        Assert.Equal(2, s.Update(new Vector2(0.01f, -0.07f), Vector2.Zero, Dirs, AllOn, 4));
        Assert.True(s.Changed);
        s.Update(new Vector2(0.01f, -0.08f), Vector2.Zero, Dirs, AllOn, 4);
        Assert.False(s.Changed);
    }

    [Fact]
    public void Selection_IsKeptWhileHandDriftsSlightlyBack()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        s.Update(new Vector2(0.06f, 0f), Vector2.Zero, Dirs, AllOn, 4);
        Assert.Equal(1, s.Update(new Vector2(0.04f, 0f), Vector2.Zero, Dirs, AllOn, 4));
        Assert.Equal(-1, s.Update(new Vector2(0.01f, 0f), Vector2.Zero, Dirs, AllOn, 4));
        Assert.True(s.Changed);
    }

    [Fact]
    public void Stick_UsesVanillaThresholdAndWinsOverHand()
    {
        var s = new RadialSelector { FlickDistance = 0.05f };
        // sqrMagnitude 0.49 < 0.5: ignored, like EmoteWheel.TestSelectSliceGamepad.
        Assert.Equal(-1, s.Update(Vector2.Zero, new Vector2(0.7f, 0f), Dirs, AllOn, 4));
        Assert.Equal(3, s.Update(new Vector2(0.1f, 0f), new Vector2(-0.8f, 0f), Dirs, AllOn, 4));
    }

    [Fact]
    public void Reset_ClearsSelection()
    {
        var s = new RadialSelector();
        s.Update(new Vector2(0f, 0.2f), Vector2.Zero, Dirs, AllOn, 4);
        s.Reset();
        Assert.Equal(-1, s.Selected);
        Assert.False(s.Changed);
    }
}
