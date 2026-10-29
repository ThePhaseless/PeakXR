using System.Collections;
using System.Collections.Generic;
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
                // Off for as long as the driver keeps it off; judged once focus is gone (a runtime may report only
                // the wear sensor, which leaves focus and input as they were).
                yield return JudgeOff(fails, notes, Watch);
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
                foreach (int state in HeadsetOnStates)
                {
                    Play(state);
                    Watch();
                    yield return Wait(0.3f);
                }
            }
            Watch();
            yield return JudgeBack(fails, notes, Watch);
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
}
