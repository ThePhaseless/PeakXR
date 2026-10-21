using System;
using System.Collections;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.Menu;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakXR.UI.RunFlow;

/// <summary>
/// Drives every scripted run transition as fade-out, stable black hold, fade-in (one
/// <see cref="RunFlowFade"/> per transition kind, each its own <see cref="VRFade"/> owner), the
/// summit portal fade ramp, shake-replacing controller rumble and the "any button" check for the
/// credits. Patches only report events here; all timing lives in this object.
/// </summary>
internal sealed class RunFlowRuntime : MonoBehaviour
{
    private const float MinWarpDistance = 1.5f;
    private const float SegmentArmTimeout = 60f;
    private const float RumblePulseInterval = 0.1f;
    private const float PortalRumble = 0.4f;

    /// <summary>One transition kind; the instance itself is the <see cref="VRFade"/> owner.</summary>
    private sealed class Channel
    {
        public readonly string Name;
        public readonly RunFlowFade Fade;
        public readonly Func<bool> Busy;
        public bool LoggedTimeout;

        public Channel(string name, RunFlowFade fade, Func<bool> busy)
        {
            Name = name;
            Fade = fade;
            Busy = busy;
        }
    }

    private static RunFlowRuntime instance;

    private readonly object portalOwner = new();
    private readonly RunFlowRumble rumble = new();
    private readonly AnalogButton leftTrigger = new();
    private readonly AnalogButton rightTrigger = new();
    private readonly ButtonEdge leftPrimary = new();
    private readonly ButtonEdge rightPrimary = new();
    private readonly ButtonEdge leftSecondary = new();
    private readonly ButtonEdge rightSecondary = new();

    private RunFlowSettings cfg;
    private Channel warp;
    private Channel segment;
    private Channel runEnd;
    private Channel credits;
    private Channel beach;
    private Channel[] channels;
    private CinemaScreen cinema;
    private bool cinemaRunning;

    private bool segmentArmed;
    private float segmentArmedAt;
    private bool segmentBusy;
    private OrbFogHandler fog;
    private PeakHandler peak;
    private bool creditsBlack;
    private Character beachCharacter;
    private bool beachPending;
    private readonly BeachWake wake = new();
    private bool portalFading;
    private float nextRumblePulse;
    private int anyButtonFrame = -1;
    private bool wasActive;

    /// <summary>True while the win credits wait for a key press (PeakHandler.cs:228-257).</summary>
    public static bool CreditsWaiting { get; private set; }

    /// <summary>True on a frame where any VR trigger, primary or secondary button went down.</summary>
    public static bool AnyButtonPressedThisFrame =>
        instance != null && instance.anyButtonFrame == Time.frameCount;

    public static void Create(RunFlowSettings cfg)
    {
        var go = new GameObject("PeakXR_RunFlow");
        DontDestroyOnLoad(go);
        var runtime = go.AddComponent<RunFlowRuntime>();
        runtime.Init(cfg);
        instance = runtime;
    }

    private void Init(RunFlowSettings settings)
    {
        cfg = settings;
        warp = new Channel(
            "warp",
            new RunFlowFade
            {
                FadeOutSeconds = 0f,
                MinHoldSeconds = 0.25f,
                MaxHoldSeconds = 4f,
                FadeInSeconds = 0.35f,
            },
            LocalWarping
        );
        segment = new Channel(
            "segment",
            new RunFlowFade
            {
                FadeOutSeconds = 0.5f,
                MinHoldSeconds = 0.3f,
                MaxHoldSeconds = 12f,
                FadeInSeconds = 0.8f,
            },
            () => segmentBusy
        );
        runEnd = new Channel(
            "run end",
            new RunFlowFade
            {
                FadeOutSeconds = 0f,
                MinHoldSeconds = 0.6f,
                MaxHoldSeconds = 45f,
                FadeInSeconds = 0.8f,
            },
            CutsceneBusy
        );
        credits = new Channel(
            "credits",
            new RunFlowFade
            {
                FadeOutSeconds = 0.6f,
                MinHoldSeconds = 1f,
                MaxHoldSeconds = 90f,
                FadeInSeconds = 0.5f,
            },
            () => creditsBlack && !LoadingScreenHandler.loading && !cinema.Showing
        );
        beach = new Channel(
            "beach",
            new RunFlowFade
            {
                FadeOutSeconds = 0f,
                MinHoldSeconds = 0.5f,
                MaxHoldSeconds = 10f,
                FadeInSeconds = 1.5f,
            },
            () => wake.Active
        );
        channels = new[] { warp, segment, runEnd, credits, beach };
        cinema = new CinemaScreen(cfg);
        // OnEnable already ran inside AddComponent, before this object had a screen.
        cinema.Enable();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        cinema?.Enable();
        LoadingScreenHandler.OnLoadingStart += OnLoadingStart;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        LoadingScreenHandler.OnLoadingStart -= OnLoadingStart;
        cinema?.Disable();
        ClearAll();
    }

