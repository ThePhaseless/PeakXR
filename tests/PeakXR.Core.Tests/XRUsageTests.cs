namespace PeakXR.Core.Tests;

public class XRUsageTests
{
    [Fact]
    public void Find_IsCaseInsensitiveAndKeepsCandidateOrder()
    {
        var names = new[] { "trigger", "TriggerTouch", "IndexTouch" };
        Assert.Equal("TriggerTouch", XRUsageNames.Find(names, "TriggerTouch", "IndexTouch"));
        Assert.Equal("trigger", XRUsageNames.Find(names, "Trigger"));
        Assert.Null(XRUsageNames.Find(names, "MenuButton"));
    }

    [Theory]
    [InlineData("IndexFinger", 1)]
    [InlineData("ThumbCurl", 0)]
    [InlineData("middle_curl", 2)]
    [InlineData("RingFinger", 3)]
    [InlineData("PinkyFinger", 4)]
    [InlineData("LittleCurl", 4)]
    [InlineData("IndexTouch", -1)]
    [InlineData("Trigger", -1)]
    [InlineData("ThumbstickClick", -1)]
    [InlineData("Primary2DAxis", -1)]
    public void FingerIndex_RecognisesCurlUsages(string name, int finger)
    {
        Assert.Equal(finger, XRUsageNames.FingerIndex(name));
    }

    [Theory]
    [InlineData(0f, 0.8f, PadZone.Up)]
    [InlineData(0.1f, -0.7f, PadZone.Down)]
    [InlineData(0.1f, 0.1f, PadZone.Center)]
    [InlineData(0.9f, 0.1f, PadZone.Center)]
    public void PadZone_SplitsClicksByHeight(float x, float y, PadZone zone)
    {
        Assert.Equal(zone, TrackpadButtons.Zone(new Vector2(x, y)));
    }

    [Fact]
    public void JointCurl_StraightIsZero()
    {
        var joints = new[] { Vector3.Zero, new(0, 0, 1), new(0, 0, 2), new(0, 0, 3) };
        TestUtil.Near(0f, JointCurl.Curl(joints, joints.Length, 180f));
    }

    [Fact]
    public void JointCurl_SumsBendAngles()
    {
        // Two 90 degree bends.
        var joints = new[] { Vector3.Zero, new(0, 0, 1), new(0, -1, 1), new(0, -1, 0) };
        TestUtil.Near(1f, JointCurl.Curl(joints, joints.Length, 180f));
        TestUtil.Near(0.5f, JointCurl.Curl(joints, 3, 180f));
    }

    [Fact]
    public void JointCurl_TooFewJointsIsNaN()
    {
        Assert.True(float.IsNaN(JointCurl.Curl(new[] { Vector3.Zero, Vector3.One }, 2, 180f)));
    }
}
