namespace PeakXR.Core.Tests;

public class MassCentreTests
{
    /// <summary>
    /// The game's ragdoll on flat ground, reduced to a chain hip, mid, torso, head (masses 3, 2, 2, 1). Each non-hip
    /// part is pulled toward hip + its animated offset, and half the pull pushes back on its joint parent and half on
    /// the hip (Bodypart.FollowRotation_Position, Bodypart.cs:193-205); every part loses 15 % of its velocity per 50 Hz
    /// tick (Bodypart.Drag); under control all parts are slippery (CharacterRagdoll.cs:89-96), so nothing grips the
    /// ground. The animation then bends the upper body forward, as the game's look-down pose or a lean does.
    /// </summary>
    static (Vector3 HipShift, Vector3 RootShift, Vector3 TorsoShift) Bend(float forward)
    {
        float[] mass = { 3f, 2f, 2f, 1f };
        int[] parent = { -1, 0, 1, 2 };
        var pos = new[]
        {
            Vector3.Zero,
            new Vector3(0f, 0.2f, 0f),
            new Vector3(0f, 0.45f, 0f),
            new Vector3(0f, 0.8f, 0f),
        };
        var target = new[]
        {
            Vector3.Zero,
            new Vector3(0f, 0.2f, forward * 0.2f),
            new Vector3(0f, 0.45f, forward * 0.55f),
            new Vector3(0f, 0.8f, forward),
        };
        var vel = new Vector3[4];
        var root0 = Root(pos, mass);
        var hip0 = pos[0];
        var torso0 = pos[2];
        const float dt = 0.02f;
        const float drive = 60f;
        for (int tick = 0; tick < 150; tick++)
        {
            var force = new Vector3[4];
            for (int i = 1; i < 4; i++)
            {
                var pull = (pos[0] + target[i] - pos[i]) * drive * mass[i];
                force[i] += pull;
                force[parent[i]] -= 0.5f * pull;
                force[0] -= 0.5f * pull;
            }
            for (int i = 0; i < 4; i++)
            {
                vel[i] = (vel[i] + force[i] / mass[i] * dt) * 0.85f;
                pos[i] += vel[i] * dt;
            }
        }
        return (pos[0] - hip0, Root(pos, mass) - root0, pos[2] - torso0);
    }

    static Vector3 Root(Vector3[] pos, float[] mass)
    {
        var c = new MassCentre();
        for (int i = 0; i < pos.Length; i++)
            c.Add(pos[i], mass[i]);
        return c.Root(pos[0].Y);
    }

    [Fact]
    public void BendingForward_PushesTheHipBack()
    {
        // Why the character slid backward when the player leaned: nothing external moves, so the hips go back.
        var (hip, _, torso) = Bend(0.3f);
        Assert.True(hip.Z < -0.05f, $"hip moved {hip.Z:0.000} m");
        // The old view rode the torso, so a bend alone moved the view forward without any head motion.
        Assert.True(torso.Z > 0.05f, $"torso moved only {torso.Z:0.000} m");
    }

    [Fact]
    public void BendingForward_DoesNotMoveTheMassCentreRoot()
    {
        var (_, root, _) = Bend(0.3f);
        Assert.True(MathF.Abs(root.X) < 1e-3f && MathF.Abs(root.Z) < 1e-3f, $"root moved {root}");
        Assert.True(MathF.Abs(root.Y) < 1e-3f, $"root height moved {root.Y:0.000} m");
    }

    [Fact]
    public void Root_IsMassWeightedLevelAtTheGivenHeight()
    {
        var c = new MassCentre();
        c.Add(new Vector3(0f, 5f, 0f), 3f);
        c.Add(new Vector3(1f, 9f, 2f), 1f);
        TestUtil.Near(new Vector3(0.25f, 1.5f, 0.5f), c.Root(1.5f));
        TestUtil.Near(new Vector3(0f, 2f, 0f), new MassCentre().Root(2f));
    }
}