    private void Update()
    {
        if (!VRSession.Active)
        {
            if (wasActive)
                ClearAll();
            wasActive = false;
            return;
        }
        wasActive = true;
        var dt = Time.unscaledDeltaTime;
        UpdateButtons(dt);
        UpdateSegmentWatch();
        UpdateWake(dt);
        UpdateBeach();
        UpdateCinema();
        foreach (var channel in channels)
            Tick(channel, dt);
        UpdateRumble(dt);
    }

    /// <summary>The run-start wake-up waits to be shown (Fade mode) and the body still lies on the sand. ui.menu
    /// keeps its loading room up meanwhile, so the room hands over straight to the scene instead of to black.</summary>
    public static bool BeachWaking =>
        instance != null && instance.beachPending && instance.wake.Active;

    // ---- events reported by the patches ----

    /// <summary>Character.WarpPlayer on the local character (Character.cs:1847).</summary>
    public static void OnLocalWarp(Character character, Vector3 target)
    {
        if (instance == null || !VRSession.Active || !instance.cfg.FadeOnWarp.Value)
            return;
        // Warps inside a loading screen (void trip, spawn) are already covered by ui.menu's loading space.
        if (LoadingScreenHandler.loading)
            return;
        if ((target - character.Center).sqrMagnitude < MinWarpDistance * MinWarpDistance)
            return;
        Request(instance.warp, snap: true);
    }

    /// <summary>MapHandler.GoToSegment advancing (MapHandler.cs:578-582).</summary>
    public static void OnSegmentAdvance()
    {
        if (instance == null || !VRSession.Active || !instance.cfg.FadeOnSegmentChange.Value)
            return;
        instance.fog = Singleton<OrbFogHandler>.Instance;
        instance.segmentArmed = true;
        instance.segmentArmedAt = Time.unscaledTime;
    }

    /// <summary>MountainProgressHandler.SetSegmentComplete, the title-card trigger (MapHandler.cs:707).</summary>
    public static void OnSegmentTitle()
    {
        if (instance != null)
            instance.segmentBusy = false;
    }

    /// <summary>Character.RPCEndGame (Character.cs:893), before windows close and the mode switches.</summary>
    public static void OnRunEnd()
    {
        if (instance == null || !VRSession.Active || !instance.cfg.FadeOnRunEnd.Value)
            return;
        instance.peak = Singleton<PeakHandler>.Instance;
        Request(instance.runEnd, snap: true);
    }

    /// <summary>PeakHandler.EndScreenComplete (PeakHandler.cs:223): the end screen closes and the credits start.</summary>
    public static void OnCreditsStart(PeakHandler handler)
    {
        CreditsWaiting = true;
        if (
            instance == null
            || !VRSession.Active
            || !instance.cfg.BlackDuringCutscene.Value
            || RunFlowFeature.CutsceneViewHandled
            || !handler.isPlayingCinematic
            || instance.cinema.Showing
        )
            return;
        instance.creditsBlack = true;
        Request(instance.credits, snap: false);
    }

    /// <summary>Character.StartPassedOutOnTheBeach (Character.cs:2034) in Fade mode: hold black once loading ends.</summary>
    public static void OnBeachWakeup(Character character)
    {
        if (instance == null || !VRSession.Active)
            return;
        instance.beachCharacter = character;
        // One budget from the spawn for the room and the black hold together.
        instance.wake.Start();
        instance.beach.Fade.MaxHoldSeconds = BeachWake.Budget;
        // Inside a loading screen ui.menu's loading room is showing: it stays up while the body lies there.
        if (LoadingScreenHandler.loading)
            instance.beachPending = true;
        else
            Request(instance.beach, snap: true);
    }

