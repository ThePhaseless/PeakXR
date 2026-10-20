namespace PeakXR.Core.Tests;

public class LookOverrideTests
{
    const float Dt = 0.02f;

    [Fact]
    public void NoOtherWriter_FollowsHmd()
    {
        var o = new LookOverride();
        var hmd = new Vector2(10f, 5f);
        Assert.Equal(hmd, o.Update(new Vector2(0f, 0f), hmd, Dt));
        var hmd2 = new Vector2(20f, -5f);
        Assert.Equal(hmd2, o.Update(hmd, hmd2, Dt));
        Assert.Equal(0f, o.Weight);
    }

    [Fact]
    public void ScriptedWrite_IsKeptWhileActive()
    {
        var o = new LookOverride();
        var written = o.Update(Vector2.Zero, Vector2.Zero, Dt);
        var scripted = new Vector2(90f, 30f);
        var result = o.Update(scripted, Vector2.Zero, Dt);
        Assert.Equal(scripted, result);
        Assert.Equal(1f, o.Weight);
        Assert.NotEqual(written, result);
    }

    [Fact]
    public void AfterScriptEnds_BlendsBackToHmd()
    {
        var o = new LookOverride { HoldTime = 0.1f, BlendTime = 0.4f };
        var hmd = Vector2.Zero;
        var last = o.Update(Vector2.Zero, hmd, Dt);
        last = o.Update(new Vector2(60f, 0f), hmd, Dt);

        float prevWeight = o.Weight;
        bool sawPartial = false;
        for (int i = 0; i < 40; i++)
        {
            last = o.Update(last, hmd, Dt);
            Assert.True(o.Weight <= prevWeight + 1e-6f);
            if (o.Weight > 0f && o.Weight < 1f)
            {
                sawPartial = true;
                Assert.True(last.X > 0f && last.X < 60f);
            }
            prevWeight = o.Weight;
        }
        Assert.True(sawPartial);
        Assert.Equal(0f, o.Weight);
        Assert.Equal(hmd, last);
    }

    [Fact]
    public void YawBlend_TakesTheShortWayAndWraps()
    {
        var o = new LookOverride { HoldTime = 0f, BlendTime = 1f };
        var last = o.Update(Vector2.Zero, new Vector2(170f, 0f), Dt);
        last = o.Update(new Vector2(-170f, 0f), new Vector2(170f, 0f), Dt);
        last = o.Update(last, new Vector2(170f, 0f), 0.5f);
        Assert.True(MathF.Abs(last.X) > 170f, $"got {last.X}");
        Assert.True(last.X <= 180f && last.X > -180f);
    }

    [Fact]
    public void WrappedEquivalentYaw_IsNotAnOverride()
    {
        var o = new LookOverride();
        o.Update(Vector2.Zero, new Vector2(180f, 0f), Dt);
        o.Update(new Vector2(-180f, 0f), new Vector2(180f, 0f), Dt);
        Assert.Equal(0f, o.Weight);
    }
}
