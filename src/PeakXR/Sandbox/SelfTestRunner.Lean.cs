using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.45-lean</c>: leaning must bend the avatar, not walk it. A scripted head stands on the calibrated spot, then
/// leans 0.3 m forward and later 0.25 m sideways (inside <c>Locomotion.LeanLimit</c>) with both hands reaching out
/// in front. Fails when the body (hip) or the rig origin moves more than <see cref="MaxLeanDrift"/> peak to peak over
/// <see cref="LeanWindow"/> seconds of standing or holding a lean (the old 0.2 m lean radius walked the body 0.1 m on
/// a 0.3 m lean and back on standing up), or when an arm fails the vr.08 judgement: hand more than 0.15 m off its
/// controller, stretched, or an elbow pointing up or back.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MaxLeanDrift = 0.03f;
    const float LeanWindow = 2f;

    IEnumerator LeanStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || rig == null || c.refs.hip == null)
        {
            Fail("no pose driver, rig or hip");
            yield break;
        }
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);

        // The head on the calibrated spot at the calibrated eye height: there the view sits on the avatar's eyes.
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        HeadFrame(out _, out var forward, out var right);
        var lean = Vector3.zero;
        var hands = applyHands;
        bodyRestore = () =>
        {
            if (VRInput.TestOverride != hands && VRInput.TestOverride != IdleHands)
                VRInput.TestOverride = finished ? null : IdleHands;
        };
        VRInput.TestOverride = () =>
        {
            hands();
            VRInput.HeadPosition = home + lean;
        };
        void Reach()
        {
            var head = home + lean;
            SetHand(true, head - right * 0.2f + Vector3.down * 0.3f + forward * 0.45f);
            SetHand(false, head + right * 0.2f + Vector3.down * 0.3f + forward * 0.45f);
        }
        Reach();

        var fails = new List<string>();
        var notes = new List<string>();
        yield return Wait(1f);
        yield return Drift(c, rig, "standing", fails, notes);

        foreach (
            var (name, offset) in new[]
            {
                ("forward", forward * 0.3f + Vector3.down * 0.05f),
                ("sideways", right * 0.25f + Vector3.down * 0.03f),
            }
        )
        {
            var from = lean;
            float start = Now;
            while (Now - start < 0.6f)
            {
                lean = Vector3.Lerp(from, offset, Mathf.SmoothStep(0f, 1f, (Now - start) / 0.6f));
                Reach();
                yield return null;
            }
            lean = offset;
            Reach();
            yield return Wait(1f);
            yield return Drift(c, rig, "lean " + name, fails, notes);
            var left = default(ArmSample);
            var rightArm = default(ArmSample);
            yield return SampleArms(
                c,
                driver,
                () => left,
                v => left = v,
                () => rightArm,
                v => rightArm = v
            );
            if (name == "forward")
                yield return ShotAt("vr.45-lean-forward", HandsMid());
            notes.Add("lean " + name + ":");
            JudgeArm(c, "L", true, left, true, fails, notes);
            JudgeArm(c, "R", false, rightArm, true, fails, notes);
            lean = Vector3.zero;
            Reach();
            yield return Wait(1f);
        }

        VRInput.TestOverride = hands;
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    /// <summary>Peak-to-peak travel of the hip and of the rig origin over <see cref="LeanWindow"/> seconds.</summary>
    static IEnumerator Drift(
        Character c,
        VRRig rig,
        string phase,
        List<string> fails,
        List<string> notes
    )
    {
        var hip = c.refs.hip.Rig;
        var body = new Bounds(hip.position, Vector3.zero);
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        float start = Now;
        while (Now - start < LeanWindow)
        {
            yield return null;
            body.Encapsulate(hip.position);
            origin.Encapsulate(rig.Origin.position);
        }
        float bodyP2P = body.size.magnitude;
        float originP2P = origin.size.magnitude;
        notes.Add(F($"{phase}: body {bodyP2P:0.000} m, rig {originP2P:0.000} m peak to peak"));
        if (bodyP2P > MaxLeanDrift)
            fails.Add(F($"{phase}: body moved {bodyP2P:0.000} m > {MaxLeanDrift:0.00} m"));
        if (originP2P > MaxLeanDrift)
            fails.Add(F($"{phase}: rig moved {originP2P:0.000} m > {MaxLeanDrift:0.00} m"));
    }
}
