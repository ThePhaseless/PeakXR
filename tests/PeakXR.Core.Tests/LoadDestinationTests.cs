namespace PeakXR.Core.Tests;

public class LoadDestinationTests
{
    [Fact]
    public void BuiltBeforeALoadWaitsForTheLoadToStart()
    {
        var d = new LoadDestination();
        Assert.False(d.Built("Level_3", loading: false, frame: 10));
        Assert.Null(d.Current);
        d.Started();
        Assert.Equal("Level_3", d.Current);
    }

    [Fact]
    public void ALoadWithoutAProcessHasNoDestination()
    {
        var d = new LoadDestination();
        d.Built("Airport", loading: false, frame: 1);
        d.Started();
        d.Ended();
        // Joining a room (PhotonDriven) builds no LoadSceneProcess.
        d.Started();
        Assert.Null(d.Current);
    }

    [Fact]
    public void BuiltInsideARunningLoadAppliesAtOnce()
    {
        // Solo play: MainMenu.StartOfflineModeRoutine builds LoadSceneProcess("Airport") inside the load.
        var d = new LoadDestination();
        d.Started();
        Assert.True(d.Built("Airport", loading: true, frame: 5));
        Assert.Equal("Airport", d.Current);
    }

    [Fact]
    public void ARejectedLoadInTheSameFrameIsUndone()
    {
        var d = new LoadDestination();
        d.Built("Level_1", loading: false, frame: 1);
        d.Started();
        Assert.True(d.Built("Airport", loading: true, frame: 7));
        Assert.True(d.Rejected(frame: 7));
        Assert.Equal("Level_1", d.Current);
        // A later rejection does not touch a destination built in an earlier frame.
        d.Built("Airport", loading: true, frame: 8);
        Assert.False(d.Rejected(frame: 9));
        Assert.Equal("Airport", d.Current);
    }

    [Fact]
    public void EndedKeepsAPendingNextLoad()
    {
        var d = new LoadDestination();
        d.Built("Level_1", loading: false, frame: 1);
        d.Started();
        d.Built("Airport", loading: false, frame: 2);
        d.Ended();
        Assert.Null(d.Current);
        d.Started();
        Assert.Equal("Airport", d.Current);
        d.Clear();
        d.Started();
        Assert.Null(d.Current);
    }
}
