namespace PeakXR.Core.Tests;

public class FingerGunTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void OnlyAnEmptyHandPings_AHoldingHandsTriggerIsUse(
        bool holdsItem,
        bool steadiesItem,
        bool mayFire
    )
    {
        Assert.Equal(mayFire, FingerGun.HandMayFire(holdsItem, steadiesItem));
    }

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
    public void LightTriggerPress_KeepsAimingUntilTheSqueeze()
    {
        // Tester: a light press of the trigger turned the ping laser off. A partly pulled trigger is the squeeze
        // on its way, so the aim holds past the window and the full press still fires.
        var g = new FingerGun { Window = 0.3f };
        g.Update(true, true, false, 0.1f);
        for (int i = 0; i < 20; i++)
        {
            Assert.False(g.Update(false, true, false, 0.1f, triggerResting: true));
            Assert.True(g.Aiming);
        }
        Assert.True(g.Update(false, true, true, 0.05f, triggerResting: true));
        Assert.False(g.Aiming);
    }

    [Fact]
    public void LightPressLetGo_WindowRunsAgain()
    {
        var g = new FingerGun { Window = 0.3f };
        g.Update(true, true, false, 0.1f);
        g.Update(false, true, false, 1f, triggerResting: true);
        g.Update(false, true, false, 0.4f);
        Assert.False(g.Aiming);
    }

    [Fact]
    public void OpeningTheGrip_EndsALightPressAim()
    {
        var g = new FingerGun();
        g.Update(true, true, false, 0.1f);
        g.Update(false, false, false, 0.1f, triggerResting: true);
        Assert.False(g.Aiming);
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

    [Fact]
    public void Aiming_WhilePointingAndThroughTheSqueeze_NotAfterFiring()
    {
        var g = new FingerGun();
        Assert.False(g.Aiming);
        g.Update(true, true, false, 0.1f);
        Assert.True(g.Aiming);
        // The index curls onto the trigger: still aiming for the window, so the preview does not blink off.
        g.Update(false, true, false, 0.1f);
        Assert.True(g.Aiming);
        Assert.True(g.Update(false, true, true, 0.05f));
        Assert.False(g.Aiming);
    }

    [Fact]
    public void Aiming_EndsWhenTheGripOpensOrTheWindowPasses()
    {
        var g = new FingerGun { Window = 0.3f };
        g.Update(true, true, false, 0.1f);
        g.Update(false, false, false, 0.01f);
        Assert.False(g.Aiming);
        g.Update(true, true, false, 0.1f);
        g.Update(false, true, false, 0.4f);
        Assert.False(g.Aiming);
    }

    // Pointing pose with the thumb lifted (0) or resting on the stick or face buttons (1).
    static FingerCurls Point(float thumb) => new(thumb, 0.1f, 0.9f, 0.9f, 0.9f);

    [Fact]
    public void FingerGun_NeedsTheThumbUp()
    {
        // External review #13: a grip with the index straight and the thumb on the stick is a common resting habit;
        // only a lifted thumb makes it the ping's finger gun.
        Assert.True(GripIntent.IsFingerGun(Point(0f)));
        Assert.False(GripIntent.IsFingerGun(Point(1f)));
        Assert.False(GripIntent.IsFingerGun(new FingerCurls(0f, 0.9f, 0.9f, 0.9f, 0.9f)));
    }

    [Fact]
    public void PointedInteract_WaitsOnlyForAFingerGun()
    {
        // Thumb down: a pointed interact, at once. Thumb up with pinging live: a ping aim, so the interact waits.
        Assert.False(GripIntent.PointedInteractWaits(Point(1f), pingLive: true));
        Assert.True(GripIntent.PointedInteractWaits(Point(0f), pingLive: true));
        Assert.False(GripIntent.PointedInteractWaits(Point(0f), pingLive: false));
    }
}
