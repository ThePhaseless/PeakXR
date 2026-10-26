namespace PeakXR.Core.Tests;

public class DrawOrderTests
{
    [Fact]
    public void HandUi_SortsUnderEveryComfortOverlay()
    {
        Assert.Equal(short.MaxValue, DrawOrder.Fade);
        Assert.True(DrawOrder.Rim < DrawOrder.Fade);
        Assert.True(DrawOrder.Vignette < DrawOrder.Rim);
        Assert.True(DrawOrder.LaserCursor < DrawOrder.Vignette);
        Assert.True(DrawOrder.LaserBeam < DrawOrder.LaserCursor);
        Assert.True(DrawOrder.Keyboard < DrawOrder.LaserBeam);
        Assert.True(DrawOrder.HandUi < DrawOrder.Keyboard);
        Assert.True(DrawOrder.HandUi > 0);
    }

    [Fact]
    public void LoadingRoom_SortsBehindEverythingOnIt()
    {
        Assert.True(DrawOrder.LoadingRoom < 0);
    }
}

public class WristPoseTests
{
    static Vector3 Rot(Quaternion q, Vector3 v) => UnityQuat.Rotate(q, v);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rest_LiesOnTheBackOfTheWrist_BarAcrossTheForearm(bool left)
    {
        var rest = WristHud.Rest(Quaternion.Identity, left);
        // Grip space (GripFrame): fingers along -Y, the back of the left hand -X, of the right +X.
        float dorsal = left ? -1f : 1f;
        // The canvas is seen from its -forward side: that side is the back of the wrist.
        TestUtil.Near(new Vector3(-dorsal, 0f, 0f), Rot(rest, Vector3.UnitZ));
        // Text up toward the fingers, so the bar (canvas x) runs across the wrist.
        TestUtil.Near(GripFrame.Fingers, Rot(rest, Vector3.UnitY));
        Assert.True(MathF.Abs(Vector3.Dot(Rot(rest, Vector3.UnitX), GripFrame.Fingers)) < 1e-4f);
    }

