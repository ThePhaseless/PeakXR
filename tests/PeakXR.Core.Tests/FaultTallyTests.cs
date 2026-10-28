using System.Collections.Generic;

namespace PeakXR.Core.Tests;

public class FaultTallyTests
{
    [Fact]
    public void FirstErrorAtASiteIsLoggedRepeatsAreQuiet()
    {
        var tally = new FaultTally();

        Assert.Equal(FaultAction.Log, tally.Record("loco", "A.cs:10", 0f));
        Assert.Equal(FaultAction.Quiet, tally.Record("loco", "A.cs:10", 1f));
        Assert.Equal(FaultAction.Log, tally.Record("loco", "B.cs:20", 2f));
    }

    [Fact]
    public void ErrorsEveryFrameTurnTheOwnerOffOnce()
    {
        var tally = new FaultTally();
        var actions = new List<FaultAction>();
        for (int i = 0; i < FaultTally.DisableAfter + 5; i++)
            actions.Add(tally.Record("loco", "A.cs:10", i / 90f));

        Assert.Equal(FaultAction.Log, actions[0]);
        Assert.Equal(FaultAction.Disable, actions[FaultTally.DisableAfter - 1]);
        Assert.Single(actions, a => a == FaultAction.Disable);
        Assert.Equal(FaultAction.Quiet, actions[^1]);
    }

    [Fact]
    public void SporadicErrorsNeverTurnTheOwnerOff()
    {
        var tally = new FaultTally();
        for (int i = 0; i < FaultTally.DisableAfter * 3; i++)
            Assert.NotEqual(
                FaultAction.Disable,
                tally.Record("loco", "A.cs:10", i * FaultTally.WindowSeconds / 2f)
            );
    }

    [Fact]
    public void OwnersAreCountedApart()
    {
        var tally = new FaultTally();
        for (int i = 0; i < FaultTally.DisableAfter - 1; i++)
        {
            tally.Record("loco", "A.cs:10", 0f);
            tally.Record("ping", "B.cs:20", 0f);
        }

        Assert.Equal(FaultAction.Disable, tally.Record("loco", "A.cs:10", 0f));
        Assert.Equal(FaultAction.Disable, tally.Record("ping", "B.cs:20", 0f));
    }
}
