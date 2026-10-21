using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.45-lean</c>: leaning bends the avatar and never moves the view. A scripted head on the calibrated spot
/// stands, leans 0.3 m forward and 0.25 m sideways (inside <c>Locomotion.LeanLimit</c>) with both hands reaching out,
/// then makes a fast 0.15 s lean, and finally leans 0.5 m (past the limit) and stands back up. The body is the
/// view's root (<see cref="RigDriver.ViewRoot"/>, the ragdoll's mass centre; the hip itself slides back when the
/// upper body bends, which is only reported). Every frame, ramps and returns included, is tracked. Fails when: inside
/// the limit, the body or the rig origin (the view's offset from the scripted head) moves more than
/// <see cref="MaxLeanDrift"/> peak to peak; the fast lean moves the rig origin more than
/// <see cref="MaxFastViewDrift"/>; the far lean does not walk the body at least 0.1 m forward, moves the rig origin,
/// or the body moves at all (beyond <see cref="MaxLeanDrift"/>) while the head stands up and for 3.5 s after; or an arm fails the vr.08 judgement during a lean (hand more than
/// 0.15 m off its controller, stretched, or an elbow pointing up or back).
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MaxLeanDrift = 0.03f;
    const float MaxFastViewDrift = 0.02f;

    Vector3 leanOffset;
    Bounds leanRoot;
    Bounds leanOrigin;
    Bounds leanHip;

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
        leanOffset = Vector3.zero;
        var hands = applyHands;
        bodyRestore = () =>
        {
            if (VRInput.TestOverride != hands && VRInput.TestOverride != IdleHands)
                VRInput.TestOverride = finished ? null : IdleHands;
        };
        VRInput.TestOverride = () =>
        {
            hands();
            VRInput.HeadPosition = home + leanOffset;
            var head = home + leanOffset;
            SetHand(true, head - right * 0.2f + Vector3.down * 0.3f + forward * 0.45f);
            SetHand(false, head + right * 0.2f + Vector3.down * 0.3f + forward * 0.45f);
        };

        var fails = new List<string>();
        var notes = new List<string>();
        yield return Wait(1f);

        // Inside the limit: tracked from here to the end of the fast lean.
        StartTrack(c, rig);
        yield return Track(c, rig, Vector3.zero, 0f, 2f);
        Drift("standing", fails, notes);
        foreach (
            var (name, offset) in new[]
            {
                ("forward", forward * 0.3f + Vector3.down * 0.05f),
                ("sideways", right * 0.25f + Vector3.down * 0.03f),
            }
        )
        {
            yield return Track(c, rig, offset, 0.6f, 1f);
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
            yield return Track(c, rig, Vector3.zero, 0.6f, 1f);
        }
        Drift("leans", fails, notes);

        // A fast lean: the view moves only with the scripted head, so the rig origin stays put.
        leanOrigin = new Bounds(rig.Origin.position, Vector3.zero);
        yield return Track(c, rig, forward * 0.3f + Vector3.down * 0.05f, 0.15f, 1f);
        yield return Track(c, rig, Vector3.zero, 0.15f, 1f);
        float fast = leanOrigin.size.magnitude;
        notes.Add(F($"fast lean: view {fast:0.000} m off the head motion"));
        if (fast > MaxFastViewDrift)
            fails.Add(F($"fast lean moved the view {fast:0.000} m > {MaxFastViewDrift:0.00} m"));

        // Past the limit: the body follows once, the view stays, and standing up does not walk it back.
        StartTrack(c, rig);
        var start = RigDriver.ViewRoot(c);
        yield return Track(c, rig, forward * 0.5f + Vector3.down * 0.06f, 0.6f, 2.5f);
        float walked = Vector3.Dot(RigDriver.ViewRoot(c) - start, forward);
        // Standing up and the 3.5 s after it: the body stays put (no walk back, no shuffle), the view never moves.
        leanRoot = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        yield return Track(c, rig, Vector3.zero, 0.6f, 3.5f);
        float back = leanRoot.size.magnitude;
        float origin = leanOrigin.size.magnitude;
        notes.Add(
            F(
                $"far lean: walked {walked:0.000} m, body {back:0.000} m while standing up and after, view {origin:0.000} m"
            )
        );
        if (walked < 0.1f)
            fails.Add(F($"far lean walked only {walked:0.000} m"));
        if (back > MaxLeanDrift)
            fails.Add(F($"body moved {back:0.000} m after standing up"));
        if (origin > MaxLeanDrift)
            fails.Add(F($"far lean moved the view {origin:0.000} m"));

        VRInput.TestOverride = hands;
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    void StartTrack(Character c, VRRig rig)
    {
        leanRoot = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        leanOrigin = new Bounds(rig.Origin.position, Vector3.zero);
        leanHip = new Bounds(c.refs.hip.Rig.position, Vector3.zero);
    }

    /// <summary>Eases the scripted lean to <paramref name="to"/> over <paramref name="seconds"/>, holds it for
    /// <paramref name="hold"/> seconds, and tracks the body root, rig origin and hip every frame.</summary>
    IEnumerator Track(Character c, VRRig rig, Vector3 to, float seconds, float hold)
    {
        var from = leanOffset;
        float start = Now;
        while (Now - start < seconds + hold)
        {
            float t = seconds > 0f ? Mathf.Clamp01((Now - start) / seconds) : 1f;
            leanOffset = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
            leanRoot.Encapsulate(RigDriver.ViewRoot(c));
            leanOrigin.Encapsulate(rig.Origin.position);
            leanHip.Encapsulate(c.refs.hip.Rig.position);
        }
        leanOffset = to;
    }

    void Drift(string phase, List<string> fails, List<string> notes)
    {
        float body = leanRoot.size.magnitude;
        float view = leanOrigin.size.magnitude;
        notes.Add(
            F(
                $"{phase}: body {body:0.000} m, view {view:0.000} m, hip {leanHip.size.magnitude:0.000} m peak to peak"
            )
        );
        if (body > MaxLeanDrift)
            fails.Add(F($"{phase}: body moved {body:0.000} m > {MaxLeanDrift:0.00} m"));
        if (view > MaxLeanDrift)
            fails.Add(F($"{phase}: view moved {view:0.000} m > {MaxLeanDrift:0.00} m"));
    }
}
