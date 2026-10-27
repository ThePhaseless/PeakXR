namespace PeakXR.Core.Tests;

// Fixtures are rows of docs/research/item-grab-shapes.csv: collider and render bounds in each prefab's own frame.
public class ItemReachTests
{
    const float GrabRadius = 0.12f; // Interaction.GrabRadius default

    // RescueHook: the collider wraps only the body; the claw mesh reaches 0.63 m past it.
    static readonly Vector3 HookColMin = new(-0.1669f, -0.3486f, -0.2358f);
    static readonly Vector3 HookColMax = new(0.1614f, 0.3521f, 0.3919f);
    static readonly Vector3 ClawMin = new(-0.2731f, -0.2023f, 0.3463f);
    static readonly Vector3 ClawMax = new(0.3283f, 0.3852f, 1.0191f);

    // Mushroom Glow: the collider mesh stands 0.17 m above the drawn mesh.
    static readonly Vector3 GlowColMin = new(-0.1903f, -0.1946f, -0.2266f);
    static readonly Vector3 GlowColMax = new(0.1588f, 0.4295f, 0.1889f);
    static readonly Vector3 GlowVisMin = new(-0.1679f, -0.183f, -0.2018f);
    static readonly Vector3 GlowVisMax = new(0.1455f, 0.2585f, 0.173f);

    [Fact]
    public void Extends_TheRescueHooksClaw_PastItsBodyCollider()
    {
        Assert.True(ItemReach.Extends(HookColMin, HookColMax, ClawMin, ClawMax));
        Assert.Equal(0.627f, ItemReach.Beyond(ClawMin, ClawMax, HookColMin, HookColMax), 3);
    }

    [Fact]
    public void Extends_NotAPartTheColliderIsBuiltFrom()
    {
        // Energy Drink: collider and mesh are the same Cube.
        var min = new Vector3(-0.0814f, -0.093f, -0.0579f);
        var max = new Vector3(0.0753f, 0.3045f, 0.0873f);
        Assert.False(ItemReach.Extends(min, max, min, max));
        Assert.False(ItemReach.Clips(min, max, min, max));
    }

    [Fact]
    public void Extends_NotAnAppleBerrysLeaves_WhichStayInGrabReachOfItsCollider()
    {
        var colMin = new Vector3(-0.2124f, -0.3755f, -0.212f);
        var colMax = new Vector3(0.209f, -0.0222f, 0.212f);
        var leavesMin = new Vector3(-0.1478f, -0.0648f, -0.2468f);
        var leavesMax = new Vector3(0.0932f, 0.031f, 0.1993f);
        Assert.False(ItemReach.Extends(colMin, colMax, leavesMin, leavesMax));
    }

    [Fact]
    public void Clips_TheGlowMushroom_WhoseColliderStandsAboveWhatIsDrawn()
    {
        Assert.True(ItemReach.Clips(GlowColMin, GlowColMax, GlowVisMin, GlowVisMax));
        Assert.False(ItemReach.Extends(GlowColMin, GlowColMax, GlowVisMin, GlowVisMax));
    }

    [Fact]
    public void Distance_AHandOnTheClaw_IsInReach_ThoughFarFromTheCollider()
    {
        var hand = new Vector3(0f, 0.1f, 0.9f); // inside the claw's box, 0.5 m past the body collider
        var claw = ReachBox.Aligned(ClawMin, ClawMax);
        float collider = Vector3.Distance(hand, Vector3.Clamp(hand, HookColMin, HookColMax));
        float extra = Vector3.Distance(hand, claw.Closest(hand));
        Assert.True(collider > GrabRadius);
        Assert.True(
            ItemReach.Distance(collider, float.PositiveInfinity, extra, false) <= GrabRadius
        );
    }

    [Fact]
    public void Distance_AboveTheGlowMushroom_OnItsColliderButNotOnTheMesh_IsOutOfReach()
    {
        var hand = new Vector3(0f, 0.43f, 0f); // on the collider's top, 0.17 m above the drawn caps
        var drawn = ReachBox.Aligned(GlowVisMin, GlowVisMax);
        float collider = Vector3.Distance(hand, Vector3.Clamp(hand, GlowColMin, GlowColMax));
        float visual = Vector3.Distance(hand, drawn.Closest(hand));
        Assert.True(collider <= GrabRadius);
        Assert.True(
            ItemReach.Distance(collider, visual, float.PositiveInfinity, true) > GrabRadius
        );
        // 5 cm over the caps still grabs.
        var near = new Vector3(0f, 0.31f, 0f);
        float c2 = Vector3.Distance(near, Vector3.Clamp(near, GlowColMin, GlowColMax));
        float v2 = Vector3.Distance(near, drawn.Closest(near));
        Assert.True(ItemReach.Distance(c2, v2, float.PositiveInfinity, true) <= GrabRadius);
    }

    [Fact]
    public void Distance_WithoutClipOrExtras_IsTheColliders()
    {
        Assert.Equal(0.3f, ItemReach.Distance(0.3f, 0.01f, float.PositiveInfinity, false));
    }

    [Fact]
    public void ReachBox_Closest_FollowsTheBoxRotation()
    {
        // A 1 x 0.2 x 0.2 box turned 90 degrees about Y: its long side lies along Z.
        var box = new ReachBox(
            Vector3.Zero,
            new Vector3(0f, 0f, -1f),
            Vector3.UnitY,
            Vector3.UnitX,
            new Vector3(0.5f, 0.1f, 0.1f)
        );
        Assert.Equal(new Vector3(0f, 0f, 0.5f), box.Closest(new Vector3(0f, 0f, 2f)));
        Assert.Equal(new Vector3(0.1f, 0f, 0f), box.Closest(new Vector3(2f, 0f, 0f)));
        var inside = new Vector3(0.05f, 0f, 0.3f);
        Assert.Equal(inside, box.Closest(inside));
    }

    [Fact]
    public void ReachBox_Encapsulate_GrowsToTheTurnedBox()
    {
        var box = new ReachBox(
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, -1f),
            Vector3.UnitY,
            Vector3.UnitX,
            new Vector3(0.5f, 0.1f, 0.2f)
        );
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        box.Encapsulate(ref min, ref max);
        Assert.Equal(new Vector3(0.8f, -0.1f, -0.5f), min);
        Assert.Equal(new Vector3(1.2f, 0.1f, 0.5f), max);
    }

    [Fact]
    public void Bones_PaddedByBonePad_ReachTheScorpionsClaws()
    {
        // Scorpion: a 0.36 m collider cube on the body; its posed bones span the claws, 0.52 m either side.
        var colMin = new Vector3(-0.18f, -0.036f, -0.18f);
        var colMax = new Vector3(0.18f, 0.224f, 0.18f);
        var bonesMin = new Vector3(-0.5195f, 0.0213f, -0.2895f) - new Vector3(ItemReach.BonePad);
        var bonesMax = new Vector3(0.5195f, 0.4267f, 0.1353f) + new Vector3(ItemReach.BonePad);
        Assert.True(ItemReach.Extends(colMin, colMax, bonesMin, bonesMax));
        // and stay inside the posed mesh (x +-0.76, y -0.03..0.46), so they never reach past what is drawn
        Assert.True(
            bonesMax.X < 0.7613f
                && bonesMin.X > -0.7613f
                && bonesMax.Y < 0.4589f + ItemReach.BonePad
        );
    }
}
