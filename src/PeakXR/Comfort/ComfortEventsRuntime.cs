using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Look;
using PeakXR.Rig;
using PeakXR.UI.Common;
using PeakXR.UI.RunFlow;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.Comfort;

/// <summary>
/// Turns reported comfort events into the existing comfort systems once per frame: vignette pulses into the
/// locomotion vignette, fades into <see cref="VRFade"/>, rumble through Interact <see cref="Haptics"/>, and the
/// horizon-locked chase view through rig.camera's takeover. Also polls what no single hook reports: scripted looks
/// (player.look's override weight), teleports (a body jump with no run transition) and launches (a sudden rise).
/// Order 550: before rig.camera (600) reads <see cref="RigDriver.EventDetach"/>.
/// </summary>
[DefaultExecutionOrder(550)]
internal sealed class ComfortEventsRuntime : MonoBehaviour
{
    /// <summary>A body jump this far in one frame, with no run transition, is a teleport.</summary>
    private const float TeleportDistance = 4f;

    /// <summary>Seconds the scene-cut fade may hold before it gives up (a leave that never completes).</summary>
    private const float SceneCutTimeout = 5f;

    /// <summary>Seconds a side push keeps its lateral bias on the rumble.</summary>
    private const float SideSeconds = 0.4f;

    private static ComfortEventsRuntime instance;

    private Vector3 sideFrom;
    private float sideUntil = -1f;

    private Vector3 pressureFrom;
    private float pressureAmplitude;
    private int pressureFrame = -10;

    /// <summary>The wind rumble's level per hand at its last feed, and when (self-test).</summary>
    internal static float PressureLeft { get; private set; }

    internal static float PressureRight { get; private set; }

    internal static float PressureAt { get; private set; } = -1f;

    /// <summary>Amplitudes of the last comfort rumble per hand (self-test: a hit from the right is stronger on
    /// the right).</summary>
    internal static float LastLeftAmplitude { get; private set; }

    internal static float LastRightAmplitude { get; private set; }

    /// <summary>When the last comfort rumble went out (unscaled time).</summary>
    internal static float LastAt { get; private set; } = -1f;

    /// <summary>Extra vignette closing (0..1) the locomotion vignette shows on top of its own.</summary>
    internal static float VignetteBoost { get; private set; }

    private readonly ComfortPulses pulses = new();
    private readonly LaunchTracker launch = new();
    private readonly object fadeOwner = new();
    private readonly object cutOwner = new();

    private ComfortResponse launchHold;
    private Character tracked;
    private Vector3 lastCenter;
    private bool scriptedLook;
    private float cutUntil = -1f;

    /// <summary>Radius of the blind haze around the eyes: hands, held items and the wrist display nearer than
    /// this stay clear (they write depth first), like the game's own depth fog.</summary>
    private const float HazeRadius = 0.6f;

    /// <summary>Haze opacity change per second: 0 to the full cap in about 0.4 s.</summary>
    private const float HazeRate = 1f;

    private GameObject haze;
    private Material hazeMaterial;
    private MeshRenderer hazeRenderer;

    /// <summary>Opacity of the blind haze drawn this frame (self-test: never above ComfortPolicy.MaxWashAlpha).</summary>
    internal static float HazeAlpha { get; private set; }

    internal static void Create()
    {
        var go = new GameObject("PeakXR.ComfortEvents") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ComfortEventsRuntime>();
    }

    /// <summary>Reports one event of the local player. Safe to call from any patch, any time.</summary>
    internal static void Raise(ComfortEvent e, float magnitude)
    {
        if (instance == null || !VRSession.Active)
            return;
        var r = Resolve(e, magnitude);
        if (r.Any)
            instance.pulses.Add(r);
    }

    /// <summary>As <see cref="Raise"/>, felt more on the hand facing <paramref name="towardSource"/> (world): the
    /// side a hit, a pull or the wind came from.</summary>
    internal static void RaiseFrom(ComfortEvent e, float magnitude, Vector3 towardSource)
    {
        if (instance == null || !VRSession.Active)
            return;
        instance.sideFrom = towardSource;
        instance.sideUntil = Time.unscaledTime + SideSeconds;
        Raise(e, magnitude);
    }

