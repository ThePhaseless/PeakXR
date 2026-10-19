namespace PeakXR.Core.Tests;

public class ClimbInputTests
{
    // Wall in front of a player facing +Z: the surface normal points back at the player.
    static readonly Vector3 Normal = new(0, 0, -1);

    [Fact]
    public void MovingUp_IsPositiveY()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(0, 1, 0), Normal, climbSpeed: 2f, deadzone: 0.05f);
        TestUtil.Near(0.5f, input.Y);
        TestUtil.Near(0f, input.X);
    }

    [Fact]
    public void MovingRight_IsPositiveX()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(1, 0, 0), Normal, 2f, 0.05f);
        TestUtil.Near(0.5f, input.X);
        TestUtil.Near(0f, input.Y);
    }

    [Fact]
    public void MotionIntoTheWall_IsIgnored()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(0, 0, 3), Normal, 2f, 0.05f);
        Assert.Equal(Vector2.Zero, input);
    }

    [Fact]
    public void Fast_IsClampedToUnitLength()
    {
        var input = ClimbInput.FromBodyVelocity(new Vector3(10, 10, 0), Normal, 2f, 0.05f);
        TestUtil.Near(1f, input.Length());
    }

    [Fact]
    public void Slow_IsInsideDeadzone()
    {
        Assert.Equal(Vector2.Zero, ClimbInput.FromBodyVelocity(new Vector3(0, 0.05f, 0), Normal, 2f, 0.1f));
    }

    [Fact]
    public void ZeroSpeed_GivesZero()
    {
        Assert.Equal(Vector2.Zero, ClimbInput.FromBodyVelocity(new Vector3(0, 1, 0), Normal, 0f, 0.1f));
    }

    [Fact]
    public void Smooth_ConvergesTowardTarget()
    {
        var v = ClimbInput.Smooth(Vector2.Zero, new Vector2(1, 0), rate: 10f, dt: 0.1f);
        Assert.InRange(v.X, 0.6f, 0.7f);
        Assert.Equal(new Vector2(1, 0), ClimbInput.Smooth(Vector2.Zero, new Vector2(1, 0), 0f, 0.1f));
    }
}
