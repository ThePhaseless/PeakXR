namespace PeakXR.Core.Tests;

public class MouthGestureTests
{
    static readonly Vector3 Head = new(0, 1.6f, 0);

    [Fact]
    public void MouthPoint_IsHeadRotatedOffset()
    {
        TestUtil.Near(
            new Vector3(0.08f, 1.53f, 0f),
            MouthGesture.MouthPoint(Head, UnityQuat.Euler(0, 90, 0), MouthGesture.DefaultOffset)
        );
    }

    [Fact]
    public void TrueOnlyAfterDwell()
    {
        var g = new MouthGesture();
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        Assert.False(g.Update(Head, Quaternion.Identity, mouth, 0.1f));
        Assert.False(g.Update(Head, Quaternion.Identity, mouth, 0.1f));
        Assert.True(g.Update(Head, Quaternion.Identity, mouth, 0.1f));
    }

    [Fact]
    public void FarPoint_NeverTriggers()
    {
        var g = new MouthGesture();
        for (int i = 0; i < 10; i++)
            Assert.False(
                g.Update(Head, Quaternion.Identity, Head + new Vector3(0, -0.5f, 0.3f), 0.1f)
            );
    }

    [Fact]
    public void LeavingResetsDwell()
    {
        var g = new MouthGesture();
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        g.Update(Head, Quaternion.Identity, mouth, 0.2f);
        g.Update(Head, Quaternion.Identity, mouth + new Vector3(1, 0, 0), 0.1f);
        Assert.False(g.Update(Head, Quaternion.Identity, mouth, 0.1f));
    }

    [Fact]
    public void Hysteresis_StaysInsideBetweenRadii()
    {
        var g = new MouthGesture { Radius = 0.12f, ExitRadius = 0.16f };
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        var between = mouth + new Vector3(0.14f, 0, 0);
        Assert.False(g.Update(Head, Quaternion.Identity, between, 0.3f));
        g.Update(Head, Quaternion.Identity, mouth, 0.3f);
        Assert.True(g.Update(Head, Quaternion.Identity, between, 0.1f));
        Assert.False(g.Update(Head, Quaternion.Identity, mouth + new Vector3(0.17f, 0, 0), 0.1f));
    }

    static readonly Vector3 Face = new(0, 0, -1);

    [Fact]
    public void PalmMute_OpenPalmFacingTheFace_AfterDwell()
    {
        var g = new PalmMute();
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        Assert.False(g.Update(Head, Quaternion.Identity, mouth, Face, 0.1f, 0.1f));
        Assert.False(g.Update(Head, Quaternion.Identity, mouth, Face, 0.1f, 0.1f));
        Assert.True(g.Update(Head, Quaternion.Identity, mouth, Face, 0.1f, 0.1f));
    }

    [Theory]
    // A closed hand at the mouth is the walkie-talkie, not mute.
    [InlineData(0.8f, 0f, 0f, -1f)]
    // Palm turned sideways or away.
    [InlineData(0.1f, 1f, 0f, 0f)]
    [InlineData(0.1f, 0f, 0f, 1f)]
    public void PalmMute_NeedsOpenFingersAndThePalmOnTheFace(
        float curl,
        float nx,
        float ny,
        float nz
    )
    {
        var g = new PalmMute();
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        for (int i = 0; i < 10; i++)
            Assert.False(
                g.Update(Head, Quaternion.Identity, mouth, new Vector3(nx, ny, nz), curl, 0.1f)
            );
    }

    [Fact]
    public void PalmMute_FollowsTheHeadsTurn()
    {
        var g = new PalmMute();
        var rot = UnityQuat.Euler(0, 90, 0);
        var mouth = MouthGesture.MouthPoint(Head, rot, MouthGesture.DefaultOffset);
        // Facing +X: the palm faces back toward -X.
        for (int i = 0; i < 3; i++)
            g.Update(Head, rot, mouth, new Vector3(-1, 0, 0), 0.1f, 0.1f);
        Assert.True(g.Active);
    }

    [Fact]
    public void PalmMute_ResetClears()
    {
        var g = new PalmMute();
        var mouth = MouthGesture.MouthPoint(Head, Quaternion.Identity, MouthGesture.DefaultOffset);
        for (int i = 0; i < 3; i++)
            g.Update(Head, Quaternion.Identity, mouth, Face, 0.1f, 0.1f);
        g.Reset();
        Assert.False(g.Active);
    }
}
