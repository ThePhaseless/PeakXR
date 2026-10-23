namespace PeakXR.Core.Tests;

/// <summary>A follow walk the body cannot finish (a wall between it and the head) must give up and recentre under the
/// fade, never walk on for good.</summary>
public class RoomScaleStallTests
{
    static readonly Vector2 Head = new(0f, 0.5f);

    [Fact]
    public void BlockedFollow_GivesUpAfterTheStall()
    {
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        Assert.NotEqual(Vector2.Zero, f.Update(Head, Vector2.Zero, true, 0.05f));
        Assert.False(f.RecenterNeeded);
        // The body does not move (blocked); the head stays put.
        for (float t = 0f; t < f.StallSeconds + 0.1f; t += 0.05f)
            f.Update(Head, Vector2.Zero, true, 0.05f);
        Assert.True(f.RecenterNeeded);
    }

    [Fact]
    public void ProgressingFollow_NeverStalls()
    {
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        var head = Head;
        for (int i = 0; i < 60; i++)
        {
            // The body walks 2 cm a frame toward the head: the offset shrinks.
            var move = f.Update(head, i == 0 ? Vector2.Zero : new Vector2(0f, 0.02f), true, 0.05f);
            Assert.False(f.RecenterNeeded);
            if (move == Vector2.Zero)
                break;
        }
    }

    /// <summary>Runs the follow with a body that walks its request at 1 m/s; returns the last request.</summary>
    static Vector2 Walk(
        RoomScaleFollow f,
        Func<float, Vector2> head,
        float seconds,
        ref Vector2 body,
        bool standingTall = true
    )
    {
        var request = Vector2.Zero;
        for (float t = 0f; t < seconds; t += 0.02f)
        {
            var step = request * 1f * 0.02f;
            body += step;
            request = f.Update(head(t), step, true, 0.02f, standingTall);
        }
        return request;
    }

    [Fact]
    public void AnOldFollowsTravel_DoesNotBlockAStepTheOtherWay()
    {
        // Run 54, vr.140: an earlier forward follow left its travel behind; stepping back 0.7 m later counted as
        // "standing back up after that follow" for good, so the body never followed and never stalled (gap 0.70 m).
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        var body = Vector2.Zero;
        Walk(f, _ => new Vector2(0f, 0.8f), 4f, ref body); // walk forward after the head
        var at = body;
        Walk(f, _ => at, 3f, ref body); // the head comes back over the body and rests
        var back = Walk(f, t => at - new Vector2(0f, 0.7f * MathF.Min(1f, t)), 1.5f, ref body);
        Assert.True(
            back.Length() > 0.05f || f.RecenterNeeded,
            $"stepping back 0.7 m: no follow (offset {f.Offset.Length():0.00} m)"
        );
    }

    [Fact]
    public void AFarLeanHeldStill_ThenStandingUp_DoesNotWalkBack()
    {
        // Run 56, vr.45: the far lean (0.5 m forward) held 2.5 s at nearly standing height cleared its stand-up
        // exemption, and standing up walked the body 0.155 m back. A held lean is not standing tall.
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        var body = Vector2.Zero;
        var lean = new Vector2(0f, 0.6f); // the walk ends 0.4 m forward, as run 57s coasted 0.38 m
        Walk(f, _ => lean, 3f, ref body, standingTall: false); // lean, the body follows, hold still
        var before = body;
        Walk(f, t => lean * (1f - MathF.Min(1f, t / 0.6f)), 0.6f, ref body, standingTall: false);
        Walk(f, _ => Vector2.Zero, 3f, ref body); // stood up, still, at standing height
        Assert.True(
            Vector2.Distance(body, before) < 0.05f,
            $"standing up walked the body {Vector2.Distance(body, before):0.00} m"
        );
    }

    [Fact]
    public void BlockedFollow_StallsThroughBriefGroundingDropouts()
    {
        // Run 52, vr.140: a body pressed into the wall loses isGrounded for a frame now and then; each dropout ended
        // the follow and the restart reset the stall clock, so it never stalled and no NotNow or fade came.
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        int frame = 0;
        for (float t = 0f; t < f.StallSeconds + 0.5f; t += 0.05f, frame++)
            f.Update(Head, Vector2.Zero, frame % 4 != 3, 0.05f);
        Assert.True(f.RecenterNeeded);
    }

    [Fact]
    public void SteppingBackAfterAStall_NeedsNoFade()
    {
        // Leaned into a rock, the follow gave up; the player steps back to the body: no recentre fade is due.
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        for (float t = 0f; t < f.StallSeconds + 0.2f; t += 0.05f)
            f.Update(Head, Vector2.Zero, true, 0.05f);
        Assert.True(f.RecenterNeeded);
        f.Update(new Vector2(0f, 0.1f), Vector2.Zero, true, 0.05f);
        Assert.False(f.RecenterNeeded);
    }

    [Fact]
    public void Recenter_ClearsTheStall()
    {
        var f = new RoomScaleFollow { LeanRadius = 0.35f };
        for (float t = 0f; t < f.StallSeconds + 0.2f; t += 0.05f)
            f.Update(Head, Vector2.Zero, true, 0.05f);
        Assert.True(f.RecenterNeeded);
        f.Recenter(Head);
        Assert.False(f.RecenterNeeded);
        Assert.Equal(Vector2.Zero, f.Update(Head, Vector2.Zero, true, 0.05f));
    }
}
