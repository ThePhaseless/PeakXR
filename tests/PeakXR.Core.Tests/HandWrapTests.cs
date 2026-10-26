namespace PeakXR.Core.Tests;

public class HandWrapTests
{
    // Scout middle finger bones from the game's Character prefab (Middle_1_R to Middle_3_R_end), metres.
    static readonly float[] Segments = { 0.053f, 0.044f, 0.037f };

    /// <summary>Fingertip in the finger's bend plane, knuckle at the origin, finger along +x, palm toward -y.</summary>
    static Vector2 Tip(float curl)
    {
        var p = Vector2.Zero;
        float angle = 0f;
        for (int j = 0; j < 3; j++)
        {
            angle += HandPose.JointAngle(2, j, curl) * MathF.PI / 180f;
            p += Segments[j] * new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        }
        return p;
    }

    [Fact]
    public void FullFist_KeepsTheFingertipsOutOfThePalm()
    {
        // The scout's fingers are twice its palm long and thick: a fist must close on a handle, not fold back into
        // the palm. The tip stays more than a finger's and the palm's half thickness under the knuckle line.
        var tip = Tip(1f);
        Assert.True(tip.Y < -0.045f, $"tip {tip}");
    }

    [Fact]
    public void WrapCurl_ClosesOnThinHandles_AndOpensOnBigItems()
    {
        Assert.Equal(1f, HandPose.WrapCurl(0.015f));
        float torch = HandPose.WrapCurl(0.08f);
        float apple = HandPose.WrapCurl(0.21f);
        Assert.True(apple < torch && torch < 1f);
        Assert.True(apple < 0.3f, $"apple {apple}");
        Assert.Equal(1f, HandPose.WrapCurl(0f));
    }

    [Fact]
    public void HeldFingers_StopAtTheItemSurface_FreeFingersDoNot()
    {
        var squeeze = new FingerCurls(1f, 1f, 1f, 1f, 1f);
        var held = HandPose.Fingers(squeeze, HandRole.Holding, GripPreset.Cylinder, 0.3f);
        for (int f = 0; f < FingerCurls.Count; f++)
            Assert.True(held[f] <= 0.3f, $"finger {f}: {held[f]}");
        var support = HandPose.Fingers(squeeze, HandRole.Support, GripPreset.TwoHandPole, 0.3f);
        Assert.True(support.Middle <= 0.3f);
        Assert.Equal(squeeze, HandPose.Fingers(squeeze, HandRole.Free, GripPreset.Cylinder, 0.3f));
    }
}
