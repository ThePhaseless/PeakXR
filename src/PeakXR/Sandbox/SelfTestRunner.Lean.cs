using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.45-lean</c>: leaning bends the avatar and never moves the view. The body must first be grounded and at
/// rest for 1 s, else "not settled". A scripted head on the calibrated spot
/// stands, leans 0.3 m forward and 0.25 m sideways (inside <c>Locomotion.LeanLimit</c>) with both hands reaching out,
/// then makes a fast 0.15 s lean, and finally leans 0.5 m (past the limit) and stands back up. The body is the
/// view's root (<see cref="RigDriver.ViewRoot"/>, the ragdoll's mass centre; the hip itself slides back when the
/// upper body bends, which is only reported). Every frame, ramps and returns included, is tracked. Fails when: inside
/// the limit, the body (horizontally: its height follows the bend) or the rig origin (the view's offset from the scripted head) moves more than
/// <see cref="MaxLeanDrift"/> peak to peak; the fast lean moves the rig origin more than
/// <see cref="MaxFastViewDrift"/>; the far lean does not walk the body at least 0.1 m forward, moves the rig origin,
/// or the body moves at all (beyond <see cref="MaxLeanDrift"/>) while the head stands up and for 3.5 s after; or an arm fails the vr.08 judgement during a lean (hand more than
/// 0.15 m off its target, stretched, or an elbow pointing up or back). The hands (0.45 m ahead of and 0.3 m under the
/// head, about 0.53 m from a person's shoulders) are out of the scout's reach, so the target is the controller
/// mapped onto the avatar's reach (<see cref="ArmReach"/>); how far that holds each hand short is reported.
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
        // HandSetup settled the body first (Settle: run 29 started right after the sprint step with the body still
        // moving, "standing: body moved 0.551 m").
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        // Run 24 leaned into the crashed plane's seats: face open ground first, or say what is in the way.
        if (!ClearHeading(c, rig, out float turn, out string blocked))
        {
            Fail("no clear 1.3 m to lean into in any heading: " + blocked);
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
        Drift("standing", fails, notes);
        var standShoulders = Shoulders(c);
        var standHip = c.refs.hip.Rig.position;
        // Run 26 set the bend's absolute shoulder shift beside the ragdoll's change from standing; both from standing.
        var standAnimShift = driver.LeanShoulderShift;
        foreach (
            var (name, offset) in new[]
            {
                ("forward", forward * 0.3f + Vector3.down * 0.05f),
                ("sideways", right * 0.25f + Vector3.down * 0.03f),
            }
        )
        {
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
            notes.Add(
                F(
                    $"lean {name} stages: gate {driver.LeanGate ?? "open"}, weight {driver.LeanWeight:0.00}, bend {leanBend:0.0} deg, animated shoulders {leanAnimShoulders:0.000} m, ragdoll shoulders {Vector3.Dot(Shoulders(c) - standShoulders, worldDir):0.000} m, hip {Vector3.Dot(c.refs.hip.Rig.position - standHip, worldDir):0.000} m along the lean"
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
        var originStart = rig.Origin.position;
        float groundStart = GroundY(start);
        // forward is tracking space (the scripted head); the body moves in the world, under the rig's yaw. Run 24
        // projected the world walk on the tracking axis and read a forward walk as "-0.287 m".
        var worldForward = rig.Origin.TransformDirection(forward);
        yield return Track(c, rig, forward * 0.5f + Vector3.down * 0.06f, 0.6f, 2.5f);
        float walked = Vector3.Dot(RigDriver.ViewRoot(c) - start, worldForward);
        float viewLean = Vector3.Dot(rig.Origin.position - originStart, worldForward);
        var originUp = rig.Origin.position;
        // Standing up and the 3.5 s after it: the body stays put (no walk back, no shuffle), the view never moves.
        var upRoot = RigDriver.ViewRoot(c);
        leanRoot = new Bounds(upRoot, Vector3.zero);
        yield return Track(c, rig, Vector3.zero, 0.6f, 3.5f);
        // Travel is horizontal: the hip's height changes with the bend (the stand force holds the bent head lower).
        float back = FlatSize(leanRoot);
        // Run 31: the far lean's walk coasted onto the head, and standing up walked the body 0.44 m back.
        float walkedBack = -Vector3.Dot(RigDriver.ViewRoot(c) - upRoot, worldForward);
        // Run 40: x/z of the view held to the millimetre while it rose 0.042 m with the hip (+0.127 m): the walk
        // went up the beach's slope, and the view must follow the ground the body stands on. So: the view's
        // horizontal range, and its height change against the ground's under the body.
        float origin = FlatSize(leanOrigin);
        float groundRise = GroundY(RigDriver.ViewRoot(c)) - groundStart;
        float viewRise = rig.Origin.position.y - originStart.y;
        float offGround = Mathf.Abs(viewRise - groundRise);
        notes.Add(
            F(
                $"far lean: walked {walked:0.000} m, body {back:0.000} m while standing up and after (height {leanRoot.size.y:0.000} m), view {origin:0.000} m (forward {viewLean:0.000} m leaning, {Vector3.Dot(rig.Origin.position - originUp, worldForward):0.000} m standing up), view rose {viewRise:0.000} m on ground rising {groundRise:0.000} m"
            )
        );
        if (walked < 0.1f)
            fails.Add(F($"far lean walked only {walked:0.000} m while leaning past the limit"));
        if (walkedBack > MaxLeanDrift)
            fails.Add(
                F(
                    $"body walked {walkedBack:0.000} m back after standing up (a follow on standing up)"
                )
            );
        if (back > MaxLeanDrift)
            fails.Add(F($"body moved {back:0.000} m after standing up"));
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

    void StartTrack(Character c, VRRig rig)
    {
        leanRoot = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        leanOrigin = new Bounds(rig.Origin.position, Vector3.zero);
        leanHip = new Bounds(c.refs.hip.Rig.position, Vector3.zero);
    }

    /// <summary>Eases the scripted lean to <paramref name="to"/> over <paramref name="seconds"/>, holds it for
    /// <paramref name="hold"/> seconds, and tracks the body root, rig origin and hip every frame.</summary>
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
        Physics.Raycast(
            at + Vector3.up * 1f,
            Vector3.down,
            out var hit,
            4f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point.y
            : float.NaN;

    void Drift(string phase, List<string> fails, List<string> notes)
    {
        float body = FlatSize(leanRoot);
        float view = leanOrigin.size.magnitude;
        notes.Add(
            F(
                $"{phase}: body {body:0.000} m (height {leanRoot.size.y:0.000} m), view {view:0.000} m, hip {leanHip.size.magnitude:0.000} m peak to peak"
            )
        );
        if (body > MaxLeanDrift)
            fails.Add(F($"{phase}: body moved {body:0.000} m > {MaxLeanDrift:0.00} m"));
        if (view > MaxLeanDrift)
            fails.Add(F($"{phase}: view moved {view:0.000} m > {MaxLeanDrift:0.00} m"));
    }
}
