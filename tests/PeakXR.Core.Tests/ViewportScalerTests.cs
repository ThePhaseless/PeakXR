namespace PeakXR.Core.Tests;

public class ViewportScalerTests
{
    [Fact]
    public void OverBudget_StepsDownAfterDelay_ToMin()
    {
        var s = new ViewportScaler
        {
            Min = 0.7f,
            Step = 0.1f,
            DownSeconds = 0.2f,
        };
        s.Update(12f, 11.1f, 0.1f);
        Assert.Equal(1f, s.Scale);
        s.Update(12f, 11.1f, 0.1f);
        TestUtil.Near(0.9f, s.Scale, 1e-5f);
        for (int i = 0; i < 100; i++)
            s.Update(12f, 11.1f, 0.1f);
        TestUtil.Near(0.7f, s.Scale, 1e-5f);
    }

    [Fact]
    public void UnderBudget_StepsUpSlowly_ToMax()
    {
        var s = new ViewportScaler
        {
            Min = 0.5f,
            Step = 0.1f,
            UpSeconds = 1f,
        };
        s.SetScale(0.5f);
        s.Update(5f, 11.1f, 0.9f);
        TestUtil.Near(0.5f, s.Scale, 1e-5f);
        s.Update(5f, 11.1f, 0.2f);
        TestUtil.Near(0.6f, s.Scale, 1e-5f);
        for (int i = 0; i < 100; i++)
            s.Update(5f, 11.1f, 0.5f);
        TestUtil.Near(1f, s.Scale, 1e-5f);
    }

    [Fact]
    public void InHysteresisBand_HoldsAndResetsTimers()
    {
        var s = new ViewportScaler { Step = 0.1f, DownSeconds = 0.3f };
        s.Update(12f, 11.1f, 0.2f); // over budget for 0.2 s
        s.Update(9f, 11.1f, 0.2f); // in band: resets the timer
        s.Update(12f, 11.1f, 0.2f);
        Assert.Equal(1f, s.Scale);
    }

    [Fact]
    public void InvalidTiming_IsIgnored()
    {
        var s = new ViewportScaler();
        s.Update(0f, 11.1f, 1f);
        s.Update(20f, 0f, 1f);
        s.Update(float.NaN, 11.1f, 1f);
        Assert.Equal(1f, s.Scale);
    }

    [Fact]
    public void FitRect_LetterboxesTheShorterSide()
    {
        // Portrait eye (922x1032) into a 16:9 window: full height, bars left and right.
        Vector4 r = MirrorMath.FitRect(922f / 1032f, 16f / 9f);
        TestUtil.Near(0f, r.Y, 1e-5f);
        TestUtil.Near(1f, r.W, 1e-5f);
        TestUtil.Near(922f / 1032f / (16f / 9f), r.Z, 1e-5f);
        TestUtil.Near((1f - r.Z) / 2f, r.X, 1e-5f);

        // Wide source into a square window: full width, bars top and bottom.
        r = MirrorMath.FitRect(2f, 1f);
        TestUtil.Near(1f, r.Z, 1e-5f);
        TestUtil.Near(0.5f, r.W, 1e-5f);
        TestUtil.Near(0.25f, r.Y, 1e-5f);

        // Degenerate sizes fill the window.
        r = MirrorMath.FitRect(0f, 1f);
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), r);
    }
}
