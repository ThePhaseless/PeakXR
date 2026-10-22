namespace PeakXR.Core.Tests;

public class BodyHoldTests
{
    const float Dt = 1f / 50f;
    static readonly Vector2 Start = new(22.7f, -373.1f);

    [Fact]
    public void NotLeaning_NoForce_AnchorFollows()
    {
        var h = new BodyHold();
        Assert.Equal(Vector2.Zero, h.Update(Start, Vector2.Zero, false, true, Dt));
        Assert.Equal(
            Vector2.Zero,
            h.Update(Start + new Vector2(0.05f, 0f), Vector2.Zero, false, true, Dt)
        );
        Assert.False(h.Holding);
    }

    [Fact]
    public void Leaning_PullsTheCreepBack()
    {
        // Run 30: the body crept 0.02-0.03 m sideways during a held lean.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start + new Vector2(0.02f, 0f), Vector2.Zero, true, true, Dt);
        Assert.True(a.X < 0f && MathF.Abs(a.Y) < 1e-6f, $"accel {a}");
        TestUtil.Near(-h.Stiffness * 0.02f, a.X, 1e-4f);
    }

    [Fact]
    public void Leaning_DampsTheCreepSpeed()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start, new Vector2(0f, 0.05f), true, true, Dt);
        Assert.True(a.Y < 0f, $"accel {a}");
    }

    [Fact]
    public void ForceIsClamped()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var a = h.Update(Start + new Vector2(0.09f, 0f), new Vector2(3f, 0f), true, true, Dt);
        Assert.True(a.Length() <= h.MaxAccel + 1e-5f);
    }

    [Fact]
    public void APush_IsLetGo()
    {
        // Past MaxOffset it is a real push or a slide: re-anchor there instead of pulling the body back.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var far = Start + new Vector2(h.MaxOffset + 0.05f, 0f);
        Assert.Equal(Vector2.Zero, h.Update(far, Vector2.Zero, true, true, Dt));
        TestUtil.Near(far.X, h.Anchor.X);
    }

    [Fact]
    public void NotFree_Releases_AndReanchors()
    {
        // Stick input or the room-scale follow walks the body: never fight it, and hold the new spot after.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        var walked = Start + new Vector2(0f, 0.3f);
        Assert.Equal(Vector2.Zero, h.Update(walked, new Vector2(0f, 1f), true, false, Dt));
        Assert.False(h.Holding);
        h.Update(walked, Vector2.Zero, true, true, Dt);
        TestUtil.Near(walked.Y, h.Anchor.Y);
    }

    [Fact]
    public void KeepsHoldingBriefly_AfterStandingUp()
    {
        // Run 28/30: the body crept 0.07 m while and after standing up from a lean.
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        for (float t = 0f; t < h.ReleaseSeconds * 0.9f; t += Dt)
            h.Update(Start, Vector2.Zero, false, true, Dt);
        Assert.True(h.Holding);
        Assert.NotEqual(
            Vector2.Zero,
            h.Update(Start + new Vector2(0.02f, 0f), Vector2.Zero, false, true, Dt)
        );
        for (float t = 0f; t < h.ReleaseSeconds; t += Dt)
            h.Update(Start, Vector2.Zero, false, true, Dt);
        Assert.False(h.Holding);
    }

    [Fact]
    public void NonFinite_NoForce()
    {
        var h = new BodyHold();
        h.Update(Start, Vector2.Zero, true, true, Dt);
        Assert.Equal(
            Vector2.Zero,
            h.Update(new Vector2(float.NaN, 0f), Vector2.Zero, true, true, Dt)
        );
        var a = h.Update(Start, new Vector2(float.NaN, 0f), true, true, Dt);
        Assert.True(float.IsFinite(a.X) && float.IsFinite(a.Y));
    }
}
