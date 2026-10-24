namespace PeakXR.Core.Tests;

public class ReachLookTests
{
    static readonly Vector3 Ahead = new(0f, 0f, 1f);

    [Fact]
    public void LookingAtTheFriend_Reaches()
    {
        Assert.True(SocialIntent.ReachSees(Ahead, new Vector3(0f, 0f, 1.5f)));
    }

    [Fact]
    public void JustInsideTheGamesCone_Reaches()
    {
        var to = new Vector3(
            MathF.Sin(59f * MathF.PI / 180f),
            0f,
            MathF.Cos(59f * MathF.PI / 180f)
        );
        Assert.True(SocialIntent.ReachSees(Ahead, to));
    }

    [Fact]
    public void PastSixtyDegrees_DoesNotReach()
    {
        var to = new Vector3(
            MathF.Sin(61f * MathF.PI / 180f),
            0f,
            MathF.Cos(61f * MathF.PI / 180f)
        );
        Assert.False(SocialIntent.ReachSees(Ahead, to));
    }

    [Fact]
    public void LookingDownAtTheHand_DoesNotReach()
    {
        // MP run 18: 57 degrees down at the hand plus a turn to the side.
        var look = Vector3.Normalize(new Vector3(0f, -1.54f, 1f));
        var to = new Vector3(0.6f, 0f, 0.4f);
        Assert.False(SocialIntent.ReachSees(look, to));
    }

    [Fact]
    public void FartherThanFourMetres_DoesNotReach()
    {
        Assert.False(SocialIntent.ReachSees(Ahead, new Vector3(0f, 0f, 4.5f)));
    }
}
