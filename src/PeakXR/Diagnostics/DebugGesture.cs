using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using PeakXR.XRInput;
using UnityEngine;

namespace PeakXR.Diagnostics;

/// <summary>
/// The in-play report gesture (Core <see cref="BugChord"/>): hold Menu on one controller and click the other
/// controller's stick. A quick chord drops a marker, 1.5 s saves a bug report. Every button already has a job, and
/// Menu is the only one that does nothing on press, so the chord is built on it: rig.camera's recenter and the
/// layout's pause, sprint and crouch skip the controls it takes (<see cref="MenuTaken"/>, <see cref="StickTaken"/>).
/// Runs right after the VR sample (VR only), before any reader of those buttons. Under SteamVR a Touch left Menu
/// opens the dashboard and the right one has none, so the both-sticks chord works on every controller: held for the
/// recenter time it recenters, held on past that it saves a report. That chord belongs to input
/// (<see cref="StickChordInput"/>: sprint, crouch and the recenter never depend on this feature); this class only
/// reads its report hold. The two chords never run together: the Menu one cannot start while the sticks are held,
/// nor the sticks one while Menu holds a stick. One-handed play blanks the other controller, so it uses the VR tab
/// buttons.
/// </summary>
internal static class DebugGesture
{
    private static readonly BugChord Chord = new();
    private static bool wasActive;

    /// <summary>A report waits this long at most for the view to clear (a recenter blink), then saves anyway.</summary>
    private const float ClearWaitSeconds = 2f;

    private static bool reportPending;
    private static bool reportLeft;
    private static float reportSince;

    public static bool MenuTaken(bool left) => Chord.MenuTaken(left);

    public static bool StickTaken(bool left) =>
        Chord.StickTaken(left) || StickChordInput.Taken(left);

    /// <summary><c>VRSession.Stopped</c>: a report waiting for a clear view is dropped, never saved on the next
    /// start.</summary>
    public static void OnVRStopped() => reportPending = false;

    /// <summary><c>XRInputSampler.AfterSample</c>: once per VR frame.</summary>
    public static void Update()
    {
        var e = Chord.Update(
            VRInput.Left.Menu,
            VRInput.Left.StickClick,
            VRInput.Right.Menu,
            VRInput.Right.StickClick,
            Time.unscaledDeltaTime,
            blocked: StickChordInput.Held
        );
        StickChordInput.MenuChordActive = Chord.Active;
        UpdateSticks();
        SavePendingReport();
        bool menuLeft = Chord.MenuLeft;
        if (Chord.Active && !wasActive)
            Affordance.Contact(!menuLeft);
        wasActive = Chord.Active;
        // The hold-to-use ramp on the Menu hand, so the report hold is felt filling.
        if (Chord.Active && Chord.Held < Chord.ReportSeconds)
            Affordance.Progress(menuLeft, Chord.Progress);
        if (e == BugChordEvent.None)
            return;
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(menuLeft) : null;
        var at = hand != null ? hand.position : Vector3.zero;
        if (e == BugChordEvent.Marker)
            DropMarker(menuLeft, at, "gesture");
        else
            RequestReport(menuLeft);
    }

    /// <summary>The screenshot is the desktop mirror: taken under a fade (the stick chord's own recenter blink, a
    /// head in rock) it is black, so the report waits for a clear view, up to <see cref="ClearWaitSeconds"/>.</summary>
    private static void RequestReport(bool left)
    {
        reportPending = true;
        reportLeft = left;
        reportSince = Time.unscaledTime;
        SavePendingReport();
    }

    private static void SavePendingReport()
    {
        if (
            !reportPending
            || (
                UI.Common.FadeView.Alpha > 0.01f
                && Time.unscaledTime - reportSince < ClearWaitSeconds
            )
        )
            return;
        reportPending = false;
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(reportLeft) : null;
        BugReport.Save(reportLeft, hand != null ? hand.position : Vector3.zero, "gesture");
    }

    /// <summary>Both sticks held past the recenter: the report hold fills on the off hand, where the recenter ring
    /// was, and saves at the end.</summary>
    private static void UpdateSticks()
    {
        bool off = VRInput.OffHandLeft;
        float progress = StickChordInput.Sticks.ReportProgress;
        if (StickChordInput.ReportNow)
            RequestReport(off);
        else if (progress > 0f && progress < 1f)
            Affordance.Progress(off, progress);
    }

    /// <summary>A marker in the rings and the log, confirmed by the notice cue and its number.</summary>
    public static void DropMarker(bool left, Vector3 at, string source)
    {
        int n = DebugLog.Mark(source);
        Affordance.Inform(
            left,
            at,
            DenyReason.HappenedHere,
            DenyHint.At(at),
            System.FormattableString.Invariant($"Marker {n}"),
            forceText: true
        );
    }
}
