using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using PeakXR.Avatar;
using PeakXR.Comfort;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Common;
using PeakXR.UI.Menu;
using PeakXR.UI.World;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Zorro.Core;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>vr.*</c> steps of the self-test, run only when a VR runtime was requested. Nobody can look into the
/// headset, so each step proves a VR path from state the mod exposes (rig, canvases, comfort outputs) and leaves a
/// screenshot of the desktop mirror. If VR never starts, <c>vr.00-session</c> fails, the later VR steps are SKIP and
/// the script still gives full flat coverage.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The comfort vignette's object under the rig head (VignetteMesh.Attach, Locomotion/VignetteMesh.cs).</summary>
    const string VignetteName = "PeakXR.ComfortVignette";

    /// <summary>Just over comfort.events' knockback threshold (50, ComfortEventsPatches.ForceAtPosition): a
    /// mild shove, so the speed-based launch detection does not hold the vignette as well.</summary>
    const float KnockbackForce = 60f;

    static readonly List<InputDevice> xrDevices = new();

    // Static: one runner per process, and CA1001 would want a MonoBehaviour to be IDisposable.
    static readonly BootstrapTail bootstrapTail = new();

    bool loadWatching;
    bool loadSeen;
    bool loadShot;
    int loadShownFrames;
    int loadCulledFrames;
    int loadOpenFrames;
    string loadError;

    /// <summary>A VR runtime was requested and XR may start (not the compat probe).</summary>
    static bool VRRun => VRSession.Requested != VRRuntime.Disabled && !VRSession.CompatProbe;

    IEnumerator VRStep(string id, float timeout, IEnumerator body)
    {
        if (!VRRun)
            yield break;
        if (!VRSession.Active)
        {
            Skip(id, "VR not active");
            yield break;
        }
        yield return Run(id, timeout, body);
    }

    /// <summary>vr.00: XR display running; what xr.bootstrap logs at activation (XRBootstrapDriver.Activate).</summary>
    IEnumerator Session()
    {
        float end = Now + 58f;
        while (!VRSession.Active)
        {
            if (Now > end)
            {
                Fail(
                    $"VR not active after 58 s (running {VRSession.Running}); last xr.bootstrap line: "
                        + (bootstrapTail.Last ?? "none")
                );
                yield break;
            }
            yield return null;
        }
        InputDevices.GetDevices(xrDevices);
        var names = new StringBuilder();
        foreach (var d in xrDevices)
            names.Append(names.Length > 0 ? ", '" : "'").Append(d.name).Append('\'');
        stepNote =
            $"{VRSession.Running}, eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, "
            + $"stereo mode {XRSettings.stereoRenderingMode}, {xrDevices.Count} device(s): {names}";
    }

    /// <summary>vr.01: rig, tracked head, stereo game camera, listener at the head (Rig/RigDriver.cs BindMainCamera).</summary>
    IEnumerator RigCheck()
    {
        yield return WaitFor(
            () =>
                VRRig.Instance != null
                && VRRig.Instance.Head != null
                && MainCamera.instance != null,
            10f
        );
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
        {
            Fail("no VRRig.Instance with a Head");
            yield break;
        }
        var p0 = rig.Head.localPosition;
        var r0 = rig.Head.localRotation;
        yield return Wait(1f);
        var p1 = rig.Head.localPosition;
        var r1 = rig.Head.localRotation;
        bool moving = (p1 - p0).sqrMagnitude > 1e-8f || Quaternion.Angle(r0, r1) > 0.01f;
        float norm = r1.x * r1.x + r1.y * r1.y + r1.z * r1.z + r1.w * r1.w;
        bool valid =
            !float.IsNaN(p1.x + p1.y + p1.z)
            && !float.IsInfinity(p1.x + p1.y + p1.z)
            && Mathf.Abs(norm - 1f) < 0.01f;

        var fails = new List<string>();
        var notes = new List<string> { $"head {(moving ? "moving" : "static")} at {p1}" };
        if (!VRInput.HeadTracked)
            fails.Add("head not tracked");
        if (!valid)
            fails.Add("head pose invalid");

        var main = MainCamera.instance;
        var cam = main != null ? main.GetComponent<Camera>() : null;
        if (cam == null)
            fails.Add("no MainCamera.instance camera");
        else
        {
            notes.Add(
                $"camera '{cam.name}' stereoEnabled {cam.stereoEnabled} targetEye {cam.stereoTargetEye}"
                    + (rig.HeadCamera == cam ? "" : $" (rig renders '{Name(rig.HeadCamera)}')")
            );
            if (!cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.Both)
                fails.Add("game camera not stereo");
        }

        var listener = NearestListener(rig.Head.position, out float listenerDistance);
        notes.Add(
            listener != null
                ? $"listener '{listener.name}' {listenerDistance:0.00} m from the head"
                : "no enabled AudioListener"
        );
        if (listener == null || listenerDistance > 0.5f)
            fails.Add("no AudioListener at the head");
        Conclude(fails, notes);
    }

    /// <summary>vr.02: title menu converted by ui.world and the title stage holding the rig origin (UI/Menu/TitleStage.cs).</summary>
    IEnumerator TitleStageCheck()
    {
        bool onBeach = Setting("UI", "TitleOnBeach", true);
        Canvas root = null;
        yield return WaitFor(
            () =>
            {
                root = RootCanvasOf(FindAnyObjectByType<MainMenuPageHandler>());
                return root != null
                    && root.renderMode == RenderMode.WorldSpace
                    && (!onBeach || (VRRig.Instance != null && VRRig.Instance.OriginOverride));
            },
            10f
        );
        var fails = new List<string>();
        var notes = new List<string>();
        if (root == null)
            fails.Add("no MainMenuPageHandler canvas");
        else
        {
            notes.Add(
                $"canvas '{root.name}' {root.renderMode}, converted {WorldCanvases.IsConverted(root)}"
            );
            if (root.renderMode != RenderMode.WorldSpace)
                fails.Add("title menu not world space");
        }
        bool held = VRRig.Instance != null && VRRig.Instance.OriginOverride;
        notes.Add($"OriginOverride {held}, UI.TitleOnBeach {onBeach}");
        if (onBeach && !held)
            fails.Add("title stage does not hold the rig origin");
        var stage = TitleStage.Instance;
        if (onBeach && stage != null && stage.Active)
        {
            // The title's button groups come to arm's reach (UI/Menu/TitleMenuDepth.cs), measured from the stage eye.
            float reach = Plugin.Settings.TryGetEntry<float>("UI", "TitleReach", out var r)
                ? r.Value
                : 0.55f;
            yield return WaitFor(() => stage.Depth.NearGroups > 0, 5f);
            float nearest = stage.Depth.NearestButton(stage.Eye);
            notes.Add(
                $"{stage.Depth.NearGroups} button groups near, nearest button {nearest:0.00} m (reach {reach:0.00})"
            );
            if (stage.Depth.NearGroups == 0)
                fails.Add("no title button group brought to reach");
            else if (nearest > reach + 0.25f)
                fails.Add(
                    $"nearest title button {nearest:0.00} m from the eye, reach is {reach:0.00}"
                );
        }
        Conclude(fails, notes);
        yield return Shot("vr-02-title");
    }

    /// <summary>
    /// vr.03: every active root canvas that ui.world converts (WorldCanvases.ShouldConvert, minus the Hidden and
    /// Fade kinds of <see cref="CanvasRules.Classify"/>) is world space. Waits past ui.world's 10 s rescan.
    /// </summary>
    IEnumerator CanvasCheck()
    {
        var offenders = new List<string>();
        float end = Now + 12f;
        string counts;
        while (true)
        {
            counts = AuditCanvases(offenders);
            if (offenders.Count == 0 || Now > end)
                break;
            yield return Wait(0.5f);
        }
        if (offenders.Count > 0)
            Fail("not world space: " + string.Join(", ", offenders) + "; " + counts);
        else
            stepNote = counts;
        yield return Shot("vr-03-airport");
    }

    static string AuditCanvases(List<string> offenders)
    {
        offenders.Clear();
        int world = 0,
            overlay = 0,
            camera = 0,
            exempt = 0;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled)
                continue;
            switch (canvas.renderMode)
            {
                case RenderMode.WorldSpace:
                    world++;
                    continue;
                case RenderMode.ScreenSpaceCamera:
                    camera++;
                    // Drawn into a render texture: already 3D, ui.world leaves it.
                    if (canvas.worldCamera != null && canvas.worldCamera.targetTexture != null)
                        continue;
                    break;
                default:
                    overlay++;
                    break;
            }
            var kind = CanvasRules.Classify(
                canvas,
                canvas.GetComponentsInChildren<Selectable>(true).Length > 0,
                canvas.GetComponentInChildren<MenuWindow>(true) != null
                    || canvas.GetComponentInParent<MenuWindow>(true) != null,
                out _
            );
            if (kind is CanvasKind.Hidden or CanvasKind.Fade)
                exempt++;
            else
                offenders.Add($"'{canvas.name}' ({canvas.renderMode}, {kind})");
        }
        return $"{world} world, {overlay} overlay, {camera} screen-camera, {exempt} left on screen by rule";
    }

    /// <summary>vr.04: the pause menu with the VR tab is a world panel in front of the head (ui.world PlaceMenu), with
    /// its flat-screen backdrops hidden (BackdropStrip).</summary>
    IEnumerator SettingsPanel()
    {
        yield return OpenVRTab();
        if (!stepOk)
            yield break;
        var root = RootCanvasOf(FindAnyObjectByType<PauseMenuHandler>());
        var rig = VRRig.Instance;
        if (root == null || rig == null || rig.Head == null)
            Fail("no pause menu canvas or rig head");
        else
        {
            var head = rig.Head;
            bool front = SelfTestChecks.InFront(
                head.position.ToNumerics(),
                head.forward.ToNumerics(),
                root.transform.position.ToNumerics(),
                out float distance,
                out float angle
            );
            // The pause menu's full-canvas BG dimmer is a backdrop whatever UI.MenuPanels says.
            int hidden = WorldCanvases.HiddenBackdrops(root, out int unraycastable);
            var note =
                $"canvas '{root.name}' {root.renderMode}, {distance:0.00} m, {angle:0} deg off the view, "
                + $"{hidden} backdrops hidden, {unraycastable} of them dropped from raycasts";
            if (root.renderMode != RenderMode.WorldSpace)
                Fail("not world space: " + note);
            else if (!front)
                Fail("not in front of the head: " + note);
            else if (hidden == 0)
                Fail("flat-screen backdrop left on the menu: " + note);
            // GraphicRaycaster skips depth -1 or culled graphics: a hidden dimmer must still block clicks behind it.
            else if (unraycastable > 0)
                Fail("hidden backdrop no longer takes raycasts: " + note);
            else
                stepNote = note;
        }
        yield return root != null
            ? ShotAt("vr-04-settings", CentreOf(root.transform))
            : Shot("vr-04-settings");
        ClosePause();
        yield return Until(() => !GUIManager.InPauseMenu, 3f, "pause menu did not close");
    }

    /// <summary>Samples the next level load in the background for vr.05; its own coroutine, so 04-beach is untouched.</summary>
    void StartLoadWatch()
    {
        if (!VRRun || !VRSession.Active)
            return;
        loadWatching = true;
        StartCoroutine(
            Guard(
                WatchLoading(),
                Now + 300f,
                e => loadError = e == null ? "timeout" : $"{e.GetType().Name}: {e.Message}"
            )
        );
    }

    /// <summary>While <c>LoadingScreenHandler.loading</c>: is ui.menu's loading space shown, and is the game
    /// camera culled to nothing under it (LoadingSpace.HideGameCamera)?</summary>
    IEnumerator WatchLoading()
    {
        float end = Now + 30f;
        while (!LoadingScreenHandler.loading)
        {
            if (!loadWatching || Now > end)
                yield break;
            yield return null;
        }
        loadSeen = true;
        var space = FindAnyObjectByType<LoadingSpace>();
        float shownAt = -1f;
        while (LoadingScreenHandler.loading && loadWatching)
        {
            if (space != null && space.Shown)
            {
                loadShownFrames++;
                if (shownAt < 0f)
                    shownAt = Now;
                var main = MainCamera.instance;
                var cam = main != null ? main.GetComponent<Camera>() : null;
                if (cam != null)
                {
                    if (cam.cullingMask == 0)
                        loadCulledFrames++;
                    else
                        loadOpenFrames++;
                }
                if (!loadShot && Now - shownAt > 1f)
                {
                    loadShot = true;
                    yield return Shot("vr-05-loading");
                    continue;
                }
            }
            yield return null;
        }
    }

    /// <summary>vr.05: what <see cref="WatchLoading"/> saw during 04-beach.</summary>
    IEnumerator LoadingCheck()
    {
        loadWatching = false;
        var note =
            $"loading space shown {loadShownFrames} frames, game camera mask 0 in {loadCulledFrames}, "
            + $"not 0 in {loadOpenFrames}, screenshot {loadShot}";
        if (loadError != null)
            Fail($"watch {loadError}; {note}");
        else if (!loadSeen)
            Fail("no level load seen");
        else if (!Setting("UI.Loading", "LoadingSpace", true))
            stepNote = "UI.Loading.LoadingSpace is off; " + note;
        else if (loadShownFrames == 0)
            Fail("loading space never shown while loading");
        else if (loadCulledFrames == 0 && loadOpenFrames > 0)
            Fail("game camera kept its cullingMask: " + note);
        else
            stepNote = note;
        yield break;
    }

    /// <summary>vr.06: after the beach load, every VR feature active, own avatar posed, HUD in the world, no stuck fade.</summary>
    IEnumerator FeaturesCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var inactive = new List<string>();
        int vrFeatures = 0;
        foreach (var f in FeatureManager.Features)
        {
            if (!f.VROnly)
                continue;
            vrFeatures++;
            if (f.State != FeatureState.Active)
                inactive.Add($"{f.Id}={f.State}");
        }
        notes.Add($"{vrFeatures - inactive.Count}/{vrFeatures} VR features active");
        if (inactive.Count > 0)
            fails.Add("not active: " + string.Join(", ", inactive));

        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null)
            fails.Add("no AvatarPoseDriver on the local character");
        else
        {
            var pose = driver.Pose;
            notes.Add(
                $"pose {Time.time - driver.LastPoseTime:0.00} s old, head {pose.HasHead}, hands {pose.HasLeftHand}/{pose.HasRightHand}"
            );
            if (!driver.HasFreshPose)
                fails.Add("avatar pose stale");
        }

        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;
        if (hud == null)
            fails.Add("no HUD canvas");
        else
        {
            notes.Add("HUD " + hud.renderMode);
            if (hud.renderMode != RenderMode.WorldSpace)
                fails.Add("HUD not world space");
        }

        var vignette = Vignette();
        notes.Add(
            vignette != null
                ? $"vignette present, shown {vignette.gameObject.activeSelf}"
                : "vignette not built yet (built on first show)"
        );

        yield return WaitFor(() => VRFade.Target <= 0f, 5f);
        notes.Add($"fade target {VRFade.Target:0.00}, drawn {FadeView.Alpha:0.00}");
        if (VRFade.Target > 0f)
            fails.Add("fade stuck");
        Conclude(fails, notes);
    }

    /// <summary>
    /// vr.07: a knockback through the game's RPC (<c>Character.RPCA_AddForceAtPosition</c>, decomp
    /// Assembly-CSharp/Character.cs:461, sent the way <c>AddForceAtPosition</c> :455 does) pulses comfort.events'
    /// vignette, which is open again within 2 s; then a warp to spawn (<c>Character.WarpToSpawn</c>) leaves <see cref="VRFade.Target"/> at 0.
    /// </summary>
    IEnumerator ComfortCheck()
    {
        Recover();
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (c == null || rig == null || rig.Head == null)
        {
            Fail("no character or rig");
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        yield return WaitFor(() => ComfortEventsRuntime.VignetteBoost <= 0f, 5f);

        var back = -Vector3.ProjectOnPlane(rig.Head.forward, Vector3.up).normalized;
        if (back.sqrMagnitude < 0.5f)
            back = Vector3.back;
        c.photonView.RPC(
            "RPCA_AddForceAtPosition",
            RpcTarget.All,
            back * KnockbackForce,
            c.Center,
            1f
        );
        var pulse = new PulseWatch();
        bool meshShown = false;
        bool shot = false;
        float prevBoost = 0f;
        float start = Now;
        while (Now - start < 3f)
        {
            float boost = ComfortEventsRuntime.VignetteBoost;
            pulse.Sample(boost, Now - start);
            var mesh = Vignette();
            bool ringUp = mesh != null && mesh.gameObject.activeInHierarchy;
            meshShown |= ringUp;
            // At the pulse's peak with the ring drawn: the first rising frame showed hardly any ring.
            if (pulse.Rose && ringUp && boost < prevBoost && !shot)
            {
                shot = true;
                yield return Shot("vr-07-knockback");
                continue;
            }
            prevBoost = boost;
            yield return null;
        }
        if (!shot)
            yield return Shot("vr-07-knockback");
        notes.Add(
            $"knockback pulse peak {pulse.Peak:0.00}, open after {pulse.SettleSeconds:0.00} s, ring shown {meshShown}"
        );
        if (!pulse.Rose)
            fails.Add("no vignette pulse");
        else if (!pulse.Settled || pulse.SettleSeconds > 2f)
            fails.Add("vignette not open within 2 s");
        else if (!meshShown)
            fails.Add("vignette ring not shown");

        // A teleport inside the segment: a segment jump here would leave the later segment loop revisiting
        // segments, which throws in vanilla SingleItemSpawner.TrySpawnItems.
        if (!SandboxActions.WarpToSpawn())
            fails.Add(SandboxActions.LastMessage);
        else
        {
            var fade = new PulseWatch();
            float warpAt = Now;
            while (Now - warpAt < 3f)
            {
                fade.Sample(VRFade.Target, Now - warpAt);
                yield return null;
            }
            float settleAt = Now;
            while (VRFade.Target > 0f && Now - settleAt < 10f)
            {
                fade.Sample(VRFade.Target, Now - warpAt);
                yield return null;
            }
            notes.Add(
                $"warp to spawn: fade peak {fade.Peak:0.00}, target {VRFade.Target:0.00} {Now - settleAt:0.0} s after"
            );
            if (VRFade.Target > 0f)
                fails.Add("VRFade.Target stuck after the warp");
            Recover();
        }
        Conclude(fails, notes);
    }

    // ---------------------------------------------------------------- VR helpers

    void Conclude(List<string> fails, List<string> notes)
    {
        var all = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + all);
        else
            stepNote = all;
    }

    /// <summary>Waits for a condition without failing the step; the caller judges the outcome.</summary>
    static IEnumerator WaitFor(Func<bool> condition, float seconds)
    {
        float end = Now + seconds;
        while (!condition() && Now <= end)
            yield return null;
    }

    static bool Setting(string section, string key, bool fallback) =>
        Plugin.Settings.TryGetEntry<bool>(section, key, out var entry) ? entry.Value : fallback;

    static Canvas RootCanvasOf(Component c)
    {
        if (c == null)
            return null;
        var canvas = c.GetComponentInParent<Canvas>(true);
        if (canvas == null)
            canvas = c.GetComponentInChildren<Canvas>(true);
        return canvas != null ? canvas.rootCanvas : null;
    }

    static Transform Vignette()
    {
        var rig = VRRig.Instance;
        return rig != null && rig.Head != null ? rig.Head.Find(VignetteName) : null;
    }

    static AudioListener NearestListener(Vector3 head, out float distance)
    {
        AudioListener best = null;
        distance = float.PositiveInfinity;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (!l.isActiveAndEnabled)
                continue;
            float d = Vector3.Distance(l.transform.position, head);
            if (d < distance)
            {
                distance = d;
                best = l;
            }
        }
        return best;
    }

    static string Name(UnityEngine.Object o) => o != null ? o.name : "none";

    /// <summary>Keeps the last <c>[xr.bootstrap]</c> log line, so a VR start failure names the bootstrap's state.
    /// BepInEx log lines do not reach Unity's <c>Application.logMessageReceived</c>.</summary>
    sealed class BootstrapTail : ILogListener
    {
        volatile string last;

        public string Last => last;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (
                eventArgs.Data is string text
                && text.StartsWith("[xr.bootstrap]", StringComparison.Ordinal)
            )
                last = text;
        }

        public void Dispose() { }
    }
}
