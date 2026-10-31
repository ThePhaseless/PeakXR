namespace PeakXR.Core.Tests;

public class TurnSkipTests
{
    const float Dt = 1f / 72f;

    // The game turns the body about its mass centre, its eyes 0.236 m ahead; a real head turns about the neck, its
    // eyes 0.1 m ahead. A turned head therefore leaves a debt of 0.136 m x (facing before - facing now).
    const float Swing = 0.236f - 0.1f;

    static Vector3 Facing(float yaw)
    {
        float r = yaw * MathF.PI / 180f;
        return new Vector3(MathF.Sin(r), 0f, MathF.Cos(r));
    }

    static Vector3 TurnDebt(float yaw) => Swing * (Facing(0f) - Facing(yaw));

    /// <summary>What the lean bends for: the share less the skip.</summary>
    static Vector3 Lean(TurnSkip s, Vector3 share, float yaw, bool walking = false) =>
        share - s.Update(share, yaw, walking, Dt);

    [Fact]
    public void Glance_TheLeanNeverBends()
    {
        // vr.302 (run 13): a 70 degree glance out and back put 0.16 m on the debt and the lean bent 9.5 degrees for it.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t < 1.5f; t += Dt)
        {
            float k =
                t < 0.35f ? t / 0.35f
                : t < 0.65f ? 1f
                : t < 1f ? 1f - (t - 0.65f) / 0.35f
                : 0f;
            float yaw = 70f * k;
            var lean = Lean(s, TurnDebt(yaw), yaw);
            Assert.True(lean.Length() < 0.01f, $"t {t:0.00}: lean {lean.Length():0.000} m");
        }
    }

    [Fact]
    public void HeldTurn_IsLeftToTheWalk_ThenTheLeanTakesTheLeftover()
    {
        // vr.45b (run 13): a head held at +90 bent the body 17 degrees before and after the walk. The walk steps under
        // the turn; only what it leaves once it is done is the lean's.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 0.6f; t += Dt)
            Lean(s, TurnDebt(150f * t), 150f * t);
        var debt = TurnDebt(90f);
        for (float t = 0f; t < 1.5f; t += Dt)
            Assert.True(Lean(s, debt, 90f).Length() < 0.01f, $"delay t {t:0.00}");
        // The walk steps under most of it; the lean bends for none of it meanwhile.
        for (float t = 0f; t < 0.5f; t += Dt)
        {
            debt *= 0.9f;
            Assert.True(Lean(s, debt, 90f, walking: true).Length() < 0.01f, $"walk t {t:0.00}");
        }
        // It springs back to 0.1 m and rests: now the lean takes it.
        var left = debt + Vector3.Normalize(debt) * 0.08f;
        Vector3 lean = default;
        for (float t = 0f; t < 2.5f; t += Dt)
            lean = Lean(s, left, 90f);
        Assert.True((lean - left).Length() < 0.01f, $"lean {lean} for {left}");
    }

    [Fact]
    public void AStaleSkip_NeverBendsTheLeanAwayFromTheView()
    {
        // vr.300 (run 13): after a snap turn the walk closed the debt to 0.015 m, but the turn's shift stayed at 0.155 m
        // in the lean's skip, so the lean bent 0.14 m away from the view (6.8 degrees) and the trim chased that.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(-0.09f, 0f, -0.12f), 45f);
        foreach (
            var share in new[]
            {
                new Vector3(-0.003f, 0f, -0.015f),
                new Vector3(0.05f, 0f, 0f),
                new Vector3(0f, 0f, 0.1f),
                Vector3.Zero,
            }
        )
        {
            var lean = Lean(s, share, 45f, walking: true);
            Assert.True(Vector3.Dot(lean, share) >= -1e-6f, $"{share}: lean {lean} points away");
            Assert.True(lean.Length() <= share.Length() + 1e-6f, $"{share}: lean {lean} past it");
        }
    }

    [Fact]
    public void ALeanWithNoTurn_IsAllTheLeans()
    {
        // vr.45: a head leaning 0.3 m forward with the look still bends for all of it, at once.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        for (float t = 0f; t <= 1f; t += Dt)
        {
            var share = new Vector3(0f, 0f, 0.3f * t);
            // Idle sway of the look stays under the turning rate.
            float yaw = 2f * MathF.Sin(t * 6f);
            Assert.True((Lean(s, share, yaw) - share).Length() < 1e-4f, $"t {t:0.00}");
        }
    }

    [Fact]
    public void AnUnwalkedTurn_GoesToTheLean_AfterAWhile()
    {
        // Under the walk's start distance no walk comes; the lean takes it once the turn has long held.
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(0.04f, 0f, 0f), 30f);
        Assert.True(Lean(s, new Vector3(0.04f, 0f, 0f), 30f).Length() < 0.005f);
        Vector3 lean = default;
        for (float t = 0f; t < s.ReleaseAfter + 2f; t += Dt)
            lean = Lean(s, new Vector3(0.04f, 0f, 0f), 30f);
        Assert.True(lean.X > 0.035f, $"lean {lean}");
    }

    [Fact]
    public void Reset_Forgets()
    {
        var s = new TurnSkip();
        Lean(s, Vector3.Zero, 0f);
        Lean(s, new Vector3(0.2f, 0f, 0f), 90f);
        s.Reset();
        var share = new Vector3(0.2f, 0f, 0f);
        Lean(s, share, 90f);
        Assert.Equal(share, Lean(s, share, 90f));
    }
}
