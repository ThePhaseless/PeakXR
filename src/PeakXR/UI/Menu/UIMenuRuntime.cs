using System.Collections;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.UI.RunFlow;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.UI.Menu;

/// <summary>
/// Scene-level comfort for menus: fades out before a level switch, shows the loading space while the
/// game loads, fades back in afterwards, and keeps the title stage applied. Runs before the world UI
/// runtime so panels are placed relative to the rig pose set here.
/// </summary>
[DefaultExecutionOrder(30500)]
internal sealed class UIMenuRuntime : MonoBehaviour
{
    private const float FadeTimeout = 2f;

    private readonly object loadOwner = new();
    private readonly object cutOwner = new();
    private TitleStage stage;
    private LoadingSpace space;
    private UIMenuSettings cfg;
    private Coroutine loadRoutine;
    private bool splash;
    private Pretitle pretitle;
    private AccessTools.FieldRef<Pretitle, bool> skipSplash;
    private readonly HoldToConfirm skipHold = new();
    private readonly LoadDestination destination = new();
    private readonly DashboardPause dashboardPause = new();

    public static UIMenuRuntime Create(
        TitleStage stage,
        LoadingSpace space,
        UIMenuSettings cfg,
        AccessTools.FieldRef<Pretitle, bool> skipSplash
    )
    {
        var go = new GameObject("PeakXR_MenuUI");
        DontDestroyOnLoad(go);
        var runtime = go.AddComponent<UIMenuRuntime>();
        runtime.stage = stage;
        runtime.space = space;
        runtime.cfg = cfg;
        runtime.skipSplash = skipSplash;
        return runtime;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        LatePose.Posed += OnBeforeRender;
        VRSession.Started += OnVRStarted;
        VRSession.Stopped += OnVRStopped;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        LatePose.Posed -= OnBeforeRender;
        VRSession.Started -= OnVRStarted;
        VRSession.Stopped -= OnVRStopped;
    }

    /// <summary>VR may come up while the boot splash is already playing.</summary>
    private void OnVRStarted() => EnterSplash();

    /// <summary>Drop every hold so a later VR restart does not come back to a black or loading headset.</summary>
    private void OnVRStopped()
    {
        StopAllCoroutines();
        loadRoutine = null;
        VRFade.Clear(loadOwner);
        VRFade.Clear(cutOwner);
        splash = false;
        space.Hide();
        space.Release();
        space.ClearDestination();
        destination.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stage.OnSceneLoaded();
        if (splash)
        {
            // Pretitle activates Title directly (Pretitle.cs:24); the cut-and-fade below covers the swap.
            splash = false;
            space.Hide();
        }
        EnterSplash();
        // Kick/disconnect paths load scenes directly, without a loading screen: go black, then fade in.
        if (VRSession.Active && !LoadingScreenHandler.loading && loadRoutine == null && !splash)
            StartCoroutine(FadeInAfterCut());
    }

    /// <summary>The boot splash (Pretitle scene, LandCrab logos) plays in the loading room: its camera draws
    /// flat-screen-sized particle bursts and post effects (LogoCombineSequence.cs:46) that look wrong in a
    /// headset, and its logos sit on a full-screen black card.</summary>
    private void EnterSplash()
    {
        if (!VRSession.Active || splash || !cfg.LoadingSpace.Value)
            return;
        pretitle = FindAnyObjectByType<Pretitle>();
        if (pretitle == null)
            return;
        splash = true;
        var cam = pretitle.GetComponentInChildren<Camera>(true);
        space.Show(cam != null ? cam : Camera.main);
        skipHold.Arm(SkipHeld());
        // The cue that the controllers do something here: a light tap on both, with the ring under the logos.
        Tap(0.15f, 0.04f);
    }

    /// <summary>Hold trigger or a face button to skip, as the game's skip keys do (Pretitle.Update sets the
    /// same flag, A/Pretitle.cs:61). Those keys are keyboard or gamepad only, unreachable from XR. A hold, not
    /// a press, so picking up a controller never skips.</summary>
    private void UpdateSplashSkip()
    {
        if (pretitle == null)
            return;
        if (skipHold.Update(Time.unscaledDeltaTime, SkipHeld()))
        {
            skipSplash(pretitle) = true;
            Tap(0.5f, 0.08f);
            Plugin.Log.LogInfo("[ui.menu] splash skipped from the controllers");
        }
        space.SetSkipProgress(skipHold.Progress);
    }

    private static bool SkipHeld() =>
        VRInput.Left.Trigger > 0.75f
        || VRInput.Right.Trigger > 0.75f
        || VRInput.Left.Primary
        || VRInput.Right.Primary
        || VRInput.Left.Secondary
        || VRInput.Right.Secondary;

