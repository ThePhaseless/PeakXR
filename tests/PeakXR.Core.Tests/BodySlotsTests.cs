using System.Linq;

namespace PeakXR.Core.Tests;

public class BodySlotsTests
{
    static readonly Vector3 NeckPos = new(0.3f, 1.45f, -0.2f);

    static Vector3 HeadFromNeck(Quaternion headRot) =>
        NeckPos + UnityQuat.Rotate(headRot, -BodySlotLayout.NeckFromHead);

    [Theory]
    [InlineData("Belt")]
    [InlineData("Chest")]
    public void Layouts_HaveHotbarSlotsThenTwoBackAnchors(string name)
    {
        var slots = BodySlotLayout.Create(name, 3, 0.12f, 0f);
        Assert.Equal(5, slots.Length);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 3 }, slots.Select(s => s.GameSlot).ToArray());
        Assert.All(slots.Take(3), s => Assert.Equal(0.12f, s.Radius));
        // Back anchors sit behind the neck, above the hotbar slots.
        Assert.True(slots[3].Offset.Z < 0f && slots[4].Offset.Z < 0f);
        Assert.True(slots[3].Offset.Y > slots[0].Offset.Y);
    }

    [Theory]
    [InlineData(1, new byte[] { 0, 3, 3 })]
    [InlineData(2, new byte[] { 0, 1, 3, 3 })]
    [InlineData(5, new byte[] { 0, 1, 2, 3, 3 })]
    public void HotbarCount_FollowsTheGameUpToThreeKeys(int count, byte[] expected)
    {
        var slots = BodySlotLayout.Create(BodySlotLayout.Belt, count, 0.12f, 0f);
        Assert.Equal(expected, slots.Select(s => s.GameSlot).ToArray());
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("Mixed")]
    [InlineData("Hips")]
    public void UnknownOrOldLayout_FallsBackToBelt(string name)
    {
        var a = BodySlotLayout.Create(name, 3, 0.1f, 0f);
        var b = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.1f, 0f);
        Assert.Equal(a.Select(s => s.Offset), b.Select(s => s.Offset));
        Assert.Equal(BodySlotLayout.Belt, BodySlotLayout.Names[0]);
    }

    [Fact]
    public void Belt_IsAnArcInFrontOfTheHips()
    {
        var slots = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.12f, 0f);
        var center = BodySlotLayout.BeltCenter;
        for (int i = 0; i < 3; i++)
        {
            var o = slots[i].Offset;
            var flat = new Vector2(o.X - center.X, o.Z - center.Z);
            TestUtil.Near(BodySlotLayout.BeltRadius, flat.Length());
            TestUtil.Near(center.Y, o.Y);
            // In front of the neck pivot, so they are seen when looking down and reached without twisting.
            Assert.True(o.Z > 0.1f, $"slot {i} at z {o.Z}");
            // Each slot faces straight out from the arc centre.
            TestUtil.Near(MathF.Atan2(flat.X, flat.Y) * UnityQuat.Rad2Deg, slots[i].Heading, 0.01f);
        }
        // Left to right, the middle one straight ahead, mirrored sides.
        Assert.True(slots[0].Offset.X < 0f && slots[2].Offset.X > 0f);
        TestUtil.Near(0f, slots[1].Offset.X);
        TestUtil.Near(-slots[0].Offset.X, slots[2].Offset.X);
        TestUtil.Near(slots[0].Offset.Z, slots[2].Offset.Z);
    }

    [Theory]
    [InlineData(0.06f)]
    [InlineData(0.12f)]
    [InlineData(0.25f)]
    public void Neighbours_NeverOverlap(float radius)
    {
        foreach (var name in BodySlotLayout.Names)
        {
            var slots = BodySlotLayout.Create(name, 3, radius, 0f);
            for (int i = 0; i < 2; i++)
            {
                float gap = Vector3.Distance(slots[i].Offset, slots[i + 1].Offset);
                Assert.True(gap >= BodySlotLayout.MinSpacing - 1e-4f, $"{name} gap {gap}");
                Assert.True(
                    gap >= slots[i].Radius + slots[i + 1].Radius - 1e-4f,
                    $"{name} gap {gap} radius {slots[i].Radius}"
                );
            }
        }
    }

    [Fact]
    public void Chest_SitsAboveBelt()
    {
        var belt = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.12f, 0f);
        var chest = BodySlotLayout.Create(BodySlotLayout.Chest, 3, 0.12f, 0f);
        Assert.True(chest[0].Offset.Y > belt[0].Offset.Y + 0.15f);
    }

    [Fact]
    public void HeightOffset_MovesHotbarSlotsOnly()
    {
        var a = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.1f, 0f);
        var b = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.1f, 0.1f);
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
        var slots = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.1f, 0f);
        var anchors = new Vector3[slots.Length];
        for (int i = 0; i < slots.Length; i++)
            anchors[i] = BodySlotLayout.Anchor(NeckPos, 0f, slots[i]);

        Assert.Equal(
            1,
            BodySlotLayout.Find(
                anchors[1] + new Vector3(0, 0.05f, 0),
                anchors,
                slots,
                slots.Length,
                1f
            )
        );
        Assert.Equal(
            -1,
            BodySlotLayout.Find(
                anchors[1] + new Vector3(0, 0.2f, 0),
                anchors,
                slots,
                slots.Length,
                1f
            )
        );
        // A larger scale (e.g. release) widens the catch radius.
        Assert.Equal(
            1,
            BodySlotLayout.Find(
                anchors[1] + new Vector3(0, 0.15f, 0),
                anchors,
                slots,
                slots.Length,
                2f
            )
        );
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

