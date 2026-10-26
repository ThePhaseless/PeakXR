namespace PeakXR.Core.Tests;

public class PracticeZoneTests
{
    [Fact]
    public void Stations_RunZoneByZone_NearestZoneFirst()
    {
        // Each zone's stations sit together, and zones come in importance order (nearest first).
        var last = PracticeZone.Basics;
        foreach (var s in PracticeFlow.Stations)
        {
            var z = PracticeZones.Of(s);
            Assert.True(z >= last, $"{s} ({z}) comes after a {last} station");
            last = z;
        }
        Assert.Equal(PracticeZone.Basics, PracticeZones.Of(PracticeFlow.Stations[0]));
    }

    [Fact]
    public void Basics_HoldTheFirstMinuteMechanics()
    {
        foreach (
            var s in new[]
            {
                PracticeStation.Turn,
                PracticeStation.Recenter,
                PracticeStation.Grab,
                PracticeStation.Refused,
            }
        )
            Assert.Equal(PracticeZone.Basics, PracticeZones.Of(s));
        Assert.Equal(PracticeZone.Body, PracticeZones.Of(PracticeStation.Holster));
        Assert.Equal(PracticeZone.Body, PracticeZones.Of(PracticeStation.Eat));
        Assert.Equal(PracticeZone.Body, PracticeZones.Of(PracticeStation.Throw));
        Assert.Equal(PracticeZone.Climb, PracticeZones.Of(PracticeStation.Climb));
        Assert.Equal(PracticeZone.Climb, PracticeZones.Of(PracticeStation.Rope));
        Assert.Equal(PracticeZone.Social, PracticeZones.Of(PracticeStation.Emote));
    }

    [Fact]
    public void Rope_FollowsTheWall_AndHasItsDemo()
    {
        int climb = Array.IndexOf(PracticeFlow.Stations, PracticeStation.Climb);
        Assert.Equal(PracticeStation.Rope, PracticeFlow.Stations[climb + 1]);
        Assert.Equal(GuideTopic.Rope, PracticeFlow.TopicOf(PracticeStation.Rope));
    }

    [Fact]
    public void FallbackStep_IsTheGapFromThePreviousZone()
    {
        // Unanchored zones chain outward from the zone before, so the walk is one short step each time.
        for (var z = PracticeZone.Body; z <= PracticeZone.Social; z++)
            Assert.Equal(
                PracticeZones.Distance(z) - PracticeZones.Distance(z - 1),
                PracticeZones.Fallback(z, 0).Distance,
                3
            );
    }

    [Fact]
    public void FallbackDistance_GrowsWithZone_BasicsAtTheStart()
    {
        Assert.Equal(0f, PracticeZones.Distance(PracticeZone.Basics));
        for (var z = PracticeZone.Body; z <= PracticeZone.Social; z++)
            Assert.True(PracticeZones.Distance(z) > PracticeZones.Distance(z - 1));
        // Walkable in one go: never further than a short stroll.
        Assert.True(PracticeZones.Distance(PracticeZone.Social) <= 10f);
    }

    [Fact]
    public void Fallback_TriesAheadFirst_ThenWiderHeadings_ThenNearer()
    {
        var first = PracticeZones.Fallback(PracticeZone.Body, 0);
        Assert.Equal(0f, first.Yaw);
        Assert.True(first.Distance >= 2f);
        float lastYaw = 0f;
        for (int i = 1; i < PracticeZones.FallbackHeadings; i++)
        {
            var f = PracticeZones.Fallback(PracticeZone.Body, i);
            Assert.True(MathF.Abs(f.Yaw) >= MathF.Abs(lastYaw));
            lastYaw = f.Yaw;
        }
        var nearer = PracticeZones.Fallback(PracticeZone.Body, PracticeZones.FallbackHeadings);
        Assert.Equal(0f, nearer.Yaw);
        Assert.True(nearer.Distance < first.Distance && nearer.Distance > 1f);
        Assert.Equal(PracticeZones.FallbackHeadings * 2, PracticeZones.FallbackAttempts);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, true)]
    [InlineData(0.9f, 0f, true)]
    [InlineData(1.2f, 0f, false)]
    [InlineData(0.8f, 0.8f, false)]
    public void Arrived_WithinAStepOfTheSpot(float dx, float dz, bool expected) =>
        Assert.Equal(expected, PracticeZones.Arrived(dx, dz));

    [Fact]
    public void DemoOffset_OpenSpace_AheadAndSlightlyAside()
    {
        var (ahead, side) = PracticeZones.DemoOffset(10f);
        Assert.Equal(1f, ahead);
        Assert.Equal(0.3f, side);
    }

    [Fact]
    public void DemoOffset_FacingAWall_StandsBesideYou_NeverInsideIt()
    {
        // Climbing: the wall is 0.6 m ahead; the demo stays out of it and steps aside so the wall stays clear.
        var (ahead, side) = PracticeZones.DemoOffset(0.6f);
        Assert.True(ahead <= 0.6f - PracticeZones.WallGap);
        Assert.True(ahead > 0f);
        Assert.True(side >= 0.6f);
    }

    [Fact]
    public void NewZone_GoesToTravel_SameZoneDoesNot()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        int i = 0;
        // Walk the whole list: a station whose zone differs from the one before starts with a walk.
        while (flow.Phase != PracticePhase.Done)
        {
            bool newZone =
                i > 0
                && PracticeZones.Of(PracticeFlow.Stations[i])
                    != PracticeZones.Of(PracticeFlow.Stations[i - 1]);
            Assert.Equal(newZone ? PracticePhase.Travel : PracticePhase.Station, flow.Phase);
            Assert.Equal(PracticeZones.Of(PracticeFlow.Stations[i]), flow.Zone);
            if (flow.Phase == PracticePhase.Travel)
                flow.Reached();
            Assert.Equal(PracticePhase.Station, flow.Phase);
            flow.Succeed();
            flow.Tick(PracticeFlow.ConfirmSeconds + 0.01f);
            i++;
        }
        Assert.Equal(PracticeFlow.Stations.Length, flow.Completed);
        Assert.Equal(0, flow.Skipped);
    }

    [Fact]
    public void SkipDuringTravel_SkipsOnlyTheWalk()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        while (flow.Phase != PracticePhase.Travel)
            flow.Skip();
        int skipped = flow.Skipped;
        int index = flow.Index;
        flow.Skip();
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(index, flow.Index);
        Assert.Equal(skipped, flow.Skipped);
    }

    [Fact]
    public void Reached_OnlyEndsATravel()
    {
        var flow = new PracticeFlow();
        flow.Begin(true);
        flow.Reached();
        Assert.Equal(PracticePhase.Loading, flow.Phase);
        flow.Arrived();
        flow.Reached();
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(0, flow.Index);
    }

    [Fact]
    public void Exit_WorksWhileTravelling()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        while (flow.Phase != PracticePhase.Travel)
            flow.Skip();
        flow.Exit();
        Assert.Equal(PracticePhase.Exiting, flow.Phase);
    }
}
