namespace PeakXR.Core.Tests;

public class FingerJointsTests
{
    static readonly float[] Curls = { -1f, 0f, 0.25f, 0.5f, 0.75f, 1f, 2f };
    static readonly bool[] Hands = { false, true };

    /// <summary>A rotation seen in the hand's mirror image across X (left from right).</summary>
    static Quaternion Mirror(Quaternion q) => new(q.X, -q.Y, -q.Z, q.W);

    static Vector3 Mirror(Vector3 v) => new(v.X, -v.Y, -v.Z);

    [Fact]
    public void CurlZero_IsTheRestPose()
    {
        foreach (bool left in Hands)
        {
            for (int f = 0; f < FingerCurls.Count; f++)
            {
                for (int j = 0; j < HandPose.JointsPerFinger; j++)
                    TestUtil.SameRotation(Quaternion.Identity, FingerJoints.Bend(f, j, 0f, left));
            }
        }
    }

    [Fact]
    public void EveryCurl_BendsOnlyAboutTheBendAxis_WithinTheJointLimits()
    {
        foreach (bool left in Hands)
        {
            for (int f = 0; f < FingerCurls.Count; f++)
            {
                for (int j = 0; j < HandPose.JointsPerFinger; j++)
                {
                    foreach (float c in Curls)
                    {
                        var q = FingerJoints.Bend(f, j, c, left);
                        FingerJoints.Measure(
                            q,
                            FingerJoints.BendAxis(f, j, left),
                            out var flex,
                            out var off
                        );
                        Assert.True(
                            off < 0.01f,
                            $"finger {f} joint {j} curl {c}: {off} deg off its axis"
                        );
                        Assert.True(
                            flex >= 0f,
                            $"finger {f} joint {j} curl {c}: hyperextended {flex}"
                        );
                        TestUtil.Near(HandPose.JointAngle(f, j, c), flex, 0.01f);
                        Assert.True(FingerJoints.Within(f, j, flex, off, 0.01f));
                    }
                }
            }
        }
    }

    [Fact]
    public void Within_RejectsHyperextensionOverflexAndSplay()
    {
        float max = HandPose.MaxJointAngle(1, 1);
        Assert.True(FingerJoints.Within(1, 1, 10f, 0f, 0.5f));
        Assert.False(FingerJoints.Within(1, 1, -5f, 0f, 0.5f));
        Assert.False(FingerJoints.Within(1, 1, max + 5f, 0f, 0.5f));
        Assert.False(FingerJoints.Within(1, 1, 10f, FingerJoints.MaxSplay(1, 1) + 5f, 0.5f));
        Assert.False(FingerJoints.Within(1, 2, 10f, 3f, 0.5f));
    }

