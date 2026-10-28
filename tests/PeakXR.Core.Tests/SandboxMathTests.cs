namespace PeakXR.Core.Tests;

public class SpawnLayoutTests
{
    [Fact]
    public void SingleRow_IsCenteredOnOriginAlongRight()
    {
        // Unity is left-handed: right = Cross(up, forward), so forward +Z gives right +X.
        var p = new Vector3[3];
        SpawnLayout.Grid(p, Vector3.Zero, Vector3.UnitZ, 1f, 10);

        TestUtil.Near(new Vector3(-1f, 0f, 0f), p[0]);
        TestUtil.Near(new Vector3(0f, 0f, 0f), p[1]);
        TestUtil.Near(new Vector3(1f, 0f, 0f), p[2]);
    }

    [Fact]
    public void RowsAdvanceAlongForward()
    {
        var p = new Vector3[5];
        SpawnLayout.Grid(p, new Vector3(0f, 2f, 5f), Vector3.UnitZ, 0.5f, 2);

        // Rows of two: [0,1] at z=5, [2,3] at z=5.5, [4] at z=6.
        TestUtil.Near(5f, p[0].Z);
        TestUtil.Near(5f, p[1].Z);
        TestUtil.Near(5.5f, p[2].Z);
        TestUtil.Near(6f, p[4].Z);
        TestUtil.Near(2f, p[3].Y);
    }

    [Fact]
    public void PartialLastRow_IsCenteredIndependently()
    {
        var p = new Vector3[3];
        SpawnLayout.Grid(p, Vector3.Zero, Vector3.UnitZ, 1f, 2);

        // Last row holds one item: it sits on the origin line.
        TestUtil.Near(0f, p[2].X);
    }

    [Fact]
    public void NonUnitForward_IsNormalized()
    {
        var p = new Vector3[2];
        SpawnLayout.Grid(p, Vector3.Zero, new Vector3(0f, 0f, 10f), 2f, 1);

        TestUtil.Near(2f, p[1].Z);
    }

    [Fact]
    public void VerticalForward_FallsBackToWorldZ()
    {
        var p = new Vector3[2];
        SpawnLayout.Grid(p, Vector3.Zero, Vector3.UnitY, 1f, 1);

        // Forward is flattened to the ground plane; a straight-up view must not produce NaN.
        Assert.False(float.IsNaN(p[1].X) || float.IsNaN(p[1].Z));
        TestUtil.Near(0f, p[1].Y);
    }

    [Fact]
    public void ZeroOrNegativePerRow_IsTreatedAsOne()
    {
        var p = new Vector3[2];
        SpawnLayout.Grid(p, Vector3.Zero, Vector3.UnitZ, 1f, 0);

        TestUtil.Near(1f, p[1].Z);
    }
}

public class NameFilterTests
{
    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        var terms = NameFilter.Parse("  ");
        Assert.True(NameFilter.Matches("Rope", terms));
    }

    [Fact]
    public void IsCaseInsensitiveSubstring()
    {
        var terms = NameFilter.Parse("rOP");
        Assert.True(NameFilter.Matches("RopeSpool", terms));
        Assert.False(NameFilter.Matches("Backpack", terms));
    }

    [Fact]
    public void AllTermsMustMatch_InAnyOrder()
    {
        var terms = NameFilter.Parse("spool rope");
        Assert.True(NameFilter.Matches("RopeSpool", terms));
        Assert.False(NameFilter.Matches("Rope", terms));
    }

    [Fact]
    public void NullName_NeverMatchesRealQuery()
    {
        Assert.False(NameFilter.Matches(null, NameFilter.Parse("a")));
        Assert.True(NameFilter.Matches(null, NameFilter.Parse("")));
    }
}

public class SandboxRunMarkTests
{
    [Fact]
    public void FirstUse_MarksTheRunAndAsksForOneNotice()
    {
        var m = new SandboxRunMark();
        Assert.False(m.Marked);
        m.Use();
        Assert.True(m.Marked);
        Assert.True(m.NoticeDue);
        m.NoticeShown();
        m.Use();
        Assert.True(m.Marked);
        Assert.False(m.NoticeDue);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Airport")]
    public void NewRun_ClearsTheMark(string scene)
    {
        var m = new SandboxRunMark();
        m.Use();
        m.SceneLoaded(scene, godMode: false);
        Assert.False(m.Marked);
        Assert.False(m.NoticeDue);
        m.Use();
        Assert.True(m.NoticeDue);
    }

    [Fact]
    public void LevelLoad_KeepsTheMark()
    {
        // Spawned in the airport, then boarded: the run that follows is still a sandbox run.
        var m = new SandboxRunMark();
        m.Use();
        m.SceneLoaded("Level_0", godMode: false);
        Assert.True(m.Marked);
    }

    [Fact]
    public void GodModeStillOn_CarriesTheMarkIntoTheNextRun()
    {
        var m = new SandboxRunMark();
        m.Use();
        m.NoticeShown();
        m.SceneLoaded("Airport", godMode: true);
        Assert.True(m.Marked);
    }
}
