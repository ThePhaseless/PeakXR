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
