namespace PeakXR.Core.Tests;

public class ClimbDebtTests
{
    // Vanilla flat wall, climbSpeed 4, all multipliers 1 (CharacterClimbing.cs:286-327, :412).
    const float ClimbSpeed = 4f,
        Fixed = 0.02f;
    static readonly float Speed = ClimbInput.WallSpeed(ClimbSpeed, 1f);
    static readonly float Down = ClimbInput.DownSpeed(ClimbSpeed, 1f);

    /// <summary>
    /// The plugin loop against the game: per rendered frame the fixed steps run first with the stick set last frame
    /// (FixedUpdate before Update) and move the climber (y up, -3 x climbSpeed down for any negative y, x sideways,
    /// 0.4/1.4 of each step landing), then the frame pays for them, adds the arm pull and sets the next stick.
    /// </summary>
    sealed class Sim
    {
        public readonly ClimbDebt Debt = new();
        public Vector2 Body;
        public Vector2 Stick;
        public int DownSteps;
        float acc;

        public void Frame(Vector2 pull, float dt, bool yMoves = true)
        {
            acc += dt;
            int steps = 0;
            while (acc >= Fixed - 1e-6f)
            {
                acc -= Fixed;
                steps++;
                float y =
                    !yMoves ? 0f
                    : Stick.Y < 0f ? -3f
                    : Stick.Y;
                if (Stick.Y < 0f && yMoves)
                    DownSteps++;
                Body += new Vector2(Stick.X, y) * (ClimbSpeed * Fixed * 0.4f / 1.4f);
            }
            Debt.Pay(Stick, Speed, Down, yMoves, steps * Fixed);
            Debt.Add(pull, yMoves);
            Stick = Debt.Stick(Speed, Down, Fixed);
        }
    }

    static float MinJerk(float t, float T)
    {
        float s = Math.Clamp(t / T, 0f, 1f);
        return s * s * s * (10f - 15f * s + 6f * s * s);
    }

    /// <summary>Hand-driven body travel along <paramref name="dir"/> over <paramref name="seconds"/>, then 1.5 s still.</summary>
    static Sim Pull(Vector2 dir, float seconds, float hz)
    {
        var sim = new Sim();
        float dt = 1f / hz;
        float last = 0f;
        for (float t = dt; t < seconds + 1.5f; t += dt)
        {
            float p = MinJerk(t, seconds);
            sim.Frame(dir * (p - last), dt);
            last = p;
        }
        return sim;
    }

    [Theory]
    [InlineData(2.5f, 90f)]
    [InlineData(0.6f, 90f)]
    [InlineData(0.3f, 90f)]
    [InlineData(0.3f, 120f)]
    [InlineData(0.3f, 72f)]
    [InlineData(0.6f, 45f)]
    public void PullDown_LiftsTheClimberAsFarAsTheHand(float seconds, float hz)
    {
        // 0.4 m in 0.3 s peaks near 2.5 m/s, past the game's 1.14 m/s: the climber catches up after the hand stops.
        var sim = Pull(new Vector2(0f, 0.4f), seconds, hz);
        TestUtil.Near(0.4f, sim.Body.Y, 0.01f);
        TestUtil.Near(0f, sim.Body.X, 1e-4f);
        Assert.Equal(Vector2.Zero, sim.Stick);
    }

    [Fact]
    public void Sideways_TraversesAsFarAsTheHand()
    {
        var sim = Pull(new Vector2(-0.3f, 0f), 0.5f, 90f);
        TestUtil.Near(-0.3f, sim.Body.X, 0.01f);
        TestUtil.Near(0f, sim.Body.Y, 1e-4f);
    }

    [Theory]
    [InlineData(0.6f, 90f)]
    [InlineData(1.5f, 90f)]
    [InlineData(0.6f, 45f)]
    public void PushUp_DescendsWithinHalfAGameStep(float seconds, float hz)
    {
        // Any negative stick drops 3 x climbSpeed (CharacterClimbing.cs:303-305): 0.137 m per 50 Hz step on a flat wall.
        float step = Down * Fixed;
        var sim = Pull(new Vector2(0f, -0.3f), seconds, hz);
        Assert.InRange(sim.Body.Y, -0.3f - step * 0.5f - 1e-3f, -0.3f + step * 0.5f + 1e-3f);
        Assert.Equal(Vector2.Zero, sim.Stick);
    }

    [Fact]
    public void StillHands_StopTheClimber()
    {
        var sim = Pull(new Vector2(0f, 0.3f), 0.4f, 90f);
        var at = sim.Body;
        for (int i = 0; i < 90; i++)
            sim.Frame(Vector2.Zero, 1f / 90f);
        Assert.Equal(at, sim.Body);
    }

    [Fact]
    public void TrackingJitter_NeitherClimbsNorDrops()
    {
        var sim = new Sim();
        for (int i = 0; i < 270; i++)
            sim.Frame(new Vector2(0f, (i % 2 == 0 ? 1f : -1f) * 0.001f), 1f / 90f);
        Assert.Equal(0, sim.DownSteps);
        Assert.InRange(sim.Body.Y, -0.005f, 0.005f);
    }

    [Fact]
    public void AfterAJump_ThePullIsNotBanked()
    {
        // The game ignores the stick's y for 0.5 s after a climb jump and with no stamina (CharacterClimbing.cs:301):
        // that pull must not come back as a climb once the game listens again.
        var sim = new Sim();
        for (int i = 0; i < 30; i++)
            sim.Frame(new Vector2(0f, 0.01f), 1f / 90f, yMoves: false);
        for (int i = 0; i < 90; i++)
            sim.Frame(Vector2.Zero, 1f / 90f);
        TestUtil.Near(0f, sim.Body.Y, 1e-4f);
    }

    [Fact]
    public void Owed_IsCappedAtOnePull()
    {
        var debt = new ClimbDebt();
        debt.Add(new Vector2(0f, 3f), true);
        TestUtil.Near(ClimbDebt.MaxOwed, debt.Owed.Length());
    }

    [Fact]
    public void Reset_ForgetsTheDebt()
    {
        var debt = new ClimbDebt();
        debt.Add(new Vector2(0.1f, 0.2f), true);
        debt.Reset();
        Assert.Equal(Vector2.Zero, debt.Stick(Speed, Down, Fixed));
    }

    [Fact]
    public void NoClimbSpeed_GivesNoStick()
    {
        var debt = new ClimbDebt();
        debt.Add(new Vector2(0.1f, 0.2f), true);
        Assert.Equal(Vector2.Zero, debt.Stick(0f, Down, Fixed));
    }
}
