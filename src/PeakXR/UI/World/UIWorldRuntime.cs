using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.UI.World;

/// <summary>
/// Per-frame driver for world-space UI. Runs late (after the rig is posed) and does nothing until VR is
/// rendering and a <see cref="VRRig"/> exists; when either goes away every canvas is put back.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class UIWorldRuntime : MonoBehaviour
{
    // Safety net only: the CanvasScaler/MenuWindow hooks and the scene-load scan catch new canvases.
    private const float RescanSeconds = 10f;

    public static bool Ready { get; private set; }

    private static readonly Transition[] noTransitions = System.Array.Empty<Transition>();
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int InId = Shader.PropertyToID("_In");

    private readonly HudLayout hud = new();
    private readonly HudSplit split = new();
    private readonly BackdropProbe backdrop = new();
    private readonly object fadeOwner = new();
    private LaserPointer pointer;
    private FadeSphere sphere;
    private VRRig sphereRig;
    private float rescanAt;
    private bool relayVRTab;

    private Transitions transitions;
    private Transition[] transitionList = noTransitions;
    private CanvasGroup[] transitionGroups = System.Array.Empty<CanvasGroup>();
    private MeshRenderer[] transitionRenderers = System.Array.Empty<MeshRenderer>();

    public static void Create()
    {
        var go = new GameObject("PeakXR_WorldUI");
        DontDestroyOnLoad(go);
        go.AddComponent<UIWorldRuntime>();
    }

    private void Awake()
    {
        pointer = new LaserPointer(transform);
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.onBeforeRender += OnBeforeRender;
        WorldCanvases.Scan(includeInactive: true);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.onBeforeRender -= OnBeforeRender;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        WorldCanvases.OnSceneLoaded();
        WorldCanvases.Scan(includeInactive: true);
    }

    private void LateUpdate()
    {
        var cfg = UIWorldFeature.Settings;
        FadeView.Seconds = cfg.FadeSeconds.Value;
        FadeView.Tick();
        WorldCanvases.ProcessPending();

        var rig = VRRig.Instance;
        var ready = VRSession.Active && rig != null && rig.Head != null;
        if (ready != Ready)
        {
            Ready = ready;
            if (ready)
            {
                WorldCanvases.ConvertAll();
                // Laid out for the headset once the menu stands in the world: the grid sizes rows in metres.
                relayVRTab = true;
            }
            else
            {
                split.Restore();
                backdrop.Release();
                // Every hand UI is off VR now (or turns off this frame and rebuilds without them).
                OnTop.DestroyClones();
                WorldCanvases.RevertAll();
                pointer.Hide();
                VRFade.Clear(fadeOwner);
                // The game's shader transitions draw on the desktop again.
                foreach (var renderer in transitionRenderers)
                    if (renderer != null)
                        renderer.forceRenderingOff = false;
                // Back to the flat paged column; a rig lost while VR runs keeps the grid until it returns.
                if (!VRSession.Active)
                    Settings.VRTab.RelayIfShown();
            }
        }
        if (!Ready)
            return;

        if (sphere == null || sphereRig != rig)
        {
            if (sphere != null)
                Destroy(sphere.gameObject);
            sphere = FadeSphere.Attach(rig.Head);
            sphereRig = rig;
        }

        if (Time.unscaledTime >= rescanAt)
        {
            rescanAt = Time.unscaledTime + RescanSeconds;
            WorldCanvases.Scan(includeInactive: false);
        }

        var dt = Time.unscaledDeltaTime;
        hud.Update(rig.Head, cfg, dt);
        var interactive = WorldCanvases.Update(rig.Head, hud, cfg);
        if (relayVRTab)
        {
            relayVRTab = false;
            Settings.VRTab.RelayIfShown();
        }
        backdrop.Tick(rig, cfg, dt);
        split.Sync(cfg, rig);
        split.PoseWrist(rig, cfg);
        split.PoseReticle(rig.Head, hud, cfg);
        pointer.Process(interactive || LaserPointer.ForceShow, cfg, rig, dt);
        MirrorTransitions();
    }

    private void OnBeforeRender()
    {
        if (!Ready)
            return;
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
            return;
        split.PoseReticle(rig.Head, hud, UIWorldFeature.Settings);
        StatusOverlays.Repose(rig);
    }

    /// <summary>The game's screen transitions (A/Transitions.cs) stay a desktop overlay; their opacity
    /// becomes a head-locked VR fade instead.</summary>
    private void MirrorTransitions()
    {
        var current = Transitions.instance;
        if (current != transitions)
        {
            transitions = current;
            transitionList =
                current != null ? current.GetComponentsInChildren<Transition>(true) : noTransitions;
            transitionGroups = new CanvasGroup[transitionList.Length];
            transitionRenderers = new MeshRenderer[transitionList.Length];
            for (int i = 0; i < transitionList.Length; i++)
            {
                transitionGroups[i] = transitionList[i].GetComponent<CanvasGroup>();
                transitionRenderers[i] = transitionList[i].GetComponent<MeshRenderer>();
            }
        }

        var alpha = 0f;
        for (int i = 0; i < transitionList.Length; i++)
        {
            var transition = transitionList[i];
            if (transition == null || !transition.gameObject.activeInHierarchy)
                continue;
            if (transitionGroups[i] != null)
                alpha = Mathf.Max(alpha, transitionGroups[i].alpha);
            else if (transition is Transition_Shader shader && transitionRenderers[i] != null)
            {
                var renderer = transitionRenderers[i];
                renderer.forceRenderingOff = true;
                var material = renderer.sharedMaterial;
                var progress = material.GetFloat(ProgressId);
                alpha = Mathf.Max(
                    alpha,
                    material.GetInt(InId) == 1
                        ? shader.inCurve.Evaluate(progress)
                        : shader.outCurve.Evaluate(progress)
                );
            }
        }

        if (alpha > 0.001f)
            VRFade.Set(fadeOwner, Mathf.Clamp01(alpha));
        else
            VRFade.Clear(fadeOwner);
    }
}
