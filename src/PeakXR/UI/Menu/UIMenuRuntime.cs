using System.Collections;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
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

    // Darker full-canvas fills on the title are ui.world's dimmers (MenuBackdrop); this takes only light ones.
    private const float LightFill = 0.35f;

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
    private readonly ScreenBacking titleCover = new();

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
        Application.onBeforeRender += OnBeforeRender;
        VRSession.Started += OnVRStarted;
        VRSession.Stopped += OnVRStopped;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.onBeforeRender -= OnBeforeRender;
        VRSession.Started -= OnVRStarted;
        VRSession.Stopped -= OnVRStopped;
    }

    /// <summary>VR may come up while the boot splash is already playing.</summary>
    private void OnVRStarted()
    {
        EnterSplash();
        HideTitleCover();
    }

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
        titleCover.Restore();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stage.OnSceneLoaded();
        if (splash)
        {
            // Pretitle activates Title directly (Pretitle.cs:22); the cut-and-fade below covers the swap.
            splash = false;
            space.Hide();
        }
        EnterSplash();
        HideTitleCover();
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
    /// same flag, A/Pretitle.cs:54). Those keys are keyboard or gamepad only, unreachable from XR. A hold, not
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

    /// <summary>The title's cream cover (MainPage/WhiteFade, shown while connecting and faded by the TitleIn
    /// animation) and the first-time setup page's cream page are opaque cards over the beach in VR.</summary>
    private void HideTitleCover()
    {
        if (!VRSession.Active)
            return;
        var menu = FindAnyObjectByType<MainMenuPageHandler>();
        var canvas = menu != null ? menu.GetComponentInChildren<Canvas>(true) : null;
        if (canvas == null && menu != null)
            canvas = menu.GetComponentInParent<Canvas>(true);
        if (canvas == null)
        {
            titleCover.Restore();
            return;
        }
        titleCover.Scan(canvas.rootCanvas, LightFill, null, null, "title");
    }

    /// <summary>LoadingScreenHandler.LoadSceneProcess was built for <paramref name="sceneName"/>.</summary>
    public void OnLoadDestination(string sceneName)
    {
        if (VRSession.Active)
            space.SetDestination(sceneName);
    }

    private void LateUpdate()
    {
        FadeView.Tick();
        if (!VRSession.Active)
            return;
        stage.ApplyOrigin(VRRig.Instance);
        titleCover.Reassert();
        if (splash)
            UpdateSplashSkip();
    }

    private void OnBeforeRender()
    {
        if (VRSession.Active)
            stage.ApplyOrigin(VRRig.Instance);
    }

    /// <summary>LoadingScreenHandler.OnLoadingStart (A/LoadingScreenHandler.cs:16, raised at :119).</summary>
    public void OnLoadingStart(string sceneName)
    {
        if (!VRSession.Active)
            return;
        Restart(EnterLoading());
    }

    /// <summary>LoadingScreenHandler.OnLoadingDone (A/LoadingScreenHandler.cs:14, raised at :129).</summary>
    public void OnLoadingDone(string sceneName)
    {
        if (!VRSession.Active && !space.Shown)
            return;
        Restart(ExitLoading());
    }

    /// <summary>A kick, ban or disconnect destroys the loading screen mid-load (KickedState.DisplayModal,
    /// A/KickedState.cs:13; NetworkConnector.cs:127,134). That kills its process coroutine, so the loading
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
        VRFade.Set(loadOwner, 1f);
        yield return WaitForBlack();
        space.Hide();
        space.SetDestination(null);
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
