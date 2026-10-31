using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Locomotion;
using PeakXR.Rig;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.45-lean</c>: leaning bends the avatar and never moves the view; a held or far lean is stepped under. The body must first be grounded and at
/// rest for 1 s, else "not settled". A scripted head on the calibrated spot
/// stands, leans 0.3 m forward and 0.25 m sideways (inside <c>Locomotion.LeanLimit</c>) with both hands reaching out,
/// then makes a fast 0.25 s lean, and finally leans 0.5 m (past the limit) and stands back up. The body is the
/// view's root (<see cref="RigDriver.ViewRoot"/>, the ragdoll's mass centre; the hip itself slides back when the
/// upper body bends, which is only reported). Every frame, ramps and returns included, is tracked. Fails when: inside
/// the limit, the body (horizontally: its height follows the bend) moves more than <see cref="MaxLeanDrift"/> peak to
/// peak before the stance walk steps it under the held lean (each lean holds past <see cref="StanceWalk.Delay"/> for
/// the arm samples, and the body settles back under the head before the next), a lean is stepped under sooner than
/// that delay from its hold (less 0.3 s) or a walk already runs as it begins (<c>LeanLimit</c> held at its default for
/// the step), or the rig origin (the view's offset
/// from the scripted head) moves more than that over the leans, walks included (standing still, the view: <see cref="MaxStandViewDrift"/>); a lean bends
/// the spine less than <see cref="MinLeanBend"/> or moves the ragdoll's shoulders less than
/// <see cref="MinLeanShoulders"/> along it; the fast lean moves the rig origin more than <see cref="MaxLeanDrift"/>; the far lean does not walk the body at least 0.1 m forward or moves the rig origin,
/// or 3.5 s after the head stands back up the avatar's eyes are more than <see cref="MaxStanceEyeOff"/> off the
/// view (the body walks back under the head); or an arm fails the vr.08 judgement during a lean (hand more than
/// 0.15 m off its target, stretched, or an elbow pointing up or back). The hands (0.45 m ahead of and 0.3 m under the
/// head, about 0.53 m from a person's shoulders) are out of the scout's reach, so the target is the controller
/// mapped onto the avatar's reach (<see cref="ArmReach"/>); how far that holds each hand short is reported.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>View (rig origin) or body travel (m, peak to peak) a lean may cause while the head holds still for
    /// most of the window. PEAK 3.0's idle sway bobbed the view 0.030-0.038 m (vr43), which a still player sees: this
    /// is the most a lean may show, not a margin over it.</summary>
    const float MaxLeanDrift = 0.03f;

    /// <summary>Standing still nothing masks a view shift, and rig.camera's view hold (Core ViewHold) keeps idle sway
    /// out of the view: half the visible 0.03 m, so a broken hold fails instead of passing by rounding (vr43: 0.030 m
    /// against 0.03). PEAK 2.6 bodies, with no hold, stood at 0.002-0.011 m (vr31-vr41).</summary>
    const float MaxStandViewDrift = 0.015f;

    /// <summary>A lean inside the limit bends the spine (vr30-vr43: 14-18 deg) and the ragdoll's shoulders follow
    /// (0.033-0.096 m along the lean). Floors well under those, so a lean that never bends fails.</summary>
    const float MinLeanBend = 5f;

    const float MinLeanShoulders = 0.02f;

    Vector3 leanOffset;
    Bounds leanRoot;

    /// <summary>The body root is still the lean's to judge: no stance walk has run since the lean began.</summary>
    bool leanRootLive;

    /// <summary><see cref="Now"/> the judged lean began (<see cref="leanRootLive"/>), and its first stance walk started
    /// (NaN: none).</summary>
    float leanRootSince;

    float leanWalkAt;
    Bounds leanOrigin;
    Bounds leanHip;

    IEnumerator LeanStep()
    {
        // The leans are sized for the default limit (0.3 m inside 0.35 m): a lower one walks them at once.
        var limit = LocomotionConfig.LeanRadiusSetting;
        float saved = limit?.Value ?? 0f;
        if (limit != null)
            limit.Value = (float)limit.DefaultValue;
        try
        {
            yield return LeanSteps();
        }
        finally
        {
            if (limit != null)
                limit.Value = saved;
        }
    }

    IEnumerator LeanSteps()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || rig == null || c.refs.hip == null)
        {
            Fail("no pose driver, rig or hip");
            yield break;
        }
        // HandSetup settled the body first (Settle: run 29 started right after the sprint step with the body still
        // moving, "standing: body moved 0.551 m").
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        // Run 24 leaned into the crashed plane's seats: face open ground first, or say what is in the way.
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no clear 1.3 m to lean into in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.8f);
        // Standing still keeps the turn's eye shift (run 26: the view 0.29 m off the avatar's eyes), which puts the
        // scripted hands that far off the shoulders. A recenter (under its fade) starts the view on the eyes.
        bool recentered = false;
        Action onRecenter = () => recentered = true;
        rig.Recentered += onRecenter;
        rig.Recenter();
        yield return WaitFor(() => recentered, 3f);
        rig.Recentered -= onRecenter;
        yield return Wait(0.8f);
        RigDriver.TraceLeanUntil = Time.unscaledTime + 60f;
        extremeLines = 0;

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
        leanContacts.Clear();
        yield return Track(c, rig, Vector3.zero, 0f, 2f);
        Drift("standing", fails, notes, MaxStandViewDrift);
        float leanBody = 0f;
        bool leanWalked = false;
        foreach (
            var (name, offset) in new[]
            {
                ("forward", forward * 0.3f + Vector3.down * 0.05f),
                ("sideways", right * 0.25f + Vector3.down * 0.03f),
            }
        )
        {
            // Each lean from the body standing under the head: a held lean is stepped under after the stance walk's
            // delay (#10), and walked back once the head stands up, so the body is judged per lean up to its walk.
            var standShoulders = Shoulders(c);
            var standHip = c.refs.hip.Rig.position;
            // Run 26 set the bend's absolute shoulder shift beside the ragdoll's change from standing; both from
            // standing.
            var standAnimShift = driver.LeanShoulderShift;
            leanRoot = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
            leanRootLive = true;
            leanRootSince = Now;
            leanWalkAt = float.NaN;
            if (RigDriver.FollowWalking)
                fails.Add($"lean {name}: a stance walk already ran as the lean began");
            leanBend = 0f;
            leanAnimShoulders = 0f;
            // Each stage of the lean, so a failure names the one that did not happen: the gate, the bend applied
            // to the animated spine, how far that moved the animated shoulders, and what the ragdoll did.
            var worldDir = rig.Origin.TransformDirection(offset.normalized);
            worldDir.y = 0f;
            worldDir.Normalize();
            leanAnimDir = worldDir;
            leanAnimFrom = standAnimShift;
            yield return Track(c, rig, offset, 0.6f, 1f);
            leanAnimDir = Vector3.zero;
            float ragdollShoulders = Vector3.Dot(Shoulders(c) - standShoulders, worldDir);
            notes.Add(
                F(
                    $"lean {name} stages: gate {driver.LeanGate ?? "open"}, weight {driver.LeanWeight:0.00}, bend {leanBend:0.0} deg, animated shoulders {leanAnimShoulders:0.000} m, ragdoll shoulders {ragdollShoulders:0.000} m, hip {Vector3.Dot(c.refs.hip.Rig.position - standHip, worldDir):0.000} m along the lean"
                )
            );
            // A lean that never bent kept the view still too, and passed.
            if (leanBend < MinLeanBend || ragdollShoulders < MinLeanShoulders)
                fails.Add(
                    F(
                        $"lean {name} did not bend the body: bend {leanBend:0.0} deg (min {MinLeanBend:0}), ragdoll shoulders {ragdollShoulders:0.000} m (min {MinLeanShoulders:0.00})"
                    )
                );
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
            notes.Add(
                F(
                    $"lean {name}: reach map holds L {driver.ReachPull(true):0.000} m, R {driver.ReachPull(false):0.000} m short of the controllers, leg shift {driver.LegShiftNow.magnitude:0.000} m"
                )
            );
            JudgeArm(c, "L", true, left, true, fails, notes);
            JudgeArm(c, "R", false, rightArm, true, fails, notes);
            yield return Track(c, rig, Vector3.zero, 0.6f, 1f);
            leanBody = Mathf.Max(leanBody, FlatSize(leanRoot));
            leanWalked |= !leanRootLive;
            // Held, the lean is stepped under only after the walk's delay from its hold (0.6 s into the lean; 0.3 s
            // for the debt to stop moving inside the walk's start distance before the ramp ends).
            float walkAt = leanWalkAt - (leanRootSince + 0.6f);
            if (walkAt < StanceDelay - 0.3f)
                fails.Add(
                    F(
                        $"lean {name} was stepped under {walkAt:0.00} s into its hold, before the {StanceDelay:0.0} s delay"
                    )
                );
            // The walk back under the head, the view still tracked: no walk may move it.
            yield return Settle(rig, leanOrigin, b => leanOrigin = b);
        }
        notes.Add(
            leanWalked
                ? "leans: held past the stance delay, stepped under and back"
                : "leans: no stance walk"
        );
        Drift("leans", fails, notes, MaxLeanDrift, leanBody);
        float slowView = leanOrigin.size.magnitude;

        // A fast lean: the view moves only with the scripted head, so the rig origin stays put. 0.25 s peaks at
        // 1.8 m/s, a quick dodge (0.15 s peaked at 3 m/s, past any real lean). The window is mostly its 1 s holds,
        // so it gets the slow leans' limit: the old 0.02 m judged idle sway (vr43: 0.032 m), not speed.
        leanOrigin = new Bounds(rig.Origin.position, Vector3.zero);
        yield return Track(c, rig, forward * 0.3f + Vector3.down * 0.05f, 0.25f, 1f);
        yield return Track(c, rig, Vector3.zero, 0.25f, 1f);
        float fast = leanOrigin.size.magnitude;
        notes.Add(
            F($"fast lean: view {fast:0.000} m off the head motion (slow leans {slowView:0.000} m)")
        );
        if (fast > MaxLeanDrift)
            fails.Add(F($"fast lean moved the view {fast:0.000} m > {MaxLeanDrift:0.00} m"));

        // Past the limit: the body walks after the head at once and the view stays. Standing back up walks it back
        // under the head (the body stays under the head, #10), the view still unmoved.
        StartTrack(c, rig);
        var start = RigDriver.ViewRoot(c);
        var originStart = rig.Origin.position;
        float groundStart = GroundY(start);
        // forward is tracking space (the scripted head); the body moves in the world, under the rig's yaw. Run 24
        // projected the world walk on the tracking axis and read a forward walk as "-0.287 m".
        var worldForward = rig.Origin.TransformDirection(forward);
        yield return Track(c, rig, forward * 0.5f + Vector3.down * 0.06f, 0.6f, 2.5f);
        float walked = Vector3.Dot(RigDriver.ViewRoot(c) - start, worldForward);
        float viewLean = Vector3.Dot(rig.Origin.position - originStart, worldForward);
        var originUp = rig.Origin.position;
        // Standing up and the 3.5 s after it: the body walks back under the head, the view never moves.
        var upRoot = RigDriver.ViewRoot(c);
        yield return Track(c, rig, Vector3.zero, 0.6f, 3.5f);
        float walkedBack = -Vector3.Dot(RigDriver.ViewRoot(c) - upRoot, worldForward);
        float eyesOff = RigDriver.TryEyeRenderers(c, out var upEye)
            ? Vector3
                .ProjectOnPlane(rig.Head.position - RigDriver.ViewHeld - upEye, Vector3.up)
                .magnitude
            : float.NaN;
        // Run 40: x/z of the view held to the millimetre while it rose 0.042 m with the hip (+0.127 m): the walk
        // went up the beach's slope, and the view must follow the ground the body stands on. So: the view's
        // horizontal range, and its height change against the ground's under the body.
        float origin = FlatSize(leanOrigin);
        float groundRise = GroundY(RigDriver.ViewRoot(c)) - groundStart;
        float viewRise = rig.Origin.position.y - originStart.y;
        float offGround = Mathf.Abs(viewRise - groundRise);
        notes.Add(
            F(
                $"far lean: walked {walked:0.000} m, back {walkedBack:0.000} m on standing up, eyes then {eyesOff:0.000} m off the view, view {origin:0.000} m (forward {viewLean:0.000} m leaning, {Vector3.Dot(rig.Origin.position - originUp, worldForward):0.000} m standing up), view rose {viewRise:0.000} m on ground rising {groundRise:0.000} m"
            )
        );
        if (walked < 0.1f)
            fails.Add(F($"far lean walked only {walked:0.000} m while leaning past the limit"));
        if (!(eyesOff <= MaxStanceEyeOff))
            fails.Add(
                F(
                    $"standing up left the avatar's eyes {eyesOff:0.000} m off the view (walked back {walkedBack:0.000} m), want {MaxStanceEyeOff:0.00} m"
                )
            );
        if (origin > MaxLeanDrift)
            fails.Add(F($"far lean moved the view {origin:0.000} m"));
        if (!float.IsNaN(groundRise) && offGround > MaxLeanDrift)
            fails.Add(
                F($"far lean view height {viewRise:0.000} m off the ground's {groundRise:0.000} m")
            );

        RigDriver.TraceLeanUntil = 0f;
        if (leanContacts.Count > 0)
            notes.Add("body touched " + string.Join(", ", leanContacts));
        VRInput.TestOverride = hands;
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    /// <summary>How far the scripted eyes sit ahead of the head's turn axis (m): a person's neck.</summary>
    const float NeckSwing = 0.1f;

    /// <summary>Most the avatar's eyes may end off the view (m, level) once the stance walk has settled.</summary>
    const float MaxStanceEyeOff = 0.05f;

    /// <summary>Most the body may bend once it stands under a turned head (degrees): no lean is left to ask for, and
    /// the old lean-back peers saw was 16 degrees.</summary>
    const float MaxStanceLean = 8f;

    /// <summary>
    /// <c>vr.45b-head-turn</c>: the scripted head on the calibrated spot turns in the room (no snap turn) to 90, 180
    /// and 270 degrees and back to 0, the eyes swinging on a <see cref="NeckSwing"/> neck. The game turns the body to
    /// the head's heading about its mass centre (Character.SetRotation, Character.cs:1539-1542), its eyes about 0.24 m
    /// ahead of it, so an unmoved body left them up to 0.27 m off the view; the stance walk
    /// (<see cref="BodyStance"/>, player.locomotion) steps the body under the head with the game's move input once the
    /// turn has held for its delay. After each turn has settled (the delay and the walk) it fails when the avatar's eyes (less the view hold's offset)
    /// are more than <see cref="MaxStanceEyeOff"/> off the view or the body bends more than
    /// <see cref="MaxStanceLean"/>; over the whole step it fails when the view (the rig origin, which only the head may
    /// move) moves more than <see cref="MaxLeanDrift"/>.
    /// </summary>
    IEnumerator HeadTurnStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || rig == null)
        {
            Fail("no pose driver or rig");
            yield break;
        }
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        // The turned body steps round its head: room for it first.
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no clear 1.3 m to turn in in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.8f);
        yield return RecenterHead(rig);

        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var forward = level * Vector3.forward;
        bodyRestore = ReleaseHead;
        var fails = new List<string>();
        var notes = new List<string>();
        var start = RigDriver.ViewRoot(c);
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        float yaw = 0f;
        foreach (float to in new[] { 90f, 180f, 270f, 360f })
        {
            float from = yaw;
            float t0 = Now;
            while (Now - t0 < 0.6f)
            {
                yaw = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, (Now - t0) / 0.6f));
                var q = Quaternion.Euler(0f, yaw, 0f);
                ScriptHead(home + (q * forward - forward) * NeckSwing, q * level);
                yield return null;
                origin.Encapsulate(rig.Origin.position);
            }
            yaw = to;
            var turned = Quaternion.Euler(0f, yaw, 0f);
            ScriptHead(home + (turned * forward - forward) * NeckSwing, turned * level);
            yield return Settle(rig, origin, b => origin = b);
            var head = rig.Head;
            if (head == null || !RigDriver.TryEyeRenderers(c, out var eye))
            {
                fails.Add("no view or avatar eyes to compare");
                break;
            }
            var off = Vector3.ProjectOnPlane(head.position - RigDriver.ViewHeld - eye, Vector3.up);
            var walked = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c) - start, Vector3.up);
            string at = F($"head {Mathf.DeltaAngle(0f, yaw):+0;-0;0} deg");
            notes.Add(
                F(
                    $"{at}: eyes {off.magnitude:0.000} m off the view, body walked {walked.magnitude:0.000} m, lean {driver.LeanDegrees:0.0} deg ({driver.LeanGate ?? "open"}), debt {RigDriver.StanceDebt.magnitude:0.000} m"
                )
            );
            if (off.magnitude > MaxStanceEyeOff)
                fails.Add(
                    F(
                        $"{at}: the avatar's eyes {off.magnitude:0.000} m off the view, want {MaxStanceEyeOff:0.00} m"
                    )
                );
            if (driver.LeanDegrees > MaxStanceLean)
                fails.Add(
                    F($"{at}: the body bends {driver.LeanDegrees:0.0} deg under a turned head")
                );
        }
        ReleaseHead();
        float drift = origin.size.magnitude;
        notes.Add(F($"view moved {drift:0.000} m"));
        if (drift > MaxLeanDrift)
            fails.Add(
                F(
                    $"the view moved {drift:0.000} m while only the head turned, want {MaxLeanDrift:0.00} m"
                )
            );
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// <c>vr.273-look-down-step-back</c>: the scripted head on the calibrated spot looks 60 degrees down and holds it.
    /// Below 35 degrees the body stands back away from the look (<see cref="BodyStance.StepBack"/>, 0.28 m at 60), so
    /// the player sees the ground before its feet. Once settled (the delay and the walk) the avatar's eyes must be
    /// at least <see cref="MinStepBack"/> behind the view along the look and the mass centre at least that far back
    /// from where it stood; looking level again, the eyes must be back under the view (<see cref="MaxStanceEyeOff"/>).
    /// Over the whole step the view (rig origin) may not move more than <see cref="MaxLeanDrift"/>.
    /// </summary>
    IEnumerator LookDownStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || rig == null)
        {
            Fail("no pose driver or rig");
            yield break;
        }
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        // The body steps back: room behind it.
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no clear 1.3 m to step in in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn + 180f);
        yield return Wait(0.8f);
        yield return RecenterHead(rig);

        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        bodyRestore = ReleaseHead;
        var fails = new List<string>();
        var notes = new List<string>();
        var start = RigDriver.ViewRoot(c);
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        foreach (float pitch in new[] { 60f, 0f })
        {
            var from = VRInput.HeadRotation;
            var to = level * Quaternion.Euler(pitch, 0f, 0f);
            float t0 = Now;
            while (Now - t0 < 0.6f)
            {
                ScriptHead(
                    home,
                    Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, (Now - t0) / 0.6f))
                );
                yield return null;
                origin.Encapsulate(rig.Origin.position);
            }
            ScriptHead(home, to);
            yield return Settle(rig, origin, b => origin = b);
            if (rig.Head == null || !RigDriver.TryEyeRenderers(c, out var eye))
            {
                fails.Add("no view or avatar eyes to compare");
                break;
            }
            var look = Flat(rig.Head.rotation * Vector3.forward);
            if (look.sqrMagnitude < 1e-4f)
                look = Flat(rig.Head.rotation * Vector3.up);
            look.Normalize();
            var off = Vector3.ProjectOnPlane(
                rig.Head.position - RigDriver.ViewHeld - eye,
                Vector3.up
            );
            float behind = Vector3.Dot(off, look);
            float back = Vector3.Dot(start - RigDriver.ViewRoot(c), look);
            string at = F($"look {-pitch:0} deg");
            notes.Add(
                F(
                    $"{at}: eyes {behind:0.000} m behind the view ({off.magnitude:0.000} m off), mass centre {back:0.000} m back, lean {driver.LeanDegrees:0.0} deg, debt {RigDriver.StanceDebt.magnitude:0.000} m"
                )
            );
            if (pitch > 0f && (behind < MinStepBack || back < MinStepBack))
                fails.Add(
                    F(
                        $"{at}: the body stood {back:0.000} m back, its eyes {behind:0.000} m behind the view, want {MinStepBack:0.00} m"
                    )
                );
            if (pitch <= 0f && off.magnitude > MaxStanceEyeOff)
                fails.Add(
                    F(
                        $"{at}: the avatar's eyes {off.magnitude:0.000} m off the view looking level again, want {MaxStanceEyeOff:0.00} m"
                    )
                );
        }
        ReleaseHead();
        float drift = origin.size.magnitude;
        notes.Add(F($"view moved {drift:0.000} m"));
        if (drift > MaxLeanDrift)
            fails.Add(
                F(
                    $"the view moved {drift:0.000} m while only the head pitched, want {MaxLeanDrift:0.00} m"
                )
            );
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    /// <summary>Least step back (m) vr.273 wants at a 60 degree look down.</summary>
    const float MinStepBack = 0.2f;

    static readonly float StanceDelay = new StanceWalk().Delay;

    /// <summary>Recenters on the current head and waits for it, so the room anchor sits on the calibrated spot.</summary>
    static IEnumerator RecenterHead(VRRig rig)
    {
        bool recentered = false;
        Action onRecenter = () => recentered = true;
        rig.Recentered += onRecenter;
        rig.Recenter();
        yield return WaitFor(() => recentered, 3f);
        rig.Recentered -= onRecenter;
        yield return Wait(0.8f);
    }

    /// <summary>Waits out the stance walk's delay (a held debt walks after <see cref="StanceWalk.Delay"/>), then while a
    /// walk runs or is still owed (at most 3 s more), tracking the rig origin meanwhile. The delay runs from when the
    /// debt holds still, which after a turn is after the ragdoll's swing: cf6aa3e judged vr.300's smooth turn just
    /// before its walk started.</summary>
    static IEnumerator Settle(VRRig rig, Bounds origin, Action<Bounds> done)
    {
        float t0 = Now;
        float delay = StanceDelay;
        while (
            Now - t0 < delay + 0.5f
            || ((RigDriver.FollowWalking || LocomotionInput.StanceOwed) && Now - t0 < delay + 3f)
        )
        {
            yield return null;
            origin.Encapsulate(rig.Origin.position);
        }
        if (RigDriver.FollowWalking || LocomotionInput.StanceOwed)
            LogStanceTimeout("settle");
        float t1 = Now;
        while (Now - t1 < 0.3f)
        {
            yield return null;
            origin.Encapsulate(rig.Origin.position);
        }
        done(origin);
    }

    /// <summary>Logs the stance walk's state when a wait for it ran out, so a timeout is not silent.</summary>
    static void LogStanceTimeout(string where)
    {
        var debt = RigDriver.StanceDebt;
        Plugin.Log.LogInfo(
            F(
                $"[selftest] {where}: stance wait timed out, debt {new Vector2(debt.x, debt.z).magnitude:0.000} m, blocked {LocomotionInput.FollowStalled}, walking {RigDriver.FollowWalking}, owed {LocomotionInput.StanceOwed}"
            )
        );
    }

    void StartTrack(Character c, VRRig rig)
    {
        leanRootLive = true;
        leanRootSince = Now;
        leanRoot = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        leanOrigin = new Bounds(rig.Origin.position, Vector3.zero);
        leanHip = new Bounds(c.refs.hip.Rig.position, Vector3.zero);
    }

    int extremeLines;

    /// <summary>
    /// Runs 35-37 failed on body and view peaks that the 0.25 s lean trace never caught (the origin read the same at
    /// every sample). Logs the frame each time the tracked body or view range grows by 5 mm or more (at most 40
    /// lines a run), with what may cause it: the frame time (a hitch), grounding, the room anchor, head and hip.
    /// </summary>
    void NoteExtreme(Character c, VRRig rig, float rootBefore, float originBefore)
    {
        float root = leanRoot.size.magnitude;
        float origin = leanOrigin.size.magnitude;
        if (extremeLines >= 40 || root - rootBefore < 0.005f && origin - originBefore < 0.005f)
            return;
        extremeLines++;
        var r = RigDriver.ViewRoot(c);
        var o = rig.Origin.position;
        var hip = c.refs.hip.Rig.position;
        var head = VRInput.HeadPosition;
        Plugin.Log.LogInfo(
            F(
                $"[selftest] lean extreme f{Time.frameCount} t{Time.unscaledTime:0.000} dt {Time.unscaledDeltaTime * 1000f:0.0} ms body {root:0.000} view {origin:0.000} root ({r.x:0.000},{r.z:0.000}) origin ({o.x:0.000},{o.y:0.000},{o.z:0.000}) hip y {hip.y:0.000} room ({rig.RoomAnchor.X:0.000},{rig.RoomAnchor.Y:0.000}) head ({head.x:0.000},{head.z:0.000}) hip ({hip.x:0.000},{hip.z:0.000}) grounded {c.data.isGrounded} since {c.data.sinceGrounded:0.00} input {c.input.movementInput.y:0.00}"
            )
        );
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
            float rootSize = leanRoot.size.magnitude;
            float originSize = leanOrigin.size.magnitude;
            // The body is a lean's until the stance walk takes it (a lean held past its delay is stepped under), also
            // when that walk ran between two tracks (the arm samples).
            bool live =
                !RigDriver.FollowWalking && LocomotionInput.StanceWalkStartedAt < leanRootSince;
            if (leanRootLive && !live)
                leanWalkAt = LocomotionInput.StanceWalkStartedAt;
            leanRootLive &= live;
            if (leanRootLive)
                leanRoot.Encapsulate(RigDriver.ViewRoot(c));
            leanOrigin.Encapsulate(rig.Origin.position);
            leanHip.Encapsulate(c.refs.hip.Rig.position);
            NoteExtreme(c, rig, rootSize, originSize);
            var driver = AvatarPoseDriver.Of(c);
            if (driver != null)
            {
                leanBend = Mathf.Max(leanBend, driver.LeanDegrees);
                leanAnimShoulders = Mathf.Max(
                    leanAnimShoulders,
                    Vector3.Dot(driver.LeanShoulderShift - leanAnimFrom, leanAnimDir)
                );
            }
            NoteContacts(c.Center);
            NoteContacts(c.Head);
        }
        leanOffset = to;
    }

    float leanBend;
    float leanAnimShoulders;

    /// <summary>While a lean is tracked: its world direction and the standing bend's shoulder shift, so the animated
    /// shoulders are reported along the lean from standing, like the ragdoll's.</summary>
    Vector3 leanAnimDir;
    Vector3 leanAnimFrom;
    readonly HashSet<string> leanContacts = new(StringComparer.Ordinal);
    readonly Collider[] contactBuffer = new Collider[8];

    /// <summary>World geometry within 0.3 m of the torso or head: something the lean pushes against moves the
    /// slippery body (and the view riding it) and stops the shoulders.</summary>
    void NoteContacts(Vector3 at)
    {
        int n = Physics.OverlapSphereNonAlloc(
            at,
            0.3f,
            contactBuffer,
            HelperFunctions.AllPhysicalExceptCharacter,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < n; i++)
            leanContacts.Add(contactBuffer[i].name);
    }

    static Vector3 Shoulders(Character c)
    {
        var parts = c.refs.ragdoll.partDict;
        return
            parts.TryGetValue(BodypartType.Arm_L, out var l)
            && parts.TryGetValue(BodypartType.Arm_R, out var r)
            ? (l.Rig.position + r.Rig.position) * 0.5f
            : c.Center;
    }

    /// <summary>
    /// The yaw turn (degrees, smallest first) that faces the body toward 1.3 m of open space ahead with 0.8 m to the
    /// right (the forward, far and sideways leans), probed with 0.3 m spheres from the torso and the head. False with
    /// the colliders in the way when no heading of 16 is clear.
    /// </summary>
    static bool ClearHeading(Character c, VRRig rig, out float turn, out string blocked)
    {
        var facing = Flat(rig.Head.forward);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 16; i++)
        {
            turn = (i + 1) / 2 * 22.5f * (i % 2 == 0 ? 1f : -1f);
            var f = Quaternion.Euler(0f, turn, 0f) * facing;
            var r = Vector3.Cross(Vector3.up, f);
            if (
                Open(c.Center, f, 1.3f, seen)
                && Open(c.Head, f, 1.3f, seen)
                && Open(c.Center, r, 0.8f, seen)
                && Open(c.Head, r, 0.8f, seen)
            )
            {
                blocked = null;
                return true;
            }
        }
        turn = 0f;
        blocked = string.Join(", ", seen);
        return false;

        static bool Open(Vector3 from, Vector3 dir, float distance, HashSet<string> seen)
        {
            if (
                !Physics.SphereCast(
                    from,
                    0.3f,
                    dir,
                    out var hit,
                    distance,
                    HelperFunctions.AllPhysicalExceptCharacter,
                    QueryTriggerInteraction.Ignore
                )
            )
                return true;
            seen.Add(hit.collider.name);
            return false;
        }
    }

    /// <summary>Horizontal peak-to-peak size of a tracked range.</summary>
    static float FlatSize(Bounds b) => new Vector2(b.size.x, b.size.z).magnitude;

    /// <summary>Ground height under <paramref name="at"/>, NaN without ground within 3 m.</summary>
    static float GroundY(Vector3 at) =>
        Terrain(at + Vector3.up * 1f, Vector3.down, 4f, out var hit) ? hit.point.y : float.NaN;

    void Drift(
        string phase,
        List<string> fails,
        List<string> notes,
        float maxView,
        float body = float.NaN
    )
    {
        if (float.IsNaN(body))
            body = FlatSize(leanRoot);
        float view = leanOrigin.size.magnitude;
        notes.Add(
            F(
                $"{phase}: body {body:0.000} m (height {leanRoot.size.y:0.000} m), view {view:0.000} m, hip {leanHip.size.magnitude:0.000} m peak to peak"
            )
        );
        if (body > MaxLeanDrift)
            fails.Add(F($"{phase}: body moved {body:0.000} m > {MaxLeanDrift:0.00} m"));
        if (view > maxView)
            fails.Add(F($"{phase}: view moved {view:0.000} m > {maxView:0.000} m"));
    }
}