    /// <summary>
    /// A steady push this frame (wind, a tornado's pull) from <paramref name="towardSource"/> (world), at
    /// <paramref name="amplitude"/> (<see cref="BodyCues.PullAmplitude"/>): a continuous wind rumble
    /// (<see cref="EnvironmentRumble"/>), stronger on the hand facing it, following the push while it lasts. The strongest push of the frame wins. Off with <c>ComfortEvents.WindPressure</c>.
    /// </summary>
    internal static void Pressure(Vector3 towardSource, float amplitude)
    {
        if (instance == null || !VRSession.Active || !ComfortEventsConfig.Pressure)
            return;
        var i = instance;
        if (i.pressureFrame == Time.frameCount && amplitude <= i.pressureAmplitude)
            return;
        i.pressureFrame = Time.frameCount;
        i.pressureAmplitude = amplitude;
        i.pressureFrom = towardSource;
    }

    private void TickPressure()
    {
        // FixedUpdate may skip a render frame: a push from the last two frames still counts.
        if (
            Time.frameCount - pressureFrame > 2
            || pressureAmplitude <= 0f
            || !ComfortEventsConfig.Haptics
        )
            return;
        float now = Time.unscaledTime;
        float left = 1f,
            right = 1f;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (head != null)
        {
            var r = head.right;
            BodyCues.SideGains(
                new System.Numerics.Vector2(pressureFrom.x, pressureFrom.z),
                System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(r.x, r.z)),
                out left,
                out right
            );
        }
        float a = pressureAmplitude * ComfortEventsConfig.Intensity;
        PressureLeft = a * left;
        PressureRight = a * right;
        PressureAt = now;
        // PullAmplitude is a pulse amplitude at the gameplay scale; the rumble takes felt strength.
        EnvironmentRumble.Feed(EnvSource.Wind, true, PressureLeft * HapticScale.Cue);
        EnvironmentRumble.Feed(EnvSource.Wind, false, PressureRight * HapticScale.Cue);
    }

    /// <summary>A camera shake: rumble length follows the shake, capped so long quakes pulse instead.</summary>
    /// <param name="scale">PerlinShake.AddShake's noise scale; NaN when unknown.</param>
    internal static void RaiseShake(float amount, float duration, float scale = float.NaN)
    {
        if (instance == null || !VRSession.Active || amount <= 0.01f)
            return;
        var r = Resolve(ComfortEvent.Shake, amount / 10f);
        // ui.runflow rumbles some world shakes itself (its ShakeHaptics); one rumble per shake.
        if (RunFlowFeature.TakeShake() || ClimbBeatOwnsShake(duration, scale))
            r.Haptic = 0f;
        if (r.Haptic > 0f)
            r.HapticSeconds = Mathf.Clamp(duration, 0.05f, 0.5f);
        if (r.Any)
            instance.pulses.Add(r);
    }

    /// <summary>CharacterClimbing.Update (CharacterClimbing.cs:127-130) shakes the camera every 0.1 s while a climber
    /// is out of stamina, with <c>AddPerlinShake(3 * outOfStaminaFor, 0.2f, 10f)</c>. The gripping hands already beat
    /// for that (<c>Interaction.StaminaHaptics</c>, interact.physical), and a steady rumble on top would bury the beat,
    /// so the beat owns that one shake's feel, and only while it is really playing (it pulses every 0.15 s out of
    /// stamina): with PhysicalClimb off, in a regrip grace or under a refusal the beat is quiet and the shake rumbles.
    /// Every other shake rumbles as before.</summary>
    private static bool ClimbBeatOwnsShake(float duration, float scale)
    {
        var c = Character.localCharacter;
        return c != null
            && Mathf.Approximately(duration, ClimbShakeSeconds)
            && Mathf.Approximately(scale, ClimbShakeScale)
            && c.data.isClimbing
            && c.data.outOfStaminaFor > 0f
            && Time.unscaledTime - Haptics.BeatAt <= BeatGap;
    }

    /// <summary>The out-of-stamina climb shake's duration and noise scale (CharacterClimbing.cs:129).</summary>
    private const float ClimbShakeSeconds = 0.2f;
    private const float ClimbShakeScale = 10f;

    /// <summary>The beat counts as playing this long after its last pulse: its out-of-stamina interval (0.15 s) and a
    /// slow frame.</summary>
    private const float BeatGap = 0.25f;

    /// <summary>The local body was launched: its treatment lasts until it lands.</summary>
    internal static void RaiseLaunch(float magnitude)
    {
        if (instance == null || !VRSession.Active)
            return;
        instance.StartLaunch(magnitude);
    }

    /// <summary>MF-19: black before a direct scene load to Title. Cleared when the next scene loads.</summary>
    internal static void FadeForSceneCut(bool snap)
    {
        if (instance == null)
            return;
        VRFade.Set(instance.cutOwner, 1f);
        if (snap)
            FadeView.Snap(1f);
        instance.cutUntil = Time.unscaledTime + SceneCutTimeout;
    }

    private static ComfortResponse Resolve(ComfortEvent e, float magnitude) =>
        ComfortPolicy.Resolve(
            e,
            ComfortEventsConfig.Treatment(e),
            magnitude,
            ComfortEventsConfig.Intensity,
            ComfortEventsConfig.Haptics
        );

    private void StartLaunch(float magnitude)
    {
        var r = Resolve(ComfortEvent.Launch, magnitude);
        pulses.Add(r);
        // While airborne only the lasting parts renew: no repeated fades or rumble.
        launchHold = new ComfortResponse
        {
            Vignette = r.Vignette,
            VignetteSeconds = r.Vignette > 0f ? 0.2f : 0f,
            DetachSeconds = r.DetachSeconds > 0f ? 0.2f : 0f,
        };
        launch.Start();
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ClearOutputs();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        VRFade.Clear(cutOwner);
        cutUntil = -1f;
        pulses.Reset();
        launch.Cancel();
        tracked = null;
    }

    private void LateUpdate()
    {
        if (!VRSession.Active)
        {
            ClearOutputs();
            return;
        }
        float dt = Time.unscaledDeltaTime;
        var mode = ComfortEventsConfig.Cutscene;
        RigDriver.FollowCutscene = mode == CutsceneView.FollowCamera;
        RunFlowFeature.CutsceneViewHandled =
            mode != CutsceneView.Screen && !RunFlowRuntime.CreditsWaiting;
        if (cutUntil > 0f && Time.unscaledTime > cutUntil)
        {
            VRFade.Clear(cutOwner);
            cutUntil = -1f;
        }

        var c = Character.localCharacter;
        Poll(c, dt);

        pulses.Update(dt);
        TickPressure();
        VignetteBoost = pulses.Vignette;
        if (pulses.SnapBlack)
            FadeView.Snap(1f);
        if (pulses.Fade > 0f)
            VRFade.Set(fadeOwner, pulses.Fade);
        else
            VRFade.Clear(fadeOwner);
        RigDriver.EventDetach = pulses.Detached;
        ShowHaze(ComfortPolicy.BlindHaze(ComfortEventsPatches.BlindWash.Strength));
        // ComfortPulses spaces the pulses and keeps the strongest hit asked for in between.
        if (pulses.HapticAmplitude > 0f)
        {
            float seconds = pulses.HapticSeconds;
            float left = 1f,
                right = 1f;
            var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
            if (Time.unscaledTime < sideUntil && head != null)
            {
                var r = head.right;
                BodyCues.SideGains(
                    new System.Numerics.Vector2(sideFrom.x, sideFrom.z),
                    System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(r.x, r.z)),
                    out left,
                    out right
                );
            }
            LastLeftAmplitude = pulses.HapticAmplitude * left;
            LastRightAmplitude = pulses.HapticAmplitude * right;
            LastAt = Time.unscaledTime;
            Haptics.Pulse(true, LastLeftAmplitude, seconds);
            Haptics.Pulse(false, LastRightAmplitude, seconds);
        }
    }

    private void Poll(Character c, float dt)
    {
        if (c == null)
        {
            tracked = null;
            launch.Cancel();
            scriptedLook = false;
            return;
        }
        var d = c.data;
        var center = c.Center;
        if (c != tracked)
        {
            tracked = c;
            lastCenter = center;
            launch.Cancel();
        }

        var hip = c.refs.hip != null ? c.refs.hip.Rig : null;
        var v = hip != null ? hip.linearVelocity : Vector3.zero;

        // Teleports without a run transition (ui.runflow fades Character.WarpPlayer itself): reconnect spawns,
        // scripted returns. The jump already happened, so the fade starts black. A long frame of a fast fall or
        // flight also moves the body far: allow twice what its speed covers.
        float jump = Mathf.Max(TeleportDistance, v.magnitude * dt * 2f);
        if ((center - lastCenter).sqrMagnitude > jump * jump && !c.warping)
            Raise(ComfortEvent.Teleport, 1f);
        lastCenter = center;

        // A scripted look (frog tongue, rescue hook, scoutmaster, grabs) took the body's heading.
        bool look = LookPatches.ScriptedLookWeight > 0.5f;
        if (look && !scriptedLook)
        {
            // The pull side: the scripted heading relative to the headset's (LookPatches.ScriptedPullYaw).
            var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
            float pull = LookPatches.ScriptedPullYaw;
            if (head != null && Mathf.Abs(pull) > 5f)
                RaiseFrom(ComfortEvent.ForcedLook, 1f, head.right * Mathf.Sign(pull));
            else
                Raise(ComfortEvent.ForcedLook, 1f);
        }
        scriptedLook = look;

        // A launched body often tumbles too (cannon, mushroom); the chase view stays through it. Climbing ends
        // a flight; spectating hands the view to rig.camera. A warp halts the body's velocity (HaltBodyVelocity,
        // A/Character.cs:1908-1960, warping set :1868-1963), which must not read as a launch.
        bool over =
            d.dead
            || d.fullyPassedOut
            || d.isClimbing
            || d.isRopeClimbing
            || d.isVineClimbing
            || c.warping;
        // A tumbling body rarely reports grounded: lying still counts as landed.
        bool landed = d.isGrounded || (d.fallSeconds > 0f && v.sqrMagnitude < 1.5f * 1.5f);
        if (over)
            launch.Cancel();
        bool wasActive = launch.Active;
        bool active = !over && launch.Update(landed, v.y, dt);
        if (active && !wasActive)
            StartLaunch(0.6f); // detected by speed, not reported by a hook
        else if (active)
            pulses.Add(launchHold);
    }

    /// <summary>The BLIND white-out as a capped white haze around the head (see ComfortEventsPatches.BlindWash).</summary>
    private void ShowHaze(float target)
    {
        // Eased, so a wash that starts at full strength fades in instead of flashing white in one frame.
        float alpha = Mathf.MoveTowards(HazeAlpha, target, Time.unscaledDeltaTime * HazeRate);
        HazeAlpha = alpha;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (alpha <= 0.002f || head == null)
        {
            if (hazeRenderer != null && hazeRenderer.enabled)
                hazeRenderer.enabled = false;
            HazeAlpha = head == null ? 0f : alpha;
            return;
        }
        if (haze == null)
        {
            if (hazeMaterial == null)
                hazeMaterial = UIMaterials.Create(Color.clear, drawOnTop: false, renderQueue: 3100);
            haze = UIMaterials.MeshObject(
                "PeakXR_BlindHaze",
                UIMaterials.SphereMesh(),
                hazeMaterial,
                head
            );
            haze.transform.localScale = Vector3.one * (HazeRadius * 2f);
            hazeRenderer = haze.GetComponent<MeshRenderer>();
        }
        else if (haze.transform.parent != head)
            haze.transform.SetParent(head, false);
        hazeMaterial.color = new Color(1f, 1f, 1f, alpha);
        if (!hazeRenderer.enabled)
            hazeRenderer.enabled = true;
    }

    private void ClearOutputs()
    {
        HazeAlpha = 0f;
        if (haze != null)
            Destroy(haze);
        if (hazeMaterial != null)
            Destroy(hazeMaterial);
        haze = null;
        hazeMaterial = null;
        hazeRenderer = null;
        VignetteBoost = 0f;
        RigDriver.EventDetach = false;
        RigDriver.FollowCutscene = false;
        RunFlowFeature.CutsceneViewHandled = false;
        pulses.Reset();
        launch.Cancel();
        VRFade.Clear(fadeOwner);
        VRFade.Clear(cutOwner);
    }
}
