using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Inventory;
using PeakXR.Locomotion;
using PeakXR.Rig;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// The body stays under the head (#10, #50, #51, #52): one debt measured from the view, one walk, the walk kept out of
/// the view. Each step faces open ground and recenters, scripts the head on the calibrated spot, and judges the
/// avatar's eyes against the view once the stance walk has settled, the view (the rig origin, or the eye for a turn)
/// against the head's own motion, and what peers see.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Sets up a stance step: the arms tracked, open ground, a recenter. Null with the step failed or
    /// skipped (room-scale walking off: the body does not walk by itself then).</summary>
    IEnumerator StanceSetup(Action<Character> ready)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || rig == null)
        {
            Fail("no pose driver or rig");
            yield break;
        }
        if (LocomotionConfig.RoomScaleSetting == null || !LocomotionConfig.RoomScale)
        {
            stepSkip =
                "RoomScaleWalking off or player.locomotion missing: the body does not walk by itself";
            yield break;
        }
        yield return WaitFor(() => driver.ArmWeight >= 1f, ArmTrackedWait);
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no clear 1.3 m to step in in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.8f);
        yield return RecenterHead(rig);
        bodyRestore = ReleaseHead;
        ready(c);
    }

    /// <summary>Most the eye renderers may sit off the eye model (m, level). Run 10 had them 0.02-0.045 m behind it
    /// standing still (lean-view renderers-model, t681 and t832-835); 0.015 m more for the head's idle sway.</summary>
    const float MaxEyeModelGap = 0.06f;

    /// <summary>
    /// The eye model's (<see cref="RigDriver.LeanEye"/>, what the stance debt measures from) level offset from the
    /// view, NaN without a view or model; <paramref name="gap"/> is the eye renderers' level distance from the model
    /// (NaN without them). The stance steps judge the walk by the model and the renderers apart: the model is learned
    /// once and kept under lean (re-learning it moves the view, #10), so the renderers' gap is the model's error, not
    /// the walk's.
    /// </summary>
    static Vector3 ModelEyesOff(Character c, out float gap)
    {
        gap = float.NaN;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (head == null || RigDriver.LeanEye is not { } model)
            return new Vector3(float.NaN, 0f, float.NaN);
        if (RigDriver.TryEyeRenderers(c, out var drawn))
            gap = Vector3.ProjectOnPlane(drawn - model, Vector3.up).magnitude;
        return Vector3.ProjectOnPlane(head.position - model, Vector3.up);
    }

    static void JudgeEyes(Character c, string at, List<string> fails, List<string> notes)
    {
        float off = ModelEyesOff(c, out float gap).magnitude;
        notes.Add(
            F(
                $"{at}: eyes {off:0.000} m off the view, drawn eyes {DrawnEyesOff(c):0.000} m, renderers {gap:0.000} m off the eye model, debt {RigDriver.StanceDebt.magnitude:0.000} m, turn skip {RigDriver.TurnSkipNow.magnitude:0.000} m"
            )
        );
        JudgeSettled(c, at, off, fails);
    }

    /// <summary>Most a settled stance walk may leave the unbent eyes off the view (m, level): what it leaves to the
    /// lean (<see cref="StanceWalk.Again"/>; run 11: pushes on less sprang back whole), and 1 cm for idle sway.</summary>
    static readonly float MaxStanceLeftover = new StanceWalk().Again + 0.01f;

    /// <summary>Most the drawn eyes may sit off the view once a walk has settled (m, level): the lean bends for what
    /// the walk left less what <see cref="TurnSkip"/> keeps out of it, and the eye model's own error
    /// (<see cref="MaxEyeModelGap"/>) stays on top.</summary>
    const float MaxDrawnEyeOff = MaxEyeModelGap;

    /// <summary>The eye renderers' level distance from the view (m), NaN without them. Without the view hold's deliberate
    /// offset, which the lean leaves out and the player cannot see with the head hidden, as vr.08 judges it (run 20,
    /// vr.395: the hold's 0.05 m plus 0.021 m of the eye model's own read 0.070 m).</summary>
    static float DrawnEyesOff(Character c)
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        return head != null && RigDriver.TryEyeRenderers(c, out var drawn)
            ? Vector3
                .ProjectOnPlane(drawn - (head.position - RigDriver.ViewHeld), Vector3.up)
                .magnitude
            : float.NaN;
    }

    /// <summary>The scripted hands' poses, for <see cref="PlaceHands"/>.</summary>
    (Vector3 Left, Quaternion LeftTurn, Vector3 Right, Quaternion RightTurn) HandsNow() =>
        (scriptLeft.Position, scriptLeft.Rotation, scriptRight.Position, scriptRight.Rotation);

    /// <summary>Carries the scripted hands with a walking or turning player, from their poses in <paramref name="from"/>:
    /// turned about <paramref name="pivot"/>, then moved. Left behind in the room they pull the mass centre the eye model
    /// rides, which a player's never do this way: 0.10 m off after vr.396's 1.2 m walk, 0.07 m in vr.45b (run 20).</summary>
    void PlaceHands(
        in (Vector3 Left, Quaternion LeftTurn, Vector3 Right, Quaternion RightTurn) from,
        Vector3 pivot,
        Quaternion turn,
        Vector3 move
    )
    {
        scriptLeft.Position = pivot + turn * (from.Left - pivot) + move;
        scriptLeft.Rotation = turn * from.LeftTurn;
        scriptRight.Position = pivot + turn * (from.Right - pivot) + move;
        scriptRight.Rotation = turn * from.RightTurn;
    }

    /// <summary>A settled stance: the walk left at most <see cref="MaxStanceLeftover"/> (<paramref name="off"/>, the
    /// unbent eye model), and the lean brought the drawn eyes within <see cref="MaxDrawnEyeOff"/> of the view.</summary>
    static void JudgeSettled(Character c, string at, float off, List<string> fails)
    {
        if (stanceTimedOut)
        {
            fails.Add(F($"{at}: the walk was still running at the judge"));
            return;
        }
        if (!(off <= MaxStanceLeftover))
            fails.Add(
                F(
                    $"{at}: the walk left the avatar's eyes {off:0.000} m off the view, want {MaxStanceLeftover:0.00} m (the lean takes the rest)"
                )
            );
        float drawn = DrawnEyesOff(c);
        if (!(drawn <= MaxDrawnEyeOff))
            fails.Add(
                F(
                    $"{at}: the avatar's drawn eyes {drawn:0.000} m off the view, want {MaxDrawnEyeOff:0.00} m"
                )
            );
    }

    /// <summary>The last <see cref="Settle"/>'s peak bend. A judge frame misses it: VR2's vr.45b passed on a 15.9 degree
    /// peak (#123). The look has stopped by then, so a bend past <see cref="MaxStanceLean"/> is under a still head.</summary>
    static void JudgePeakBend(string at, List<string> fails, List<string> notes)
    {
        notes.Add(F($"{at}: peak bend {settlePeakBend:0.0} deg"));
        if (settlePeakBend > MaxStanceLean)
            fails.Add(
                F(
                    $"{at}: the body bent {settlePeakBend:0.0} deg under a still head after the look stopped"
                )
            );
    }

    void Verdict(List<string> fails, List<string> notes)
    {
        var note = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// <c>vr.300-snap-turn-step</c>: two 45 degree snap turns and a smooth 90 degree turn (VRRig.RotateYaw, as the
    /// turn stick does) with the head still. A turn pivots on the eye and the body turns about its mass centre, so
    /// before #52 the eyes stayed 0.33 to 0.35 m off. Once each turn has settled the walk may leave at most
    /// <see cref="MaxStanceLeftover"/> and the drawn eyes must be within <see cref="MaxDrawnEyeOff"/> of the view, and over the step the eye (the scripted head never moves in the
    /// room) may not move more than <see cref="MaxLeanDrift"/> level.
    /// </summary>
    IEnumerator SnapTurnStepStep()
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        ScriptHead(home, Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward)));
        yield return null;
        var fails = new List<string>();
        var notes = new List<string>();
        var eye = new Bounds(Vector3.ProjectOnPlane(rig.Head.position, Vector3.up), Vector3.zero);
        foreach (
            var (name, smooth) in new[]
            {
                ("snap 45", false),
                ("snap 90", false),
                ("smooth 180", true),
            }
        )
        {
            if (smooth)
            {
                float t0 = Now;
                while (Now - t0 < 1f)
                {
                    rig.RotateYaw(90f * Time.unscaledDeltaTime);
                    yield return null;
                    eye.Encapsulate(Vector3.ProjectOnPlane(rig.Head.position, Vector3.up));
                }
            }
            else
            {
                RigDriver.TraceEveryStepUntil = Time.unscaledTime + 0.5f;
                rig.RotateYaw(45f);
            }
            var origin = new Bounds(rig.Origin.position, Vector3.zero);
            float t1 = Now;
            yield return Settle(rig, origin, _ => { });
            // Settle tracks the origin; the eye is what a turn must hold, so sample it over the same time.
            eye.Encapsulate(Vector3.ProjectOnPlane(rig.Head.position, Vector3.up));
            JudgeEyes(c, name, fails, notes);
            JudgePeakBend(name, fails, notes);
            notes.Add(F($"{name} settled in {Now - t1:0.0} s"));
        }
        ReleaseHead();
        float drift = new Vector2(eye.size.x, eye.size.z).magnitude;
        notes.Add(F($"eye moved {drift:0.000} m"));
        if (drift > MaxLeanDrift)
            fails.Add(
                F($"the eye moved {drift:0.000} m over the turns, want {MaxLeanDrift:0.00} m")
            );
        Verdict(fails, notes);
    }

    /// <summary>
    /// <c>vr.301-room-step</c>: the scripted head steps 0.25 m forward in the room (inside the lean limit, so the body
    /// leans first and steps under after the delay), then 0.6 m to the right (past it: the body walks at once), then
    /// back to the start. Before #10's rework a room walk left the body up to 0.6 m off and 0.19 m after. After each
    /// the walk may leave at most <see cref="MaxStanceLeftover"/> and the drawn eyes must be within
    /// <see cref="MaxDrawnEyeOff"/> of the view, and over the whole step the rig
    /// origin may not move more than <see cref="MaxLeanDrift"/> level: only the head moves the view.
    /// </summary>
    IEnumerator RoomStepStep()
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var forward = level * Vector3.forward;
        var right = level * Vector3.right;
        var fails = new List<string>();
        var notes = new List<string>();
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        var at = Vector3.zero;
        var hands = HandsNow();
        foreach (
            var (name, to) in new[]
            {
                ("0.25 m forward", forward * 0.25f),
                ("0.6 m right", forward * 0.25f + right * 0.6f),
                ("back", Vector3.zero),
            }
        )
        {
            var from = at;
            float t0 = Now;
            while (Now - t0 < 0.8f)
            {
                at = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, (Now - t0) / 0.8f));
                ScriptHead(home + at, level);
                PlaceHands(hands, home, Quaternion.identity, at);
                yield return null;
                origin.Encapsulate(rig.Origin.position);
            }
            at = to;
            ScriptHead(home + at, level);
            PlaceHands(hands, home, Quaternion.identity, at);
            yield return Settle(rig, origin, b => origin = b);
            JudgeEyes(c, name, fails, notes);
        }
        ReleaseHead();
        float drift = FlatSize(origin);
        notes.Add("view moved " + ViewMoved(origin));
        if (drift > MaxLeanDrift)
            fails.Add(
                F(
                    $"the view moved {drift:0.000} m while only the head stepped, want {MaxLeanDrift:0.00} m"
                )
            );
        Verdict(fails, notes);
    }

    /// <summary>
    /// <c>vr.302-glance-still</c>: the scripted head looks 70 degrees aside and back within a second, then holds still
    /// 2 s. A glance returns before the stance walk's delay, so the body must not step (peers saw it shuffle, #50):
    /// no stance walk may start, the mass centre may move at most 0.05 m level, and the rig origin, which only the head
    /// may move, at most <see cref="MaxLeanDrift"/> level, also over 3 s more, once
    /// <see cref="PeakXR.Core.BodyHold.MaxAfterLean"/> has let the body go. What the body does in those 3 s (a late slide,
    /// a walk) is only a note: #50's open decision, accept it or step the feet instead of holding them. The note also
    /// names how far the head's forward stays off the look at the glance's peak (<see cref="BodyTurn.HeadOnNeck"/>).
    /// </summary>
    IEnumerator GlanceStillStep()
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var forward = level * Vector3.forward;
        var root = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        var lastOrigin = rig.Origin.position;
        float viewStep = 0f;
        void ReadView()
        {
            var now = rig.Origin.position;
            origin.Encapsulate(now);
            viewStep = Mathf.Max(
                viewStep,
                Vector3.ProjectOnPlane(now - lastOrigin, Vector3.up).magnitude
            );
            lastOrigin = now;
        }
        bool walked = false;
        float headOff = 0f;
        float t0 = Now;
        RigDriver.TraceEveryStepUntil = Time.unscaledTime + 3f;
        while (Now - t0 < 3f)
        {
            ScriptGlance(home, level, forward, Now - t0);
            yield return null;
            walked |= RigDriver.FollowWalking;
            root.Encapsulate(RigDriver.ViewRoot(c));
            ReadView();
            if (Now - t0 is > 0.5f and < 0.65f)
                headOff = Mathf.Max(
                    headOff,
                    Vector3.Angle(Flat(c.refs.head.transform.forward), c.data.lookDirection_Flat)
                );
        }
        float moved = FlatSize(root);
        var after = new Bounds(RigDriver.ViewRoot(c), Vector3.zero);
        bool walkedLate = false;
        t0 = Now;
        while (Now - t0 < 3f)
        {
            yield return null;
            walkedLate |= RigDriver.FollowWalking;
            after.Encapsulate(RigDriver.ViewRoot(c));
            ReadView();
        }
        ReleaseHead();
        float view = FlatSize(origin);
        var note = F(
            $"glance: stance walk {(walked ? "started" : "never started")}, body moved {moved:0.000} m; 3 s later the body moved {FlatSize(after):0.000} m more, {(walkedLate ? "with a walk" : "no walk")}; view moved {ViewMoved(origin)} over all 6 s, largest step {viewStep:0.000} m; head forward {headOff:0} deg off the look at the peak"
        );
        if (walked || moved > 0.05f || view > MaxLeanDrift)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>The scripted head of a glance <paramref name="t"/> seconds in: out over 0.35 s, held
    /// <paramref name="held"/> s, back over 0.35 s, at 70 degrees; the eyes swing on a 0.1 m neck as in vr.45b.</summary>
    void ScriptGlance(Vector3 home, Quaternion level, Vector3 forward, float t, float held = 0.3f)
    {
        float k =
            t < 0.35f ? t / 0.35f
            : t < 0.35f + held ? 1f
            : t < 0.7f + held ? 1f - (t - 0.35f - held) / 0.35f
            : 0f;
        var q = Quaternion.Euler(0f, 70f * Mathf.SmoothStep(0f, 1f, k), 0f);
        ScriptHead(home + (q * forward - forward) * NeckSwing, q * level);
    }

    /// <summary>
    /// <c>vr.303-belt-look-down</c>: the belt slots sit on the body the player sees (#51). The scripted head looks
    /// level, then 60 degrees down, where the body stands back about 0.28 m (vr.273). The belt's level place along
    /// the look, from the hip, may change by at most 0.1 m between the two: before, the slots stayed at the real body
    /// and floated 0.28 m ahead of the avatar's hips.
    /// </summary>
    /// <summary>Impulse (per part, inside a 1 m radius) sent every physics step, as wind sends it (WindChillZone.cs:250), for
    /// <see cref="StancePushSeconds"/>: run 10's single impulse of 20 moved it 0.004 m against its planted feet. A
    /// steady push slides a standing scout without knocking it down. 10 at 50 Hz is about the 6 a frame at 90 Hz it
    /// was sent at before.</summary>
    const float StancePush = 10f;

    /// <summary>How long the push lasts (s). Run 12 stopped it once the mass centre had moved 0.08 m, at 0.10 s: the
    /// upper body rocks first, and the body came back to 0.033 m, too little to judge. Run 11's 0.11 s moved it
    /// 0.106 m.</summary>
    const float StancePushSeconds = 0.3f;

    /// <summary>
    /// <c>vr.304-push-moves-view</c>: a push on a standing body (the game's knockback RPC,
    /// <c>Character.RPCA_AddForceAtPosition</c>, Character.cs:458, as vr.07 sends it) is vanilla motion. The view must
    /// go with the body (its level travel within 0.05 m of the body's after 2 s) and no stance walk may start in those
    /// 2 s, so a push is never walked back and peers never see the scout resist it.
    /// </summary>
    IEnumerator PushMovesViewStep()
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        ScriptHead(home, Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward)));
        yield return Wait(0.5f);
        var right = Vector3.Cross(Vector3.up, Flat(rig.Head.forward));
        var body0 = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c), Vector3.up);
        var origin0 = Vector3.ProjectOnPlane(rig.Origin.position, Vector3.up);
        bool walked = false;
        float t0 = Now;
        float pushed = 0f;
        var tick = new WaitForFixedUpdate();
        while (Now - t0 < 2f)
        {
            if (Now - t0 < StancePushSeconds)
            {
                c.photonView.RPC(
                    "RPCA_AddForceAtPosition",
                    Photon.Pun.RpcTarget.All,
                    right * StancePush,
                    c.Center,
                    1f
                );
                pushed = Now - t0;
            }
            // Once a physics step, as wind pushes (WindChillZone.FixedUpdate, WindChillZone.cs:164, :250).
            yield return tick;
            walked |= RigDriver.FollowWalking;
        }
        ReleaseHead();
        var body = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c), Vector3.up) - body0;
        var view = Vector3.ProjectOnPlane(rig.Origin.position, Vector3.up) - origin0;
        float apart = (view - body).magnitude;
        var note = F(
            $"push: {pushed:0.00} s, body {body.magnitude:0.000} m, view {view.magnitude:0.000} m ({apart:0.000} m apart), stance walk {(walked ? "started" : "never started")}{(c.data.fallSeconds > 0f ? ", knocked down" : "")}"
        );
        if (body.magnitude < 0.05f)
            Fail(note + "; the push did not move the body enough to judge");
        else if (walked || apart > 0.05f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>Wind's reach, as <c>WindChillZone.forceRadius</c> (WindChillZone.cs:39).</summary>
    const float WindRadius = 2f;

    /// <summary>The storm's full force per part (<c>WindChillZone.windForce</c>, WindChillZone.cs:52), and a weak one: the
    /// range a hold capped at 2 m/s^2 could matter over.</summary>
    const float WindFull = 15f;
    const float WindWeak = 1f;

    /// <summary>How long a pass blows (s), and its ramp up, as <c>currentForceMult</c> (WindChillZone.cs:167).</summary>
    const float WindSeconds = 3f;
    const float WindRampSeconds = 1f;

    /// <summary>Seconds after the last force before a pass reads its travel, so the view's lag has closed.</summary>
    const float WindTailSeconds = 1f;

    /// <summary>Seconds after the first force <see cref="BodyHold"/> may still read an open gate: the marker reaches the
    /// gate on the next rendered frame, and then the next physics step, at 25 fps up to 0.06 s.</summary>
    const float WindGateSeconds = 0.15f;

    /// <summary>What one <see cref="WindPass"/> read.</summary>
    sealed class WindRun
    {
        public float Body;
        public float Apart;
        public float View;
        public int Held;
        public bool Marked;
        public bool Walked;
        public bool Down;
    }

    /// <summary>
    /// <c>vr.305-wind-through-glance</c>: wind moves body and view together at any strength, with the head still or
    /// looking around (#139). Wind-like impulses (<c>Character.RPCA_AddForceAtPosition</c>, Character.cs:458, every physics
    /// step as WindChillZone.cs:250 sends them, so the outside marker sees them) blow for 3 s, at a weak level and at the
    /// storm's full one, once with the head still and once with vr.302's glance repeating, from the same spot. The body's
    /// travel with the glance must be within max(0.05 m, 15%) of its travel with the head still; after the first force
    /// <see cref="BodyHold"/> must not hold; body and view must end within 0.05 m; no stance walk may start. A run knocked
    /// down rides a ragdoll, so its travel and view aren't compared. A last pass sends zero force through the same RPC with
    /// the glance, as a storm does in shelter (#140): that is no outside force, so the rig origin stays within
    /// <see cref="MaxLeanDrift"/> level.
    /// </summary>
    IEnumerator WindThroughGlanceStep()
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var start = c.Center;
        var along = Flat(VRRig.Instance.Head.forward);
        bodyRestore += () =>
        {
            if (c != null)
                c.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, start, false);
        };
        var fails = new List<string>();
        var notes = new List<string>();
        foreach (var (level, impulse) in new[] { ("weak", WindWeak), ("full", WindFull) })
        {
            WindRun still = null;
            WindRun glancing = null;
            yield return WindPass(c, start, along * impulse, false, r => still = r);
            yield return WindPass(c, start, along * impulse, true, r => glancing = r);
            JudgeWind(level, still, glancing, fails, notes);
        }
        WindRun zero = null;
        yield return WindPass(c, start, Vector3.zero, true, r => zero = r);
        notes.Add(F($"zero force: view moved {zero.View:0.000} m, body {zero.Body:0.000} m"));
        if (zero.Marked)
            fails.Add("zero force: marked as an outside force");
        if (zero.View > MaxLeanDrift)
            fails.Add(
                F(
                    $"zero force: the view moved {zero.View:0.000} m through the glance, want {MaxLeanDrift:0.00} m"
                )
            );
        Verdict(fails, notes);
    }

    /// <summary>One <see cref="WindThroughGlanceStep"/> pass from <paramref name="start"/>: placed there and recentered,
    /// <paramref name="wind"/> ramps up and blows, the head still or glancing, then the tail.</summary>
    IEnumerator WindPass(
        Character c,
        Vector3 start,
        Vector3 wind,
        bool glance,
        Action<WindRun> done
    )
    {
        var run = new WindRun();
        done(run);
        var rig = VRRig.Instance;
        var driver = AvatarPoseDriver.Of(c);
        c.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, start, false);
        yield return Wait(0.5f);
        yield return WaitFor(
            () =>
                !c.warping
                && c.data.fullyConscious
                && c.data.isGrounded
                && !LocomotionInput.OutsideNow,
            ViewHoldGate.OutsideSeconds + 3f
        );
        if (LocomotionInput.OutsideNow)
            Fail("the last pass's outside force never ended");
        yield return RecenterHead(rig);
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var forward = level * Vector3.forward;
        ScriptHead(home, level);
        yield return Wait(0.5f);
        var body0 = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c), Vector3.up);
        var origin0 = Vector3.ProjectOnPlane(rig.Origin.position, Vector3.up);
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        var tick = new WaitForFixedUpdate();
        float t0 = Now;
        float gateBy = float.PositiveInfinity;
        while (Now - t0 < WindSeconds + WindTailSeconds)
        {
            float t = Now - t0;
            if (t < WindSeconds)
            {
                var force = wind * Mathf.Clamp01(t / WindRampSeconds);
                c.photonView.RPC(
                    "RPCA_AddForceAtPosition",
                    Photon.Pun.RpcTarget.All,
                    force,
                    c.Center,
                    WindRadius
                );
                if (float.IsPositiveInfinity(gateBy) && force.sqrMagnitude > 1e-6f)
                    gateBy = t + WindGateSeconds;
                if (glance)
                    ScriptGlance(home, level, forward, t % 1f);
            }
            else if (glance)
                ScriptGlance(home, level, forward, 1f);
            // Once a physics step, as wind pushes (WindChillZone.FixedUpdate, WindChillZone.cs:164, :250).
            yield return tick;
            run.Marked |= LocomotionInput.OutsideNow;
            run.Walked |= RigDriver.FollowWalking;
            run.Down |= c.data.fallSeconds > 0f;
            if (t >= gateBy && driver != null && driver.BodyHeld)
                run.Held++;
            origin.Encapsulate(rig.Origin.position);
        }
        var body = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c), Vector3.up) - body0;
        var view = Vector3.ProjectOnPlane(rig.Origin.position, Vector3.up) - origin0;
        run.Body = body.magnitude;
        run.Apart = (view - body).magnitude;
        run.View = FlatSize(origin);
    }

    /// <summary>Adds one wind level's verdict: each run's, and the glance's travel against the still head's.</summary>
    static void JudgeWind(
        string level,
        WindRun still,
        WindRun glancing,
        List<string> fails,
        List<string> notes
    )
    {
        foreach (var (name, run) in new[] { ("still", still), ("glance", glancing) })
        {
            string what = level + " " + name;
            notes.Add(
                F(
                    $"{what}: body {run.Body:0.000} m ({run.Apart:0.000} m from the view), hold on {run.Held} steps{(run.Down ? ", knocked down" : "")}"
                )
            );
            if (!run.Marked)
                fails.Add(what + ": the outside marker never saw the force");
            if (run.Held > 0)
                fails.Add(F($"{what}: the hold held {run.Held} steps after the first force"));
            if (run.Walked)
                fails.Add(what + ": a stance walk started");
            if (!run.Down && run.Apart > 0.05f)
                fails.Add(F($"{what}: body and view {run.Apart:0.000} m apart, want 0.05 m"));
        }
        if (still.Down || glancing.Down)
        {
            notes.Add(level + ": knocked down, travel not compared");
            return;
        }
        float most = Mathf.Max(0.05f, 0.15f * still.Body);
        if (Mathf.Abs(glancing.Body - still.Body) > most)
            fails.Add(
                F(
                    $"{level}: the glance moved the body {glancing.Body:0.000} m, the still head {still.Body:0.000} m, want within {most:0.000} m"
                )
            );
        if (level == "full" && still.Body < 0.05f)
            fails.Add(F($"full: moved the body {still.Body:0.000} m, too little to judge"));
    }

    /// <summary>Most the drawn eyes may lead the view while walking forward (m, along the walk): the view trails a
    /// stick walk by at most the 0.1 m <c>Camera.BodySmoothing</c> lag, and room-scale's walk may coast a little past.</summary>
    const float MaxWalkLead = 0.15f;

    /// <summary>Most the spine may bend while walking forward (degrees): a stick walk has no lean (its gate); walking in the
    /// room the lean bends toward the head until the stance walk steps under it, by at most
    /// <see cref="BodyStance.WalkLeanLimit"/> meanwhile from the first stride (about 6 degrees).</summary>
    static float MaxWalkBend(bool room) => room ? 18f : 5f;

    /// <summary>The animated legs' speed against the ground's (<see cref="WalkForwardStep"/>): 1 when the stance foot
    /// stays put. Outside this the legs visibly run ahead of, or drag behind, the body. The floor sits under the game's
    /// own full-stick walk: <c>vr.390-walk-legs-flat</c> read 0.65 with VR off (run 21, one sample, all at input 0.5 and
    /// over), and vr.395 read 0.55 and 0.71 across runs 20 and 21, with PeakXR's only part in that gait
    /// <c>input.movementInput</c>, what the animator (CharacterAnimations.cs:186-187) and the force
    /// (Bodypart.cs:291-298) read in vanilla. So the game's own value moves around that range (#97).</summary>
    const float MinLegRatio = 0.5f;

    const float MaxLegRatio = 1.6f;

    static AccessTools.FieldRef<Bodypart, Vector3> animOffset;

    /// <summary>The animated legs' pace against the ground's over physics steps, all of it and under 0.5 move input
    /// (#97: the stance walk pushes mostly at 0.27-0.46): the stance foot (the lower animated foot,
    /// <c>Bodypart.targetOffsetRelativeToHip</c>, Bodypart.cs:39, :130) moves back along the hip at the legs' speed, so
    /// its world travel is the slip. The animation runs on the physics step (CharacterRagdoll.cs:155-174), so the frame
    /// rate does not change the reading.</summary>
    sealed class LegMeter
    {
        readonly Character c;
        readonly Bodypart left;
        readonly Bodypart right;
        readonly Vector3 along;
        float legs;
        float legsLow;
        float groundLow;
        int stance = -1;
        Vector3 lastFoot;
        Vector3 lastHip;

        LegMeter(Character c, Bodypart left, Bodypart right, Vector3 along)
        {
            this.c = c;
            this.left = left;
            this.right = right;
            this.along = along;
        }

        /// <summary>Metres the body walked at pace.</summary>
        public float Ground { get; private set; }

        /// <summary>The legs' pace over the ground's, NaN under 0.05 m of walking.</summary>
        public float Ratio => Ground > 0.05f ? legs / Ground : float.NaN;

        /// <summary>A meter for walking along <paramref name="along"/>, null without feet.</summary>
        public static LegMeter For(Character c, Vector3 along)
        {
            animOffset ??= GameMember.Field<Bodypart, Vector3>("targetOffsetRelativeToHip");
            var parts = c.refs.ragdoll.partDict;
            return
                parts.TryGetValue(BodypartType.Foot_L, out var l)
                && parts.TryGetValue(BodypartType.Foot_R, out var r)
                ? new LegMeter(c, l, r, along)
                : null;
        }

        /// <summary>Once per physics step.</summary>
        public void Tick()
        {
            // The stance foot's animated place in the world: the hip plus its animated offset (Bodypart.WorldTargetPos).
            var hip = c.refs.hip.transform.position;
            var l = hip + animOffset(left);
            var r = hip + animOffset(right);
            int now = l.y <= r.y ? 0 : 1;
            var foot = now == 0 ? l : r;
            float hipStep = Vector3.Dot(hip - lastHip, along);
            // Walking pace only: the start and the stop are the gait blending in and out.
            if (now == stance && hipStep > 0.3f * Time.fixedDeltaTime)
            {
                float slip = hipStep - Vector3.Dot(foot - lastFoot, along);
                legs += slip;
                Ground += hipStep;
                if (c.input.movementInput.magnitude < 0.5f)
                {
                    legsLow += slip;
                    groundLow += hipStep;
                }
            }
            stance = now;
            lastFoot = foot;
            lastHip = hip;
        }

        /// <summary>The ratio, whole and split at 0.5 move input.</summary>
        public string Note()
        {
            float groundHigh = Ground - groundLow;
            return F(
                $"legs {Ratio:0.00} x the ground speed over {Ground:0.00} m (input under 0.5: {(groundLow > 0.05f ? legsLow / groundLow : float.NaN):0.00} over {groundLow:0.00} m; 0.5 and over: {(groundHigh > 0.05f ? (legs - legsLow) / groundHigh : float.NaN):0.00} over {groundHigh:0.00} m)"
            );
        }
    }

    /// <summary>
    /// <c>vr.390-walk-legs-flat</c>, flat runs only: the same reading as vr.395 with VR off, the move input held full
    /// forward by the input patch (<see cref="SelfTestInputPatch.WalkUntil"/>) and nothing of PeakXR's in the gait, so
    /// the note is the game's own baseline for <see cref="MinLegRatio"/> (#97). Fails outside the same limits; a walk
    /// that something blocks is skipped.
    /// </summary>
    IEnumerator WalkLegsFlatStep()
    {
        var c = Character.localCharacter;
        var meter = c != null ? LegMeter.For(c, c.data.lookDirection_Flat.normalized) : null;
        if (meter == null)
        {
            Fail("no character or feet");
            yield break;
        }
        var tick = new WaitForFixedUpdate();
        float until = Now + 1.2f;
        SelfTestInputPatch.WalkUntil = until;
        while (Now < until)
        {
            yield return tick;
            meter.Tick();
        }
        var note = "flat walk: " + meter.Note();
        if (meter.Ground < 0.5f)
            stepSkip = "the walk was blocked: " + note;
        else if (meter.Ratio < MinLegRatio || meter.Ratio > MaxLegRatio)
            Fail(note);
        else
            stepNote = note;
    }

    bool walkSampling;
    float walkLead;
    float walkModelLead;

    /// <summary>The leads of <see cref="WalkForwardStep"/>, at the end of each rendered frame, where the view, the eye
    /// model and the eye renderers are the same frame's. Read after a physics step, the renderers were up to a frame
    /// and a step ahead of the view (run 12, vr.395: 0.216 m against the model's 0.085 m at 2.5 m/s).</summary>
    IEnumerator SampleWalkLead(Character c, VRRig rig, Vector3 along)
    {
        var end = new WaitForEndOfFrame();
        float t0 = Now;
        while (walkSampling && Now - t0 < 10f && c != null)
        {
            yield return end;
            var view = rig.Head.position;
            if (RigDriver.TryEyeRenderers(c, out var drawn))
                walkLead = Mathf.Max(walkLead, Vector3.Dot(drawn - view, along));
            if (RigDriver.LeanEye is { } model)
                walkModelLead = Mathf.Max(walkModelLead, Vector3.Dot(model - view, along));
        }
    }

    /// <summary>
    /// <c>vr.395-walk-forward-stick</c> and <c>vr.396-walk-forward-room</c>: walking forward, the body stays with the
    /// head and the legs keep pace with it (a tester saw the scout bend and its legs outrun the body). Stick: the move
    /// stick held forward 1.2 s. Room: the scripted head walks 1.2 m forward in the room over 2 s, then holds until the
    /// stance walk has settled. Every rendered frame it measures how far the drawn eyes (and the eye model) lead the
    /// view along the walk (<see cref="SampleWalkLead"/>), and every physics step the spine's bend and the animated
    /// legs' speed against the ground's (<see cref="LegMeter"/>). The hands walk with the head. Fails when the eyes lead by more than
    /// <see cref="MaxWalkLead"/>, the spine bends more than <see cref="MaxWalkBend"/>, or the legs' ratio leaves
    /// <see cref="MinLegRatio"/>..<see cref="MaxLegRatio"/>; the room walk then judges the settled stance as vr.301.
    /// </summary>
    IEnumerator WalkForwardStep(bool room)
    {
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        var driver = AvatarPoseDriver.Of(c);
        if (driver == null || LegMeter.For(c, Vector3.zero) == null)
        {
            ReleaseHead();
            Fail("no pose driver or feet");
            yield break;
        }
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var forward = level * Vector3.forward;
        ScriptHead(home, level);
        yield return Wait(0.3f);
        var along = Flat(rig.Origin.rotation * forward).normalized;
        bool hand = !VRInput.LeftHanded;
        walkLead = walkModelLead = float.NegativeInfinity;
        walkSampling = true;
        StartCoroutine(SampleWalkLead(c, rig, along));
        float bend = 0f;
        var meter = LegMeter.For(c, along);
        var hands = HandsNow();
        var tick = new WaitForFixedUpdate();
        float seconds = room ? 2f : 1.2f;
        float t0 = Now;
        if (!room)
            Script(hand).Stick = new Vector2(0f, 1f);
        while (Now - t0 < seconds)
        {
            if (room)
            {
                var walked = forward * (1.2f * Mathf.SmoothStep(0f, 1f, (Now - t0) / seconds));
                ScriptHead(home + walked, level);
                PlaceHands(hands, home, Quaternion.identity, walked);
            }
            yield return tick;
            bend = Mathf.Max(bend, driver.LeanDegrees);
            meter.Tick();
        }
        Script(hand).Stick = Vector2.zero;
        walkSampling = false;
        float lead = walkLead;
        float modelLead = walkModelLead;
        var fails = new List<string>();
        var notes = new List<string>();
        float ratio = meter.Ratio;
        notes.Add(
            F(
                $"{(room ? "room" : "stick")} walk: drawn eyes led the view by {lead:0.000} m at most (eye model {modelLead:0.000} m), spine bent {bend:0.0} deg at most, {meter.Note()}"
            )
        );
        if (!(lead <= MaxWalkLead))
            fails.Add(
                F(
                    $"the drawn eyes led the view by {lead:0.000} m walking, want {MaxWalkLead:0.00} m"
                )
            );
        if (bend > MaxWalkBend(room))
            fails.Add(F($"the spine bent {bend:0.0} deg walking, want {MaxWalkBend(room):0} deg"));
        if (float.IsNaN(ratio))
            fails.Add(F($"the body walked only {meter.Ground:0.000} m at pace: nothing to judge"));
        else if (ratio < MinLegRatio || ratio > MaxLegRatio)
            fails.Add(
                F(
                    $"the legs moved {ratio:0.00} x the ground speed, want {MinLegRatio:0.0}..{MaxLegRatio:0.0}"
                )
            );
        // The stick walk too: a sample of the walking eyes kept as the eye model left the standing body's drawn eyes
        // 0.16 m off the view (run 17).
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        yield return Settle(rig, origin, b => origin = b);
        JudgeEyes(c, "after the walk", fails, notes);
        ReleaseHead();
        Verdict(fails, notes);
    }

    IEnumerator BeltLookDownStep()
    {
        var inv = BodyInventory.Instance;
        Character c = null;
        yield return StanceSetup(x => c = x);
        if (c == null)
            yield break;
        var rig = VRRig.Instance;
        if (!inv.Valid || c.refs.hip == null)
        {
            ReleaseHead();
            stepSkip = "body slots off";
            yield break;
        }
        var home = new Vector3(
            rig.RoomAnchor.X,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.RoomAnchor.Y
        );
        var level = Quaternion.LookRotation(Flat(VRInput.HeadRotation * Vector3.forward));
        var look = Flat(rig.Origin.rotation * (level * Vector3.forward));
        var origin = new Bounds(rig.Origin.position, Vector3.zero);
        var along = new List<float>();
        foreach (float pitch in new[] { 0f, 60f })
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
            }
            ScriptHead(home, to);
            yield return Settle(rig, origin, b => origin = b);
            if (stanceTimedOut)
            {
                ReleaseHead();
                Fail(F($"look {-pitch:0} deg: the walk was still running at the judge"));
                yield break;
            }
            var belt = Vector3.zero;
            int n = 0;
            for (int i = 0; i < inv.Count; i++)
            {
                // The hotbar's belt slots only: the backpack's and the shoulders' sit elsewhere.
                if (inv.Slot(i).GameSlot >= 3)
                    continue;
                belt += rig.Origin.TransformPoint(inv.Anchor(i));
                n++;
            }
            if (n == 0)
            {
                ReleaseHead();
                stepSkip = "no belt slots in the layout";
                yield break;
            }
            along.Add(Vector3.Dot(belt / n - c.refs.hip.Rig.position, look));
        }
        ReleaseHead();
        float shift = along[1] - along[0];
        var note = F(
            $"belt from the hip along the look: {along[0]:0.000} m level, {along[1]:0.000} m looking down (moved {shift:0.000} m)"
        );
        if (Mathf.Abs(shift) > 0.1f)
            Fail(note);
        else
            stepNote = note;
    }
}
