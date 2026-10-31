namespace PeakXR.Core.Tests;

public class SlideCueTests
{
    const float Dt = 1f / 90f;

    static bool Run(SlideCue s, float seconds, bool flag, bool grounded, float downSpeed)
    {
        bool fired = false;
        for (float t = 0f; t < seconds; t += Dt)
            fired |= s.Update(flag, grounded, downSpeed, Dt);
        return fired;
    }

    [Fact]
    public void SlidingDownASteepSlope_FiresOnce()
    {
        var s = new SlideCue();
        Assert.True(Run(s, 0.5f, true, false, 2f));
        Assert.False(Run(s, 1f, true, false, 2f));
    }

    [Fact]
    public void AStepEdgeUnderStandingFeet_IsNotASlide()
    {
        // vr.63: the game's hip ray met a steep edge (a rock lip, debris) while the body stood on the beach.
        Assert.False(Run(new SlideCue(), 2f, true, true, 0f));
    }

    [Fact]
    public void ABriefSteepHit_IsNotASlide()
    {
        var s = new SlideCue();
        Assert.False(Run(s, 0.1f, true, false, 2f));
        Assert.False(Run(s, 1f, false, false, 2f));
    }

    [Fact]
    public void SteepButNotMovingDown_IsNotASlide()
    {
        Assert.False(Run(new SlideCue(), 1f, true, false, 0.1f));
    }

    [Fact]
    public void ANewSlideAfterARest_FiresAgain()
    {
        var s = new SlideCue();
        Assert.True(Run(s, 0.5f, true, false, 2f));
        Assert.False(Run(s, 1.5f, false, true, 0f));
        Assert.True(Run(s, 0.5f, true, false, 2f));
    }
}

public class BodyCueHandTests
{
    [Fact]
    public void FreePreferredHand_GetsIt()
    {
        Assert.True(BodyCueHand.Pick(true, false, false, false, false, out bool left));
        Assert.True(left);
    }

    [Fact]
    public void BusyPreferredHand_HandsItToTheFreeOne()
    {
        // The right hand grips a sticky item: the body's refusal goes to the free left hand.
        Assert.True(BodyCueHand.Pick(false, false, true, false, false, out bool left));
        Assert.True(left);
    }

    [Fact]
    public void BothBusy_PreferredUnlessItsOwnCueIsUp()
    {
        Assert.True(BodyCueHand.Pick(false, true, true, false, false, out bool left));
        Assert.False(left);
        // The hand's own refusal is showing: the body cue yields, so it is not replaced.
        Assert.False(BodyCueHand.Pick(false, true, true, false, true, out _));
    }

    [Fact]
    public void FreeHandWithItsOwnCueUp_FallsBackToTheOther()
    {
        Assert.True(BodyCueHand.Pick(true, false, false, true, false, out bool left));
        Assert.False(left);
    }
}
