namespace PeakXR.Core.Tests;

public class SelfTestReportTests
{
    [Fact]
    public void PassingStep_FormatsIdStatusAndDuration()
    {
        var r = new SelfTestReport();
        r.Begin("00-title", 1f);
        r.End(true, 3.5f, null);

        Assert.Equal("00-title PASS 2.50s", r.Line(0));
        Assert.Equal(1, r.Passed);
        Assert.Equal(0, r.Failed);
    }

    [Fact]
    public void LoggedException_FailsStepAndKeepsOnlyItsFirstLine()
    {
        var r = new SelfTestReport();
        r.Begin("item.Rope", 0f);
        r.Exception("NullReferenceException: boom\n  at Foo.Bar ()");
        r.Exception("second");
        r.End(true, 1f, null);

        Assert.False(r.Steps[0].Passed);
        Assert.Equal("item.Rope FAIL 1.00s | NullReferenceException: boom", r.Line(0));
    }

    [Fact]
    public void LoggedError_IsReportedButDoesNotFail()
    {
        var r = new SelfTestReport();
        r.Begin("mob.BeeSwarm", 0f);
        r.Error("Missing sound");
        r.End(true, 1f, "spawned");

        Assert.True(r.Steps[0].Passed);
        Assert.Equal("mob.BeeSwarm PASS 1.00s spawned | Missing sound", r.Line(0));
    }

    [Fact]
    public void LogOutsideAStep_IsIgnored()
    {
        var r = new SelfTestReport();
        r.Exception("before");
        r.Begin("a", 0f);
        r.End(false, 0f, "timeout");
        r.Exception("after");

        Assert.Equal("a FAIL 0.00s timeout", r.Line(0));
        Assert.Equal(1, r.Failed);
    }

    [Fact]
    public void SkippedStep_CountsAsNeither()
    {
        var r = new SelfTestReport();
        r.Skip("mob.PlayerGhost", "needs a dead player");

        Assert.Equal("mob.PlayerGhost SKIP 0.00s needs a dead player", r.Line(0));
        Assert.Equal(0, r.Passed + r.Failed);
    }

    [Fact]
    public void SafeName_KeepsLettersDigitsAndDashes()
    {
        Assert.Equal(
            "05-mob-Character_Scoutmaster",
            SelfTestReport.SafeName("05-mob-Character_Scoutmaster")
        );
        Assert.Equal("03-items-Cure-All_x", SelfTestReport.SafeName("03-items-Cure-All x"));
        Assert.Equal("a_b_c", SelfTestReport.SafeName("a/b:c"));
    }
}
