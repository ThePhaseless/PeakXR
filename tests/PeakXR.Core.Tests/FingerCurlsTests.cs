namespace PeakXR.Core.Tests;

public class FingerCurlsTests
{
    [Fact]
    public void Map_OpenHandWhenNothingTouched()
    {
        var c = FingerCurlMapper.Map(0f, false, 0f, false);
        Assert.Equal(default, c);
    }

    [Fact]
    public void Map_TriggerDrivesIndex_TouchGivesPartialCurl()
    {
        Assert.Equal(
            FingerCurlMapper.TriggerTouchCurl,
            FingerCurlMapper.Map(0f, true, 0f, false).Index
        );
        Assert.Equal(1f, FingerCurlMapper.Map(1f, true, 0f, false).Index);
        TestUtil.Near(0.5f, FingerCurlMapper.Map(0.5f, false, 0f, false).Index, 1e-6f);
        Assert.True(FingerCurlMapper.Map(0.5f, true, 0f, false).Index > 0.5f);
    }

    [Fact]
    public void Map_GripDrivesLastThreeFingers()
    {
        var c = FingerCurlMapper.Map(0f, false, 0.8f, false);
        Assert.Equal(0.8f, c.Middle);
        Assert.Equal(0.8f, c.Ring);
        Assert.Equal(0.8f, c.Pinky);
        Assert.Equal(0f, c.Index);
    }

    [Fact]
    public void Map_ThumbTouchCurlsThumb()
    {
        Assert.Equal(
            FingerCurlMapper.ThumbTouchCurl,
            FingerCurlMapper.Map(0f, false, 0f, true).Thumb
        );
    }

    [Fact]
    public void Map_ClampsInputs()
    {
        var c = FingerCurlMapper.Map(2f, false, -1f, false);
        Assert.Equal(1f, c.Index);
        Assert.Equal(0f, c.Middle);
    }

    [Fact]
    public void Map_SkeletalCurlsWin_PerFinger_NaNFallsBack()
    {
        var skel = new[] { 0.1f, 0.2f, float.NaN, 0.4f, 0.5f };
        var c = FingerCurlMapper.Map(1f, true, 1f, true, skel);
        Assert.Equal(0.1f, c.Thumb);
        Assert.Equal(0.2f, c.Index);
        Assert.Equal(1f, c.Middle);
        Assert.Equal(0.4f, c.Ring);
        Assert.Equal(0.5f, c.Pinky);
    }

    [Fact]
    public void Indexer_ReadsAndWritesFingersInOrder()
    {
        var c = new FingerCurls(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        Assert.Equal(0.3f, c[2]);
        c[4] = 0.9f;
        Assert.Equal(0.9f, c.Pinky);
    }

    [Fact]
    public void Smoother_ApproachesTargetExponentially()
    {
        var s = new FingerCurlSmoother { Rate = 10f };
        var target = new FingerCurls(1f, 1f, 1f, 1f, 1f);
        var c = s.Update(target, 0.1f);
        TestUtil.Near(1f - MathF.Exp(-1f), c.Index, 1e-4f);
        for (int i = 0; i < 200; i++)
            c = s.Update(target, 0.1f);
        TestUtil.Near(1f, c.Pinky, 1e-4f);
    }

    [Fact]
    public void Smoother_NonPositiveRateSnaps()
    {
        var s = new FingerCurlSmoother { Rate = 0f };
        var target = new FingerCurls(0.3f, 0.6f, 0.9f, 0.2f, 0.1f);
        Assert.Equal(target, s.Update(target, 0.01f));
    }
}