    [Fact]
    public void Measure_SplitsTheBendFromAnySidewaysTurn()
    {
        var axis = FingerJoints.BendAxis(1, 1, false);
        var bend = Quaternion.CreateFromAxisAngle(axis, 30f * UnityQuat.Deg2Rad);
        var side = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 8f * UnityQuat.Deg2Rad);
        FingerJoints.Measure(UnityQuat.Mul(side, bend), axis, out var flex, out var off);
        TestUtil.Near(30f, flex, 0.01f);
        TestUtil.Near(8f, off, 0.01f);
        FingerJoints.Measure(Quaternion.Inverse(bend), axis, out flex, out _);
        TestUtil.Near(-30f, flex, 0.01f);
    }

    [Fact]
    public void Fingers_CurlTowardThePalm()
    {
        // Canonical hand: fingers +Y, palm +X on the right hand and -X on the left.
        for (int f = 1; f < FingerCurls.Count; f++)
        {
            for (int j = 0; j < HandPose.JointsPerFinger; j++)
            {
                Assert.True(
                    UnityQuat.Rotate(FingerJoints.Bend(f, j, 1f, false), Vector3.UnitY).X > 0.3f
                );
                Assert.True(
                    UnityQuat.Rotate(FingerJoints.Bend(f, j, 1f, true), Vector3.UnitY).X < -0.3f
                );
            }
        }
    }

    [Fact]
    public void Thumb_SwingsInFrontOfThePalm_ThenFoldsAcrossItTowardThePinky()
    {
        foreach (bool left in Hands)
        {
            float palm = left ? -1f : 1f;
            // Opposition: the thumb's side of the hand (+Z) swings toward the palm.
            Assert.True(
                palm * UnityQuat.Rotate(FingerJoints.Bend(0, 0, 1f, left), Vector3.UnitZ).X > 0.3f
            );
            // Flexion: the thumb (along the fingers) folds toward the pinky side (-Z), in front of the palm.
            for (int j = 1; j < HandPose.JointsPerFinger; j++)
                Assert.True(
                    UnityQuat.Rotate(FingerJoints.Bend(0, j, 1f, left), Vector3.UnitY).Z < -0.3f
                );
        }
    }

    [Fact]
    public void MirroredHands_AreSymmetric()
    {
        for (int f = 0; f < FingerCurls.Count; f++)
        {
            for (int j = 0; j < HandPose.JointsPerFinger; j++)
            {
                foreach (float c in Curls)
                    TestUtil.SameRotation(
                        Mirror(FingerJoints.Bend(f, j, c, false)),
                        FingerJoints.Bend(f, j, c, true)
                    );
            }
        }
    }

    // Scout prefab (Character, resources.assets): local rotations of the right index chain, equal to the bind pose.
    // The left chain is the same with y and z negated (the rig is mirrored across the hand's X).
    static readonly Quaternion HandUpperR = new(0f, 0f, 0.545f, 0.839f);
    static readonly Quaternion Index1R = new(0.484f, 0.017f, -0.365f, 0.795f);
    static readonly Quaternion Index2R = new(-0.339f, 0f, -0.242f, 0.909f);
    static readonly Quaternion Index3R = new(-0.007f, 0f, -0.005f, 1f);

    static Quaternion[] IndexRestInHand(bool left)
    {
        var chain = new[] { HandUpperR, Index1R, Index2R, Index3R };
        var rest = new Quaternion[3];
        var q = Quaternion.Identity;
        for (int i = 0; i < chain.Length; i++)
        {
            q = UnityQuat.Mul(q, Quaternion.Normalize(left ? Mirror(chain[i]) : chain[i]));
            if (i > 0)
                rest[i - 1] = q;
        }
        return rest;
    }

    [Fact]
    public void ScoutRig_BendsAboutTheSameAxesAsTheGamesOwnFist()
    {
        // A_Scout_Idle1 / A_Scout_Emote_Fist, decoded offline: every right index joint turns about these axes in its
        // own rest frame (all the hand's -Z, the knuckle line), by 24, 51 and 122 degrees.
        var game = new[]
        {
            new Vector3(0.38f, -0.76f, -0.53f),
            new Vector3(0.58f, 0f, -0.81f),
            new Vector3(0.58f, 0.02f, -0.81f),
        };
        // Scout bind pose: the fingers run along the hand bone's +Y, the index knuckle sits +Z of the pinky's.
        var frame = FingerJoints.HandFrame(Vector3.UnitY, Vector3.UnitZ);
        foreach (bool left in Hands)
        {
            var rest = IndexRestInHand(left);
            for (int j = 0; j < 3; j++)
            {
                var axis = FingerJoints.LocalAxis(frame, rest[j], 1, j, left);
                TestUtil.Near(left ? Mirror(game[j]) : game[j], axis, 0.03f);
            }
        }
        float[] fist = { 24.1f, 51.1f, 122.1f };
        for (int j = 0; j < 3; j++)
            TestUtil.Near(fist[j], HandPose.MaxJointAngle(1, j), 0.5f);
    }

    [Fact]
    public void HandFrame_MapsTheCanonicalHandOntoTheRig()
    {
        var fingers = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));
        var index = Vector3.UnitZ;
        var frame = FingerJoints.HandFrame(fingers, index);
        TestUtil.Near(fingers, UnityQuat.Rotate(frame, Vector3.UnitY));
        Assert.True(Vector3.Dot(UnityQuat.Rotate(frame, Vector3.UnitZ), index) > 0.98f);
    }
}
