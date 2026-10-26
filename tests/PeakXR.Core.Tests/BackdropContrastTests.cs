namespace PeakXR.Core.Tests;

public class BackdropContrastTests
{
    [Fact]
    public void BrightSnowBehind_GetsTheFullPlate()
    {
        Assert.Equal(0.85f, BackdropContrast.NeededOpacity(1f, 0.85f), 4);
    }

    [Fact]
    public void DarkInteriorBehind_ThinsThePlateToItsFloor()
    {
        Assert.Equal(BackdropContrast.MinOpacity, BackdropContrast.NeededOpacity(0.02f, 0.85f), 4);
    }

    [Fact]
    public void MidBackground_ReachesTheTargetContrastExactly()
    {
        float o = BackdropContrast.NeededOpacity(0.5f, 1f);
        Assert.InRange(o, BackdropContrast.MinOpacity, 1f);
        float under = o * Contrast.Luminance(MenuBackplate.Color) + (1f - o) * 0.5f;
        Assert.Equal(BackdropContrast.TargetContrast, Contrast.Ratio(1f, under), 2);
    }

    [Fact]
    public void BrighterBackground_NeverThinsThePlate()
    {
        float last = 0f;
        for (float l = 0f; l <= 1f; l += 0.05f)
        {
            float o = BackdropContrast.NeededOpacity(l, 0.9f);
            Assert.True(o >= last - 1e-5f);
            last = o;
        }
    }

    [Fact]
    public void Meter_ReadsTheBrightPartNotTheAverage()
    {
        // A dark interior with a bright window over a fifth of the plate: text over the window must still read.
        var m = new BackdropContrast.Meter();
        for (int i = 0; i < 80; i++)
            m.Add(10, 10, 10);
        for (int i = 0; i < 20; i++)
            m.Add(250, 250, 250);
        Assert.True(m.Bright() > 0.8f);
    }

    [Fact]
    public void Meter_AllDark_ReadsDark_AndEmptyReadsBright()
    {
        var m = new BackdropContrast.Meter();
        for (int i = 0; i < 50; i++)
            m.Add(5, 8, 12);
        Assert.True(m.Bright() < 0.02f);
        m.Clear();
        Assert.Equal(1f, m.Bright());
    }

    [Fact]
    public void Approach_DarkensFastAndLightensSlowly()
    {
        float up = BackdropContrast.Approach(0.4f, 0.85f, 0.1f);
        float down = BackdropContrast.Approach(0.85f, 0.4f, 0.1f);
        Assert.True(up - 0.4f > 0.85f - down);
        Assert.InRange(up, 0.4f, 0.85f);
        Assert.InRange(down, 0.4f, 0.85f);
        Assert.Equal(0.85f, BackdropContrast.Approach(0.4f, 0.85f, 10f), 3);
    }
}
