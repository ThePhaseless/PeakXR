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
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.onBeforeRender -= OnBeforeRender;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stage.OnSceneLoaded();
        // Kick/disconnect paths load scenes directly, without a loading screen: go black, then fade in.
        if (VRSession.Active && !LoadingScreenHandler.loading && loadRoutine == null)
            StartCoroutine(FadeInAfterCut());
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
