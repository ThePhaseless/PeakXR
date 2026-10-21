using PeakXR.Core;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// Drives the ui.spatial placements. Parts that are no longer wanted are put back in <c>Update</c>, before ui.world's
/// late pass re-lays the HUD (it takes the status bar back for the wrist); parts are taken and posed late, after the
/// HUD canvas has been placed, and posed again right before rendering for the ones that follow hands or the head.
/// </summary>
[DefaultExecutionOrder(31200)]
internal sealed class SpatialRuntime : MonoBehaviour
{
    /// <summary>Never smaller than this per canvas pixel: 30 px HUD text stays about 1.35 degrees tall.</summary>
    private const float MinDegreesPerPixel = 0.045f;

    /// <summary>Angular size of one HUD pixel for world-placed parts: the body panel's, or the floor above.</summary>
    public static float DegreesPerPixel { get; private set; } = MinDegreesPerPixel;

    private readonly TargetPrompt target = new();
    private readonly ButtonLabels labels = new();
    private readonly WorldBanners banners = new();
    private readonly HeldGauges gauges = new();
    private readonly SpectateView spectate = new();
    private readonly StatusAlerts alerts = new();
    private readonly WristExtras wristExtras = new();
    private GUIManager gui;
    private bool ready,
        wantTarget,
        wantLabels,
        wantBanners,
        wantGauges,
        wantSpectate,
        wantAlerts,
        wantWrist;

    public static void Create()
    {
        var go = new GameObject("PeakXR_SpatialUI");
        DontDestroyOnLoad(go);
        go.AddComponent<SpatialRuntime>();
    }

    private void OnEnable() => Application.onBeforeRender += OnBeforeRender;

    private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

    private void OnDestroy() => ReleaseAll();

    private void Update()
    {
        var current = GUIManager.instance;
        if (current != gui)
        {
            // The old HUD died with its scene, and every moved part with it.
            target.Forget();
            labels.Forget();
            banners.Forget();
            gauges.Forget();
            spectate.Forget();
            wristExtras.Forget();
            SpatialBox.PruneMaterials();
            gui = current;
        }

        var cfg = SpatialFeature.Settings;
        var rig = VRRig.Instance;
        ready =
            UIWorldRuntime.Ready
            && rig != null
            && rig.Head != null
            && gui != null
            && gui.hudCanvas != null;
        var c = Character.localCharacter;
        var observingSelf = c != null && Character.observedCharacter == c;
        var spec = MainCameraMovement.specCharacter;

        wantTarget = ready && cfg.TargetPrompt.Value && c != null;
        wantLabels = ready && cfg.ButtonLabels.Value && observingSelf;
        wantBanners = ready && cfg.WorldBanners.Value;
        wantGauges = ready && cfg.HeldItemGauges.Value && observingSelf;
        wantSpectate = ready && cfg.SpectateOnPlayer.Value && spec != null && spec != c;
        wantAlerts = ready && cfg.StatusAlerts.Value && observingSelf && c.data.fullyConscious;
        // Small status icons follow the status bar to the wrist; in Body mode they stay on the body panel.
        wantWrist = ready && UIWorldFeature.Settings.Hud.Value == HudMode.Wrist;
        SpectateView.ClaimsStatus = wantSpectate;
        SpectateView.BeltShowing = wantSpectate;

        if (!wantTarget)
            target.Release();
        if (!wantLabels)
            labels.Release();
        if (!wantBanners)
            banners.Release();
        if (!wantGauges)
            gauges.Release();
        if (!wantSpectate)
            spectate.Release();
        if (!wantAlerts)
            alerts.Release();
        if (!wantWrist)
            wristExtras.Release();

        if (ready)
        {
            var ui = UIWorldFeature.Settings;
            var hudMpp = PanelMath.MetersPerPixel(ui.HudWidth.Value, 1920f);
            DegreesPerPixel = Mathf.Max(
                MinDegreesPerPixel,
                SpatialMath.DegreesPerPixel(hudMpp, ui.HudDistance.Value)
            );
        }
    }

    private void LateUpdate()
    {
        // ui.world may have just put the canvases back (VR stopped this frame): let go now, not a frame later.
        if (ready && !UIWorldRuntime.Ready)
        {
            ready = false;
            ReleaseAll();
        }
        if (!ready || gui == null)
            return;
        var rig = VRRig.Instance;
        var hudRoot = (RectTransform)gui.hudCanvas.transform;
        var dt = Time.unscaledDeltaTime;
        if (wantTarget)
            target.Tick(gui, hudRoot, rig);
        if (wantLabels)
            labels.Tick(gui, hudRoot, rig, dt);
        if (wantBanners)
            banners.Tick(gui, hudRoot, rig);
        if (wantGauges)
            gauges.Tick(gui, hudRoot, rig);
        if (wantSpectate)
            spectate.Tick(gui, hudRoot, rig);
        if (wantAlerts)
            alerts.Tick(Character.localCharacter, dt);
        if (wantWrist)
            wristExtras.Tick(gui, hudRoot, rig);
    }

    private void OnBeforeRender()
    {
        if (!ready || !UIWorldRuntime.Ready)
            return;
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
            return;
        if (wantTarget)
            target.Pose(rig);
        if (wantLabels)
            labels.Pose(rig);
        if (wantGauges)
            gauges.Pose(rig);
        if (wantSpectate)
            spectate.Pose(rig);
        if (wantWrist)
            wristExtras.Pose(rig);
    }

    private void ReleaseAll()
    {
        target.Release();
        labels.Release();
        banners.Release();
        gauges.Release();
        spectate.Release();
        alerts.Release();
        wristExtras.Release();
        SpectateView.ClaimsStatus = false;
        SpectateView.BeltShowing = false;
    }

    /// <summary>Rotation that shows a canvas's front to the eye, upright.</summary>
    public static Quaternion FaceEye(Vector3 position, Vector3 eye)
    {
        var to = position - eye;
        return to.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(to, Vector3.up)
            : Quaternion.identity;
    }

    /// <summary>Like <see cref="FaceEye"/> but turning on yaw only, so text at a target stays level.</summary>
    public static Quaternion FaceEyeYaw(Vector3 position, Vector3 eye, Vector3 fallbackForward)
    {
        var to = position - eye;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-6f)
        {
            to = fallbackForward;
            to.y = 0f;
        }
        return to.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(to, Vector3.up)
            : Quaternion.identity;
    }
}
