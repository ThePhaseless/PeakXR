using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;

namespace PeakXR.Sandbox;

/// <summary>
/// Cue assertions: a step proves the feedback a player gets, not only that the game refused. Stable API:
/// <list type="bullet">
/// <item><c>float CueMark()</c>: the cue clock now. Take it just before the attempt.</item>
/// <item><c>IEnumerator ExpectCue(bool left, DenyReason reason, float since, float within = 1f)</c>: waits until
/// <c>since + within</c> for the reason, a readable ring inside the headset's view (<see cref="RingOutOfView"/>), the refused (or notice) sound and a delivered haptic pulse
/// on that hand (<see cref="CueCapture.Fired(bool, DenyReason, float, out string)"/>); fails the step naming the missing channel. On a pass,
/// <see cref="cueNote"/> describes the cue.</item>
/// <item><c>IEnumerator ExpectCue(bool left, DenyReason reason, float since, bool tint, float within = 1f, bool seen = true)</c>:
/// the same, and with <c>tint</c> the red preview must have shown. <c>seen: false</c> is for a hand the player is not
/// watching (over the shoulder, behind the back, busy climbing or moving the body): the ring need not be in view, and
/// the sound and the haptic, which every cue requires, carry it. A step acting with a hand in front of the body looks
/// at it first (<see cref="LookAtHand"/>) and keeps the view check.</item>
/// <item><c>IEnumerator ExpectFeel(bool left, CueFeel feel, float since, float within = 1f)</c>: a non-refusal feel
/// (progress, contact, cancel, end stop, danger) reached that hand.</item>
/// <item><c>IEnumerator ExpectNoCue(bool left, float since, float within = 1f)</c>: for paths that must stay quiet;
/// fails on any reason, ring or pulse on that hand since <c>since</c> (<see cref="CueCapture.Quiet"/>). Waits
/// until <c>since + within</c>; pass 0 to check at once.</item>
/// </list>
/// The capture records ui.affordance's refusals and notices (<see cref="Affordance.Deny"/>, <c>Inform</c>), not hovers.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The last passing <see cref="ExpectCue"/>'s description of the cue, for a step's note.</summary>
    string cueNote;

    static float CueMark() => CueLog.Now;

    IEnumerator ExpectCue(
        bool left,
        DenyReason reason,
        float since,
        float within = 1f,
        bool seen = true
    ) => ExpectCue(left, reason, since, false, within, seen);

    /// <summary>As above; with <paramref name="tint"/> the red preview of the refused thing must also have shown
    /// while the ring was drawn (<see cref="CueHand.TintAt"/>). Pass false for refusals with nothing to tint (a jump,
    /// another player: D10).</summary>
    IEnumerator ExpectCue(
        bool left,
        DenyReason reason,
        float since,
        bool tint,
        float within = 1f,
        bool seen = true
    )
    {
        cueNote = null;
        string why;
        while (
            !CueLog.Capture.Fired(left, reason, since, tint, out why) && CueLog.Now < since + within
        )
            yield return null;
        // A glancing head may still be turning when the cue fires; judge the view once the eyes are on the hand.
        if (seen)
            yield return GlanceSettled(0.4f);
        if (!CueLog.Capture.Fired(left, reason, since, tint, out why))
            Fail(
                $"{reason} cue on the {(left ? "left" : "right")} hand within {within:0.0} s: {why}"
            );
        else if (!seen)
            cueNote = why + ", hand out of sight: sound and haptic carry it";
        else if (RingOutOfView(left, out var view) is { } outside)
            Fail($"{reason} cue on the {(left ? "left" : "right")} hand: {why}; {outside}");
        else
            cueNote = why + view;
    }

    /// <summary>
    /// Null when the hand's last drawn ring lies wholly inside the headset's view from the eye, else why not. The
    /// field is the head camera's <c>fieldOfView</c>, which XR drives from the headset's projection and which is the
    /// vertical (narrower) field (Unity Camera.fieldOfView docs); the margin is the ring's own half size, so all of it
    /// shows. The head pose is this frame's, which a scripted head holds still through the wait. <paramref name="view"/> gives the angle off
    /// the view for the note. Flat play, or no head camera, checks nothing.
    /// </summary>
    static string RingOutOfView(bool left, out string view)
    {
        view = "";
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || rig.Head == null || rig.HeadCamera == null)
            return null;
        ref var h = ref CueLog.Capture.Hand(left);
        float fov = rig.HeadCamera.fieldOfView;
        bool inside = Legibility.InFieldOfView(
            rig.Head.position.N(),
            rig.Head.forward.N(),
            h.RingCenter,
            fov,
            h.RingDegrees,
            out float off
        );
        view = F($", ring {off:0} deg off the view (field {fov:0} deg)");
        // The real spot out of sight: its in-view edge copy (CueEdge), drawn alongside the last ring, carries it.
        if (
            !inside
            && h.EdgeAt >= h.RingAt - 0.1f
            && Legibility.InFieldOfView(
                rig.Head.position.N(),
                rig.Head.forward.N(),
                h.EdgeCenter,
                fov,
                h.EdgeDegrees,
                out float edgeOff
            )
        )
        {
            view = F(
                $", ring {off:0} deg off the view, shown by its edge copy {edgeOff:0} deg off"
            );
            return null;
        }
        return inside
            ? null
            : F(
                $"ring out of view: {off:0} deg off the view, a {h.RingDegrees:0.0} deg ring not wholly inside the {fov * 0.5f:0} deg half field"
            );
    }

    /// <summary>Waits until <c>since + within</c> for a non-refusal feel (progress, contact, cancel, end stop,
    /// danger) on that hand (<see cref="CueCapture.Felt"/>); fails the step naming what is missing.</summary>
    IEnumerator ExpectFeel(bool left, CueFeel feel, float since, float within = 1f)
    {
        // As ExpectCue: a failed feel must not leave the previous cue's description for the step's note.
        cueNote = null;
        string why;
        while (!CueLog.Capture.Felt(left, feel, since, out why) && CueLog.Now < since + within)
            yield return null;
        if (CueLog.Capture.Felt(left, feel, since, out why))
            cueNote = why;
        else
            Fail($"{feel} on the {(left ? "left" : "right")} hand within {within:0.0} s: {why}");
    }

    IEnumerator ExpectNoCue(bool left, float since, float within = 1f)
    {
        string why;
        while (CueLog.Capture.Quiet(left, since, out why) && CueLog.Now < since + within)
            yield return null;
        if (!CueLog.Capture.Quiet(left, since, out why))
            Fail($"expected no cue on the {(left ? "left" : "right")} hand: {why}");
    }
}
