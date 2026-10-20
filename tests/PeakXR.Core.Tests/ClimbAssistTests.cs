namespace PeakXR.Core.Tests;

public class ClimbAssistTests
{
    static readonly Vector3 Normal = new(0, 0, -1);

    [Fact]
    public void Grace_KeepsClimbBrieflyWithNoHandOnTheWall()
    {
        var g = new ClimbGrace { Grace = 0.25f };
        Assert.True(g.Update(true, 0.1f));
        Assert.True(g.Update(false, 0.1f));
        Assert.True(g.Update(false, 0.1f));
        Assert.False(g.Update(false, 0.1f));
    }

    [Fact]
    public void Grace_RegripRestartsTheWindow()
    {
        var g = new ClimbGrace { Grace = 0.25f };
        g.Update(false, 0.2f);
        Assert.True(g.Update(true, 0.1f));
        Assert.True(g.Update(false, 0.2f));
    }

    [Fact]
    public void Grace_ExtendLengthensTheCurrentWindowOnly()
    {
        var g = new ClimbGrace { Grace = 0.2f };
        g.Extend(0.6f);
        Assert.True(g.Update(false, 0.5f));
        Assert.False(g.Update(false, 0.2f));
        g.Update(true, 0.1f);
        Assert.False(g.Update(false, 0.3f));
    }

    [Fact]
    public void Lunge_FastUpwardRelease_GivesUpStick()
    {
        var dir = ClimbInput.Lunge(new Vector3(0, 3f, 0), Normal, 2f);
        TestUtil.Near(0f, dir.X);
        TestUtil.Near(1f, dir.Y);
    }

    [Fact]
    public void Lunge_DiagonalRelease_KeepsSideComponent()
    {
        var dir = ClimbInput.Lunge(new Vector3(2f, 2f, 0), Normal, 1.5f);
        TestUtil.Near(1f, dir.Length());
        Assert.True(dir.X > 0.5f && dir.Y > 0.5f);
    }

    [Fact]
    public void Lunge_SlowOrDownwardOrSidewaysRelease_IsNone()
    {
        Assert.Equal(Vector2.Zero, ClimbInput.Lunge(new Vector3(0, 1f, 0), Normal, 2f));
        Assert.Equal(Vector2.Zero, ClimbInput.Lunge(new Vector3(0, -4f, 0), Normal, 2f));
        Assert.Equal(Vector2.Zero, ClimbInput.Lunge(new Vector3(4f, 0.5f, 0), Normal, 2f));
    }

    [Theory]
    [InlineData(90f, true)]
    [InlineData(50f, true)]
    [InlineData(45f, false)]
    [InlineData(170f, true)]
    [InlineData(175f, false)]
    [InlineData(0f, false)]
    public void GrabAngle_MatchesVanillaLimits(float angleFromUp, bool ok)
    {
        Assert.Equal(ok, ClimbRules.GrabAngleOk(angleFromUp));
    }

    [Fact]
    public void HoldKind_ReadsSurfaceRules()
    {
        Assert.Equal(HoldKind.Normal, ClimbRules.Kind(false, false, false, 1f));
        Assert.Equal(HoldKind.Hazard, ClimbRules.Kind(true, true, true, 1f));
        Assert.Equal(HoldKind.Slippery, ClimbRules.Kind(true, true, false, 1f));
        Assert.Equal(HoldKind.Easy, ClimbRules.Kind(true, false, false, 0.3f));
        Assert.Equal(HoldKind.Normal, ClimbRules.Kind(true, false, false, 1f));
    }

    [Fact]
    public void SlideSpeed_CountsDownwardSlideAndSlipperySurfaces()
    {
        TestUtil.Near(3f, ClimbRules.SlideSpeed(-2f, false, 1f, 1.5f));
        TestUtil.Near(6f, ClimbRules.SlideSpeed(0f, true, 0.5f, 2f));
        TestUtil.Near(8f, ClimbRules.SlideSpeed(-2f, true, 0.5f, 2f));
        TestUtil.Near(0f, ClimbRules.SlideSpeed(3f, false, 1f, 1f));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(30f, true)]
    [InlineData(49f, true)]
    [InlineData(50f, false)]
    [InlineData(90f, false)]
    public void LedgeTop_IsWhereVanillaLetsGoWhilePushing(float angleFromUp, bool top)
    {
        Assert.Equal(top, ClimbRules.IsLedgeTop(angleFromUp));
    }

    [Fact]
    public void WalkStick_MapsWorldDirectionToLookRelativeStick()
    {
        var fwd = new Vector3(0, 0, 1);
        var right = new Vector3(1, 0, 0);
        var s = ClimbInput.WalkStick(new Vector3(0, 0, 1), fwd, right);
        TestUtil.Near(0f, s.X);
        TestUtil.Near(1f, s.Y);
        s = ClimbInput.WalkStick(new Vector3(1, 0, 0), fwd, right);
        TestUtil.Near(1f, s.X);
        TestUtil.Near(0f, s.Y);
    }

    [Fact]
    public void WalkStick_IgnoresPitchAndHeight()
    {
        var s = ClimbInput.WalkStick(
            new Vector3(0, 5f, 2f),
            new Vector3(0, -0.5f, 1f),
            new Vector3(1, 0, 0)
        );
        TestUtil.Near(0f, s.X);
        TestUtil.Near(1f, s.Y);
    }

    [Fact]
    public void WalkStick_TurnedLook_RotatesStick()
    {
        // Looking along +X, right is -Z (Unity is left-handed); a +Z direction is full left.
        var s = ClimbInput.WalkStick(
            new Vector3(0, 0, 1),
            new Vector3(1, 0, 0),
            new Vector3(0, 0, -1)
        );
        TestUtil.Near(-1f, s.X);
        TestUtil.Near(0f, s.Y);
    }

    [Fact]
    public void WalkStick_VerticalDirection_IsZero()
    {
        var s = ClimbInput.WalkStick(
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(1, 0, 0)
        );
        Assert.Equal(Vector2.Zero, s);
    }
}
