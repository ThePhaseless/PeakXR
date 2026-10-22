namespace PeakXR.Core.Tests;

public class ViewVerticalTests
{
    [Fact]
    public void Airborne_KeepsTheViewsRate() =>
        Assert.Equal(20f, RigMath.ViewVerticalRate(20f, false, false));

    [Fact]
    public void Grounded_CapsItAsBefore() =>
        Assert.Equal(RigMath.GroundedVerticalRate, RigMath.ViewVerticalRate(20f, true, false));

    [Fact]
    public void FollowWalk_HoldsTheViewsHeight() =>
        Assert.Equal(RigMath.FollowVerticalRate, RigMath.ViewVerticalRate(20f, true, true));

    /// <summary>The eye anchor's height through <see cref="RigMath.Follow"/> at 25 Hz (run 39's 40 ms frames) under a
    /// hip bobbing 3 cm at 2 Hz for a second, as a room-scale follow walks the body under a still head.</summary>
    static float Bob(float verticalRate)
    {
        var anchor = new Vector3(0f, 1.6f, 0f);
        float min = anchor.Y,
            max = anchor.Y;
        for (float t = 0f; t < 1.2f; t += 0.04f)
        {
            var goal = new Vector3(0f, 1.6f - 0.03f * MathF.Abs(MathF.Sin(t * 2f * MathF.PI)), 0f);
            anchor = RigMath.Follow(anchor, goal, 0.04f, 20f, verticalRate, 1f);
            min = MathF.Min(min, anchor.Y);
            max = MathF.Max(max, anchor.Y);
        }
        return max - min;
    }

    [Fact]
    public void FollowWalkBob_DoesNotReachTheView()
    {
        // Run 39 far lean: the view range grew 0.006 -> 0.051 m over f12861-f12871 while x/z of the origin stayed
        // put to the millimetre: it moved vertically, with the walk (input 0.9 -> 0) and its settle.
        Assert.True(Bob(RigMath.GroundedVerticalRate) > 0.015f, "the grounded rate passes the bob");
        float held = Bob(RigMath.ViewVerticalRate(20f, true, true));
        Assert.True(held < 0.01f, $"view bobbed {held:0.000} m");
    }
}