    /// <summary>A world event that adds a Perlin camera shake in flat play.</summary>
    public static void Rumble(float shakeAmount, float seconds)
    {
        if (instance == null || !VRSession.Active)
            return;
        instance.rumble.Add(
            RunFlowRumble.FromShake(shakeAmount, instance.cfg.ShakeHaptics.Value),
            seconds
        );
    }

    /// <summary>Same as <see cref="Rumble"/>, after a scaled-time delay matching the game's coroutine.</summary>
    public static void RumbleAfter(float delay, float shakeAmount, float seconds)
    {
        if (instance == null || !VRSession.Active)
            return;
        instance.StartCoroutine(DelayedRumble(delay, shakeAmount, seconds));
    }

    /// <summary>Summit portal hold (PeakGatePortal.cs:95-121): fade follows the hold progress.
    /// Returns whether the VR replacement is active, so the caller mutes the warp volumes.</summary>
    public static bool PortalProgress(float progress, bool finished)
    {
        if (instance == null)
            return false;
        if (!VRSession.Active || !instance.cfg.PortalFade.Value)
        {
            instance.ClearPortal();
            return false;
        }
        // After the cast the run-end fade (snapped black inside RPCEndGame) owns the screen.
        if (finished || progress <= 0.001f)
            instance.ClearPortal();
        else
        {
            VRFade.Set(instance.portalOwner, progress);
            instance.portalFading = true;
            instance.rumble.Add(progress * PortalRumble * instance.cfg.ShakeHaptics.Value, 0.2f);
        }
        return true;
    }

    // ---- per-frame work ----

    private static void Request(Channel channel, bool snap)
    {
        channel.LoggedTimeout = false;
        channel.Fade.Request(snap ? 1f : 0f);
        if (snap)
            FadeView.Snap(1f);
        VRFade.Set(channel, channel.Fade.Alpha);
    }

    private static void Tick(Channel channel, float dt)
    {
        if (!channel.Fade.Active)
            return;
        var alpha = channel.Fade.Update(dt, channel.Busy());
        if (channel.Fade.TimedOut && !channel.LoggedTimeout)
        {
            channel.LoggedTimeout = true;
            Plugin.Log.LogWarning($"[ui.runflow] {channel.Name} fade hold timed out; fading in");
        }
        if (channel.Fade.Active)
            VRFade.Set(channel, alpha);
        else
            VRFade.Clear(channel);
    }

    private void UpdateSegmentWatch()
    {
        if (!segmentArmed)
            return;
        if (Time.unscaledTime - segmentArmedAt > SegmentArmTimeout)
        {
            segmentArmed = false;
            return;
        }
        // Same exit condition as OrbFogHandler.WaitForFogCatchUp (OrbFogHandler.cs:148); the swap
        // follows 1 s later (MapHandler.cs:595), enough for the fade-out.
        if (fog != null && fog.currentSize > 30f && fog.isMoving && !fog.hasArrived)
            return;
        segmentArmed = false;
        segmentBusy = true;
        Request(segment, snap: false);
    }

    private void UpdateBeach()
    {
        if (!beachPending || LoadingScreenHandler.loading || LoadingSpace.RoomUp)
            return;
        beachPending = false;
        // The room waited for the wake-up (BeachWaking): black only for what is left of the shared budget, if
        // anything. ui.menu has just faded out of the room, so no snap is needed.
        if (!wake.Active)
            return;
        beach.Fade.MaxHoldSeconds = wake.Remaining;
        Request(beach, snap: false);
    }

    private void UpdateWake(float dt)
    {
        var c = beachCharacter;
        if (c == null)
        {
            wake.Stop();
            return;
        }
        var hip = c.refs.hip != null ? c.refs.hip.Rig : null;
        var speed = hip != null ? hip.linearVelocity.magnitude : 0f;
        wake.Update(dt, BeachFalling(), c.data.isGrounded, speed);
    }

