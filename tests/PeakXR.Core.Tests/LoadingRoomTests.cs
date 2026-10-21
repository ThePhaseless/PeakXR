namespace PeakXR.Core.Tests;

public class LoadingDestinationTests
{
    static readonly Rgba Black = new(0f, 0f, 0f, 1f);
    static readonly Rgba Cream = new(0.99f, 0.85f, 0.57f, 0.43f);

    // AscentData "Ascent 4" (resources.assets AscentData.ascents[6]).
    static readonly Rgba AscentRed = new(0.58f, 0.17f, 0.18f, 1f);

    [Fact]
    public void GreyScreenTakesTheDestinationColour()
    {
        Assert.Equal(
            LoadingPalette.FromKey(AscentRed),
            LoadingPalette.For(new[] { Black }, AscentRed)
        );
    }

    [Fact]
    public void ColourfulScreenWinsOverTheDestination()
    {
        Assert.Equal(
            LoadingPalette.FromKey(Cream),
            LoadingPalette.For(new[] { Black, Cream }, AscentRed)
        );
    }

    [Fact]
    public void GreyDestinationFallsBackToDefault()
    {
        var grey = new Rgba(0.24f, 0.25f, 0.24f, 1f);
        Assert.Equal(LoadingPalette.Default, LoadingPalette.For(new[] { Black }, grey));
        Assert.Equal(LoadingPalette.Default, LoadingPalette.For(new[] { Black }, null));
    }
}

public class LoadingLayoutTests
{
    const float Width = 1920f;

    static LoadingLayout Fit(float minX, float maxX, float minY, float maxY) =>
        LoadingLayout.Fit(minX, maxX, minY, maxY, Width, 2.4f, 3f, 1.6f);

    [Fact]
    public void SmallContentKeepsTheDefaultScaleAndSitsAtEyeHeight()
    {
        // LoadingScreenSimple: only the text, 1185x200 at y -300.
        var l = Fit(-592f, 592f, -400f, -200f);
        TestUtil.Near(2.4f / Width, l.MetersPerUnit);
        TestUtil.Near(1.6f, l.CenterHeight + -300f * l.MetersPerUnit);
    }

    [Fact]
    public void TallContentShrinksToTheComfortableViewAndStaysAboveTheFloor()
    {
        // LoadingScreen (plane): polar grid disc 3439 units across, centred 1054 units below the canvas centre.
        var l = Fit(-1720f, 1720f, -2774f, 666f);
        var bottom = l.CenterHeight + -2774f * l.MetersPerUnit;
        var top = l.CenterHeight + 666f * l.MetersPerUnit;
        Assert.True(l.MetersPerUnit < 2.4f / Width);
        Assert.True(bottom >= LoadingLayout.FloorMargin - 1e-4f, $"bottom {bottom}");
        var halfAngle = MathF.Atan((top - bottom) * 0.5f / 3f) * 180f / MathF.PI;
        Assert.True(halfAngle <= LoadingLayout.MaxHalfAngleV + 0.01f, $"half angle {halfAngle}");
        TestUtil.Near(bottom, l.ContentBottom);
    }

    [Fact]
    public void ContentIsNeverPushedBelowTheFloorMargin()
    {
        // A very low eye (seated on the floor): the art centred on it would dip under the margin.
        var l = LoadingLayout.Fit(-100f, 100f, -900f, -700f, Width, 2.4f, 3f, 0.3f);
        TestUtil.Near(LoadingLayout.FloorMargin, l.ContentBottom);
    }

    [Fact]
    public void WideContentShrinksToFitHorizontally()
    {
        var l = Fit(-6000f, 6000f, -10f, 10f);
        var halfAngle = MathF.Atan(6000f * l.MetersPerUnit / 3f) * 180f / MathF.PI;
        TestUtil.Near(LoadingLayout.MaxHalfAngleH, halfAngle, 0.01f);
    }

    [Fact]
    public void EmptyContentUsesTheDefaults()
    {
        var l = Fit(0f, 0f, 0f, 0f);
        TestUtil.Near(2.4f / Width, l.MetersPerUnit);
        TestUtil.Near(1.6f, l.CenterHeight);
    }
}

public class HoldToConfirmTests
{
    [Fact]
    public void FiresOnceAfterAFullHold()
    {
        var h = new HoldToConfirm { HoldSeconds = 0.5f };
        Assert.False(h.Update(0.25f, held: true));
        TestUtil.Near(0.5f, h.Progress);
        Assert.True(h.Update(0.25f, held: true));
        Assert.False(h.Update(0.25f, held: true));
    }

    [Fact]
    public void ReleasingDrainsTheRing()
    {
        var h = new HoldToConfirm { HoldSeconds = 0.5f };
        h.Update(0.25f, held: true);
        h.Update(0.1f, held: false);
        Assert.True(h.Progress < 0.5f);
        h.Update(1f, held: false);
        Assert.Equal(0f, h.Progress);
    }

    [Fact]
    public void AButtonAlreadyHeldWhenArmedDoesNotCount()
    {
        // The controller is often picked up with a finger on the trigger: only a fresh press counts.
        var h = new HoldToConfirm { HoldSeconds = 0.5f };
        h.Arm(held: true);
        Assert.False(h.Update(1f, held: true));
        Assert.Equal(0f, h.Progress);
        h.Update(0.1f, held: false);
        h.Update(0.3f, held: true);
        Assert.True(h.Update(0.3f, held: true));
    }
}
