namespace PeakXR.Core.Tests;

public class CameraPolicyTests
{
    [Fact]
    public void State_FollowsVanillaPriority()
    {
        Assert.Equal(CameraState.Body, CameraPolicy.State(false, false, false, false, false));
        Assert.Equal(CameraState.Ragdoll, CameraPolicy.State(false, false, false, false, true));
        Assert.Equal(CameraState.Rocket, CameraPolicy.State(false, false, false, true, true));
        Assert.Equal(CameraState.Spectate, CameraPolicy.State(false, false, true, true, true));
        Assert.Equal(CameraState.Override, CameraPolicy.State(false, true, true, true, true));
        Assert.Equal(CameraState.Cutscene, CameraPolicy.State(true, true, true, true, true));
    }

    [Theory]
    [InlineData(CameraState.Body, TakeoverView.ThirdPerson, ViewKind.FirstPerson)]
    [InlineData(CameraState.Body, TakeoverView.FirstPerson, ViewKind.FirstPerson)]
    [InlineData(CameraState.Ragdoll, TakeoverView.ThirdPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Ragdoll, TakeoverView.FirstPerson, ViewKind.FirstPerson)]
    [InlineData(CameraState.Spectate, TakeoverView.ThirdPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Spectate, TakeoverView.FirstPerson, ViewKind.FollowFirstPerson)]
    [InlineData(CameraState.Rocket, TakeoverView.ThirdPerson, ViewKind.ThirdPerson)]
    [InlineData(CameraState.Rocket, TakeoverView.FirstPerson, ViewKind.FollowFirstPerson)]
    [InlineData(CameraState.Override, TakeoverView.ThirdPerson, ViewKind.Static)]
    [InlineData(CameraState.Override, TakeoverView.FirstPerson, ViewKind.Static)]
    [InlineData(CameraState.Cutscene, TakeoverView.ThirdPerson, ViewKind.Static)]
    [InlineData(CameraState.Cutscene, TakeoverView.FirstPerson, ViewKind.Static)]
    public void View_PerStateAndPreference(
        CameraState state,
        TakeoverView pref,
        ViewKind expected
    ) => Assert.Equal(expected, CameraPolicy.View(state, pref, detachOnRagdoll: true));

    [Fact]
    public void View_RagdollStaysInBodyWhenDetachIsOff() =>
        Assert.Equal(
            ViewKind.FirstPerson,
            CameraPolicy.View(CameraState.Ragdoll, TakeoverView.ThirdPerson, detachOnRagdoll: false)
        );

    [Fact]
    public void IsDetached_OnlyFirstPersonIsAttached()
    {
        Assert.False(CameraPolicy.IsDetached(ViewKind.FirstPerson));
        Assert.True(CameraPolicy.IsDetached(ViewKind.FollowFirstPerson));
        Assert.True(CameraPolicy.IsDetached(ViewKind.ThirdPerson));
        Assert.True(CameraPolicy.IsDetached(ViewKind.Static));
    }
}

public class LongPressTests
{
    [Fact]
    public void FiresOnceWhenHeldLongEnough()
    {
        var lp = new LongPress { Seconds = 1f };
        Assert.False(lp.Update(true, 0f));
        Assert.False(lp.Update(true, 0.5f));
        Assert.True(lp.Update(true, 0.6f));
        Assert.False(lp.Update(true, 0.5f));
        Assert.False(lp.Update(false, 0.1f));
        Assert.False(lp.Update(true, 0.1f));
        Assert.True(lp.Update(true, 1f));
    }

    [Fact]
    public void ShortTapsNeverFire()
    {
        var lp = new LongPress { Seconds = 1f };
        for (int i = 0; i < 10; i++)
        {
            Assert.False(lp.Update(true, 0.4f));
            Assert.False(lp.Update(false, 0.4f));
        }
    }

    [Fact]
    public void ProgressRisesWhileHeldAndClearsOnRelease()
    {
        var lp = new LongPress { Seconds = 1f };
        Assert.Equal(0f, lp.Progress);
        lp.Update(true, 0.25f);
        TestUtil.Near(0.25f, lp.Progress);
        lp.Update(true, 1f);
        Assert.Equal(1f, lp.Progress);
        Assert.True(lp.Fired);
        lp.Update(false, 0.1f);
        Assert.Equal(0f, lp.Progress);
        Assert.False(lp.Fired);
    }
}