    private static void Tap(float amplitude, float seconds)
    {
        // Follows the player's haptics setting (Interaction section); with it off the ring is the cue.
        Haptics.Pulse(true, amplitude, seconds);
        Haptics.Pulse(false, amplitude, seconds);
    }

    /// <summary>
    /// LoadingScreenHandler.LoadSceneProcess was built for <paramref name="sceneName"/>. Every client builds it:
    /// the load RPCs run on all (A/AirportCheckInKiosk.cs:151 BeginIslandLoadRPC, A/GameOverHandler.cs:44
    /// BeginAirportLoadRPC). Built before a load starts, it is held until the load really starts
    /// (<see cref="OnLoadingStart"/>); built inside a running load (solo play, A/MainMenu.cs:153) it applies at once,
    /// unless a Load call rejects it as "already loading" in the same frame (<see cref="OnLoadRejected"/>).
    /// </summary>
    public void OnLoadDestination(string sceneName)
    {
        if (
            VRSession.Active
            && destination.Built(sceneName, LoadingScreenHandler.loading, Time.frameCount)
        )
            space.SetDestination(destination.Current);
    }

    /// <summary>LoadingScreenHandler.Load refused to start (A/LoadingScreenHandler.cs:96, :109): the process
    /// built for it never runs.</summary>
    public void OnLoadRejected()
    {
        if (destination.Rejected(Time.frameCount))
            space.SetDestination(destination.Current);
    }

    private void LateUpdate()
    {
        FadeView.Tick();
        if (!VRSession.Active)
            return;
        stage.ApplyOrigin(VRRig.Instance);
        dashboardPause.Tick(cfg.PauseOnDashboard.Value);
        ModalBellPatch.Tick();
        if (splash)
            UpdateSplashSkip();
    }

    private void OnBeforeRender()
    {
        if (VRSession.Active)
            stage.ApplyOrigin(VRRig.Instance);
    }

    /// <summary>LoadingScreenHandler.OnLoadingStart (A/LoadingScreenHandler.cs:16, raised at :119).</summary>
    public void OnLoadingStart(string _)
    {
        if (!VRSession.Active)
            return;
        destination.Started();
        space.SetDestination(destination.Current);
        Restart(EnterLoading());
    }

    /// <summary>LoadingScreenHandler.OnLoadingDone (A/LoadingScreenHandler.cs:14, raised at :129).</summary>
    public void OnLoadingDone(string _)
    {
        if (!VRSession.Active && !space.Shown)
            return;
        Restart(ExitLoading());
    }

    /// <summary>A kick, ban or disconnect destroys the loading screen mid-load (KickedState.DisplayModal,
    /// A/KickedState.cs:13; NetworkConnector.cs:130,134). That kills its process coroutine, so the loading
    /// routine never ends and <see cref="OnLoadingDone"/> never fires: leave the loading space here.</summary>
    public void OnLoadingScreenKilled()
    {
        if (space.Shown || loadRoutine != null)
            Restart(ExitLoading());
    }

    private void Restart(IEnumerator routine)
    {
        if (loadRoutine != null)
            StopCoroutine(loadRoutine);
        loadRoutine = StartCoroutine(routine);
    }

    private IEnumerator EnterLoading()
    {
        VRFade.Set(loadOwner, 1f);
        yield return WaitForBlack();
        if (cfg.LoadingSpace.Value)
        {
            space.Show();
            VRFade.Clear(loadOwner);
        }
        loadRoutine = null;
    }

    private IEnumerator ExitLoading()
    {
        destination.Ended();
        space.ClearDestination();
        // Lying on the sand at run start (ui.runflow Fade mode) would be black: the room stays up instead.
        // ui.runflow ends BeachWaking within BeachWake.Budget; the deadline only guards a runtime gone missing.
        var until = Time.unscaledTime + BeachWake.Budget;
        while (space.Shown && RunFlowRuntime.BeachWaking && Time.unscaledTime < until)
            yield return null;
        VRFade.Set(loadOwner, 1f);
        yield return WaitForBlack();
        space.Hide();
        // Give the new scene's camera a frame to take over before revealing it.
        yield return null;
        yield return null;
        VRFade.Clear(loadOwner);
        loadRoutine = null;
    }

    private IEnumerator FadeInAfterCut()
    {
        FadeView.Snap(1f);
        VRFade.Set(cutOwner, 1f);
        yield return new WaitForSecondsRealtime(0.15f);
        VRFade.Clear(cutOwner);
    }

    private static IEnumerator WaitForBlack()
    {
        var until = Time.unscaledTime + FadeTimeout;
        while (FadeView.Alpha < 0.99f && Time.unscaledTime < until)
            yield return null;
    }
}
