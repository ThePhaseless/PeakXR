namespace PeakXR.Core.Tests;

public class FingerGunTests
{
    [Fact]
    public void PointThenSqueeze_Fires()
    {
        var g = new FingerGun();
        Assert.False(g.Update(pointing: true, gripHeld: true, triggerPressed: false, 0.1f));
        Assert.True(g.Update(pointing: false, gripHeld: true, triggerPressed: true, 0.05f));
    }

    [Fact]
    public void SqueezeWithoutPointingFirst_DoesNotFire()
    {
        var g = new FingerGun();
        Assert.False(g.Update(false, true, false, 0.1f));
        Assert.False(g.Update(false, true, true, 0.1f));
    }

    [Fact]
    public void PointedTooLongAgo_DoesNotFire()
    {
        var g = new FingerGun { Window = 0.3f };
        g.Update(true, true, false, 0.1f);
        g.Update(false, true, false, 0.4f);
        Assert.False(g.Update(false, true, true, 0.05f));
    }

    [Fact]
    public void OpeningTheGrip_DisarmsIt()
    {
        var g = new FingerGun();
        g.Update(true, true, false, 0.1f);
        g.Update(false, false, false, 0.01f);
        Assert.False(g.Update(false, true, true, 0.01f));
    }
}