public class BackReachTests
{
    static readonly Vector3 NeckPos = new(0.3f, 1.45f, -0.2f);

    static int FindAt(Vector3 point, float yaw = 0f)
    {
        var slots = BodySlotLayout.Create(BodySlotLayout.Belt, 3, 0.12f, 0f);
        var anchors = new Vector3[slots.Length];
        for (int i = 0; i < slots.Length; i++)
            anchors[i] = BodySlotLayout.Anchor(NeckPos, yaw, slots[i]);
        return BodySlotLayout.Find(point, anchors, slots, slots.Length, 1f, NeckPos, yaw);
    }

    [Theory]
    // Palm on top of the right shoulder, just behind it.
    [InlineData(0.17f, -0.15f, -0.05f)]
    // Reaching further down the back.
    [InlineData(0.12f, -0.25f, -0.2f)]
    // Over the left shoulder.
    [InlineData(-0.2f, -0.1f, -0.1f)]
    public void ReachOverTheShoulder_FindsTheBackpack(float x, float y, float z)
    {
        int i = FindAt(NeckPos + new Vector3(x, y, z));
        Assert.True(i >= 3, $"found {i}");
    }

    [Theory]
    // Hand at the mouth, in front of the neck.
    [InlineData(0.05f, 0f, 0.12f)]
    // Beside the ear and in front of the neck pivot.
    [InlineData(0.15f, 0.1f, 0.06f)]
    // Raised above the head.
    [InlineData(0.15f, 0.3f, -0.1f)]
    // On the collarbone, in front of the shoulder: inside the zone's sphere, but not reaching back.
    [InlineData(0.16f, -0.1f, 0.05f)]
    public void HandsNearTheHeadButNotBehindIt_DoNotFindTheBackpack(float x, float y, float z) =>
        Assert.Equal(-1, FindAt(NeckPos + new Vector3(x, y, z)));

    [Fact]
    public void BackGate_TurnsWithTheBody()
    {
        // Facing +X: behind is -X. The same over-the-shoulder reach, turned.
        Assert.True(FindAt(NeckPos + new Vector3(-0.05f, -0.15f, -0.17f), 90f) >= 3);
        Assert.Equal(-1, FindAt(NeckPos + new Vector3(0.12f, 0f, -0.05f), 90f));
    }
}
