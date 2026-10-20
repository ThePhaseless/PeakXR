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
/// effects, before the avatar (1000) reads the rig.
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

    /// <summary>Eye height above the torso until the first upright sample of the character.</summary>
    private const float DefaultEyeAboveCenter = 0.6f;

    private static RigDriver instance;

    private readonly TrackedDevices devices = new();
    private readonly CameraTakeover takeover = new();
    private readonly LongPress recenterPress = new();
    private readonly DesktopMirror mirror = new();
    private readonly List<Camera> silencedCameras = new();
    private readonly List<AudioListener> mutedListeners = new();
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
    private float eyeAboveCenter = DefaultEyeAboveCenter;
    private Vector3 anchor;
    private bool snap = true;

    private ViewKind shownKind;
    private Character shownTarget;
    private Transform staticSource;
    private Vector3 staticPoint;

    private bool needInitialRecenter = true;
    private bool pendingRecenter;
    private bool pendingHeight;
    private bool lastSeated;

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
        SetFallback(false);
        UnbindMainCamera();
        takeover.Reset(false);
        shownKind = ViewKind.FirstPerson;
        VRFade.Clear(this);
    }

    private void OnDestroy()
    {
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
            eyeAboveCenter = DefaultEyeAboveCenter;
            needInitialRecenter = c != null;
            snap = true;
        }
        UpdateEyeOffset(c, dt);
        HandleRecenterInput(c, dt);

        if (rig.OriginOverride)
            ResetTakeover();
        else
            PlaceOrigin(c, dt);

        DriveCamera();
    }

    /// <summary>Latest HMD pose right before rendering: the lowest-latency head tracking Unity offers.</summary>
    private void OnBeforeRender()
    {
        if (!VRSession.Active)
            return;
        UpdateHead();
        if (!fallbackActive && boundCam != null)
            boundCam.transform.SetPositionAndRotation(rig.Head.position, rig.Head.rotation);
    }

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
        if (devices.TryGetAim(left, out var p, out var r))
            aim.SetLocalPositionAndRotation(p, r);
        else if (h.Tracked)
            aim.SetLocalPositionAndRotation(h.Position, h.Rotation);
    }

    /// <summary>
    /// Learns how far above the torso the character's eyes are, from the head bone while the body stands
    /// upright (slow average, so walk bob does not reach the view). The vanilla eye point is
    /// <c>head.TransformPoint(up * 1 + forward * -0.5)</c> (Character.cs:1358); only its height above the
    /// head bone (the head's scale) is used, so head pitch from player.look cannot move the view.
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
            || d.fallSeconds > 0f
            || d.currentRagdollControll < 0.9f
        )
            return;
        var head = CameraPatches.GetBodypart(c, BodypartType.Head);
        if (head == null)
            return;
        float sample = head.transform.position.y + head.transform.lossyScale.y - c.Center.y;
        if (!(sample > 0.1f && sample < 2f))
            return;
        eyeAboveCenter = haveEyeOffset
            ? Mathf.Lerp(eyeAboveCenter, sample, 1f - Mathf.Exp(-dt))
            : sample;
        haveEyeOffset = true;
    }

    private void HandleRecenterInput(Character c, float dt)
    {
        recenterPress.Seconds = CameraFeature.RecenterHoldSeconds.Value;
        if (
            recenterPress.Update(VRInput.Left.Menu || VRInput.Right.Menu, dt)
            || rig.RecenterRequested
        )
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
            CameraFeature.DetachOnRagdoll.Value
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
        if (!TryGoal(c, yaw, out var goal, out float rate, out float maxLag))
            return;
        anchor = snap ? goal : RigMath.Follow(anchor.N(), goal.N(), dt, rate, rate, maxLag).U();
        snap = false;

        var origin = RigMath.OriginPosition(
            anchor.N(),
            yaw,
            rig.RoomAnchor,
            RigMath.TrackedEyeHeight(rig.HeightOffset)
        );
        rig.Origin.SetPositionAndRotation(origin.U(), Quaternion.Euler(0f, yaw, 0f));
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
                goal = BodyEye(c);
                return true;
            case ViewKind.FollowFirstPerson:
                if (target == null)
                    return false;
                goal = BodyEye(target);
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
                if (
                    staticSource != null
                    && Vector3.Distance(staticSource.position, staticPoint) > 3f
                )
                    takeover.Blink();
                goal = staticPoint;
                snap = true;
                return true;
        }
    }

    private Vector3 BodyEye(Character c)
    {
        var center = c.VirtualCenter;
        return new Vector3(center.x, center.y + eyeAboveCenter, center.z);
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
            if (found == null && cam != fallbackCam && cam.targetTexture == null)
                found = cam.transform;
            cameraBuffer[i] = null;
        }
        return found;
    }

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
            boundCam.transform.SetPositionAndRotation(rig.Head.position, rig.Head.rotation);
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
            if (
                cam == fallbackCam
                || cam.targetTexture != null
                || cam.stereoTargetEye == StereoTargetEyeMask.None
            )
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
            if (l == fallbackListener || !l.enabled)
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
