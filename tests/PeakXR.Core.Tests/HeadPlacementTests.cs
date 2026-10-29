namespace PeakXR.Core.Tests;

public class RaisedViewTests
{
    static float Degrees(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f))
        * 180f
        / MathF.PI;

    static (Vector3 Gaze, Vector3 Up) Head(float pitch, float yaw, float roll)
    {
        var q = UnityQuat.Euler(pitch, yaw, roll);
        return (UnityQuat.Rotate(q, Vector3.UnitZ), UnityQuat.Rotate(q, Vector3.UnitY));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(-60f, 40f, 0f)]
    [InlineData(60f, -120f, 0f)]
    [InlineData(0f, 90f, 30f)]
    [InlineData(-60f, 10f, 30f)]
    [InlineData(89f, 0f, 0f)]
    public void RaisedAboveTheGaze_TowardTheHeadsUp_AndUprightInView(
        float pitch,
        float yaw,
        float roll
    )
    {
        var (gaze, up) = Head(pitch, yaw, roll);
        var (dir, cardUp) = PanelMath.RaisedView(gaze, up, 22f);
        TestUtil.Near(1f, dir.Length(), 1e-4f);
        TestUtil.Near(1f, cardUp.Length(), 1e-4f);
        TestUtil.Near(22f, Degrees(gaze, dir), 0.05f);
        // Raised along the head's own up: no sideways part in the head's frame, whatever the roll.
        var right = Vector3.Cross(up, gaze);
        TestUtil.Near(0f, Vector3.Dot(dir, right), 1e-4f);
        Assert.True(Vector3.Dot(dir, up) > 0f);
        // Its plane faces the eye (up lies in it) and its up is the head's, turned by the raise only.
        TestUtil.Near(0f, Vector3.Dot(dir, cardUp), 1e-4f);
        TestUtil.Near(22f, Degrees(up, cardUp), 0.05f);
        TestUtil.Near(0f, Vector3.Dot(cardUp, right), 1e-4f);
    }

    [Fact]
    public void UpAlongTheGaze_FallsBackToWorldUp_WithoutNaN()
    {
        var (dir, up) = PanelMath.RaisedView(Vector3.UnitZ, Vector3.UnitZ, 10f);
        Assert.False(float.IsNaN(dir.X) || float.IsNaN(up.Y));
        Assert.True(dir.Y > 0f);
        TestUtil.Near(0f, Vector3.Dot(dir, up), 1e-4f);
    }
}

public class EyeFollowTests
{
    [Fact]
    public void InsideTheDeadZone_TheEyePointStays()
    {
        var f = new EyeFollow();
        var eye = Vector3.Zero;
        for (int i = 0; i < 90; i++)
            eye = f.Update(eye, new Vector3(0.3f, 0f, 0.2f), false, 1f / 90f);
        Assert.Equal(Vector3.Zero, eye);
        Assert.False(f.Gliding);
    }

    [Fact]
    public void AViewCarriedAway_IsCaughtUpWithinTheBudget_ThenStops()
    {
        var f = new EyeFollow();
        var eye = Vector3.Zero;
        var head = new Vector3(3f, 0f, 0f);
        float t = 0f;
        while (t < LazyFollow.CatchUpBudget && (f.Gliding || t == 0f))
        {
            eye = f.Update(eye, head, false, 1f / 90f);
            t += 1f / 90f;
        }
        Assert.False(f.Gliding);
        Assert.True(Vector3.Distance(eye, head) <= f.Settle);
        // Settled: a small lean afterwards leaves it where it is.
        var settled = eye;
        eye = f.Update(eye, head + new Vector3(0.2f, 0f, 0f), false, 1f / 90f);
        Assert.Equal(settled, eye);
    }

    [Fact]
    public void Held_NeverMoves()
    {
        var f = new EyeFollow();
        var eye = f.Update(Vector3.Zero, new Vector3(3f, 0f, 0f), true, 0.1f);
        Assert.Equal(Vector3.Zero, eye);
        Assert.False(f.Gliding);
    }
}
