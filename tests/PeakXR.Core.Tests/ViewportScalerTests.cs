namespace PeakXR.Core.Tests;

public class ViewportScalerTests
{
    [Fact]
    public void OverBudget_StepsDownAfterDelay_ToMin()
    {
        var s = new ViewportScaler { Min = 0.7f, Step = 0.1f, DownSeconds = 0.2f };
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
        var s = new ViewportScaler { Min = 0.5f, Step = 0.1f, UpSeconds = 1f };
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
    public void EnvelopeUv_CropsTheLongerSide()
    {
        // Square eye into a 16:9 window: crop top and bottom.
        Vector4 uv = MirrorMath.EnvelopeUv(1f, 16f / 9f);
        TestUtil.Near(0f, uv.X, 1e-5f);
        TestUtil.Near(1f, uv.Z, 1e-5f);
        TestUtil.Near(9f / 16f, uv.W, 1e-5f);
        TestUtil.Near((1f - 9f / 16f) / 2f, uv.Y, 1e-5f);

        // Wide source into a square window: crop the sides.
        uv = MirrorMath.EnvelopeUv(2f, 1f);
        TestUtil.Near(0.5f, uv.Z, 1e-5f);
        TestUtil.Near(0.25f, uv.X, 1e-5f);
        TestUtil.Near(1f, uv.W, 1e-5f);

        // Degenerate sizes show the whole image.
        uv = MirrorMath.EnvelopeUv(0f, 1f);
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), uv);
    }
}
