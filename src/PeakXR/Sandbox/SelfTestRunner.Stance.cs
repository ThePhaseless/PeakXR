using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
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

    static void JudgeGap(string at, float gap, List<string> fails)
    {
        if (!(gap <= MaxEyeModelGap))
            fails.Add(
                F(
                    $"{at}: the eye renderers {gap:0.000} m off the eye model, want {MaxEyeModelGap:0.00} m"
                )
            );
    }

    static void JudgeEyes(Character c, string at, List<string> fails, List<string> notes)
    {
        float off = ModelEyesOff(c, out float gap).magnitude;
        notes.Add(
            F(
                $"{at}: eyes {off:0.000} m off the view, renderers {gap:0.000} m off the eye model, debt {RigDriver.StanceDebt.magnitude:0.000} m"
            )
        );
        if (!(off <= MaxStanceEyeOff))
            fails.Add(
                F(
                    $"{at}: the avatar's eyes {off:0.000} m off the view, want {MaxStanceEyeOff:0.00} m"
                )
            );
        JudgeGap(at, gap, fails);
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
    /// before #52 the eyes stayed 0.33 to 0.35 m off. Once each turn has settled the avatar's eyes must be within
    /// <see cref="MaxStanceEyeOff"/> of the view, and over the step the eye (the scripted head never moves in the
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
                rig.RotateYaw(45f);
            var origin = new Bounds(rig.Origin.position, Vector3.zero);
            float t1 = Now;
            yield return Settle(rig, origin, _ => { });
            // Settle tracks the origin; the eye is what a turn must hold, so sample it over the same time.
            eye.Encapsulate(Vector3.ProjectOnPlane(rig.Head.position, Vector3.up));
            JudgeEyes(c, name, fails, notes);
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
    /// the avatar's eyes must be within <see cref="MaxStanceEyeOff"/> of the view, and over the whole step the rig
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
                yield return null;
                origin.Encapsulate(rig.Origin.position);
            }
            at = to;
            ScriptHead(home + at, level);
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
    /// no stance walk may start, and the mass centre may move at most 0.05 m level.
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
        bool walked = false;
        float t0 = Now;
        while (Now - t0 < 3f)
        {
            float t = Now - t0;
            // Out over 0.35 s, held 0.3 s, back over 0.35 s; the eyes swing on a 0.1 m neck as in vr.45b.
            float k =
                t < 0.35f ? t / 0.35f
                : t < 0.65f ? 1f
                : t < 1f ? 1f - (t - 0.65f) / 0.35f
                : 0f;
            var q = Quaternion.Euler(0f, 70f * Mathf.SmoothStep(0f, 1f, k), 0f);
            ScriptHead(home + (q * forward - forward) * NeckSwing, q * level);
            yield return null;
            walked |= RigDriver.FollowWalking;
            root.Encapsulate(RigDriver.ViewRoot(c));
        }
        ReleaseHead();
        float moved = FlatSize(root);
        var note = F(
            $"glance: stance walk {(walked ? "started" : "never started")}, body moved {moved:0.000} m"
        );
        if (walked || moved > 0.05f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>
    /// <c>vr.303-belt-look-down</c>: the belt slots sit on the body the player sees (#51). The scripted head looks
    /// level, then 60 degrees down, where the body stands back about 0.28 m (vr.273). The belt's level place along
    /// the look, from the hip, may change by at most 0.1 m between the two: before, the slots stayed at the real body
    /// and floated 0.28 m ahead of the avatar's hips.
    /// </summary>
    /// <summary>Impulse (per part, inside a 1 m radius) sent every physics step, as wind sends it (WindChillZone.cs:250), until
    /// the body has moved <see cref="StancePushTravel"/>: run 10's single impulse of 20 moved it 0.004 m against its
    /// planted feet. A steady push slides a standing scout without knocking it down. 10 at 50 Hz is about the 6 a
    /// frame at 90 Hz it was sent at before (unmeasured in game).</summary>
    const float StancePush = 10f;

    const float StancePushTravel = 0.08f;

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
            // Pushed for at most 1 s, until the body has moved far enough to judge.
            var moved = Vector3.ProjectOnPlane(RigDriver.ViewRoot(c), Vector3.up) - body0;
            if (Now - t0 < 1f && moved.magnitude < StancePushTravel)
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
