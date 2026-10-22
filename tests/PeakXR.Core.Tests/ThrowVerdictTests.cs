namespace PeakXR.Core.Tests;

public class ThrowVerdictTests
{
    static ThrowVerdict Judge(
        float distance = 1.9f,
        float pitch = 10f,
        bool ownBody = false,
        bool contact = true,
        float contactSlope = 5f,
        float contactAt = 3f,
        float flatRange = 3.5f
    ) =>
        SelfTestChecks.JudgeThrow(
            distance,
            2f,
            pitch,
            ownBody,
            contact,
            contactSlope,
            contactAt,
            flatRange
        );

    [Fact]
    public void FarEnough_IsOk() => Assert.Equal(ThrowVerdict.Ok, Judge(distance: 3.4f));

    [Fact]
    public void FirstContactOnOwnBody_IsOwnBody() =>
        Assert.Equal(ThrowVerdict.OwnBody, Judge(ownBody: true, pitch: -20f));

    [Fact]
    public void LaunchedDownward_IsAimedDown() =>
        Assert.Equal(ThrowVerdict.AimedDown, Judge(pitch: -12f));

    [Fact]
    public void SteepFirstContact_IsHitOnTheWay() =>
        Assert.Equal(ThrowVerdict.HitOnTheWay, Judge(contactSlope: 80f));

    [Fact]
    public void GroundContactWellShortOfTheArc_IsHitOnTheWay() =>
        Assert.Equal(ThrowVerdict.HitOnTheWay, Judge(contactAt: 1f));

    [Fact]
    public void GroundContactAtTheArcEnd_IsShortOnFlat() =>
        Assert.Equal(ThrowVerdict.ShortOnFlat, Judge(contactAt: 3.3f));

    [Fact]
    public void FlatRange_MatchesBallistics()
    {
        // 5 m/s level from 1.25 m under g 10: 0.5 s in the air.
        TestUtil.Near(2.5f, SelfTestChecks.FlatRange(5f, 0f, 1.25f, 10f));
        TestUtil.Near(0f, SelfTestChecks.FlatRange(0f, 0f, 1f, 10f));
    }
}
