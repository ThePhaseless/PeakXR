namespace PeakXR.Core.Tests;

public class PracticeTests
{
    [Fact]
    public void FromTitle_WaitsForTheLoad_ThenRunsTheFirstStation()
    {
        var flow = new PracticeFlow();
        flow.Begin(fromTitle: true);
        Assert.Equal(PracticePhase.Loading, flow.Phase);
        flow.Arrived();
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(PracticeFlow.Stations[0], flow.Current);
    }

    [Fact]
    public void InPlace_StartsAtTheFirstStation()
    {
        var flow = new PracticeFlow();
        flow.Begin(fromTitle: false);
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(0, flow.Index);
    }

    [Fact]
    public void Success_Confirms_ThenMovesOn()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        flow.Succeed();
        Assert.Equal(PracticePhase.Confirm, flow.Phase);
        Assert.Equal(1, flow.Completed);
        // A second success during the confirm counts nothing.
        flow.Succeed();
        Assert.Equal(1, flow.Completed);
        flow.Tick(PracticeFlow.ConfirmSeconds + 0.01f);
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(1, flow.Index);
    }

    [Fact]
    public void Skip_MovesOn_AndIsCounted()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        flow.Skip();
        Assert.Equal(PracticePhase.Station, flow.Phase);
        Assert.Equal(1, flow.Index);
        Assert.Equal(1, flow.Skipped);
    }

    [Fact]
    public void LastStation_EndsInDone_ThenExits()
    {
        var flow = new PracticeFlow();
        flow.Begin(false);
        // A skip during a walk to the next zone skips only the walk, so it takes one more skip per zone.
        for (
            int i = 0;
            i < PracticeFlow.Stations.Length * 2 && flow.Phase != PracticePhase.Done;
            i++
        )
            flow.Skip();
        Assert.Equal(PracticePhase.Done, flow.Phase);
        Assert.Equal(PracticeFlow.Stations.Length, flow.Skipped);
        flow.Tick(PracticeFlow.DoneSeconds + 0.01f);
        Assert.Equal(PracticePhase.Exiting, flow.Phase);
    }

    [Fact]
    public void Exit_WorksFromAnyRunningPhase_AndNotWhenIdle()
    {
        var idle = new PracticeFlow();
        idle.Exit();
        Assert.Equal(PracticePhase.Idle, idle.Phase);

        var loading = new PracticeFlow();
        loading.Begin(true);
        loading.Exit();
        Assert.Equal(PracticePhase.Exiting, loading.Phase);

        var confirm = new PracticeFlow();
        confirm.Begin(false);
        confirm.Succeed();
        confirm.Exit();
        Assert.Equal(PracticePhase.Exiting, confirm.Phase);
    }

    [Fact]
    public void Stations_IncludeTheRefusedAndRecenterDrills()
    {
        Assert.Contains(PracticeStation.Refused, PracticeFlow.Stations);
        Assert.Contains(PracticeStation.Recenter, PracticeFlow.Stations);
        Assert.Equal(GuideTopic.Refused, PracticeFlow.TopicOf(PracticeStation.Refused));
        Assert.Equal(GuideTopic.PauseRecenter, PracticeFlow.TopicOf(PracticeStation.Recenter));
    }

    [Fact]
    public void Stations_StartWithTurning_AndEachHasADemo()
    {
        Assert.Equal(PracticeStation.Turn, PracticeFlow.Stations[0]);
        foreach (var s in PracticeFlow.Stations)
            Assert.Equal(PracticeFlow.TopicOf(s), GuideTable.Get(PracticeFlow.TopicOf(s)).Topic);
    }

    [Theory]
    // inRoom, offline, atTitle, inAirport, loading, enteringRoom
    [InlineData(false, false, true, false, false, false, PracticeEntry.FromTitle)]
    [InlineData(false, true, true, false, false, false, PracticeEntry.FromTitle)]
    [InlineData(true, true, false, true, false, false, PracticeEntry.InPlace)]
    // Play Solo (and the title's PLAY OFFLINE path into it) sets EnteringRoom and nothing clears it until the title
    // shows again (MainMenu.cs:135, MainMenuMainPage.cs:55): in the airport it is stale, not a join in progress.
    [InlineData(true, true, false, true, false, true, PracticeEntry.InPlace)]
    [InlineData(false, true, true, false, false, true, PracticeEntry.Refused)] // title: a join is on its way
    [InlineData(true, false, false, true, false, false, PracticeEntry.Refused)] // online lobby
    [InlineData(true, false, true, false, false, false, PracticeEntry.Refused)] // online, whatever the scene
    [InlineData(true, true, false, false, false, false, PracticeEntry.Refused)] // offline run in a level
    [InlineData(false, false, true, false, true, false, PracticeEntry.Refused)] // still loading
    public void Start_OnlyOffline_FromTheTitleOrTheSoloAirport(
        bool inRoom,
        bool offline,
        bool atTitle,
        bool inAirport,
        bool loading,
        bool enteringRoom,
        PracticeEntry expected
    ) =>
        Assert.Equal(
            expected,
            PracticeStart.Decide(inRoom, offline, atTitle, inAirport, loading, enteringRoom)
        );

    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, false, false, false, false)]
    public void Offer_OnceInVR_OnTheTitleMainPage_NeverInTheSelfTest(
        bool vr,
        bool offered,
        bool selfTest,
        bool titleMainPage,
        bool expected
    ) => Assert.Equal(expected, PracticeStart.ShouldOffer(vr, offered, selfTest, titleMainPage));

    [Fact]
    public void Dwell_FiresOnceAfterHolding_AndRearmsOnExit()
    {
        var b = new DwellButton(0.5f);
        Assert.False(b.Tick(true, 0.3f));
        TestUtil.Near(0.6f, b.Progress);
        Assert.True(b.Tick(true, 0.3f));
        Assert.False(b.Tick(true, 0.3f));
        Assert.False(b.Tick(false, 0.1f));
        Assert.Equal(0f, b.Progress);
        Assert.False(b.Tick(true, 0.3f));
        Assert.True(b.Tick(true, 0.3f));
    }

    [Fact]
    public void Dwell_LeavingEarly_Resets()
    {
        var b = new DwellButton(0.5f);
        b.Tick(true, 0.4f);
        b.Tick(false, 0.01f);
        Assert.False(b.Tick(true, 0.2f));
    }

    [Fact]
    public void TurnTracker_AddsTurnsBothWays_AcrossTheWrap()
    {
        var t = new TurnTracker();
        t.Reset(350f);
        t.Update(20f);
        TestUtil.Near(30f, t.Total);
        t.Update(350f);
        TestUtil.Near(60f, t.Total);
    }

    [Fact]
    public void RefusedStart_SaysWhereItWorks()
    {
        Assert.Equal("Not here", DenyText.Label(DenyReason.NotHere));
        Assert.Equal("Start it from the title", DenyText.Hint(DenyReason.NotHere));
        Assert.Equal(CueGlyph.Padlock, DenyGlyph.For(DenyReason.NotHere));
    }
}
