namespace PeakXR.Core.Tests;

public class StatueTouchTests
{
    private static readonly Vector3 Face = new(0, 1, 1);

    [Fact]
    public void ASpentStatueIsUsedUp() =>
        Assert.Equal(DenyReason.UsedUp, StatueTouch.Reason(true, true, true));

    [Fact]
    public void NobodyDownStillRunsTheCastForItems() =>
        Assert.Equal(DenyReason.None, StatueTouch.Reason(false, false, false));

    [Fact]
    public void SomeoneDownWithRevivesAllowedRevives() =>
        Assert.Equal(DenyReason.None, StatueTouch.Reason(false, true, true));

    [Fact]
    public void SomeoneDownInARunWithoutRevivesSaysSo() =>
        Assert.Equal(DenyReason.NoRevive, StatueTouch.Reason(false, true, false));

    [Fact]
    public void AFreePalmOnTheFaceTouches() =>
        Assert.True(StatueTouch.HandOn(Vector3.UnitZ, new Vector3(0, 1, 0.97f), Face, true));

    [Fact]
    public void AHandHoldingSomethingDoesNotTouch() =>
        Assert.False(StatueTouch.HandOn(Vector3.UnitZ, new Vector3(0, 1, 0.97f), Face, false));

    [Fact]
    public void AHandAwayFromTheFaceDoesNotTouch() =>
        Assert.False(StatueTouch.HandOn(Vector3.UnitZ, new Vector3(0, 1, 0.8f), Face, true));

    [Fact]
    public void AGripThatTookTheStatueHoldsAtZeroOnceTheHandIsOff()
    {
        Assert.True(StatueTouch.HoldsAtZero(touching: false, gripOnStatue: true));
        Assert.False(StatueTouch.HoldsAtZero(touching: true, gripOnStatue: true));
    }

    [Fact]
    public void APointedOrHeadAimedHoldKeepsTheVanillaHold() =>
        Assert.False(StatueTouch.HoldsAtZero(touching: false, gripOnStatue: false));
}
