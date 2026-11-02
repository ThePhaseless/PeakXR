using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact.Climb;
using PeakXR.Render;
using PeakXR.Rig;
using PeakXR.UI.Common;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;

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
        bool savedIdle = XRSessionState.OpenVRIdle;
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
                XRSessionState.OpenVRIdle = savedIdle;
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

    /// <summary>Simulated mode: the state the backend would write for this XrSessionState (OpenVR has two flags: focus,
    /// and the headset idle for the states the headset does not show).</summary>
    static void Play(int state)
    {
        if (VRSession.Running == VRRuntime.OpenVR)
        {
            XRSessionState.OpenVRFocusLost = state != 5;
            XRSessionState.OpenVRIdle = !XRFocus.Shown(state);
        }
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
    /// over 0.5 s) exactly while the headset shows none of its frames (<see cref="XRBootstrapDriver.HeadsetHidden"/>:
    /// OpenXR state, OpenVR idle), and leaves pacing to the runtime otherwise.</summary>
    static IEnumerator JudgePacing(List<string> fails, List<string> notes)
    {
        bool hidden = XRBootstrapDriver.HeadsetHidden;
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

    /// <summary>
    /// vr.489: a controller's state does not reach the game's actions. A trigger value queued on the real Input System
    /// device, as the runtime would send it, must leave the control as it was: the game's UI module would otherwise
    /// take every pull as a second click (#133, #134). The game's rebinding prompt still reads that event, so it must not
    /// take the control either, and a rebind made past the patched entry point must, or the probe proved nothing. A
    /// self-test's hands never pass through there, so no other step can see this.
    /// </summary>
    IEnumerator XRStateIsolatedCheck()
    {
        AxisControl trigger = null;
        foreach (var device in InputSystem.devices)
            if (device is XRController && trigger == null)
                trigger = device.TryGetChildControl<AxisControl>("trigger");
        if (trigger == null)
        {
            stepSkip = "no XR controller with a trigger among the Input System's devices";
            yield break;
        }
        float before = trigger.ReadValue();
        float probe = before > 0.5f ? 0.1f : 0.9f;
        // Throwaway action, so nothing can land on a real binding. The long wait keeps a match from completing.
        using var action = new InputAction("selftest-rebind", binding: "<Keyboard>/f12");
        using var bare = new InputActionRebindingExtensions.RebindingOperation();
        bare.WithAction(action).OnMatchWaitForAnother(5f).Start();
        using var prompt = action.PerformInteractiveRebinding().OnMatchWaitForAnother(5f).Start();
        InputSystem.QueueDeltaStateEvent(trigger, probe);
        InputSystem.Update();
        float read = trigger.ReadValue();
        bool bareTook = bare.candidates.Contains(trigger);
        bool promptTook = prompt.candidates.Contains(trigger);
        bare.Cancel();
        prompt.Cancel();
        var note =
            $"{trigger.path}: {before:0.00}, queued {probe:0.00}, read {read:0.00}; "
            + $"rebind took it: bare {bareTook}, prompt {promptTook}";
        InputSystem.QueueDeltaStateEvent(trigger, before);
        InputSystem.Update();
        if (Mathf.Abs(read - before) > 0.01f)
            Fail("the game's actions still read the controller: " + note);
        else if (promptTook)
            Fail("the game's rebinding prompt takes the controller: " + note);
        else if (!bareTook)
            Fail("the probe reached no rebind, so the prompt's result says nothing: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.490: the game's rebinding prompt, opened in the pause menu on a throwaway action. A controller press leaves it
    /// up and gets the refusal on that hand with the text saying why and how out; the headset coming off and back on,
    /// through the session state or the wear sensor alone, leaves it up, a player reaching for the keyboard; a Menu tap and the dashboard each end it through the page's own cancel, the
    /// action enabled again and the pause menu still open on the Controls page.
    /// </summary>
    IEnumerator RebindPromptExitCheck()
    {
        var gui = GUIManager.instance;
        var c = Character.localCharacter;
        if (gui == null || c == null)
        {
            Fail("no GUIManager or character");
            yield break;
        }
        yield return HandsAside();
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(
            () => GUIManager.InPauseMenu && FindAnyObjectByType<PauseMenuHandler>() != null,
            5f,
            "pause menu did not open"
        );
        if (!stepOk)
            yield break;
        var handler = FindAnyObjectByType<PauseMenuHandler>();
        // The game's loop needs a binding group (A/PauseMenuRebindKeyPage.cs:54-65); the key is one nobody presses.
        using var action = new InputAction("selftest-rebind");
        action.AddBinding("<Keyboard>/f12", groups: "Keyboard&Mouse");
        var savedTarget = RebindPromptTarget(
            (action, "PROMPT_REBIND", Zorro.ControllerSupport.InputScheme.KeyboardMouse)
        );
        int savedState = XRSessionState.OpenXRState;
        bool savedLost = XRSessionState.OpenVRFocusLost;
        bool savedIdle = XRSessionState.OpenVRIdle;
        action.Enable();
        var notes = new List<string>();
        try
        {
            if (handler.TransistionToPage<PauseMenuRebindKeyPage>() == null)
            {
                Fail("the pause menu has no rebinding prompt");
                yield break;
            }
            yield return Wait(0.2f);

            float mark = CueMark();
            Script(false).Primary = true;
            yield return ExpectCue(false, DenyReason.Locked, mark, within: 1f, seen: false);
            Script(false).Primary = false;
            if (!stepOk)
                yield break;
            notes.Add(cueNote);
            var label = UI.Affordances.AffordanceRuntime.LabelText;
            if (label == null || !label.Contains("Keyboard or gamepad"))
            {
                Fail($"the refusal does not say why: '{label}'");
                yield break;
            }
            mark = CueMark();
            Script(true).Trigger = 1f;
            yield return ExpectCue(true, DenyReason.Locked, mark, within: 1f, seen: false);
            Script(true).Trigger = 0f;
            if (!stepOk)
                yield break;
            if (handler.currentPage is not PauseMenuRebindKeyPage || action.enabled)
            {
                Fail("a controller press changed the prompt");
                yield break;
            }

            Play(3);
            yield return Wait(RebindPromptInput.DashboardSeconds + 0.5f);
            Play(4);
            yield return Wait(RebindPromptInput.DashboardSeconds + 0.5f);
            Play(5);
            yield return Wait(0.3f);
            if (handler.currentPage is not PauseMenuRebindKeyPage || action.enabled)
            {
                Fail("the headset coming off or back on (a long VISIBLE) ended the prompt");
                yield break;
            }

            yield return RebindPromptWearSensor(handler, action);
            if (!stepOk)
                yield break;

            Script(true).Menu = true;
            yield return Wait(0.15f);
            Script(true).Menu = false;
            yield return Until(
                () => handler.currentPage is PauseMenuControlsPage,
                2f,
                "a Menu tap did not end the prompt"
            );
            if (!stepOk)
                yield break;
            if (!action.enabled || !GUIManager.InPauseMenu)
            {
                Fail(
                    $"Menu tap: action enabled {action.enabled}, in pause menu {GUIManager.InPauseMenu}"
                );
                yield break;
            }
            notes.Add("Menu tap: back on Controls, action enabled");

            handler.TransistionToPage<PauseMenuRebindKeyPage>();
            yield return Wait(0.2f);
            Play(4);
            yield return Until(
                () => handler.currentPage is PauseMenuControlsPage,
                RebindPromptInput.DashboardSeconds + 2f,
                "the dashboard did not end the prompt"
            );
            Play(5);
            if (!stepOk)
                yield break;
            if (!action.enabled || !GUIManager.InPauseMenu)
            {
                Fail(
                    $"dashboard: action enabled {action.enabled}, in pause menu {GUIManager.InPauseMenu}"
                );
                yield break;
            }
            notes.Add("dashboard: back on Controls, action enabled");
            Note(string.Join("; ", notes));
        }
        finally
        {
            XRSessionState.OpenXRState = savedState;
            XRSessionState.OpenVRFocusLost = savedLost;
            XRSessionState.OpenVRIdle = savedIdle;
            // A failed step leaves no live rebind for the action's dispose or the next step.
            if (handler != null && handler.currentPage is PauseMenuRebindKeyPage page)
                page.OnPageExit();
            ClosePause();
            RebindPromptTarget(savedTarget);
        }
    }

    /// <summary>vr.490: a headset taken off that only the wear sensor reports (OpenXR's session stays FOCUSED, so focus
    /// is lost but <see cref="XRBootstrapDriver.HeadsetHidden"/> stays false) leaves the prompt up past the dashboard
    /// wait. As in vr.262, the sensor goes back to "not reported" before the real one.</summary>
    IEnumerator RebindPromptWearSensor(PauseMenuHandler handler, InputAction action)
    {
        bool? reading = true;
        XRBootstrapDriver.SelfTestPresence = () => reading;
        try
        {
            yield return Wait(0.3f);
            reading = false;
            yield return Wait(RebindPromptInput.DashboardSeconds + 0.5f);
            if (!XRBootstrapDriver.TakenOff || VRSession.HasFocus)
                Fail(
                    $"worn then not worn did not read as off: {XRBootstrapDriver.FocusSource}, focus {VRSession.HasFocus}"
                );
            reading = true;
            yield return Wait(0.3f);
            if (handler.currentPage is not PauseMenuRebindKeyPage || action.enabled)
                Fail("the wear sensor reading off ended the prompt");
        }
        finally
        {
            reading = null;
        }
        yield return null;
        yield return null;
        XRBootstrapDriver.SelfTestPresence = null;
    }

    /// <summary>Sets what the game's rebinding prompt rebinds, as a rebind button does (A/PauseMenuRebindButton.cs:72-74);
    /// returns what it was.</summary>
    static (
        InputAction action,
        string index,
        Zorro.ControllerSupport.InputScheme scheme
    ) RebindPromptTarget(
        (InputAction action, string index, Zorro.ControllerSupport.InputScheme scheme) target
    )
    {
        var action = GameMember.StaticField<PauseMenuRebindKeyPage, InputAction>("inputAction");
        var index = GameMember.StaticField<PauseMenuRebindKeyPage, string>("inputLocIndex");
        var scheme = GameMember.StaticField<
            PauseMenuRebindKeyPage,
            Zorro.ControllerSupport.InputScheme
        >("forcedInputScheme");
        var was = (action(), index(), scheme());
        action() = target.action;
        index() = target.index;
        scheme() = target.scheme;
        return was;
    }

    /// <summary>A raycaster on a canvas ui.world converted, for vr.360.</summary>
    static UnityEngine.UI.GraphicRaycaster ConvertedRaycaster()
    {
        foreach (
            var raycaster in FindObjectsByType<UnityEngine.UI.GraphicRaycaster>(
                FindObjectsSortMode.None
            )
        )
            if (
                raycaster.TryGetComponent<Canvas>(out var canvas)
                && UI.World.WorldCanvases.IsConverted(canvas.rootCanvas)
            )
                return raycaster;
        return null;
    }

    /// <summary>
    /// vr.360: while the headset shows none of the game's frames (OpenXR SYNCHRONIZED; OpenVR headset idle, written
    /// into <see cref="XRSessionState"/> as the backend would), the desktop gets a flat view: the rig's head camera
    /// renders as a plain camera (<see cref="Rig.DesktopMirror.Flat"/>, eye mask None, URP XR rendering off) and the
    /// game paces itself at the idle rate on either runtime. Shown again, the same camera is back in stereo with XR
    /// rendering on and pacing off. Screenshot <c>vr-360-flat-view</c> is the window while off. While off the converted
    /// canvases' event camera is that head camera, so a mouse click lands under the cursor; back on, it is the laser's
    /// (#132). The laser is judged with the menu the focus loss opens up, hidden while off and showing back on: without
    /// a menu it hides either way, so that is only noted.
    /// </summary>
    IEnumerator FlatViewCheck()
    {
        var rig = VRRig.Instance;
        var cam = rig != null ? rig.HeadCamera : null;
        if (cam == null)
        {
            stepSkip = "no head camera";
            yield break;
        }
        if (CameraFeature.Mirror.Value == MirrorEye.Off)
        {
            stepSkip = "Camera.DesktopMirror Off";
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        int savedState = XRSessionState.OpenXRState;
        bool savedLost = XRSessionState.OpenVRFocusLost;
        bool savedIdle = XRSessionState.OpenVRIdle;
        try
        {
            Play(3);
            yield return Wait(0.3f);
            var raycaster = ConvertedRaycaster();
            if (raycaster == null)
                fails.Add("no converted canvas to read the event camera from");
            var flat = Rig.DesktopMirror.Flat;
            if (raycaster != null && raycaster.eventCamera != cam)
                fails.Add($"event camera '{raycaster.eventCamera}' while off, not the head camera");
            if (!GUIManager.InPauseMenu)
                notes.Add("no menu up while off: laser not judged");
            else if (UI.World.LaserPointer.Showing)
                fails.Add("the laser shows while the head camera draws the window");
            var data = cam.GetUniversalAdditionalCameraData();
            if (!XRBootstrapDriver.HeadsetHidden)
                fails.Add($"not hidden after the state change ({XRBootstrapDriver.FocusSource})");
            if (flat != cam)
                fails.Add(
                    $"flat view on '{(flat != null ? flat.name : "nothing")}', not the head camera '{cam.name}'"
                );
            if (cam.stereoTargetEye != StereoTargetEyeMask.None || data.allowXRRendering)
                fails.Add(
                    $"head camera still stereo while off (eyes {cam.stereoTargetEye}, XR rendering {data.allowXRRendering})"
                );
            yield return Shot("vr-360-flat-view");
            yield return JudgePacing(fails, notes);
            Play(5);
            yield return Wait(0.3f);
            if (Rig.DesktopMirror.Flat != null)
                fails.Add("flat view kept after the headset shows frames again");
            if (raycaster != null && raycaster.eventCamera != UI.World.LaserPointer.EventCamera)
                fails.Add($"event camera '{raycaster.eventCamera}' back on, not the laser's");
            if (!GUIManager.InPauseMenu)
                notes.Add("no menu up back on: laser not judged");
            else if (!UI.World.LaserPointer.Showing)
                fails.Add("the laser stays hidden after the headset shows frames again");
            if (cam.stereoTargetEye != StereoTargetEyeMask.Both || !data.allowXRRendering)
                fails.Add(
                    $"head camera not back in stereo (eyes {cam.stereoTargetEye}, XR rendering {data.allowXRRendering})"
                );
            yield return JudgePacing(fails, notes);
        }
        finally
        {
            XRSessionState.OpenXRState = savedState;
            XRSessionState.OpenVRFocusLost = savedLost;
            XRSessionState.OpenVRIdle = savedIdle;
            ClosePause();
        }
        notes.Insert(0, $"{VRSession.Running}, head camera '{cam.name}'");
        var all = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + all);
        else
            stepNote = all;
    }
}
