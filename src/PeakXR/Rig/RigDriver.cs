using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace PeakXR.Rig;

/// <summary>
/// Runs <see cref="VRRig"/>: tracked head, hands and aim inside the tracking space, the tracking origin on
/// the character (or on a comfort view while the game owns the camera), recentering, and the game's
/// <c>MainCamera</c> as the stereo eye camera. Order 600: after MainCameraMovement (500) has run its side
/// effects, before the avatar (1000) reads the rig. Unverified that Unity honours the order for a runtime-loaded
/// type; if not, the camera still ends at the head (the MainCameraMovement postfix and OnBeforeRender).
/// </summary>
[DefaultExecutionOrder(600)]
internal sealed class RigDriver : MonoBehaviour
{
    private const string Tag = "[rig.camera]";

    /// <summary>Below this ragdoll control the vanilla camera mostly follows the tumbling head
    /// (MainCameraMovement.cs:286 blends by 1 - control), so the view counts as taken.</summary>
    private const float RagdollControlThreshold = 0.5f;

    /// <summary>Same cap as vanilla characterPovMaxDistance (MainCameraMovement.cs:21).</summary>
    private const float MaxBodyLag = 0.1f;

    /// <summary>Eyes in front of (x) and above (y) the torso until the first upright sample of the character; a
    /// rough guess, the first sample replaces it at once.</summary>
    private static readonly System.Numerics.Vector2 DefaultEyeOffset = new(0.1f, 0.4f);

    /// <summary>The eyes are sampled only while the look is within about 20 degrees of level: the animated head
    /// pitches with the look (Look Y), which would carry the eye renderers down and forward.</summary>
    private const float LevelLookY = 0.35f;

    /// <summary>Eye-offset samples are skipped while the lean has moved the avatar's eyes further than this (m).</summary>
    private const float MaxSampleLean = 0.05f;

    /// <summary>Vertical follow rate (1/s) on the ground: the torso is a driven rigidbody that bobs at step rate
    /// (about 2 Hz), which a 20/s follow passes almost whole. Jumps and falls keep the full rate.</summary>
    private const float GroundedVerticalRate = 5f;

    /// <summary>The view stops this far (m) in front of a wall the head went into.</summary>
    private const float WallMargin = 0.05f;

    /// <summary>Follow rate (1/s) of the opt-in cutscene glide; slow, never a cut.</summary>
    private const float CutsceneFollowRate = 1.5f;

    /// <summary>Glide rate (1/s) back onto the avatar's eyes at full move input.</summary>
    private const float TurnSettleRate = 1.5f;

    /// <summary>Largest turn shift (m): twice the eye's forward offset, a 180 degree turn, with margin.</summary>
    private const float MaxTurnShift = 0.3f;

    /// <summary>After a turn the eye is held this long (s) while the ragdoll settles onto the new heading.</summary>
    private const float TurnHoldSeconds = 0.6f;

    private static RigDriver instance;

    /// <summary>comfort.events wants the horizon-locked chase view this frame (a launch).</summary>
    internal static bool EventDetach;

    /// <summary>comfort.events opted in to gliding after a moving cutscene camera instead of fading between
    /// still viewpoints.</summary>
    internal static bool FollowCutscene;

    /// <summary>The first-person eye anchor in world space, the point the view rides; null while the view is
    /// elsewhere or jumped this frame. Room-scale following absorbs its movement.</summary>
    internal static Vector3? BodyAnchor;

    /// <summary>The view rides the local body (first person). False while it is detached (third person, spectate,
    /// rocket, cutscene) or held by the title stage: the tracked hands are then near the view, not the body.</summary>
    internal static bool ViewOnBody = true;

    private readonly TrackedDevices devices = new();
    private readonly CameraTakeover takeover = new();
    private readonly LongPress recenterPress = new();
    private readonly HoldCue holdCue = new();
    private readonly SpectateFloor spectateFloor = new();
    private readonly DesktopMirror mirror = new();
    private readonly List<Camera> silencedCameras = new();
    private readonly List<AudioListener> mutedListeners = new();
    private readonly object wallFadeOwner = new();
    private Camera[] cameraBuffer = new Camera[16];

    private VRRig rig;
    private bool headTracked;

    private MainCamera boundMain;
    private Camera boundCam;
    private MainCameraMovement boundMovement;
    private AudioListener mainListener;
    private float savedNear;

    private Camera fallbackCam;
    private AudioListener fallbackListener;
    private bool fallbackActive;
    private float nextListenerScan;

    private Character boundCharacter;
    private bool haveEyeOffset;
    private System.Numerics.Vector2 eyeOffset = DefaultEyeOffset;
    private Vector3 anchor;
    private bool snap = true;
    private Vector3 turnShift;

