namespace PeakXR.Core.Tests;

public class HandWidgetPlaceTests
{
    static readonly Vector3 Eye = Vector3.Zero;
    static readonly Vector3 Mouth = new(0f, -0.07f, 0.08f);

    static Vector3 Ring(Vector3 hand, bool left = false, Quaternion? head = null) =>
        HandWidgetPlace.ClearOfFace(
            HandWidgetPlace.AboveHand(hand, Eye, 0.08f, 0.03f),
            hand,
            Eye,
            head ?? Quaternion.Identity,
            HandWidgetPlace.RingParked,
            left
        );

    [Fact]
    public void AboveHand_LiftsAndLeansTowardTheEyes()
    {
        var p = HandWidgetPlace.AboveHand(new Vector3(0f, -0.92f, 0.5f), Eye, 0.08f, 0.03f);
        // Lifted to (0, -0.84, 0.5), then 3 cm along the line back to the eye.
        var lifted = new Vector3(0f, -0.84f, 0.5f);
        TestUtil.Near(lifted + Vector3.Normalize(Eye - lifted) * 0.03f, p);
    }

    [Fact]
    public void HandOutInFront_RingStaysOnTheHand()
    {
        var hand = new Vector3(0.1f, -0.4f, 0.4f);
        TestUtil.Near(HandWidgetPlace.AboveHand(hand, Eye, 0.08f, 0.03f), Ring(hand));
    }

    [Fact]
    public void HandAtTheMouth_RingParksLowAtReadingDistance()
    {
        // vr.12: the ring rode 8 cm above the hand at the mouth, a few cm from the eyes and half the view wide.
        var p = Ring(Mouth);
        TestUtil.Near(HandWidgetPlace.RingParked, p);
        Assert.True(p.Length() >= 0.4f, $"ring {p.Length():0.00} m from the eyes");
        Assert.True(p.Y <= -0.1f, "ring not below the sight line");
    }

    [Fact]
    public void LeftHand_ParksOnTheLeft()
    {
        Assert.True(Ring(Mouth, left: false).X > 0.05f);
        Assert.True(Ring(Mouth, left: true).X < -0.05f);
    }

    [Fact]
    public void Parked_FollowsTheHead()
    {
        // Head turned 90 degrees right (+Y yaw takes +Z to +X): the parked spot turns with it.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var p = Ring(Mouth, head: yaw);
        TestUtil.Near(Vector3.Transform(HandWidgetPlace.RingParked, yaw), p);
    }

    [Fact]
    public void Parked_IgnoresHeadPitchAndRoll()
    {
        // Looking down at the food, or tilting the head, must not drag the ring around: yaw only.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
        var tilted =
            yaw
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
        TestUtil.Near(Ring(Mouth, head: yaw), Ring(Mouth, head: tilted));
    }

    [Fact]
    public void BringingFoodToTheMouth_RingNeverComesCloseToTheEyes()
    {
        for (int i = 0; i <= 50; i++)
        {
            float z = 0.08f + i * 0.01f;
            var hand = new Vector3(0f, -0.07f - i * 0.004f, z);
            var p = Ring(hand);
            Assert.True(
                p.Length() >= 0.3f,
                $"hand at {hand}: ring {p.Length():0.00} m from the eyes"
            );
        }
    }

    static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    [Theory]
    [InlineData(90f)]
    [InlineData(-90f)]
    public void Parked_LookingStraightUpOrDown_KeepsTheHeadsYaw(float pitchDeg)
    {
        // Forward parallel to up: the yaw comes from where the face is turned, not from a degenerate forward.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
        var head = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchDeg * MathF.PI / 180f);
        var p = Ring(Mouth, head: head);
        Assert.True(Finite(p));
        TestUtil.Near(Ring(Mouth, head: yaw), p, 1e-3f);
    }

    [Fact]
    public void HandAtTheEye_ParksFully()
    {
        var p = Ring(Eye);
        Assert.True(Finite(p));
        TestUtil.Near(HandWidgetPlace.RingParked, p);
    }

    [Fact]
    public void AboveHand_LiftedOntoTheEye_StaysFinite()
    {
        // Lifted exactly onto the eye there is no direction toward it: the lifted point stands.
        var hand = Eye - Vector3.UnitY * 0.08f;
        Assert.Equal(Eye, HandWidgetPlace.AboveHand(hand, Eye, 0.08f, 0.03f));
    }
}
