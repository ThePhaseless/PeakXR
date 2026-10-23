namespace PeakXR.Core.Tests;

public class LidCapGrabOffTests
{
    [Fact]
    public void WithPhysicalGrabOffATwoHandLidKeepsTheVanillaHold() =>
        Assert.False(LidCap.TryFraction(false, 0f, true, false, out _));

    [Fact]
    public void AnUntrackedHoldOfATwoHandLidIsRefused() =>
        Assert.True(LidCap.RefusesUntracked(false, true, true, false));

    [Fact]
    public void AHandOnTheCaseIsLeftToTheOneHandGate() =>
        Assert.False(LidCap.RefusesUntracked(false, true, true, true));

    [Fact]
    public void ATrackedLiftOrAnOrdinaryCaseOrGrabOffIsNotRefused()
    {
        Assert.False(LidCap.RefusesUntracked(true, true, true, false));
        Assert.False(LidCap.RefusesUntracked(false, false, true, false));
        Assert.False(LidCap.RefusesUntracked(false, true, false, false));
    }
}
