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

    [Fact]
    public void VanillaException_IsNotedButDoesNotFail()
    {
        var r = new SelfTestReport();
        r.Begin("item.Stone", 0f);
        r.Exception(
            "InvalidOperationException: boom",
            "ItemOptimizationManager.DeregisterItem ()\nPeakXR.Sandbox.SandboxActions.SpawnItemAt ()\nPeakXR.Sandbox.SelfTestRunner+<SpawnItem>d__3.MoveNext ()"
        );
        r.End(true, 1f, null);

        Assert.True(r.Steps[0].Passed);
        Assert.Equal(1, r.VanillaExceptions);
        Assert.Equal("item.Stone PASS 1.00s vanilla: InvalidOperationException: boom", r.Line(0));
    }

    [Fact]
    public void VanillaException_WithUnityCoroutineFrames_IsNotedButDoesNotFail()
    {
        // Verbatim shape of the stack Unity logged for the spawned stone in the first in-game run.
        var r = new SelfTestReport();
        r.Begin("item.Stone", 0f);
        r.Exception(
            "ArgumentNullException: Value cannot be null.",
            "Peak.ItemOptimizer:OnDisable()\n"
                + "Photon.Pun.PhotonNetwork:InstantiateItemRoom(String, Vector3, Quaternion, Boolean)\n"
                + "PeakXR.Sandbox.SandboxActions:SpawnItemAt(Item, Vector3)\n"
                + "PeakXR.Sandbox.<SpawnItem>d__24:MoveNext()\n"
                + "PeakXR.Sandbox.<Guard>d__35:MoveNext()\n"
                + "UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)"
        );
        r.End(true, 1f, null);

        Assert.True(r.Steps[0].Passed);
        Assert.Equal(1, r.VanillaExceptions);
    }

    [Fact]
    public void ExceptionWithOtherPeakXRFrame_StillFails()
    {
        var r = new SelfTestReport();
        r.Begin("item.Rope", 0f);
        r.Exception("NullReferenceException", "PeakXR.Comfort.Vignette.Update ()");
        r.End(true, 1f, null);

        Assert.False(r.Steps[0].Passed);
        Assert.Equal(0, r.VanillaExceptions);
    }

    [Fact]
    public void ExceptionWithoutStack_StillFails()
    {
        var r = new SelfTestReport();
        r.Begin("a", 0f);
        r.Exception("boom", "");
        r.End(true, 1f, null);

        Assert.False(r.Steps[0].Passed);
    }

    [Fact]
    public void ThrowSite_SkipsUnitysThrowHelperFrame()
    {
        const string trace =
            "  at UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException (System.Object obj) [0x00018] in <89f3>:0\n"
            + "  at UnityEngine.Component.TryGetComponent[T] (T& component) [0x00001] in <89f3>:0\n"
            + "  at PeakXR.Sandbox.SelfTestRunner+<UseItem>d__42.MoveNext () [0x001a2] in <abcd>:0\n";

        Assert.Equal(
            "at UnityEngine.Component.TryGetComponent[T] (T& component) [0x00001] in <89f3>:0 < at PeakXR.Sandbox.SelfTestRunner+<UseItem>d__42.MoveNext () [0x001a2] in <abcd>:0",
            SelfTestReport.ThrowSite(trace)
        );
    }

    [Fact]
    public void ThrowSite_KeepsAPlainFirstFrame()
    {
        Assert.Equal("at Foo.Bar ()", SelfTestReport.ThrowSite("  at Foo.Bar ()\n  at Baz.Qux ()"));
        Assert.Equal("", SelfTestReport.ThrowSite(null));
    }

    [Fact]
    public void CurrentId_NamesTheRunningStep()
    {
        var r = new SelfTestReport();
        Assert.Null(r.CurrentId);
        r.Begin("use.Apple", 0f);
        Assert.Equal("use.Apple", r.CurrentId);
    }
}
