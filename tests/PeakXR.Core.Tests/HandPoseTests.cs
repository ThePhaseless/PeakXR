namespace PeakXR.Core.Tests;

public class HandPoseTests
{
    [Fact]
    public void Presets_AreDistinct()
    {
        var cyl = HandPose.Preset(GripPreset.Cylinder);
        var pinch = HandPose.Preset(GripPreset.Pinch);
        var flat = HandPose.Preset(GripPreset.FlatPalm);
        var pole = HandPose.Preset(GripPreset.TwoHandPole);
        Assert.True(pinch.Middle < cyl.Middle);
        Assert.True(flat.Index < pinch.Index);
        Assert.True(pole.Middle >= cyl.Middle);
    }

    [Fact]
    public void EmptyHand_UsesController()
    {
        var c = new FingerCurls(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        Assert.Equal(c, HandPose.Fingers(c, HandRole.Free, GripPreset.Cylinder));
    }

    [Fact]
    public void HoldingHand_UsesPreset_IndexCanCurlFurther()
    {
        var c = new FingerCurls(0f, 1f, 0f, 0f, 0f);
        var f = HandPose.Fingers(c, HandRole.Holding, GripPreset.FlatPalm);
        var preset = HandPose.Preset(GripPreset.FlatPalm);
        Assert.Equal(preset.Middle, f.Middle);
        Assert.Equal(1f, f.Index);
    }

    [Fact]
    public void SecondHand_UsesPoleGripForPoleItems()
    {
        var c = new FingerCurls();
        var f = HandPose.Fingers(c, HandRole.Support, GripPreset.TwoHandPole);
        Assert.Equal(HandPose.Preset(GripPreset.TwoHandPole).Middle, f.Middle);
    }

    [Fact]
    public void SecondHand_OnOtherItems_UsesSupportGrip()
    {
        var f = HandPose.Fingers(new FingerCurls(), HandRole.Support, GripPreset.Pinch);
        Assert.Equal(HandPose.Preset(GripPreset.Cylinder).Middle, f.Middle);
    }

    [Fact]
    public void JointAngle_ZeroWhenStraight_MaxWhenCurled()
    {
        for (int finger = 0; finger < FingerCurls.Count; finger++)
        {
            for (int joint = 0; joint < HandPose.JointsPerFinger; joint++)
            {
                Assert.Equal(0f, HandPose.JointAngle(finger, joint, 0f));
                Assert.Equal(
                    HandPose.MaxJointAngle(finger, joint),
                    HandPose.JointAngle(finger, joint, 1f)
                );
                Assert.True(HandPose.MaxJointAngle(finger, joint) > 0f);
            }
        }
    }

    [Fact]
    public void JointAngle_ClampsCurl()
    {
        Assert.Equal(HandPose.MaxJointAngle(1, 0), HandPose.JointAngle(1, 0, 2f));
        Assert.Equal(0f, HandPose.JointAngle(1, 0, -1f));
    }
}
