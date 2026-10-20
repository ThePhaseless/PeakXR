namespace PeakXR.Core.Tests;

public class BodySlotsTests
{
    static readonly Vector3 NeckPos = new(0.3f, 1.45f, -0.2f);

    static Vector3 HeadFromNeck(Quaternion headRot) =>
        NeckPos + UnityQuat.Rotate(headRot, -BodySlotLayout.NeckFromHead);

    [Theory]
    [InlineData("Mixed")]
    [InlineData("Hips")]
    [InlineData("Chest")]
    public void Layouts_HaveThreeHotbarSlotsAndTwoBackAnchors(string name)
    {
        var slots = BodySlotLayout.Create(name, 0.12f, 0f);
        Assert.Equal(5, slots.Length);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 3 }, slots.Select(s => s.GameSlot).ToArray());
        Assert.All(slots.Take(3), s => Assert.Equal(0.12f, s.Radius));
        // Back anchors sit behind the neck, above the hotbar slots.
        Assert.True(slots[3].Offset.Z < 0f && slots[4].Offset.Z < 0f);
        Assert.True(slots[3].Offset.Y > slots[0].Offset.Y);
    }

    [Fact]
    public void UnknownLayout_FallsBackToMixed()
    {
        var a = BodySlotLayout.Create("nope", 0.1f, 0f);
        var b = BodySlotLayout.Create("Mixed", 0.1f, 0f);
        Assert.Equal(a.Select(s => s.Offset), b.Select(s => s.Offset));
    }

    [Fact]
    public void HeightOffset_MovesHotbarSlotsOnly()
    {
        var a = BodySlotLayout.Create("Mixed", 0.1f, 0f);
        var b = BodySlotLayout.Create("Mixed", 0.1f, 0.1f);
        TestUtil.Near(a[0].Offset + new Vector3(0, 0.1f, 0), b[0].Offset);
        TestUtil.Near(a[3].Offset, b[3].Offset);
    }

    [Theory]
    [InlineData(-60f)]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(80f)]
    public void Neck_IgnoresHeadPitch(float pitch)
    {
        var rot = UnityQuat.Euler(pitch, 30f, 0f);
        TestUtil.Near(NeckPos, BodySlotLayout.Neck(HeadFromNeck(rot), rot), 1e-4f);
    }

    [Fact]
    public void Anchor_RotatesWithBodyYawOnly()
    {
        var slot = new BodySlot(0, new Vector3(0.2f, -0.6f, 0f), 0.1f);
        TestUtil.Near(new Vector3(0.5f, 0.85f, -0.2f), BodySlotLayout.Anchor(NeckPos, 0f, slot));
        // Facing +X (yaw 90): body right is -Z.
        TestUtil.Near(new Vector3(0.3f, 0.85f, -0.4f), BodySlotLayout.Anchor(NeckPos, 90f, slot));
    }

    [Fact]
    public void Find_ReturnsNearestSlotInsideItsRadius()
    {
        var slots = BodySlotLayout.Create("Mixed", 0.1f, 0f);
        var anchors = new Vector3[slots.Length];
        for (int i = 0; i < slots.Length; i++)
            anchors[i] = BodySlotLayout.Anchor(NeckPos, 0f, slots[i]);

        Assert.Equal(1, BodySlotLayout.Find(anchors[1] + new Vector3(0.05f, 0, 0), anchors, slots, slots.Length, 1f));
        Assert.Equal(-1, BodySlotLayout.Find(anchors[1] + new Vector3(0.2f, 0, 0), anchors, slots, slots.Length, 1f));
        // A larger scale (e.g. release) widens the catch radius.
        Assert.Equal(1, BodySlotLayout.Find(anchors[1] + new Vector3(0.15f, 0, 0), anchors, slots, slots.Length, 2f));
    }

    [Fact]
    public void GhostAlpha_FullInsideFadesToZero()
    {
        Assert.Equal(1f, BodySlotLayout.GhostAlpha(0.05f, 0.1f, 0.3f));
        TestUtil.Near(0.5f, BodySlotLayout.GhostAlpha(0.2f, 0.1f, 0.3f));
        Assert.Equal(0f, BodySlotLayout.GhostAlpha(0.4f, 0.1f, 0.3f));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(30f, 90f)]
    [InlineData(-45f, -120f)]
    [InlineData(89f, 60f)]
    public void HeadYawAndPitch(float pitch, float yaw)
    {
        var rot = UnityQuat.Euler(pitch, yaw, 0f);
        TestUtil.Near(0f, BodySlotLayout.DeltaAngle(yaw, BodySlotLayout.HeadYawDeg(rot)), 0.5f);
        TestUtil.Near(pitch, BodySlotLayout.HeadPitchDeg(rot), 0.5f);
    }

    [Fact]
    public void DeltaAngle_Wraps()
    {
        TestUtil.Near(20f, BodySlotLayout.DeltaAngle(350f, 10f));
        TestUtil.Near(-20f, BodySlotLayout.DeltaAngle(10f, -10f));
        TestUtil.Near(180f, MathF.Abs(BodySlotLayout.DeltaAngle(0f, 180f)));
    }
}

public class BodyYawTests
{
    [Fact]
    public void StartsAtHeadYaw()
    {
        var b = new BodyYaw();
        Assert.Equal(40f, b.Update(40f, 0f, 0.01f));
    }

    [Fact]
    public void LookingDown_HoldsYawInsideDeadzone()
    {
        var b = new BodyYaw();
        b.Update(0f, 0f, 0.01f);
        for (int i = 0; i < 100; i++)
            b.Update(40f, 50f, 0.02f);
        Assert.Equal(0f, b.Yaw);
    }

    [Fact]
    public void LookingDown_ClampsToDeadzone()
    {
        var b = new BodyYaw { Deadzone = 60f };
        b.Update(0f, 0f, 0.01f);
        b.Update(100f, 50f, 0.02f);
        TestUtil.Near(40f, b.Yaw);
    }

    [Fact]
    public void LookingLevel_FollowsAtLimitedSpeed()
    {
        var b = new BodyYaw { FollowSpeed = 90f };
        b.Update(0f, 0f, 0.01f);
        b.Update(30f, 0f, 0.1f);
        TestUtil.Near(9f, b.Yaw);
        for (int i = 0; i < 20; i++)
            b.Update(30f, 0f, 0.1f);
        TestUtil.Near(30f, b.Yaw);
    }

    [Fact]
    public void FollowsAcrossWrap()
    {
        var b = new BodyYaw { FollowSpeed = 1000f };
        b.Update(170f, 0f, 0.01f);
        b.Update(-170f, 0f, 0.1f);
        TestUtil.Near(0f, BodySlotLayout.DeltaAngle(190f, b.Yaw), 0.01f);
    }
}