    /// <summary>Eased push that keeps a spectate view out of the watched body (<see cref="RigMath.ClearOfBody"/>).</summary>
    private Vector3 clearShift,
        clearVelocity;
    private const float ClearSmoothSeconds = 0.12f;
    private float turnYaw;
    private float turnHoldUntil;
    private Vector3 lastBodyEye;

    /// <summary>Self-test (vr.16): log the turn per frame while set.</summary>
    internal static float TraceTurnsUntil;

    private ViewKind shownKind;
    private Character shownTarget;
    private Transform staticSource;
    private Vector3 staticPoint;
    private Vector3 staticSourceLast;
    private float staticSince;
    private bool haveSavedYaw;
    private float savedYaw;

    private bool needInitialRecenter = true;
    private bool pendingRecenter;
    private bool pendingHeight;
    private bool lastSeated;
    private bool headBlocked;
    private Vector3 blockedEye;

    internal static void OnVRStarted()
    {
        if (instance == null)
            instance = Create();
        instance.enabled = true;
    }

    internal static void OnVRStopped()
    {
        if (instance != null)
            instance.enabled = false;
    }

    private static RigDriver Create()
    {
        var root = new GameObject("PeakXR.VRRig");
        DontDestroyOnLoad(root);
        var newRig = root.AddComponent<VRRig>();
        newRig.Origin = root.transform;
        newRig.Head = Child("Head", root.transform);
        newRig.LeftHand = Child("LeftHand", root.transform);
        newRig.RightHand = Child("RightHand", root.transform);
        newRig.LeftAim = Child("LeftAim", root.transform);
        newRig.RightAim = Child("RightAim", root.transform);
        VRRig.Instance = newRig;
        Plugin.Log.LogInfo($"{Tag} VRRig created");
        return root.AddComponent<RigDriver>();
    }

    private static Transform Child(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private void Awake()
    {
        rig = GetComponent<VRRig>();

        // Renders to the headset only while the game's camera is disabled (end cutscenes deactivate the
        // MainCamera object, PeakHandler.cs:77): the view never rides a cutscene camera.
        var go = new GameObject("PeakXR.FallbackCamera");
        go.transform.SetParent(rig.Head, false);
        fallbackCam = go.AddComponent<Camera>();
        fallbackCam.enabled = false;
        fallbackCam.stereoTargetEye = StereoTargetEyeMask.Both;
        DisableAutoTracking(fallbackCam, true);
        fallbackListener = go.AddComponent<AudioListener>();
        fallbackListener.enabled = false;
    }

    private void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
        mirror.Enable();
        needInitialRecenter = true;
        snap = true;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
        mirror.Disable();
        holdCue.Show(null, 0f, 0f);
        spectateFloor.Hide();
        SetFallback(false);
        UnbindMainCamera();
        takeover.Reset(false);
        shownKind = ViewKind.FirstPerson;
        VRFade.Clear(this);
        VRFade.Clear(wallFadeOwner);
        headBlocked = false;
        BodyAnchor = null;
    }

    private void OnDestroy()
    {
        holdCue.Destroy();
        spectateFloor.Destroy();
        if (instance == this)
            instance = null;
        if (VRRig.Instance == rig)
            VRRig.Instance = null;
    }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        BindMainCamera();
        UpdateHead();
        UpdateHands();

        var c = Character.localCharacter;
        if (c != boundCharacter)
        {
            // New scene or respawned character: calibrate again once the head is tracked.
            boundCharacter = c;
            haveEyeOffset = false;
            eyeOffset = DefaultEyeOffset;
            needInitialRecenter = c != null;
            snap = true;
        }
        UpdateEyeOffset(c, dt);
        HandleRecenterInput(c, dt);

        BodyAnchor = null;
        if (rig.OriginOverride)
            ResetTakeover();
        else
            PlaceOrigin(c, dt);
        ViewOnBody = !rig.OriginOverride && shownKind == ViewKind.FirstPerson;