    private void UpdateButtons(float dt)
    {
        leftTrigger.Update(VRInput.Left.Trigger, dt);
        rightTrigger.Update(VRInput.Right.Trigger, dt);
        leftPrimary.Update(VRInput.Left.Primary, dt);
        rightPrimary.Update(VRInput.Right.Primary, dt);
        leftSecondary.Update(VRInput.Left.Secondary, dt);
        rightSecondary.Update(VRInput.Right.Secondary, dt);
        if (
            leftTrigger.Pressed
            || rightTrigger.Pressed
            || leftPrimary.Pressed
            || rightPrimary.Pressed
            || leftSecondary.Pressed
            || rightSecondary.Pressed
        )
            anyButtonFrame = Time.frameCount;
    }

    private void UpdateRumble(float dt)
    {
        var amplitude = rumble.Update(dt);
        if (amplitude < 0.01f || Time.unscaledTime < nextRumblePulse)
            return;
        nextRumblePulse = Time.unscaledTime + RumblePulseInterval;
        UIHaptics.Tick(true, amplitude, RumblePulseInterval * 1.2f);
        UIHaptics.Tick(false, amplitude, RumblePulseInterval * 1.2f);
    }

    private static IEnumerator DelayedRumble(float delay, float shakeAmount, float seconds)
    {
        yield return new WaitForSeconds(delay);
        Rumble(shakeAmount, seconds);
    }

    /// <summary>Cutscene cameras on the cinema screen while the win cutscene or the credits run
    /// (PeakHandler.isPlayingCinematic, set in PrepareEndCutscene L66 and never cleared before the
    /// scene unloads). Kept through the airport loading screen so the restored scene camera never
    /// renders to the headset; stopped on scene load.</summary>
    private void UpdateCinema()
    {
        var handler = Singleton<PeakHandler>.Instance;
        var wanted =
            cfg.CutsceneScreen.Value
            && !RunFlowFeature.CutsceneViewHandled
            && ((handler != null && handler.isPlayingCinematic) || CreditsWaiting);
        if (wanted)
        {
            cinemaRunning = true;
            cinema.Tick(VRRig.Instance);
        }
        else
            StopCinema();
    }

    private void StopCinema()
    {
        if (!cinemaRunning)
            return;
        cinemaRunning = false;
        cinema.Stop();
    }

    // ---- hold conditions ----

    private static bool LocalWarping()
    {
        var character = Character.localCharacter;
        return character != null && character.warping;
    }

    /// <summary>Black while the win cutscene's scene cameras run, until the end screen opens
    /// (PeakHandler.cs:64-131) or the cinema screen shows the cutscene. Losses open the end screen at
    /// once, so this is false for them.</summary>
    private bool CutsceneBusy()
    {
        if (
            !cfg.BlackDuringCutscene.Value
            || RunFlowFeature.CutsceneViewHandled
            || cinema.Showing
            || peak == null
            || !peak.isPlayingCinematic
        )
            return false;
        var gui = GUIManager.instance;
        return gui == null || gui.endScreen == null || !gui.endScreen.isOpen;
    }

    /// <summary>Character lies on the sand: passedOutOnTheBeach (3 s) and the Fall(7) ragdoll (Character.cs:2038-2039).</summary>
    private bool BeachFalling() =>
        beachCharacter != null
        && (beachCharacter.data.passedOutOnTheBeach > 0f || beachCharacter.data.fallSeconds > 0f);

    // ---- resets ----

    private void OnLoadingStart(string sceneName) => EndTransitions();

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EndTransitions();
        CreditsWaiting = false;
        beachPending = false;
        beachCharacter = null;
        wake.Stop();
        rumble.Reset();
        StopCinema();
    }

    /// <summary>A level switch takes over (ui.menu fades into its loading space): end every hold.</summary>
    private void EndTransitions()
    {
        segmentArmed = false;
        segmentBusy = false;
        creditsBlack = false;
        peak = null;
        fog = null;
        if (channels != null)
            foreach (var channel in channels)
                channel.Fade.Release();
        ClearPortal();
    }

    private void ClearAll()
    {
        if (channels == null)
            return;
        foreach (var channel in channels)
        {
            channel.Fade.Reset();
            VRFade.Clear(channel);
        }
        ClearPortal();
        rumble.Reset();
        StopCinema();
    }

    private void ClearPortal()
    {
        if (!portalFading)
            return;
        portalFading = false;
        VRFade.Clear(portalOwner);
    }
}
