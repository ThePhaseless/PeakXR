namespace PeakXR.Core.Tests;

public class LeftHoldFrontTests
{
    // Drawn sizes from the game's meshes (docs/research/item-left-holds.csv).
    static readonly Vector3 Flag = new(1.37f, 0.99f, 0.29f);
    static readonly Vector3 Glizzy = new(0.61f, 1.05f, 0.18f);
    static readonly Vector3 CactusBall = new(0.91f, 0.90f, 0.91f);
    static readonly Vector3 BingBong = new(1.19f, 1.19f, 0.46f);
    static readonly Vector3 ShellBig = new(0.89f, 0.81f, 0.72f);

    [Fact]
    public void HasLongAxis_OnlyWhenTheLongestSideClearlyLeads()
    {
        Assert.True(LeftHold.HasLongAxis(Flag));
        Assert.True(LeftHold.HasLongAxis(Glizzy));
        // A ball, a plush as wide as it is tall, a round shell: no far end to keep.
        Assert.False(LeftHold.HasLongAxis(CactusBall));
        Assert.False(LeftHold.HasLongAxis(BingBong));
        Assert.False(LeftHold.HasLongAxis(ShellBig));
    }

    [Fact]
    public void HasFront_WhenFrontAndBackDiffer()
    {
        // Collider-probe asymmetries across z: a medallion's relief (Amulet_Clone) against a skewer (Glizzy).
        Assert.True(LeftHold.HasFront(0.117f));
        Assert.False(LeftHold.HasFront(0.012f));
    }

    [Fact]
    public void AnItemWithAFront_NeverTakesTheZPlane()
    {
        // Lopsided across x, even front to back by shape but with a front: x through the centre, not the half turn.
        var plane = LeftHold.Choose(
            0.05f,
            0.25f,
            0.3f,
            0.05f,
            0.01f,
            keepForward: LeftHold.HasFront(0.1f)
        );
        Assert.Equal(LeftPlane.CentreX, plane);
    }

    [Fact]
    public void WithoutALongAxis_ThePlainMirrorStays_ItsFrontKept()
    {
        // BingBong in the headset: the plain mirror turned no meaningful "far end", yet the z plane showed its back.
        bool turns = LeftHold.HasLongAxis(BingBong);
        var plane = LeftHold.Choose(
            0.20f,
            0.20f,
            0.08f,
            0.085f,
            0.035f,
            false,
            originTurns: turns,
            xTurns: turns
        );
        Assert.Equal(LeftPlane.Origin, plane);
    }
}