        CheckHeadInWall(c);
        DriveCamera();
        TraceTurn(c);
    }

    /// <summary>Latest HMD pose right before rendering: the lowest-latency head tracking Unity offers.</summary>
    private void OnBeforeRender()
    {
        if (!VRSession.Active)
            return;
        UpdateHead();
        if (!fallbackActive && boundCam != null)
            boundCam.transform.SetPositionAndRotation(EyePosition(), rig.Head.rotation);
    }

    private Vector3 EyePosition() => headBlocked ? blockedEye : rig.Head.position;

    private void UpdateHead()
    {
        if (devices.TryGetHead(out var p, out var r))
        {
            headTracked = true;
            rig.Head.SetLocalPositionAndRotation(p, r);
        }
        else if (VRInput.HeadTracked)
        {
            headTracked = true;
            rig.Head.SetLocalPositionAndRotation(VRInput.HeadPosition, VRInput.HeadRotation);
        }
        else
        {
            headTracked = false;
        }
    }

    private void UpdateHands()
    {
        UpdateHand(true, rig.LeftHand, rig.LeftAim);
        UpdateHand(false, rig.RightHand, rig.RightAim);
    }

    private void UpdateHand(bool left, Transform grip, Transform aim)
    {
        ref var h = ref VRInput.Hand(left);
        if (h.Tracked)
            grip.SetLocalPositionAndRotation(h.Position, h.Rotation);
        // Scripted self-test hands have no aim pose of their own: aim along the grip pose like a runtime without one.
        if (VRInput.TestOverride == null && devices.TryGetAim(left, out var p, out var r))
            aim.SetLocalPositionAndRotation(p, r);
        else if (h.Tracked)
            aim.SetLocalPositionAndRotation(h.Position, h.Rotation);
    }

    /// <summary>
    /// Learns where the character's eyes are relative to the torso (forward and up in its facing frame) while the
    /// body stands upright and looks level, as a slow average so walk bob does not reach the view. The eyes are the
    /// face's eye renderers (CustomizationRefs.EyeRenderers, CharacterCustomization.cs:286). Not the vanilla camera
    /// point <c>head.TransformPoint(up * 1 + forward * -0.5)</c> (Character.cs:1358): that sits on top of the head,
    /// about 0.2 m above the eyes, and straight over the torso, so looking down showed the top of your own body.
    /// </summary>
    private void UpdateEyeOffset(Character c, float dt)
    {
        if (c == null)
            return;
        var d = c.data;
        if (
            !d.fullyConscious
            || !d.isGrounded
            || d.isClimbingAnything
            || d.isCrouching
            || d.fallSeconds > 0f
            || d.currentRagdollControll < 0.9f
            || Mathf.Abs(d.lookDirection.y) > LevelLookY
            || d.lookDirection_Flat.sqrMagnitude < 1e-4f
            // Mid-turn the look heading is new but the ragdoll still faces the old one: a skewed sample.
            || Time.unscaledTime < turnHoldUntil
            || Mathf.Abs(Mathf.DeltaAngle(turnYaw, rig.Origin.eulerAngles.y)) > 0.01f
        )
            return;
        if (!TryEyePoint(c, out var eye))
            return;
        // Measure the unleaned body (avatar.local's lean bends the torso and eyes toward the headset): sampling the
        // leaned eyes would carry the head's offset into the anchor and slide the view after the head.
        var driver = AvatarPoseDriver.Of(c);
        if (driver != null)
        {
            if (haveEyeOffset && driver.EyeLeanShift.sqrMagnitude > MaxSampleLean * MaxSampleLean)
                return;
            eye -= driver.EyeLeanShift;
        }
        float facing = Mathf.Atan2(d.lookDirection_Flat.x, d.lookDirection_Flat.z) * Mathf.Rad2Deg;
        var sample = RigMath.EyeOffset(
            eye.N(),
            (c.Center - AvatarPoseDriver.LeanShiftOf(c)).N(),
            facing
        );
        if (!(sample.Y > 0.1f && sample.Y < 2f && Mathf.Abs(sample.X) < 0.5f))
            return;
        // Snap only while the view is placed anyway (a new character, a recenter); otherwise glide, never jump.
        eyeOffset =
            haveEyeOffset || !snap
                ? System.Numerics.Vector2.Lerp(eyeOffset, sample, 1f - Mathf.Exp(-dt))
                : sample;
        if (!haveEyeOffset)
            Plugin.Log.LogInfo(
                $"{Tag} Eyes {sample.X:0.00} m ahead of and {sample.Y:0.00} m above the torso"
            );
        haveEyeOffset = true;
    }

    /// <summary>Middle of the face's eye renderers; without them, the old top-of-head point (no forward offset).</summary>
    private static bool TryEyePoint(Character c, out Vector3 eye)
    {
        if (TryEyeRenderers(c, out eye))
            return true;
        var head = CameraPatches.GetBodypart(c, BodypartType.Head);
        if (head == null)
            return false;
        var center = c.Center;
        eye = new Vector3(
            center.x,
            head.transform.position.y + head.transform.lossyScale.y,
            center.z
        );
        return true;
    }

    /// <summary>Middle of the face's eye renderers (CustomizationRefs.EyeRenderers); false when there are none.
    /// Also read by the self-test (vr.08).</summary>
    internal static bool TryEyeRenderers(Character c, out Vector3 eye)
    {
        var custom = c.refs.customization;
        var eyes = custom != null && custom.refs != null ? custom.refs.EyeRenderers : null;
        eye = Vector3.zero;
        int n = 0;
        if (eyes != null)
        {
            for (int i = 0; i < eyes.Length; i++)
            {
                var r = eyes[i];
                if (r == null)
                    continue;
                var b = r.bounds;
                if (b.extents.sqrMagnitude <= 0f)
                    continue;
                eye += b.center;
                n++;
            }
        }
        if (n == 0)
            return false;
        eye /= n;
        return true;
    }

    private void HandleRecenterInput(Character c, float dt)
    {
        recenterPress.Seconds = CameraFeature.RecenterSeconds;
        bool fired = recenterPress.Update(VRInput.Left.Menu || VRInput.Right.Menu, dt);
        holdCue.Show(rig.Head, recenterPress.HeldSeconds, recenterPress.Progress);
        if (fired || rig.RecenterRequested)
        {
            rig.RecenterRequested = false;
            pendingRecenter = true;
        }
        if (rig.SeatedMode != lastSeated)
            pendingHeight = true;
        if (needInitialRecenter && headTracked && c != null)
        {
            // First frame with a body: nothing to fade from yet (loading just ended).
            needInitialRecenter = false;
            Recenter(c);
            pendingRecenter = false;
        }
    }

    private void Recenter(Character c)
    {
        var hp = rig.Head.localPosition;
        rig.RoomAnchor = new System.Numerics.Vector2(hp.x, hp.z);
        CalibrateHeight();
        if (c != null)
        {
            var body = c.data.lookDirection_Flat;
            var hmd = rig.Head.localRotation * Vector3.forward;
            if (body.sqrMagnitude > 1e-4f && (hmd.x * hmd.x + hmd.z * hmd.z) > 1e-4f)
            {
                float yaw = RigMath.RecenterYaw(
                    Mathf.Atan2(body.x, body.z) * Mathf.Rad2Deg,
                    Mathf.Atan2(hmd.x, hmd.z) * Mathf.Rad2Deg
                );
                rig.Origin.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
        snap = true;
        Plugin.Log.LogInfo(
            $"{Tag} Recentered: head y {hp.y:0.00} m, height offset {rig.HeightOffset:0.00} m, "
                + $"floor tracking {XRRuntimeInfo.FloorTracking}, seated {rig.SeatedMode}"
        );
        rig.RaiseRecentered();
    }

    private void CalibrateHeight()
    {
        lastSeated = rig.SeatedMode;
        pendingHeight = false;
        rig.HeightOffset = RigMath.HeightOffset(
            XRRuntimeInfo.FloorTracking,
            rig.SeatedMode ? HeightMode.Seated : HeightMode.Standing,
            rig.Head.localPosition.y
        );
    }

    private void ResetTakeover()
    {
        spectateFloor.Hide();
        if (haveSavedYaw)
            rig.Origin.rotation = Quaternion.Euler(0f, savedYaw, 0f);
        haveSavedYaw = false;
        takeover.Reset(false);
        shownKind = ViewKind.FirstPerson;
        shownTarget = null;
        staticSource = null;
        snap = true;
        VRFade.Clear(this);
    }

    private void PlaceOrigin(Character c, float dt)
    {
        bool cutscene = boundMain != null && !boundMain.gameObject.activeInHierarchy;
        if (c == null && !cutscene)
        {
            ResetTakeover();
            return;
        }

        var camOverride = boundMain != null ? CameraPatches.CamOverride(boundMain) : null;
        // Binoculars stay first person: a zoomed view is impossible in a headset (MainCamera.cs:27 via
        // Action_ShowBinocularOverlay.cs:29); scripted overrides (Action_OverrideCamera.cs:7) are shown still.
        bool scripted = camOverride != null && camOverride is not CameraOverride_Binoculars;
        bool spectating = c != null && (c.data.fullyPassedOut || c.data.dead);
        bool rocketing = boundMovement != null && CameraPatches.IsRocketing(boundMovement);
        bool ragdoll =
            c != null
            && (
                c.data.passedOut
                || c.data.fallSeconds > 0f
                || c.data.currentRagdollControll < RagdollControlThreshold
            );
        var state = CameraPolicy.State(cutscene, scripted, spectating, rocketing, ragdoll);
        var kind = CameraPolicy.View(
            state,
            CameraFeature.TakeoverViewEntry.Value,
            CameraFeature.DetachOnRagdoll.Value,
            EventDetach
        );
        var target = c;
        if (state == CameraState.Spectate && MainCameraMovement.specCharacter != null)
            target = MainCameraMovement.specCharacter;
        Transform source =
            state == CameraState.Override ? camOverride.transform
            : state == CameraState.Cutscene ? FindCutsceneCamera()
            : null;

        bool want = CameraPolicy.IsDetached(kind);
        if (
            takeover.Detached
            && want
            && (kind != shownKind || target != shownTarget || source != staticSource)
        )
            takeover.Blink();
        if (pendingRecenter || pendingHeight)
            takeover.Blink();

        takeover.FadeSeconds = CameraFeature.FadeSeconds.Value;
        takeover.Update(want, dt);
        if (takeover.SwitchedThisFrame)
        {
            shownKind = takeover.Detached ? kind : ViewKind.FirstPerson;
            shownTarget = target;
            staticSource = source;
            staticPoint = source != null ? source.position : anchor;
            staticSourceLast = staticPoint;
            staticSince = Time.unscaledTime;
            FaceStaticView(source);
            snap = true;
            if (pendingRecenter)
                Recenter(c);
            else if (pendingHeight)
                CalibrateHeight();
            pendingRecenter = false;
        }
        if (takeover.Fade > 0f)
            VRFade.Set(this, takeover.Fade);
        else
            VRFade.Clear(this);

        float yaw = rig.Origin.eulerAngles.y;
        UpdateTurnShift(c, yaw, dt);
        if (!TryGoal(c, yaw, out var goal, out float rate, out float maxLag))
        {
            spectateFloor.Hide();
            return;
        }
        float verticalRate =
            shownKind == ViewKind.FirstPerson && c.data.isGrounded && !c.data.isClimbingAnything
                ? Mathf.Min(rate, GroundedVerticalRate)
                : rate;
        anchor = snap
            ? goal
            : RigMath.Follow(anchor.N(), goal.N(), dt, rate, verticalRate, maxLag).U();
        if (shownKind == ViewKind.FirstPerson && !snap)
            BodyAnchor = anchor;
        snap = false;

        var origin = RigMath.OriginPosition(
            anchor.N(),
            yaw,
            rig.RoomAnchor,
            RigMath.TrackedEyeHeight(rig.HeightOffset)
        );
        rig.Origin.SetPositionAndRotation(origin.U(), Quaternion.Euler(0f, yaw, 0f));

        // Spectating: the watched player's head and body never fill the eyes (a lean or a slow follow can carry the
        // view into them); a dead one is parked out of the world (Character.DeathPos, Character.cs:561), so skipped.
        bool watching = state == CameraState.Spectate && shownKind != ViewKind.FirstPerson;
        var push = Vector3.zero;
        if (watching && shownTarget != null && shownTarget != c && !shownTarget.data.dead)
        {
            var eye = rig.Head.position;
            push =
                RigMath.ClearOfBody(eye.N(), shownTarget.Head.N(), shownTarget.Center.N()).U()
                - eye;
        }
        if (!watching || takeover.SwitchedThisFrame)
        {
            // A new view starts behind a fade: no easing to carry over.
            clearShift = push;
            clearVelocity = Vector3.zero;
        }
        else
        {
            // Eased (critically damped), so the view does not shake 1:1 with the remote head's network jitter.
            clearShift = Vector3.SmoothDamp(
                clearShift,
                push,
                ref clearVelocity,
                ClearSmoothSeconds,
                float.PositiveInfinity,
                dt
            );
        }
        if (clearShift.sqrMagnitude > 1e-8f)
            rig.Origin.position += clearShift;
        spectateFloor.Tick(watching, rig);
    }

    /// <summary>
    /// Snap and smooth turns rotate the tracking space around the head (<see cref="VRRig.RotateYaw"/>), but the eye
    /// anchor sits ahead of the torso and swings round it with the rig heading. <see cref="turnShift"/> cancels that
    /// swing so the eye stays put while the body turns under it. The shift glides back onto the avatar's eyes only
    /// while the player walks (their own motion masks it, <see cref="RigMath.TurnSettleRate"/>) and drops at once
    /// behind a black fade; standing still it stays, capped at <see cref="MaxTurnShift"/>. Hands follow the view, so
    /// they stay where the controllers are.
    /// </summary>
    private void UpdateTurnShift(Character c, float yaw, float dt)
    {
        if (snap || shownKind != ViewKind.FirstPerson || c == null || VRFade.Target >= 0.99f)
        {
            // The view is being placed anyway (recenter, new view) or nothing is seen: start on the avatar's eyes.
            turnShift = Vector3.zero;
            turnYaw = yaw;
            turnHoldUntil = 0f;
            if (c != null)
                lastBodyEye = BodyEye(c, yaw);
            return;
        }
        var bodyEye = BodyEye(c, yaw);
        float move = c.input.movementInput.magnitude;
        if (Mathf.Abs(Mathf.DeltaAngle(turnYaw, yaw)) > 0.01f)
        {
            turnShift = RigMath.TurnShift(turnShift.N(), turnYaw, yaw, eyeOffset.X).U();
            turnYaw = yaw;
            turnHoldUntil = Time.unscaledTime + TurnHoldSeconds;
        }
        else if (Time.unscaledTime < turnHoldUntil && move <= 0.1f)
        {
            // SetRotation turns the animated rig at once (Character.cs:1545); the ragdoll torso the eye hangs on
            // follows over several frames and swings sideways. Hold the eye through that swing too.
            turnShift = RigMath.HoldLevel(turnShift.N(), lastBodyEye.N(), bodyEye.N()).U();
        }
        else
        {
            float rate = RigMath.TurnSettleRate(move, TurnSettleRate);
            if (rate > 0f)
                turnShift = RigMath.Follow(turnShift.N(), default, dt, rate, rate, -1f).U();
        }
        turnShift = Vector3.ClampMagnitude(turnShift, MaxTurnShift);
        lastBodyEye = bodyEye;
    }

    /// <summary>Self-test (vr.16): one line per frame of eye, rig and body yaw, torso and turn shift.</summary>
    private void TraceTurn(Character c)
    {
        if (Time.unscaledTime >= TraceTurnsUntil || c == null)
            return;
        var eye = rig.Head.position;
        var torso = c.Center;
        float bodyYaw =
            c.refs.rigCreator != null ? c.refs.rigCreator.transform.eulerAngles.y : float.NaN;
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"{Tag} turn f{Time.frameCount} eye ({eye.x:0.000},{eye.z:0.000}) rig {rig.Origin.eulerAngles.y:0.0} body {bodyYaw:0.0} torso ({torso.x:0.000},{torso.z:0.000}) shift ({turnShift.x:0.000},{turnShift.z:0.000}) offset {eyeOffset.X:0.000} hold {(Time.unscaledTime < turnHoldUntil ? 1 : 0)}"
            )
        );
    }

    /// <summary>
    /// The head can lean up to 1 m from a blocked body before room-scale recenters, so it can enter rock or a wall.
    /// Then the view fades to black and the camera stays on the body's side of the surface; the game's interact,
    /// thorn and climb-start rays start at the camera (Interaction.cs:204, :158, CharacterClimbing.cs:541), so a
    /// VR player cannot see, grab or climb through geometry.
    /// </summary>
    private void CheckHeadInWall(Character c)
    {
        headBlocked = false;
        if (c != null && !rig.OriginOverride && shownKind == ViewKind.FirstPerson && headTracked)
        {
            var from = c.Center;
            if (
                Physics.Linecast(
                    from,
                    rig.Head.position,
                    out var hit,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                headBlocked = true;
                blockedEye = Vector3.MoveTowards(hit.point, from, WallMargin);
            }
        }
        if (headBlocked)
            VRFade.Set(wallFadeOwner, 1f);
        else
            VRFade.Clear(wallFadeOwner);
    }

    /// <summary>Where the player's calibrated eyes go this frame, and how the anchor follows it.</summary>
    private bool TryGoal(Character c, float yaw, out Vector3 goal, out float rate, out float maxLag)
    {
        rate = CameraFeature.BodySmoothing.Value;
        maxLag = MaxBodyLag;
        goal = anchor;
        var target = shownTarget != null ? shownTarget : c;
        switch (shownKind)
        {
            case ViewKind.FirstPerson:
                if (c == null)
                    return false;
                goal = BodyEye(c, yaw) + turnShift + Vector3.up * rig.CrouchLift;
                return true;
            case ViewKind.FollowFirstPerson:
                if (target == null)
                    return false;
                goal = BodyEye(target, yaw);
                return true;
            case ViewKind.ThirdPerson:
                if (target == null)
                    return false;
                float distance = CameraFeature.ThirdPersonDistance.Value;
                goal = RigMath
                    .ThirdPersonPoint(
                        target.VirtualCenter.N(),
                        yaw,
                        distance,
                        CameraFeature.ThirdPersonHeight.Value
                    )
                    .U();
                rate = CameraFeature.ThirdPersonFollow.Value;
                maxLag = -1f;
                // A still or slow view that lost the body re-places itself behind a fade.
                if (!snap && Vector3.Distance(anchor, goal) > Mathf.Max(4f, 3f * distance))
                    takeover.Blink();
                return true;
            default:
                if (FollowCutscene && staticSource != null)
                {
                    goal = staticSource.position;
                    rate = CutsceneFollowRate;
                    maxLag = -1f;
                    return true;
                }
                // A still viewpoint: when the shot cuts or travels away, fade and re-place it.
                if (staticSource != null)
                {
                    var p = staticSource.position;
                    if (
                        RigMath.ReplaceStillView(
                            Vector3.Distance(p, staticSourceLast),
                            Vector3.Distance(p, staticPoint),
                            Time.unscaledTime - staticSince
                        )
                    )
                        takeover.Blink();
                    staticSourceLast = p;
                }
                goal = staticPoint;
                snap = true;
                return true;
        }
    }

    /// <summary>
    /// At black, turn the tracking space so the player faces where the cutscene camera looks (only yaw: the
    /// horizon stays level), and turn it back when the view returns to the body, so the character's heading
    /// is the same as before the shot.
    /// </summary>
    private void FaceStaticView(Transform source)
    {
        if (shownKind == ViewKind.Static && source != null)
        {
            if (!haveSavedYaw)
            {
                savedYaw = rig.Origin.eulerAngles.y;
                haveSavedYaw = true;
            }
            var fwd = source.forward;
            var hmd = rig.Head.localRotation * Vector3.forward;
            if ((fwd.x * fwd.x + fwd.z * fwd.z) > 1e-4f && (hmd.x * hmd.x + hmd.z * hmd.z) > 1e-4f)
            {
                float yaw = RigMath.RecenterYaw(
                    Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg,
                    Mathf.Atan2(hmd.x, hmd.z) * Mathf.Rad2Deg
                );
                rig.Origin.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            return;
        }
        if (shownKind != ViewKind.Static && haveSavedYaw)
        {
            rig.Origin.rotation = Quaternion.Euler(0f, savedYaw, 0f);
            haveSavedYaw = false;
        }
    }

    /// <summary>The avatar's calibrated eyes over its torso. For the local player the torso's lean is taken out
    /// (<see cref="AvatarPoseDriver.LeanShift"/>): the lean follows the real head, so letting it move the anchor would
    /// move the view after the head and lean the body further, a loop that slides the view.</summary>
    private Vector3 BodyEye(Character c, float yaw)
    {
        var center = c.VirtualCenter;
        if (c.IsLocal)
            center -= AvatarPoseDriver.LeanShiftOf(c);
        return RigMath.EyeAnchor(center.N(), yaw, eyeOffset).U();
    }

    /// <summary>The camera the game renders its end cutscene with (any enabled screen camera but ours).</summary>
    private Transform FindCutsceneCamera()
    {
        if (staticSource != null && staticSource.gameObject.activeInHierarchy)
            return staticSource;
        int count = Camera.allCamerasCount;
        if (cameraBuffer.Length < count)
            cameraBuffer = new Camera[count * 2];
        count = Camera.GetAllCameras(cameraBuffer);
        Transform found = null;
        for (int i = 0; i < count; i++)
        {
            var cam = cameraBuffer[i];
            if (found == null && IsSceneScreenCamera(cam))
                found = cam.transform;
            cameraBuffer[i] = null;
        }
        return found;
    }

    /// <summary>A camera of the loaded level that renders to the screen (cutscene cameras). Cameras in
    /// DontDestroyOnLoad or runtime scenes (build index -1) belong to PeakXR features (rig, loading space)
    /// and are left alone.</summary>
    private static bool IsSceneScreenCamera(Camera cam) =>
        cam.targetTexture == null && cam.gameObject.scene.buildIndex >= 0;

    private void BindMainCamera()
    {
        var main = MainCamera.instance;
        if (main == boundMain && (main == null || boundCam != null))
            return;
        UnbindMainCamera();
        boundMain = main;
        if (main == null)
            return;
        boundCam = CameraPatches.Cam(main);
        boundMovement = main.GetComponent<MainCameraMovement>();
        mainListener = main.GetComponent<AudioListener>();
        if (boundCam == null)
            return;
        savedNear = boundCam.nearClipPlane;
        boundCam.stereoTargetEye = StereoTargetEyeMask.Both;
        DisableAutoTracking(boundCam, true);
        CopyToFallback(boundCam);
        Plugin.Log.LogInfo($"{Tag} Driving '{main.name}' from the headset");
    }

    private void UnbindMainCamera()
    {
        if (boundCam != null)
        {
            boundCam.nearClipPlane = savedNear;
            DisableAutoTracking(boundCam, false);
        }
        boundMain = null;
        boundCam = null;
        boundMovement = null;
        mainListener = null;
    }

    private void CopyToFallback(Camera src)
    {
        // The loading space zeroes the game camera's mask while shown; keep the last real one (or Everything).
        if (src.cullingMask != 0)
            fallbackCam.cullingMask = src.cullingMask;
        fallbackCam.clearFlags = src.clearFlags;
        fallbackCam.backgroundColor = src.backgroundColor;
        fallbackCam.farClipPlane = src.farClipPlane;
        fallbackCam.depth = src.depth;
        var s = src.GetUniversalAdditionalCameraData();
        var d = fallbackCam.GetUniversalAdditionalCameraData();
        d.renderPostProcessing = s.renderPostProcessing;
        d.volumeLayerMask = s.volumeLayerMask;
        d.antialiasing = s.antialiasing;
        d.renderShadows = s.renderShadows;
    }

    private void DriveCamera()
    {
        bool mainLive = boundCam != null && boundMain.gameObject.activeInHierarchy;
        // Only when the game switched its camera off (end cutscene); with no game camera at all (between
        // scenes) whatever the UI shows keeps rendering.
        SetFallback(boundCam != null && !mainLive && VRSession.Active);
        float near = CameraFeature.NearClip.Value;
        if (mainLive)
        {
            if (boundCam.nearClipPlane != near)
                boundCam.nearClipPlane = near;
            boundCam.transform.SetPositionAndRotation(EyePosition(), rig.Head.rotation);
            rig.HeadCamera = boundCam;
        }
        else
        {
            fallbackCam.nearClipPlane = near;
            rig.HeadCamera = fallbackActive ? fallbackCam : null;
        }
        if (fallbackActive)
            KeepFallbackExclusive();
    }

    /// <summary>
    /// Switches headset rendering and the audio listener between the game's camera and the rig's own.
    /// While the rig's camera is in use, other screen cameras (cutscene cameras) stop rendering to the
    /// headset and their listeners are muted; voice chat distance follows the active listener.
    /// </summary>
    private void SetFallback(bool on)
    {
        if (on == fallbackActive)
            return;
        fallbackActive = on;
        fallbackCam.enabled = on;
        fallbackListener.enabled = on;
        if (on)
        {
            Plugin.Log.LogInfo($"{Tag} Game camera disabled (cutscene): rendering from the rig");
            nextListenerScan = 0f;
            KeepFallbackExclusive();
            PointVoiceAt(fallbackListener);
            return;
        }

        for (int i = 0; i < silencedCameras.Count; i++)
            if (silencedCameras[i] != null)
                silencedCameras[i].stereoTargetEye = StereoTargetEyeMask.Both;
        silencedCameras.Clear();
        for (int i = 0; i < mutedListeners.Count; i++)
            if (mutedListeners[i] != null)
                mutedListeners[i].enabled = true;
        mutedListeners.Clear();
        if (mainListener != null)
            PointVoiceAt(mainListener);
    }

    private void KeepFallbackExclusive()
    {
        int count = Camera.allCamerasCount;
        if (cameraBuffer.Length < count)
            cameraBuffer = new Camera[count * 2];
        count = Camera.GetAllCameras(cameraBuffer);
        for (int i = 0; i < count; i++)
        {
            var cam = cameraBuffer[i];
            cameraBuffer[i] = null;
            if (!IsSceneScreenCamera(cam) || cam.stereoTargetEye == StereoTargetEyeMask.None)
                continue;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            silencedCameras.Add(cam);
        }

        // Cutscene cameras switch on their own listeners by animation; check twice a second, not per frame.
        if (Time.unscaledTime < nextListenerScan)
            return;
        nextListenerScan = Time.unscaledTime + 0.5f;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (!l.enabled || l.gameObject.scene.buildIndex < 0)
                continue;
            l.enabled = false;
            mutedListeners.Add(l);
        }
    }

    private static void PointVoiceAt(AudioListener listener)
    {
        foreach (var v in FindObjectsByType<CharacterVoiceHandler>(FindObjectsSortMode.None))
            CameraPatches.VoiceListener(v) = listener;
    }

    /// <summary>The rig sets the camera pose itself; Unity must not also apply the HMD pose to it.</summary>
    private static void DisableAutoTracking(Camera cam, bool disabled)
    {
#pragma warning disable CS0618 // XRDevice lives in the deprecated VRModule, but it is the only switch for implicit camera tracking.
        XRDevice.DisableAutoXRCameraTracking(cam, disabled);
#pragma warning restore CS0618
    }
}
