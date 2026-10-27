namespace PeakXR.Core.Tests;

public class HandScaleTests
{
    [Fact]
    public void Default_DrawsThePalmAtARealHandsWidthThroughTheWorldScale()
    {
        Assert.Equal(0.55f, HandScale.Default, 3);
        // Seen through the rig's growth, the drawn palm is an adult's 8.5 cm across.
        float seen =
            HandScale.ScoutPalmWidth * HandScale.Default / WorldScale.RigScale(WorldScale.Default);
        TestUtil.Near(0.085f, seen, 0.005f);
    }

    [Fact]
    public void LifeSize_IsTheMedianRatioThroughTheRig_RoundedUpToFiveHundredths()
    {
        // Median of 0.48, 0.28, 0.42, 0.93 is 0.45; at world scale 1 that is below the range and clamps up.
        TestUtil.Near(0.5f, HandScale.LifeSize(HandScale.References, 1f));
        // 0.45 x 1.149 = 0.517, rounded up toward the bigger, more readable hand.
        TestUtil.Near(0.55f, HandScale.LifeSize(HandScale.References, 1f / 0.87f));
        // An exact step stays put (no float creep to the next one).
        TestUtil.Near(0.6f, HandScale.LifeSize(new[] { (1f, 0.6f) }, 1f));
        TestUtil.Near(1f, HandScale.LifeSize(new[] { (1f, 2f) }, 1f));
    }

    [Fact]
    public void Clamp_KeepsTheSettingInRange()
    {
        Assert.Equal(HandScale.Min, HandScale.Clamp(0.1f));
        Assert.Equal(HandScale.Max, HandScale.Clamp(3f));
        Assert.Equal(HandScale.Max, HandScale.Clamp(float.NaN));
        Assert.Equal(0.7f, HandScale.Clamp(0.7f));
    }

    [Fact]
    public void UpperArm_TapersHalfwayBetweenTheBodyAndTheHand()
    {
        Assert.Equal(1f, HandScale.UpperArm(1f));
        TestUtil.Near(0.775f, HandScale.UpperArm(0.55f));
    }

    [Fact]
    public void Ghost_MatchesTheDrawnHand()
    {
        // The ghost is modelled on a real 8.5 cm palm: at the default it is as big as the drawn palm, which is what
        // the rig's real-size scale gave it before.
        TestUtil.Near(
            WorldScale.RigScale(WorldScale.Default),
            HandScale.Ghost(HandScale.Default, 0.085f),
            0.06f
        );
        // At game size it grows with the scout's chunky hand.
        TestUtil.Near(0.178f / 0.085f, HandScale.Ghost(1f, 0.085f));
    }

    [Fact]
    public void Wrap_SmallerHandOpensOnTheSameItem()
    {
        const float can = 0.05f;
        Assert.Equal(HandPose.WrapCurl(can), HandScale.Wrap(can, 1f));
        Assert.True(HandScale.Wrap(can, 0.55f) < HandScale.Wrap(can, 1f));
        // A thin handle still gets a full fist.
        Assert.Equal(1f, HandScale.Wrap(0.01f, 0.55f));
    }

    [Fact]
    public void HandPoseWrap_StopsEveryFingerAtTheSurface()
    {
        var c = HandPose.Wrap(new FingerCurls(0.9f, 0.2f, 1f, 1f, 0.5f), 0.6f);
        Assert.Equal(0.6f, c.Thumb);
        Assert.Equal(0.2f, c.Index);
        Assert.Equal(0.6f, c.Middle);
        Assert.Equal(0.5f, c.Pinky);
    }
}
