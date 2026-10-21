namespace PeakXR.Core.Tests;

public class TitleLayoutTests
{
    static readonly Vector3 Eye = new(0f, 1.6f, 0f);

    [Fact]
    public void PullFactor_BringsAGroupToReach()
    {
        TestUtil.Near(0.25f, TitleLayout.PullFactor(2.2f, 0.55f));
    }

    [Fact]
    public void PullFactor_NeverPushesAGroupAway()
    {
        Assert.Equal(1f, TitleLayout.PullFactor(0.5f, 0.55f));
        Assert.Equal(1f, TitleLayout.PullFactor(0f, 0.55f));
    }

    [Fact]
    public void AlongSightLine_KeepsDirectionAndScalesDistance()
    {
        var p = new Vector3(-0.5f, 1.2f, 2.1f);
        var q = TitleLayout.AlongSightLine(Eye, p, 0.25f);
        TestUtil.Near(Vector3.Normalize(p - Eye), Vector3.Normalize(q - Eye), 1e-5f);
        TestUtil.Near(0.25f * Vector3.Distance(Eye, p), Vector3.Distance(Eye, q), 1e-5f);
    }

    [Fact]
    public void PulledGroupScaledByTheFactor_LooksTheSameFromTheEye()
    {
        // A group's pivot moves along its sight line and the group scales by the same factor about the pivot:
        // every point of it then stays on its own sight line, so the group keeps its angular size and spot.
        var pivot = new Vector3(-0.4f, 1.1f, 2.2f);
        var corner = pivot + new Vector3(0.3f, -0.2f, 0f);
        const float k = 0.3f;
        var pulledPivot = TitleLayout.AlongSightLine(Eye, pivot, k);
        var pulledCorner = pulledPivot + (corner - pivot) * k;
        TestUtil.Near(
            Vector3.Normalize(corner - Eye),
            Vector3.Normalize(pulledCorner - Eye),
            1e-5f
        );
    }

    [Fact]
    public void IsHandSized_AcceptsASignpostAndRejectsAFullPanel()
    {
        // The title signpost: about 0.6 x 0.65 m on a panel 2.2 m away (16 x 17 degrees).
        Assert.True(TitleLayout.IsHandSized(0.6f, 0.65f, 2.2f));
        // The settings page: nearly the whole 1.9 m panel.
        Assert.False(TitleLayout.IsHandSized(1.8f, 0.9f, 2.2f));
        // Tall but narrow still counts by its larger side.
        Assert.False(TitleLayout.IsHandSized(0.2f, 1.5f, 2.2f));
        Assert.False(TitleLayout.IsHandSized(0.1f, 0.1f, 0f));
    }

    [Fact]
    public void Entrance_EasesFromZeroToOne()
    {
        Assert.Equal(0f, TitleLayout.Entrance(0f, 0.4f));
        TestUtil.Near(0.5f, TitleLayout.Entrance(0.2f, 0.4f));
        Assert.Equal(1f, TitleLayout.Entrance(0.4f, 0.4f));
        Assert.Equal(1f, TitleLayout.Entrance(5f, 0.4f));
        Assert.Equal(1f, TitleLayout.Entrance(0f, 0f));
        Assert.True(TitleLayout.Entrance(0.1f, 0.4f) < TitleLayout.Entrance(0.3f, 0.4f));
    }

    [Fact]
    public void IsBacking_FullCanvasGraphicIsAlwaysABacking()
    {
        // WhiteFade, Darken, the settings and first-launch page backgrounds.
        Assert.True(TitleLayout.IsBacking(1f, 1f, flatFill: false, holdsText: false));
        Assert.True(TitleLayout.IsBacking(0.95f, 0.92f, flatFill: true, holdsText: true));
    }

    [Fact]
    public void IsBacking_LargeFlatFillBehindTextIsABacking()
    {
        // The credits strip: a sprite-less black rect a quarter of the canvas wide, full height, holding the names.
        Assert.True(TitleLayout.IsBacking(0.25f, 1f, flatFill: true, holdsText: true));
    }

    [Fact]
    public void IsBacking_KeepsArtSmallFillsAndEmptyFills()
    {
        // A sprite panel (the timer's wooden tag) is art.
        Assert.False(TitleLayout.IsBacking(0.3f, 0.9f, flatFill: false, holdsText: true));
        // A small flat fill (a checkmark, a field) stays.
        Assert.False(TitleLayout.IsBacking(0.1f, 0.05f, flatFill: true, holdsText: true));
        // A big fill with no text in it is not a backing behind text.
        Assert.False(TitleLayout.IsBacking(0.3f, 0.9f, flatFill: true, holdsText: false));
    }

    [Fact]
    public void LightOutline_ContrastsWithTheTextColour()
    {
        Assert.False(TitleLayout.LightOutline(1f, 1f, 1f));
        Assert.False(TitleLayout.LightOutline(0.87f, 0.86f, 0.76f));
        Assert.True(TitleLayout.LightOutline(0.2f, 0.15f, 0.1f));
        Assert.True(TitleLayout.LightOutline(0f, 0f, 1f));
    }
}
