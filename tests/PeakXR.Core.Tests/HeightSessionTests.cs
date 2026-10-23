namespace PeakXR.Core.Tests;

public class HeightSessionTests
{
    const float Dt = 1f / 90f;

    static void Watch(HeightSession s, float headY, float seconds, bool seated = false)
    {
        for (float t = 0f; t < seconds; t += Dt)
            s.Watch(headY, seated, Dt);
    }

    [Fact]
    public void FirstScene_Measures()
    {
        var s = new HeightSession();
        Assert.True(s.ShouldMeasure(HeightCause.SceneLoad));
    }

    [Fact]
    public void LaterScenes_KeepTheSessionsHeight()
    {
        var s = new HeightSession();
        s.Measured(1.6f);
        // Seated, kneeling or leaning at the next load: the height is not taken again.
        Watch(s, 1.1f, 30f);
        Assert.False(s.ShouldMeasure(HeightCause.SceneLoad));
    }

    [Fact]
    public void RecenterAndSeatedChange_AlwaysMeasure()
    {
        var s = new HeightSession();
        s.Measured(1.6f);
        Assert.True(s.ShouldMeasure(HeightCause.Recenter));
        Assert.True(s.ShouldMeasure(HeightCause.SeatedChange));
    }

    [Fact]
    public void StandingTallerForLong_RemeasuresAtTheNextLoad()
    {
        // Measured kneeling at the first load, then standing for the rest of the level.
        var s = new HeightSession();
        s.Measured(1.1f);
        Watch(s, 1.6f, HeightSession.SustainSeconds + 0.5f);
        Assert.True(s.ShouldMeasure(HeightCause.SceneLoad));
        s.Measured(1.6f);
        Assert.False(s.ShouldMeasure(HeightCause.SceneLoad));
    }

    [Fact]
    public void BriefOrSmallOrSeatedChanges_DoNot()
    {
        var s = new HeightSession();
        s.Measured(1.5f);
        // Standing on tiptoe or a short stretch, a few centimetres taller all along, or taller in seated mode.
        Watch(s, 1.8f, HeightSession.SustainSeconds - 1f);
        Watch(s, 1.5f, 1f);
        Watch(s, 1.8f, HeightSession.SustainSeconds - 1f);
        Watch(s, 1.5f + HeightSession.TallerBy - 0.02f, 60f);
        Watch(s, 1.8f, 60f, seated: true);
        Assert.False(s.ShouldMeasure(HeightCause.SceneLoad));
    }
}
