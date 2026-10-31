using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.281-finger-limits</c> (controller input: trigger, grip and a thumb on A) and <c>vr.282-finger-skeletal</c>
/// (skeletal input, the same curl on all five fingers): both scripted idle hands at curl 0, 0.5 and 1. Every finger
/// joint of both hands must stay inside its limits (<see cref="FingerJoints.Within"/>: never back past its rest pose,
/// never past its fist, never turned sideways past its splay) and turn only about its own bend axis, at the angle its
/// drawn curl asks for (curl 0 is the bind pose), each within <see cref="FingerSlack"/>. The note logs the worst turn
/// off a bend axis. vr.282 takes a screenshot of both hands open, half curled and closed. With live controllers (hold mode) it
/// judges the hands as they are.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Degrees a joint may sit off its bend axis or off the bend its curl asks for (float rounding).</summary>
    const float FingerSlack = 1f;

    static readonly float[] FingerTestCurls = { 0f, 0.5f, 1f };

    IEnumerator FingerLimitsCheck(bool skeletal)
    {
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        var leftRig = driver != null ? driver.HandRigOf(true) : null;
        var rightRig = driver != null ? driver.HandRigOf(false) : null;
        if (leftRig == null || rightRig == null || !leftRig.HasFingers || !rightRig.HasFingers)
        {
            stepSkip = "no pose driver or finger bones";
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        var worst = new FingerWorst();
        if (VRInput.TestOverride != IdleHands)
        {
            JudgeFingers(leftRig, "live L", fails, ref worst);
            JudgeFingers(rightRig, "live R", fails, ref worst);
        }
        else
        {
            try
            {
                foreach (float curl in FingerTestCurls)
                {
                    var skeletalCurls = new[] { curl, curl, curl, curl, curl };
                    VRInput.TestOverride = () =>
                    {
                        ApplyIdle();
                        FingerInput(ref VRInput.Left, curl, skeletal ? skeletalCurls : null);
                        FingerInput(ref VRInput.Right, curl, skeletal ? skeletalCurls : null);
                    };
                    // The curls ease in at 20/s (FingerCurlSmoother): settled well inside 0.5 s.
                    yield return Wait(0.5f);
                    JudgeFingers(leftRig, F($"curl {curl:0.0} L"), fails, ref worst);
                    JudgeFingers(rightRig, F($"curl {curl:0.0} R"), fails, ref worst);
                    if (skeletal)
                        yield return ShotAt(
                            curl >= 1f ? "vr.282-fingers-closed"
                                : curl > 0f ? "vr.282-fingers-half"
                                : "vr.282-fingers-open",
                            HandsMid()
                        );
                }
            }
            finally
            {
                VRInput.TestOverride = IdleHands;
            }
        }
        notes.Add(
            F(
                $"worst turn off a bend axis {worst.Off:0.00} deg ({worst.At}), worst bend off its curl {worst.Flex:0.00} deg"
            )
        );
        Conclude(fails, notes);
    }

    /// <summary>Controller input for <paramref name="curl"/>: the trigger and grip pulled that far, the index resting on
    /// the trigger and the thumb on A once they curl at all; or skeletal curls when given.</summary>
    static void FingerInput(ref HandState s, float curl, float[] skeletal)
    {
        s.SkeletalCurls = skeletal;
        if (skeletal != null)
            return;
        s.Trigger = curl;
        s.Grip = curl;
        s.TriggerTouch = curl > 0f;
        s.PrimaryTouch = curl > 0f;
    }

    struct FingerWorst
    {
        public float Off,
            Flex;
        public string At;
    }

    /// <summary>Each finger joint of <paramref name="rig"/> against its limits, its bend axis and its drawn curl.</summary>
    static void JudgeFingers(HandRig rig, string label, List<string> fails, ref FingerWorst worst)
    {
        var drawn = rig.Drawn;
        for (int f = 0; f < FingerCurls.Count; f++)
        {
            for (int j = 0; j < HandPose.JointsPerFinger; j++)
            {
                if (!rig.Measure(f, j, out float flex, out float off))
                    continue;
                float want = HandPose.JointAngle(f, j, drawn[f]);
                string at = F($"{label} finger {f} joint {j}");
                if (off >= worst.Off)
                {
                    worst.Off = off;
                    worst.At = at;
                }
                worst.Flex = Mathf.Max(worst.Flex, Mathf.Abs(flex - want));
                if (!FingerJoints.Within(f, j, flex, off, FingerSlack))
                    fails.Add(
                        F(
                            $"{at}: bent {flex:0.0} deg, {off:0.0} off its axis, outside 0-{HandPose.MaxJointAngle(f, j):0} deg and {FingerJoints.MaxSplay(f, j):0} sideways"
                        )
                    );
                else if (off > FingerSlack)
                    fails.Add(F($"{at}: {off:0.0} deg off its bend axis"));
                if (Mathf.Abs(flex - want) > FingerSlack)
                    fails.Add(
                        F(
                            $"{at}: bent {flex:0.0} deg, its curl {drawn[f]:0.00} asks for {want:0.0}"
                        )
                    );
            }
        }
    }
}