    [Fact]
    public void Rest_ReadsLeftToRightOnBothHands()
    {
        // Each hand raised like a watch (fingers up, back of the hand to the eye): canvas x is the viewer's right.
        var eyeToHand = new Vector3(0f, 0f, 1f);
        var up = Vector3.UnitY;
        foreach (bool left in new[] { true, false })
        {
            float dorsal = left ? -1f : 1f;
            // Grip X (dorsal side toward the eye) and grip -Y (the fingers) up.
            var x = -eyeToHand * dorsal;
            var y = -up;
            var hand = UnityQuat.LookRotation(Vector3.Cross(x, y), y);
            TestUtil.Near(-eyeToHand, Rot(hand, new Vector3(dorsal, 0f, 0f)));
            TestUtil.Near(up, Rot(hand, GripFrame.Fingers));
            var rest = UnityQuat.Mul(hand, WristHud.Rest(Quaternion.Identity, left));
            TestUtil.Near(new Vector3(1f, 0f, 0f), Rot(rest, Vector3.UnitX));
            TestUtil.Near(up, Rot(rest, Vector3.UnitY));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Anchor_SitsBehindTheGripOnTheBackOfTheWrist(bool left)
    {
        var a = WristHud.Anchor(Vector3.Zero, Quaternion.Identity, left);
        Assert.True(Vector3.Dot(a, GripFrame.Forearm) > 0.08f, "toward the elbow");
        Assert.True(left ? a.X < -0.03f : a.X > 0.03f, "out of the back of the wrist");
        var mirror = WristHud.Anchor(Vector3.Zero, Quaternion.Identity, !left);
        TestUtil.Near(new Vector3(-a.X, a.Y, a.Z), mirror);
    }

    [Fact]
    public void Anchor_FollowsTheHand()
    {
        var q = UnityQuat.Euler(0f, 90f, 0f);
        var p = new Vector3(1f, 2f, 3f);
        TestUtil.Near(
            p + Rot(q, WristHud.Anchor(Vector3.Zero, Quaternion.Identity, true)),
            WristHud.Anchor(p, q, true)
        );
    }
}

public class ButtonSpotTests
{
    [Fact]
    public void Spots_MirrorBetweenHands()
    {
        foreach (var c in Enum.GetValues<VRControl>())
        {
            var right = ControllerLabels.Spot(c, left: false);
            TestUtil.Near(new Vector3(-right.X, right.Y, right.Z), ControllerLabels.Spot(c, true));
        }
    }

    [Fact]
    public void Spots_SitWhereTheFingersFindThem()
    {
        // Face buttons and stick on top of the head, trigger under its front, grip down the handle.
        var a = ControllerLabels.Spot(VRControl.Primary, false);
        var b = ControllerLabels.Spot(VRControl.Secondary, false);
        var trigger = ControllerLabels.Spot(VRControl.Trigger, false);
        var grip = ControllerLabels.Spot(VRControl.Grip, false);
        var stick = ControllerLabels.Spot(VRControl.StickClick, false);
        Assert.True(a.Y > trigger.Y && b.Y > trigger.Y && stick.Y > trigger.Y);
        Assert.True(trigger.Y > grip.Y);
        Assert.True(trigger.Z > grip.Z);
        Assert.True(b.Z > a.Z);
        Assert.True(
            ControllerLabels.Spot(VRControl.StickUp, false).Z
                > ControllerLabels.Spot(VRControl.StickDown, false).Z
        );
        // Every spot is on the controller, within a hand's width of the grip.
        foreach (var c in Enum.GetValues<VRControl>())
            Assert.True(ControllerLabels.Spot(c, false).Length() < 0.09f);
    }

    [Fact]
    public void Glyphs_NameEveryControlShortly()
    {
        Assert.Equal("A", ControllerLabels.Glyph(VRControl.Primary));
        Assert.Equal("B", ControllerLabels.Glyph(VRControl.Secondary));
        foreach (var c in Enum.GetValues<VRControl>())
        {
            var g = ControllerLabels.Glyph(c);
            Assert.False(string.IsNullOrEmpty(g));
            Assert.True(g.Length <= 8, g);
        }
        Assert.NotEqual(
            ControllerLabels.Glyph(VRControl.StickUp),
            ControllerLabels.Glyph(VRControl.StickDown)
        );
    }

    [Fact]
    public void Touch_MapsCapacitiveSensorsToControls()
    {
        var t = new ControlTouch { Trigger = true };
        Assert.True(ControllerLabels.Touched(VRControl.Trigger, t));
        Assert.False(ControllerLabels.Touched(VRControl.Primary, t));
        t = new ControlTouch { Stick = true };
        Assert.True(ControllerLabels.Touched(VRControl.StickUp, t));
        Assert.True(ControllerLabels.Touched(VRControl.StickDown, t));
        Assert.True(ControllerLabels.Touched(VRControl.StickClick, t));
        t = new ControlTouch { Primary = true, Secondary = true };
        Assert.True(ControllerLabels.Touched(VRControl.Primary, t));
        Assert.True(ControllerLabels.Touched(VRControl.Secondary, t));
        // The grip is wrapped by the fingers whenever an item is held: it never counts as a touch.
        t = new ControlTouch
        {
            Trigger = true,
            Primary = true,
            Secondary = true,
            Stick = true,
        };
        Assert.False(ControllerLabels.Touched(VRControl.Grip, t));
        Assert.False(ControllerLabels.Touched(VRControl.Menu, t));
    }

    [Fact]
    public void Emphasis_LightsTheTouchedLabelAndDimsTheRestOnThatHand()
    {
        float lit = ControllerLabels.Emphasis(touched: true, anyOnHand: true);
        float dim = ControllerLabels.Emphasis(touched: false, anyOnHand: true);
        float rest = ControllerLabels.Emphasis(touched: false, anyOnHand: false);
        Assert.Equal(1f, lit);
        Assert.True(dim < rest && rest < lit);
        // Dimmed, never gone: the others still read.
        Assert.True(dim >= 0.3f);
    }

    [Fact]
    public void Prompts_UseTheirBindingRows()
    {
        // Left-handed, item in the right hand: use on the right trigger, secondary use on the left one.
        Assert.True(
            ControllerLabels.ForPrompt(ItemPrompt.Main, false, true, "None", out var l, out var c)
        );
        Assert.False(l);
        Assert.Equal(VRControl.Trigger, c);
        ControllerLabels.ForPrompt(ItemPrompt.Secondary, false, true, "None", out l, out c);
        Assert.True(l);
        Assert.Equal(VRControl.Trigger, c);
        // Drop on the dominant secondary button: the left B for a left-handed player.
        ControllerLabels.ForPrompt(ItemPrompt.Drop, false, true, "DominantSecondary", out l, out c);
        Assert.True(l);
        Assert.Equal(VRControl.Secondary, c);
    }
}
