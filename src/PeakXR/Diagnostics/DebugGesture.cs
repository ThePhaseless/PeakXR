using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Diagnostics;

/// <summary>
/// The in-play report gesture (Core <see cref="BugChord"/>): hold Menu on one controller and click the other
/// controller's stick. A quick chord drops a marker, 1.5 s saves a bug report. Every button already has a job, and
/// Menu is the only one that does nothing on press, so the chord is built on it: rig.camera's recenter and the
/// layout's pause, sprint and crouch skip the controls it takes (<see cref="MenuTaken"/>, <see cref="StickTaken"/>).
/// Runs right after the VR sample (VR only), before any reader of those buttons. Under SteamVR a Touch left Menu
/// opens the dashboard and the right one has none, so the both-sticks chord (Core <see cref="StickChord"/>) works on
/// every controller: held for the recenter time it recenters (rig.camera reads <see cref="SticksHeld"/>), held on
/// past that it saves a report. One-handed play blanks the other controller, so it uses the VR tab buttons.
/// </summary>
internal static class DebugGesture
{
    private static readonly BugChord Chord = new();
    private static readonly StickChord Sticks = new();
    private static bool wasActive;

    public static bool MenuTaken(bool left) => Chord.MenuTaken(left);

    public static bool StickTaken(bool left) => Chord.StickTaken(left) || Sticks.StickTaken(left);

    /// <summary>Both stick clicks are held as one chord: rig.camera's recenter hold.</summary>
    public static bool SticksHeld => Sticks.Active;

    /// <summary><c>XRInputSampler.AfterSample</c>: once per VR frame.</summary>
    public static void Update()
    {
        var e = Chord.Update(
            VRInput.Left.Menu,
            VRInput.Left.StickClick,
            VRInput.Right.Menu,
            VRInput.Right.StickClick,
            Time.unscaledDeltaTime
        );
        UpdateSticks();
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
            BugReport.Save(menuLeft, at, "gesture");
    }

    /// <summary>Both sticks held past the recenter: the report hold fills on the off hand, where the recenter ring
    /// was, and saves at the end.</summary>
    private static void UpdateSticks()
    {
        Sticks.RecenterSeconds = Rig.CameraFeature.RecenterSeconds;
        Sticks.ReportSeconds = Chord.ReportSeconds;
        var e = Sticks.Update(
            VRInput.Left.StickClick,
            VRInput.Right.StickClick,
            Chord.Active,
            Time.unscaledDeltaTime
        );
        bool off = VRInput.OffHandLeft;
        if (e == BugChordEvent.Report)
        {
            var hand = VRRig.Instance != null ? VRRig.Instance.Hand(off) : null;
            BugReport.Save(off, hand != null ? hand.position : Vector3.zero, "gesture");
        }
        else if (Sticks.ReportProgress > 0f && Sticks.ReportProgress < 1f)
            Affordance.Progress(off, Sticks.ReportProgress);
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
