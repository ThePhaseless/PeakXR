using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Interact.Climb;
using PeakXR.Render;
using PeakXR.UI.Common;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.260-headset-off</c>: the headset taken off and put back on. Chosen by name (<c>-peakxr-selftest-only</c>) it
/// waits for a real one from outside (SteamVrMcp's <c>set_proximity</c>, or a person): a focus loss, a session state
/// other than FOCUSED, or the wear sensor reading off, then for all three back. In a full run nobody is there to do it,
/// so it writes the states a runtime reports for it (VISIBLE, SYNCHRONIZED, STOPPING, IDLE, READY and back to FOCUSED)
/// into <see cref="XRSessionState"/>, where the backend writes them. Either way, while off the local player's input
/// pauses under the focus fade, and once back on VR is still active, the fade and input are back and the head
/// camera renders in stereo.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const string HeadsetOffId = "vr.260-headset-off";

    /// <summary>The OpenXR states of a headset taken off (Meta's runtime: Khronos forum, "Resuming application after
    /// removing headset") and put back on, as XrSessionState numbers.</summary>
    static readonly int[] HeadsetOffStates = { 4, 3, 6, 1 };
    static readonly int[] HeadsetOnStates = { 2, 3, 4, 5 };

    /// <summary>Live mode: the step was chosen by name, so someone outside drives the headset.</summary>
    bool HeadsetOffLive => filter != null && filter.Matches(HeadsetOffId);

    IEnumerator HeadsetOffCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var seen = new List<string>();
        bool stopped = false;
        void Watch()
        {
            string now = $"{XRBootstrapDriver.FocusSource}, wear sensor {Worn()}";
            if (seen.Count == 0 || seen[seen.Count - 1] != now)
                seen.Add(now);
            stopped |= !VRSession.Active;
        }
        // Focus covers every OpenXR state but FOCUSED (XRFocus.Focused).
        static bool Off() => !VRSession.HasFocus || XRBootstrapDriver.UserPresent == false;

        yield return WaitFor(() => VRSession.HasFocus && FadeView.Alpha < 0.01f, 10f);
        if (!VRSession.HasFocus)
        {
            stepSkip = $"no runtime focus to start from ({XRBootstrapDriver.FocusSource})";
            yield break;
        }

        int savedState = XRSessionState.OpenXRState;
        bool savedLost = XRSessionState.OpenVRFocusLost;
        bool live = HeadsetOffLive;
        try
        {
            if (live)
            {
                Plugin.Log.LogInfo($"[selftest] {HeadsetOffId}: take the headset off now");
                float end = Now + 120f;
                while (!Off() && Now < end)
                {
                    Watch();
                    yield return null;
                }
                if (!Off())
                {
                    Fail(
                        $"nothing changed within 120 s of the prompt: focus, session state and wear sensor all still on ({XRBootstrapDriver.FocusSource}, wear sensor {Worn()})"
                    );
                    yield break;
                }
                Plugin.Log.LogInfo(
                    $"[selftest] {HeadsetOffId}: headset off seen ({XRBootstrapDriver.FocusSource}, wear sensor {Worn()}); put it back on"
                );
                // Off for as long as the driver keeps it off; judged once focus is gone (a runtime that reports only
                // the wear sensor loses it once the sensor has read worn, XRBootstrapDriver.TakenOff).
                yield return JudgeOff(fails, notes, Watch);
                yield return JudgePacing(fails, notes);
                end = Now + 600f;
                while (Off() && Now < end)
                {
                    Watch();
                    yield return null;
                }
                if (Off())
                {
                    Fail($"headset not back on within 600 s ({XRBootstrapDriver.FocusSource})");
                    yield break;
                }
            }
            else
            {
                foreach (int state in HeadsetOffStates)
                {
                    Play(state);
                    Watch();
                    yield return Wait(0.3f);
                }
                yield return JudgeOff(fails, notes, Watch);
                yield return JudgePacing(fails, notes);
                foreach (int state in HeadsetOnStates)
                {
                    Play(state);
                    Watch();
                    yield return Wait(0.3f);
                }
            }
            Watch();
            yield return JudgeBack(fails, notes, Watch);
            yield return JudgePacing(fails, notes);
        }
        finally
        {
            if (!live)
            {
                XRSessionState.OpenXRState = savedState;
                XRSessionState.OpenVRFocusLost = savedLost;
            }
            ClosePause();
        }

        if (stopped)
            fails.Add("VR stopped while the headset was off (the game went flat)");
        notes.Insert(0, (live ? "live: " : "simulated: ") + string.Join(" > ", seen));
        var all = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + all);
        else
            stepNote = all;
    }

    static string Worn() =>
        XRBootstrapDriver.UserPresent switch
        {
            true => "worn",
            false => "off",
            null => "not reported",
        };

    /// <summary>Simulated mode: the state the backend would write for this XrSessionState (OpenVR has one flag).</summary>
    static void Play(int state)
    {
        if (VRSession.Running == VRRuntime.OpenVR)
            XRSessionState.OpenVRFocusLost = state != 5;
        else
            XRSessionState.OpenXRState = state;
    }

    /// <summary>While off: input paused under the focus fade (<c>XR.PauseInputOnFocusLoss</c>), when the runtime took
    /// focus at all.</summary>
    static IEnumerator JudgeOff(List<string> fails, List<string> notes, System.Action watch)
    {
        float end = Now + 2f;
        while (VRSession.HasFocus && Now < end)
        {
            watch();
            yield return null;
        }
        if (VRSession.HasFocus)
        {
            notes.Add(
                $"focus kept while off ({XRBootstrapDriver.FocusSource}, wear sensor {Worn()}): input not paused"
            );
            yield break;
        }
        // The pause menu the focus loss opens blocks input by itself; closed, only the focus pause is left to hold it.
        yield return null;
        bool paused = GUIManager.InPauseMenu;
        ClosePause();
        yield return null;
        var c = Character.localCharacter;
        if (
            c != null
            && FocusInputPatch.CanDoInput != null
            && BootstrapFeature.PauseOnFocusLoss.Value
            && (bool)FocusInputPatch.CanDoInput.Invoke(c, null)
        )
            fails.Add("input not paused while off");
        if (VRFade.Target < 0.6f - 0.001f)
            fails.Add($"focus fade not held while off (target {VRFade.Target:0.00})");
        notes.Add(
            $"off: fade target {VRFade.Target:0.00}, pause menu {(paused ? "opened" : "not opened")}"
        );
    }

    /// <summary>Back on: VR active, focus back, the fade gone, the head tracked and drawn in stereo, and the player's
    /// input back once the pause menu the focus loss opened is closed.</summary>
    static IEnumerator JudgeBack(List<string> fails, List<string> notes, System.Action watch)
    {
        float end = Now + 3f;
        while (
            (!VRSession.HasFocus || FadeView.Alpha >= 0.01f || !VRInput.HeadTracked) && Now < end
        )
        {
            watch();
            yield return null;
        }
        if (!VRSession.Active)
        {
            fails.Add($"VR not active after the headset came back (running {VRSession.Running})");
            yield break;
        }
        if (!VRSession.HasFocus)
            fails.Add($"focus not back ({XRBootstrapDriver.FocusSource})");
        if (FadeView.Alpha >= 0.01f)
            fails.Add(
                $"view still faded 3 s after (drawn {FadeView.Alpha:0.00}, target {VRFade.Target:0.00}, eye guard black {Rig.RigDriver.EyeGuardBlack})"
            );
        if (!VRInput.HeadTracked)
            fails.Add("head not tracked after the headset came back");
        var rig = VRRig.Instance;
        var cam = rig != null ? rig.HeadCamera : null;
        if (
            cam == null
            || !cam.isActiveAndEnabled
            || cam.stereoTargetEye != StereoTargetEyeMask.Both
        )
            fails.Add(
                cam == null
                    ? "no head camera after the headset came back"
                    : $"head camera '{cam.name}' enabled {cam.isActiveAndEnabled}, eyes {cam.stereoTargetEye}"
            );
        var display = XRRuntimeInfo.Display;
        int passes = display != null && display.running ? display.GetRenderPassCount() : 0;
        if (passes == 0)
            fails.Add("the headset gets no render pass after it came back");
        ClosePause();
        yield return null;
        var c = Character.localCharacter;
        bool input =
            c == null
            || FocusInputPatch.CanDoInput == null
            || (bool)FocusInputPatch.CanDoInput.Invoke(c, null);
        if (!input)
            fails.Add("player input still blocked after the pause menu closed");
        notes.Add(
            $"back: fade {FadeView.Alpha:0.00}, {passes} render pass(es), wear sensor {Worn()}"
        );
    }

    /// <summary>The game paces itself (<see cref="RenderDriver.IdlePacing"/>, at most <see cref="XRFocus.IdleHz"/> fps
    /// over 0.5 s) exactly while the OpenXR session shows none of its frames, and leaves pacing to the runtime
    /// otherwise.</summary>
    static IEnumerator JudgePacing(List<string> fails, List<string> notes)
    {
        bool hidden =
            VRSession.Running == VRRuntime.OpenXR && !XRFocus.Shown(XRSessionState.OpenXRState);
        yield return null;
        int frames = 0;
        float start = Now;
        while (Now - start < 0.5f)
        {
            frames++;
            yield return null;
        }
        float fps = frames / (Now - start);
        if (RenderDriver.IdlePacing != hidden)
            fails.Add(
                $"idle pacing {(RenderDriver.IdlePacing ? "on" : "off")} while the headset {(hidden ? "shows nothing" : "shows frames")} ({XRBootstrapDriver.FocusSource})"
            );
        else if (hidden && fps > XRFocus.IdleHz * 1.25f)
            fails.Add(
                $"{fps:0} fps while the headset shows nothing, past the {XRFocus.IdleHz} fps idle pace"
            );
        notes.Add($"{(hidden ? "hidden" : "shown")}: {fps:0} fps");
    }

    /// <summary>
    /// vr.261: VR stops while the player hangs from a rope, the hands open while flat, and VR comes back the way
    /// XRBootstrapDriver.Update brings a restarted runtime back (session IDLE and stopped, then READY to FOCUSED). The
    /// player is still on the rope after twice the regrip grace (ClimbHands forgets its anchors while VR is off, so the
    /// open hands are not a let-go), no pause menu opened (a restart is not the dashboard: DashboardPause.Reset), and
    /// a headset mic the session switched to was put back while flat (HeadsetMic.Restore).
    /// </summary>
    IEnumerator VRRestartHangStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        yield return HangRope();
        if (testRope == null)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.8f);
        int savedState = XRSessionState.OpenXRState;
        bool savedLost = XRSessionState.OpenVRFocusLost;
        OnRestore(() =>
        {
            XRSessionState.OpenXRState = savedState;
            XRSessionState.OpenVRFocusLost = savedLost;
            VRSession.SetActive(true);
        });
        string chosen = PeakXR.Voice.VoiceRuntime.Mic?.Chosen;

        // Stop: the session ends (IDLE) and the display stops; focus stays as it was, as the driver leaves it.
        Play(1);
        VRSession.SetActive(false);
        Script(left).Grip = 0f;
        yield return Wait(1f);
        bool flatHeld = c.data.isRopeClimbing;
        var mic =
            SettingsHandler.Instance != null
                ? SettingsHandler.Instance.GetSetting<MicrophoneSetting>()
                : null;
        string micFlat = mic != null ? mic.Value.id : null;

        // Back: as the driver does in one Update, VR on with the session's focus (READY: none) before the UI ticks.
        bool paused = false;
        Play(2);
        VRSession.SetActive(true);
        VRSession.HasFocus = false;
        foreach (int state in HeadsetOnStates)
        {
            Play(state);
            for (float end = Now + 0.3f; Now < end; )
            {
                paused |= GUIManager.InPauseMenu;
                yield return null;
            }
        }
        float grace = Interact.InteractConfig.Instance.ClimbRegripGrace;
        for (float end = Now + grace * 2f + 0.5f; Now < end && c.data.isRopeClimbing; )
        {
            paused |= GUIManager.InPauseMenu;
            yield return null;
        }
        bool kept = c.data.isRopeClimbing;
        var note =
            $"on the rope while flat {flatHeld}, after the restart with open hands {kept}, pause menu {(paused ? "opened" : "not opened")}, "
            + (
                chosen == null
                    ? "no headset mic switch to undo"
                    : $"mic while flat '{micFlat}' (headset '{chosen}')"
            );
        if (!flatHeld)
            Fail("the player left the rope while VR was off: " + note);
        else if (!kept)
            Fail("the restart dropped the player off the rope: " + note);
        else if (paused)
            Fail("the restart opened the pause menu: " + note);
        else if (chosen != null && micFlat == chosen)
            Fail("the headset mic stayed selected while flat: " + note);
        else
            stepNote = note;
        ClosePause();
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.262: a runtime that reports the headset taken off only through the wear sensor (XRBootstrapDriver.TakenOff,
    /// through <see cref="XRBootstrapDriver.SelfTestPresence"/>). A sensor that reads "not worn" without ever having
    /// read worn changes nothing (a stuck driver must not freeze play); worn, then not worn, pauses input under the
    /// focus fade (<see cref="JudgeOff"/>); worn again brings everything back (<see cref="JudgeBack"/>).
    /// </summary>
    IEnumerator WearSensorCheck()
    {
        yield return WaitFor(() => VRSession.HasFocus && FadeView.Alpha < 0.01f, 10f);
        if (!VRSession.HasFocus)
        {
            stepSkip = $"no runtime focus to start from ({XRBootstrapDriver.FocusSource})";
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        var seen = new List<string>();
        void Watch()
        {
            string now = $"{XRBootstrapDriver.FocusSource}, wear sensor {Worn()}";
            if (seen.Count == 0 || seen[seen.Count - 1] != now)
                seen.Add(now);
        }
        bool? reading = null;
        XRBootstrapDriver.SelfTestPresence = () => reading;
        try
        {
            yield return Wait(0.2f);
            reading = false;
            yield return Wait(0.5f);
            if (!VRSession.HasFocus || XRBootstrapDriver.TakenOff)
                fails.Add("a sensor that never read worn paused play on \"not worn\"");
            reading = true;
            yield return Wait(0.3f);
            reading = false;
            yield return JudgeOff(fails, notes, Watch);
            if (VRSession.HasFocus)
                fails.Add("worn then not worn kept focus: input not paused");
            reading = true;
            yield return JudgeBack(fails, notes, Watch);
        }
        finally
        {
            // Back to "not reported" before the real sensor: its own reading must not count as a transition.
            reading = null;
            ClosePause();
        }
        yield return null;
        yield return null;
        XRBootstrapDriver.SelfTestPresence = null;
        notes.Insert(0, string.Join(" > ", seen));
        var all = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + all);
        else
            stepNote = all;
    }
}
