using System.Collections;
using PeakXR.Core;
using PeakXR.UI.Affordances;

namespace PeakXR.Sandbox;

/// <summary>
/// Cue assertions: a step proves the feedback a player gets, not only that the game refused. Stable API:
/// <list type="bullet">
/// <item><c>float CueMark()</c>: the cue clock now. Take it just before the attempt.</item>
/// <item><c>IEnumerator ExpectCue(bool left, DenyReason reason, float since, float within = 1f)</c>: waits until
/// <c>since + within</c> for the reason, a readable ring, the refused (or notice) sound and a delivered haptic pulse
/// on that hand (<see cref="CueCapture.Fired"/>); fails the step naming the missing channel. On a pass,
/// <see cref="cueNote"/> describes the cue.</item>
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

    IEnumerator ExpectCue(bool left, DenyReason reason, float since, float within = 1f)
    {
        cueNote = null;
        string why;
        while (!CueLog.Capture.Fired(left, reason, since, out why) && CueLog.Now < since + within)
            yield return null;
        if (CueLog.Capture.Fired(left, reason, since, out why))
            cueNote = why;
        else
            Fail(
                $"{reason} cue on the {(left ? "left" : "right")} hand within {within:0.0} s: {why}"
            );
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
