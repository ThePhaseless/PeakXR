namespace PeakXR.Core.Tests;

public class PackDropTests
{
    [Fact]
    public void AFreePocket_TakesTheItem() =>
        Assert.Equal(DenyReason.None, PackDrop.Refusal(false, DenyReason.CantSwitch, true, 2));

    [Fact]
    public void NoFreePocket_IsPackFull() =>
        Assert.Equal(DenyReason.PackFull, PackDrop.Refusal(false, DenyReason.CantSwitch, true, -1));

    [Fact]
    public void AnItemTheWheelRefuses_CantGoIn() =>
        Assert.Equal(
            DenyReason.CantHolster,
            PackDrop.Refusal(false, DenyReason.CantSwitch, false, 0)
        );

    [Fact]
    public void ABusyHand_SaysWhyFirst() =>
        Assert.Equal(DenyReason.TooSoon, PackDrop.Refusal(true, DenyReason.TooSoon, false, -1));
}
