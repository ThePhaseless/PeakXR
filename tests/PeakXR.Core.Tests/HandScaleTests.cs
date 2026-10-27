namespace PeakXR.Core.Tests;

public class HandScaleTests
{
    [Fact]
    public void Default_ReadsAsARealHandAtOneToOne()
    {
        // The world scale follows the player's real eye height (about 1), so the hand is derived at 1:1.
        Assert.Equal(0.5f, HandScale.Default, 3);
        // Drawn over real for width, palm thickness, finger thickness and length: their geometric mean is just
        // over 1 (rounded up toward the bigger hand), and none is off by more than 2x either way.
        float log = 0f;
        foreach (var (game, real) in HandScale.References)
        {
            float r = game * HandScale.Default / real;
            Assert.InRange(r, 0.5f, 2f);
            log += MathF.Log(r);
        }
        Assert.InRange(MathF.Exp(log / HandScale.References.Length), 1f, 1.1f);
    }

    [Fact]
    public void LifeSize_IsTheGeometricMeanRatioThroughTheRig_RoundedUpToFiveHundredths()
    {
        // Geometric mean of 0.48, 0.28, 0.42, 0.93 is 0.48: no one ratio decides.
        TestUtil.Near(0.5f, HandScale.LifeSize(HandScale.References, 1f));
        // Through a grown rig (world scale 0.87) the hand may be drawn bigger.
        TestUtil.Near(0.6f, HandScale.LifeSize(HandScale.References, 1f / 0.87f));
        // An exact step stays put (no float creep to the next one).
        TestUtil.Near(0.6f, HandScale.LifeSize(new[] { (1f, 0.6f) }, 1f));
        TestUtil.Near(1f, HandScale.LifeSize(new[] { (1f, 2f) }, 1f));
        // The extremes balance: a 4x and a 1/4x ratio cancel.
        TestUtil.Near(1f, HandScale.LifeSize(new[] { (1f, 4f), (4f, 1f) }, 1f));
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
        // The ghost is modelled on a real 8.5 cm palm: at the default the drawn palm is about that wide too.
        TestUtil.Near(1f, HandScale.Ghost(HandScale.Default, 0.085f), 0.06f);
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
