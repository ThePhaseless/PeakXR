using System.Collections;
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

    private readonly object loadOwner = new();
    private readonly object cutOwner = new();
    private TitleStage stage;
    private LoadingSpace space;
    private UIMenuSettings cfg;
    private Coroutine loadRoutine;
    private bool splash;

    public static UIMenuRuntime Create(TitleStage stage, LoadingSpace space, UIMenuSettings cfg)
    {
        var go = new GameObject("PeakXR_MenuUI");
        DontDestroyOnLoad(go);
        var runtime = go.AddComponent<UIMenuRuntime>();
        runtime.stage = stage;
        runtime.space = space;
        runtime.cfg = cfg;
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
        stage.RestoreBacking();
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
        var pretitle = FindAnyObjectByType<Pretitle>();
        if (pretitle == null)
            return;
        splash = true;
        var cam = pretitle.GetComponentInChildren<Camera>(true);
        space.Show(cam != null ? cam : Camera.main);
    }

    private void LateUpdate()
    {
        FadeView.Tick();
        if (VRSession.Active)
            stage.ApplyOrigin(VRRig.Instance);
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
