using PeakXR.Avatar;
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
    internal const float MinDegreesPerPixel = 0.045f;

    /// <summary>Angular size of one HUD pixel for world-placed parts: the body panel's, or the floor above, grown by
    /// <c>UI.TextScale</c>. Every placed text and cue label sizes from it, so the setting applies live.</summary>
    public static float DegreesPerPixel { get; private set; } = MinDegreesPerPixel;

    /// <summary><c>UI.TextScale</c> as applied (<see cref="ReadableText.Scale"/>), for sizes set in degrees.</summary>
    public static float TextScale { get; private set; } = 1f;

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

    private void OnEnable() => LatePose.Posed += OnBeforeRender;

    private void OnDisable() => LatePose.Posed -= OnBeforeRender;

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
        // Your own body too (solo, or the last one standing): its "spectating YOURSELF" label goes on it. Only while
        // really spectating: a rocket ride also sets specCharacter to you (MainCameraMovement.cs:102).
        wantSpectate =
            ready
            && cfg.SpectateOnPlayer.Value
            && spec != null
            && c != null
            && (c.data.dead || c.data.fullyPassedOut);
        var friend = wantSpectate && spec != c;
        wantAlerts = ready && cfg.StatusAlerts.Value && observingSelf && c.data.fullyConscious;
        // Small status icons follow the status bar to the wrist; in Body mode they stay on the body panel.
        wantWrist = ready && UIWorldFeature.Settings.Hud.Value == HudMode.Wrist;
        // Your own status bar stays on your wrist; only a friend's goes on their nameplate.
        SpectateView.ClaimsStatus = friend;
        SpectateView.BeltShowing = friend;

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
            var hudMpp = PanelMath.MetersPerPixel(ui.HudWidthWorld, 1920f);
            TextScale = ReadableText.Scale(ui.TextScale.Value);
            DegreesPerPixel = ReadableText.Scaled(
                SpatialMath.DegreesPerPixel(hudMpp, ui.HudDistanceWorld),
                MinDegreesPerPixel,
                TextScale
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

    /// <summary>Like <see cref="FaceEye(Vector3, Vector3)"/>, for text read from any angle (a target prompt, a refusal
    /// label): full look-at the eye so looking down never squashes it, roll fixed by world up, or by
    /// <paramref name="headUp"/> when seen straight from above or below (<see cref="SpatialMath.FaceUp"/>).</summary>
    public static Quaternion FaceEye(Vector3 position, Vector3 eye, Vector3 headUp)
    {
        var to = position - eye;
        return to.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(to, SpatialMath.FaceUp(to.N(), headUp.N()).U())
            : Quaternion.identity;
    }

    /// <summary>Like <see cref="FaceEye(Vector3, Vector3)"/> but turning on yaw only, so text at a target stays level.</summary>
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
