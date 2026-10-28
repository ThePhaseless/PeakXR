namespace PeakXR.Core.Tests;

public class FaultTallyTests
{
    private const float Frame = 1f / 90f;

    [Fact]
    public void FirstErrorAtASiteIsLoggedRepeatsAreQuiet()
    {
        var tally = new FaultTally();

        Assert.Equal(FaultAction.Log, tally.Record("loco|A.cs:10", 1, 0f));
        Assert.Equal(FaultAction.Quiet, tally.Record("loco|A.cs:10", 2, 1f));
        Assert.Equal(FaultAction.Log, tally.Record("loco|B.cs:20", 3, 2f));
    }

    [Fact]
    public void ErrorsEveryFrameForSecondsMarkTheSitePersistentOnce()
    {
        var tally = new FaultTally();
        int persistent = 0;
        int firstAt = -1;
        for (int i = 0; i < 90 * 10; i++)
        {
            if (tally.Record("loco|A.cs:10", i, i * Frame) != FaultAction.Persistent)
                continue;
            persistent++;
            if (firstAt < 0)
                firstAt = i;
        }

        Assert.Equal(1, persistent);
        Assert.True(firstAt * Frame >= FaultTally.MinSpanSeconds);
        Assert.True(firstAt * Frame <= FaultTally.WindowSeconds);
    }

    [Fact]
    public void ASceneLoadRaceOnEveryCharacterOnlyLogs()
    {
        // 8 characters, every frame for 4 frames: 32 errors on 4 distinct frames.
        var tally = new FaultTally();
        for (int frame = 0; frame < 4; frame++)
        for (int character = 0; character < 8; character++)
            Assert.NotEqual(
                FaultAction.Persistent,
                tally.Record("avatar|Ik.cs:5", frame, frame * Frame)
            );
    }

    [Fact]
    public void ABurstShorterThanTheMinimumSpanIsNotPersistent()
    {
        var tally = new FaultTally();
        int frames = (int)(FaultTally.MinSpanSeconds / Frame) - 1;
        for (int i = 0; i < frames; i++)
            Assert.NotEqual(FaultAction.Persistent, tally.Record("a|A.cs:1", i, i * Frame));
    }

    [Fact]
    public void SporadicErrorsNeverBecomePersistent()
    {
        // Once a second for ten minutes.
        var tally = new FaultTally();
        for (int i = 0; i < 600; i++)
            Assert.NotEqual(FaultAction.Persistent, tally.Record("a|A.cs:1", i * 90, i));
    }

    [Fact]
    public void SitesAreCountedApart()
    {
        var tally = new FaultTally();
        bool other = false;
        for (int i = 0; i < 90 * 5; i++)
        {
            tally.Record("a|A.cs:1", i, i * Frame);
            // Another site of the same owner fails once every half second.
            if (i % 45 == 0)
                other |= tally.Record("a|B.cs:2", i, i * Frame) == FaultAction.Persistent;
        }

        Assert.False(other);
    }
}
