namespace PeakXR.Core.Tests;

public class VRTabColumnTests
{
    [Fact]
    public void Width_30DegreesAt1p5m_IsAbout0p8m()
    {
        // 0.001 m per canvas pixel: 2 * 1.5 * tan(15 deg) = 0.804 m = 804 px.
        Assert.Equal(804f, VRTabColumn.Width(30f, 1.5f, 0.001f), 0);
    }

    [Fact]
    public void Width_ScalesWithDistance() =>
        Assert.True(VRTabColumn.Width(30f, 3f, 0.001f) > VRTabColumn.Width(30f, 1.5f, 0.001f));

    [Fact]
    public void TextScale_GrowsSmallTextToTheFloor()
    {
        // 24 px at 0.001 m/px is 0.92 deg at 1.5 m: scaled up to 1.3 deg.
        float s = VRTabColumn.TextScale(24f, 1.5f, 0.001f);
        Assert.True(s > 1.3f);
        Assert.Equal(ReadableText.MinDegrees, ReadableText.Degrees(24f * 0.001f * s, 1.5f), 2);
    }

    [Fact]
    public void FarDistance_IsThePanelCornerFromTheEye()
    {
        // A 0.6 x 0.8 m panel 1 m away: its corner is sqrt(1 + 0.09 + 0.16) m from the eye.
        Assert.Equal(1.118f, VRTabColumn.FarDistance(1f, 0.6f, 0.8f), 3);
        Assert.Equal(1f, VRTabColumn.FarDistance(1f, 0f, 0f));
    }

    [Fact]
    public void TextScaledAtTheFarCorner_MeetsTheFloorEverywhereOnThePanel()
    {
        // VR run 56: the rail's last chip, far below the centre, read 1.09 deg.
        float near = 0.9f,
            far = VRTabColumn.FarDistance(near, 0.5f, 0.45f);
        float s = VRTabColumn.TextScale(24f, far, 0.001f);
        Assert.True(ReadableText.Degrees(24f * 0.001f * s, far) >= ReadableText.MinDegrees - 0.01f);
        Assert.True(ReadableText.Degrees(24f * 0.001f * s, near) > ReadableText.MinDegrees);
    }

    [Fact]
    public void TextScale_NeverShrinksLargeText() =>
        Assert.Equal(1f, VRTabColumn.TextScale(60f, 1.5f, 0.001f));

    [Fact]
    public void RailYaw_TurnsTheOuterEdgeTowardTheEye()
    {
        // Canvas forward points away from the eye; a left rail hinged on its right edge turns by a negative yaw.
        Assert.Equal(-18f, VRTabColumn.RailYaw(left: true));
        Assert.Equal(18f, VRTabColumn.RailYaw(left: false));
        Assert.InRange(VRTabColumn.RailDegrees, 15f, 20f);
    }

    [Theory]
    [InlineData(-50f, 1000f, 400f, 0f)]
    [InlineData(300f, 1000f, 400f, 300f)]
    [InlineData(900f, 1000f, 400f, 600f)]
    [InlineData(100f, 300f, 400f, 0f)] // all rows fit: no scroll
    public void ClampScroll(float offset, float content, float viewport, float expected) =>
        Assert.Equal(expected, VRTabColumn.ClampScroll(offset, content, viewport));

    [Fact]
    public void LastRowShows_AtTheEndOfTheScroll()
    {
        float content = 1000f,
            viewport = 400f;
        float end = VRTabColumn.ClampScroll(float.MaxValue, content, viewport);
        // The last row spans 940..1000 from the top of the rows.
        Assert.True(VRTabColumn.Shows(940f, 1000f, end, viewport));
        Assert.False(VRTabColumn.Shows(940f, 1000f, 0f, viewport));
    }
}
