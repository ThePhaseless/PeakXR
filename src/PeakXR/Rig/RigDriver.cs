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

    /// <summary>Eyes in front of (x) the mass centre and above (y) the hip until the first upright sample of the character; a
    /// rough guess, the first sample replaces it at once.</summary>
    private static readonly System.Numerics.Vector2 DefaultEyeOffset = new(0.1f, 0.8f);

    /// <summary>Eyes in front of and above the torso (the root while the body is pitched), same rough guess.</summary>
    private static readonly System.Numerics.Vector2 DefaultTorsoEyeOffset = new(0.1f, 0.4f);

    /// <summary>Seconds the view root takes to blend between the mass centre and the torso.</summary>
    private const float RootBlendSeconds = 0.3f;

    /// <summary>The eyes are sampled only while the look is within about 20 degrees of level: the animated head
    /// pitches with the look (Look Y), which would carry the eye renderers down and forward.</summary>
    private const float LevelLookY = 0.35f;

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

    /// <summary>Called with <see cref="BodyAnchor"/> the frame it is set, before the rig origin is placed, so
    /// player.locomotion can absorb the body's travel into <see cref="VRRig.RoomAnchor"/> in the same frame.</summary>
    internal static System.Action<Vector3> BodyAnchorMoved;

    /// <summary>World offset of the first-person view from the avatar's calibrated eyes that is not the player's head
    /// motion: follow lag, the turn shift and the crouch lift. avatar.local leans the body from the head minus this,
    /// so stick walking or a snap turn does not lean the avatar; also <see cref="LeanNeutral"/>. Zero while the view
    /// is elsewhere.</summary>
    internal static Vector3 ViewOffset;

    /// <summary>Tracking-space head offset that counts as upright (<see cref="RoomScaleFollow.Neutral"/>, written by
    /// player.locomotion): part of <see cref="ViewOffset"/>, so the avatar straightens after standing back up from a
    /// follow without the body walking back.</summary>
    internal static System.Numerics.Vector2 LeanNeutral;

    /// <summary>player.locomotion's room-scale follow is walking the body under a still head, or settling from it:
    /// the view's height holds (<see cref="RigMath.FollowVerticalRate"/>).</summary>
    internal static bool FollowWalking;

    /// <summary>The local avatar's calibrated, unleaned eyes this frame (the point the first-person view rides before
    /// follow lag); null while the view is elsewhere. avatar.local leans from the head's offset to this point.</summary>
    internal static Vector3? LeanEye;

    /// <summary>The local avatar's eyes have been measured (avatar.local holds the lean until then).</summary>
    internal static bool EyeCalibrated => instance != null && instance.massEyes.Converged;

    /// <summary>The view rides the local body (first person). False while it is detached (third person, spectate,
    /// rocket, cutscene) or held by the title stage: the tracked hands are then near the view, not the body.</summary>
    internal static bool ViewOnBody = true;

    /// <summary>A takeover view is off the own head (the chase view, or another player's eyes while spectating),
    /// not the title stage, a rocket's own-eyes view or a cutscene shot: avatar.local shows the whole body, head
    /// included, meanwhile.</summary>
    internal static bool ViewDetached;

    /// <summary>World offset the idle-sway hold (<see cref="ViewHold"/>) keeps the view off the body's eye anchor this
    /// frame, up to its deadband; zero when not holding. Read by the self-test, which judges eye placement without it.</summary>
    internal static Vector3 ViewHeld;

    /// <summary>The eye guard (finding: warps into rock) wants black; read by the self-test.</summary>
    internal static bool EyeGuardBlack => instance != null && instance.eyeGuard.Black;

    /// <summary>The eye guard is watching a fresh view (a spawn, warp, jump or takeover): the body and the view are
    /// still settling, so avatar.local does not learn the player's reach meanwhile.</summary>
    internal static bool ViewSettling => instance != null && instance.eyeGuard.Watching;

    /// <summary>Whether a point is inside level geometry: within <see cref="EyeRadius"/> of a surface, or inside a
    /// closed mesh (the first face above it is a back face, and the same collider shows a back face below it too).
    /// Also read by the self-test.</summary>
    internal static bool EyeInside(Vector3 eye) => EyeInside(eye, out _);

    /// <inheritdoc cref="EyeInside(Vector3)"/>
    /// <param name="eye">The point to test.</param>
    /// <param name="by">The collider the eye is in or against (for the log); null when clear.</param>
    internal static bool EyeInside(Vector3 eye, out Collider by)
    {
        by = null;
        if (
            Physics.OverlapSphereNonAlloc(
                eye,
                EyeRadius,
                nearHits,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            ) > 0
        )
        {
            by = nearHits[0];
            return true;
        }
        // A point inside a non-convex mesh collider overlaps nothing, and a default ray from it passes the faces
        // turned away from it; with back faces on, the first face above is a back face (normal up). A one-sided
        // surface overhead (thin platform, ledge, upper floor) looks the same from below, so the same collider must
        // also close the point in from underneath: a back face (normal down) on a ray straight down.
        var backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            if (
                !Physics.Raycast(
                    eye,
                    Vector3.up,
                    out var above,
                    InsideProbeLength,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                || above.normal.y <= 0f
            )
                return false;
            int count = Physics.RaycastNonAlloc(
                eye,
                Vector3.down,
                insideHits,
                InsideProbeLength,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
                if (insideHits[i].collider == above.collider && insideHits[i].normal.y < 0f)
                {
                    by = above.collider;
                    return true;
                }
            return false;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    private static readonly RaycastHit[] insideHits = new RaycastHit[16];
    private static readonly Collider[] nearHits = new Collider[1];

    /// <summary>An eye closer than this (m) to rock sees the inside of it (near clip 0.03 m).</summary>
    private const float EyeRadius = 0.05f;

    /// <summary>How far up (m) the inside probe looks for the back face of the rock around the eye.</summary>
    private const float InsideProbeLength = 30f;

    /// <summary>A view goal that moves this far (m) in one frame jumped (warp, revive, segment jump).</summary>
    private const float JumpDistance = 1.5f;

    /// <summary>The chase view stays this far (m) in front of rock between it and the watched body.</summary>
    private const float ChaseMargin = 0.3f;

    /// <summary>A chase view pulled closer than this (m) to the watched body rises above it instead.</summary>
    private const float ChaseMinDistance = 1f;

    private readonly EyeGuard eyeGuard = new();
    private readonly ChaseBlock chaseBlock = new();

    /// <summary>This frame's view goal before the chase pull-in: a jump of it is a warp, a pull-in is not.</summary>
    private Vector3 rawGoal;
    private Vector3 lastRawGoal;
    private Vector3 lastGoal;
    private bool haveLastGoal;
    private bool wasLoading;

    private readonly TrackedDevices devices = new();
    private readonly CameraTakeover takeover = new();
    private readonly LongPress recenterPress = new();
    private readonly HoldCue holdCue = new();
    private readonly WallCue wallCue = new();
    private bool recenterLeft = true;

    /// <summary>The recenter hold ring, for the self-test (vr.185); null without a rig driver.</summary>
    internal static HoldCue RecenterCue => instance != null ? instance.holdCue : null;
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
    private float nextListenerFind;
    private AudioListener[] sceneListeners = System.Array.Empty<AudioListener>();

    private Character boundCharacter;

    /// <summary>The eyes over the mass-centre root (upright) and over the torso (pitched bodies), learned together.</summary>
    private readonly EyeCalibration massEyes = new(DefaultEyeOffset);
    private readonly EyeCalibration torsoEyes = new(DefaultTorsoEyeOffset);

    /// <summary>A first eye sample that would visibly move the view, waiting for the blink it lands behind
    /// (<see cref="EyeCalibration.FirstSampleNeedsBlink"/>); applied when the takeover switches at black.</summary>
    private bool pendingEyes;
    private System.Numerics.Vector2 pendingMassEyes,
        pendingTorsoEyes;
    private string pendingEyesLog;

    /// <summary>1: the view rides the mass centre (upright on foot, where the lean runs); 0: the torso, as before the
    /// lean. On a pitched body (climbing, hanging, gliding, ragdolled) a root at the hip puts the eyes 0.8 (1 - cos
    /// pitch) off, twice the torso's error, so the root blends over <see cref="RootBlendSeconds"/>, never in a frame.</summary>
    private float rootBlend = 1f;

    /// <summary>The eye offset the view uses now (forward is what a turn swings).</summary>
    private System.Numerics.Vector2 EyeOffsetNow =>
        System.Numerics.Vector2.Lerp(torsoEyes.Offset, massEyes.Offset, rootBlend);
    private Vector3 anchor;
    private readonly ViewMotionLimit chaseLimit = new();
    private Vector3 lastOriginWritten;
    private bool haveOriginWritten;

    /// <summary>
    /// One LateUpdate's view motion, recorded where it is made so a reader pairs each move with its own frame time
    /// (a coroutine samples the previous LateUpdate's write with the next frame's dt, which turns frame-time jitter
    /// into fake jolts). <c>Anchor</c> is the follow (limited while chasing); <c>Origin</c> the rig origin's move
    /// this frame, including every other writer (room anchor, turn, the spectate push).
    /// </summary>
    internal struct ViewStepTrace
    {
        public int Frame;
        public float Dt;
        public Vector3 Anchor;
        public Vector3 Origin;
        public Vector3 ChaseVelocity;
        public bool Chasing;
        public bool Snap;
    }

    /// <summary>The last LateUpdate's <see cref="ViewStepTrace"/> (self-test).</summary>
    internal static ViewStepTrace LastViewStep;
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

    /// <summary>Self-test (vr.45, vr.15): while set, rig.camera, avatar.local and player.locomotion log the lean
    /// chain every <see cref="LeanTraceEvery"/> s (unscaled), so a run shows which stage did not happen.</summary>
    internal static float TraceLeanUntil;

    internal const float LeanTraceEvery = 0.25f;

    /// <summary>True on the ticks a lean trace line is due for <paramref name="next"/>; advances it.</summary>
    internal static bool LeanTraceDue(ref float next)
    {
        float now = Time.unscaledTime;
        // Debug mode (Debug.Mode) keeps the lean trace running.
        if ((now >= TraceLeanUntil && !Diagnostics.DebugLog.Verbose) || now < next)
            return false;
        next = now + LeanTraceEvery;
        return true;
    }

    private float nextLeanTrace;

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
        StereoWidth.Install();
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
        LatePose.SetRig(OnBeforeRender);
        mirror.Enable();
        needInitialRecenter = true;
        snap = true;
    }

    private void OnDisable()
    {
        LatePose.SetRig(null);
        mirror.Disable();
        holdCue.Show(null, null, 0f, 0f);
        wallCue.Show(null, null, false, Vector3.zero);
        spectateFloor.Hide();
        SetFallback(false);
        UnbindMainCamera();
        takeover.Reset(false);
        shownKind = ViewKind.FirstPerson;
        VRFade.Clear(this);
        VRFade.Clear(wallFadeOwner);
        headBlocked = false;
        eyeGuard.Reset();
        chaseBlock.Reset();
        haveLastGoal = false;
        ViewDetached = false;
        BodyAnchor = null;
    }

    private void OnDestroy()
    {
        holdCue.Destroy();
        wallCue.Destroy();
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
            massEyes.Reset();
            torsoEyes.Reset();
            pendingEyes = false;
            standingEye.Reset();
            needInitialRecenter = c != null;
            snap = true;
            // A spawn or respawn can put the body (and the eyes) in rock.
            if (c != null)
                eyeGuard.Arm();
        }
        bool eyeSampled = UpdateEyeOffset(c, dt, out var eyePoint);
        MeasureStandingEye(c, eyeSampled, eyePoint);
        HandleRecenterInput(c, dt);
        float upright = c != null && Upright(c) ? 1f : 0f;
        rootBlend = snap ? upright : Mathf.MoveTowards(rootBlend, upright, dt / RootBlendSeconds);

        BodyAnchor = null;
        ViewOffset = Vector3.zero;
        LeanEye = null;
        if (rig.OriginOverride)
        {
            // The title stage and its menus stand at the game's own size; the scale returns behind the load.
            ApplyScale(1f);
            rig.Scale = 1f;
            haveScale = false;
            ResetTakeover();
            // The stage places the origin itself (TitleStage re-latches on Recentered).
            if (pendingRecenter)
            {
                pendingRecenter = false;
                rig.RaiseRecentered();
            }
        }
        else
            PlaceOrigin(c, dt);
        ViewOnBody = !rig.OriginOverride && shownKind == ViewKind.FirstPerson;
        // Off the own head only: the chase view, or another player's eyes. A rocket's FollowFirstPerson rides the
        // own eyes (target is the local player), and Static cutscene shots keep vanilla's hidden body.
        ViewDetached =
            !rig.OriginOverride
            && (
                shownKind == ViewKind.ThirdPerson
                || shownKind == ViewKind.FollowFirstPerson
                    && shownTarget != null
                    && shownTarget != c
            );

        CheckHeadInWall(c, dt);
        DriveCamera();
        TraceTurn(c);
        TraceLeanView(c);
    }

    /// <summary>Lean trace, view side: where the view root sits against the hip and torso (forward, right in the
    /// look frame), the root blend, the view offset the lean ignores, the calibrated eyes, and the rig origin.</summary>
    private void TraceLeanView(Character c)
    {
        if (c == null || c.refs.hip == null || !LeanTraceDue(ref nextLeanTrace))
            return;
        var f = c.data.lookDirection_Flat;
        if (f.sqrMagnitude < 1e-4f)
            return;
        f.Normalize();
        var r = new Vector3(f.z, 0f, -f.x);
        var root = ViewRoot(c);
        var hip = c.refs.hip.Rig.position;
        var torso = c.Center;
        var origin = rig.Origin.position;
        var eyes = EyeOffsetNow;
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"{Tag} lean-view t{Time.unscaledTime:0.00} root ({root.x:0.000},{root.z:0.000}) root-hip f{Vector3.Dot(root - hip, f):0.000} r{Vector3.Dot(root - hip, r):0.000} torso-hip f{Vector3.Dot(torso - hip, f):0.000} r{Vector3.Dot(torso - hip, r):0.000} blend {rootBlend:0.00} viewOffset f{Vector3.Dot(ViewOffset, f):0.000} r{Vector3.Dot(ViewOffset, r):0.000} neutral ({LeanNeutral.X:0.000},{LeanNeutral.Y:0.000}) eyes f{eyes.X:0.000} u{eyes.Y:0.000} converged {massEyes.Converged} origin ({origin.x:0.000},{origin.z:0.000}) head-eye {Vector3.Distance(rig.Head.position, BodyEye(c, rig.Origin.eulerAngles.y)):0.000} view-hold {viewHoldWhy ?? "on"} {viewHold.Offset:0.000}"
            )
        );
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

    private bool haveScale;

    /// <summary>The scout's standing eye height over the ground (game units) for <c>Camera.WorldScaleMode</c> Auto:
    /// measured over a steady stand on flat ground (<see cref="MeasureStandingEye"/>), kept across scenes (every
    /// scout is built the same), the calibration's default until the first measure.</summary>
    private static float avatarStandingEye = HeightCalibration.DefaultStandingEyeHeight;

    /// <summary>World units per real metre the settings ask for now. Auto: the calibrated standing eye height (a
    /// standing adult's in seated mode or without a floor-level tracking space, where none is known) over the
    /// scout's, so head-to-floor is the same in the room and in the game. Its inputs only change on a calibration,
    /// a seated switch, a standing-eye measure past its deadband or a setting, and each change lands behind a blink.</summary>
    private float WantScale()
    {
        if (CameraFeature.WorldScaleModeValue == WorldScaleMode.Manual)
            return WorldScale.RigScale(CameraFeature.WorldScaleValue);
        return WorldScale.RigScale(WorldScale.Auto(AutoRealEye(), avatarStandingEye));
    }

    private float AutoRealEye() =>
        rig.SeatedMode || !XRRuntimeInfo.FloorTracking
            ? HeightCalibration.DefaultStandingEyeHeight
            : RigMath.TrackedEyeHeight(rig.HeightOffset);

    private void LogScale()
    {
        if (CameraFeature.WorldScaleModeValue == WorldScaleMode.Manual)
            Plugin.Log.LogInfo(
                $"{Tag} World scale {CameraFeature.WorldScaleValue:0.00} (manual): rig x{rig.Scale:0.00}"
            );
        else
            Plugin.Log.LogInfo(
                $"{Tag} World scale auto {1f / rig.Scale:0.00}: real eyes {AutoRealEye():0.00} m{(rig.SeatedMode ? " (seated: a standing adult's)" : "")} over the scout's standing {avatarStandingEye:0.000} ({(haveStandingEye ? "measured" : "default until measured")}); rig x{rig.Scale:0.000}{(haveStandingEye ? ", the game floor on the real floor" : "")}"
            );
    }

    /// <summary>The tracking space's world size (<see cref="VRRig.Scale"/>): head motion, hand reach and, through
    /// <see cref="StereoWidth"/>, the eye separation all scale together.</summary>
    private void ApplyScale(float scale)
    {
        var s = Vector3.one * scale;
        if (rig.Origin.localScale == s)
            return;
        rig.Origin.localScale = s;
        // Cameras stay unscaled (the fallback rides the head): StereoWidth widens the eyes, once.
        if (fallbackCam != null)
            fallbackCam.transform.localScale = Vector3.one / scale;
    }

    private void UpdateHead()
    {
        // A scripted self-test head (vr.45 leans, vr.40 crouches) must move the view and the avatar's lean like a
        // real one; run 26 read the static HMD here, so the avatar never saw the lean the hands and room-scale did.
        if (
            VRInput.HeadTracked
            && VRInput.TestOverride == null
            && devices.TryGetHead(out var p, out var r)
        )
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
        // The one place tracked hands become the rig's: grabs, gestures, the avatar arm and the net pose all read
        // these transforms, so the seated reach gain lands everywhere once. The aim moves with the grip.
        var drop = Vector3.zero;
        if (h.Tracked)
        {
            drop.y = SeatedReachY(h.Position.y) - h.Position.y;
            grip.SetLocalPositionAndRotation(h.Position + drop, h.Rotation);
        }
        // Without an aim pose of its own, a hand aims from its grip: a runtime (the OpenVR plugin) out of the
        // controller's front (GripFrame.AimFromGrip), a scripted self-test hand along its fingers (GripFrame.FromHand,
        // SelfTestRunner.HandLook), never up the handle.
        if (
            VRInput.TestOverride == null
            && h.Tracked
            && devices.TryGetAim(left, out var p, out var r)
        )
            aim.SetLocalPositionAndRotation(p + drop, r);
        else if (h.Tracked)
            aim.SetLocalPositionAndRotation(
                h.Position + drop,
                h.Rotation * (VRInput.TestOverride == null ? AimFromGrip : AimFromHand)
            );
    }

    private static readonly Quaternion AimFromGrip = GripFrame.AimFromGrip.U();
    private static readonly Quaternion AimFromHand = GripFrame.FromHand.U();

    /// <summary>Tracking-space hand Y after the seated reach gain (identity standing, at gain 1 or outside VR).</summary>
    private float SeatedReachY(float y)
    {
        if (!VRSession.Active || !rig.SeatedMode)
            return y;
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        // shortcut: the virtual floor assumes the avatar's eyes 1.65 m over its feet, as the height calibration does;
        // read the feet if a character's proportions ever differ.
        return SeatedReach.Y(
            y,
            eye,
            eye - HeightCalibration.DefaultStandingEyeHeight,
            rig.SeatedReachGain,
            true
        );
    }

    /// <summary>
    /// Learns where the character's eyes are relative to the torso (forward and up in its facing frame) while the
    /// body stands upright and looks level, as a slow average so walk bob does not reach the view. The eyes are the
    /// face's eye renderers (CustomizationRefs.EyeRenderers, CharacterCustomization.cs:282). Not the vanilla camera
    /// point <c>head.TransformPoint(up * 1 + forward * -0.5)</c> (Character.cs:1355): that sits on top of the head,
    /// about 0.2 m above the eyes, and straight over the torso, so looking down showed the top of your own body.
    /// </summary>
    private bool UpdateEyeOffset(Character c, float dt, out Vector3 eye)
    {
        eye = Vector3.zero;
        if (c == null)
            return false;
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
            // The first sample (taken whole) waits for a still stand, past the beach wake-up: the first upright frame after
            // the wake-up fall reads a body still rising (eyes 1.32-1.43 m over the ground against 1.77 m standing),
            // and the calibration kept what that frame said (run 21: eyes 0.16 m ahead of the mass centre on that
            // frame, 0.12 m for the whole beach, a taller back-tilted stand and a hip bobbing 0.03 m; run 20: 0.25 m).
            || !massEyes.Have
                && (
                    d.passedOutOnTheBeach > 0f
                    || d.avarageVelocity.sqrMagnitude >= StillSpeed * StillSpeed
                )
        )
            return false;
        // Only the unleaned eyes (EyeCalibration): any lean puts them on the headset, and sampling that would carry
        // the head's offset into the anchor and slide the view after the head. The lean waits for convergence.
        var driver = AvatarPoseDriver.Of(c);
        bool leaning = driver != null && driver.LeanWeight > 0f;
        if (leaning || !TryEyePoint(c, out eye))
            return false;
        float facing = Mathf.Atan2(d.lookDirection_Flat.x, d.lookDirection_Flat.z) * Mathf.Rad2Deg;
        var sample = RigMath.EyeOffset(eye.N(), ViewRoot(c).N(), facing);
        var torsoSample = RigMath.EyeOffset(eye.N(), c.Center.N(), facing);
        if (!(sample.Y > 0.1f && sample.Y < 2f && Mathf.Abs(sample.X) < 0.5f))
            return false;
        // One first sample waits for its blink; newer ones would not match the line logged for it.
        if (pendingEyes)
            return true;
        bool first = !massEyes.Have;
        // The first sample replaces the starting guess outright: a glide from the guess is the slow drift the still
        // stand gate exists to avoid. It comes long after the view was placed, so a change big enough to see lands
        // behind a blink (PlaceOrigin applies it at black), never as a jump in view; later samples glide.
        bool blink =
            first
            && (
                massEyes.FirstSampleNeedsBlink(sample)
                || torsoEyes.FirstSampleNeedsBlink(torsoSample)
            );
        if (blink)
        {
            pendingMassEyes = sample;
            pendingTorsoEyes = torsoSample;
            takeover.Blink();
        }
        else
        {
            massEyes.Sample(sample, leaning, dt);
            torsoEyes.Sample(torsoSample, leaning, dt);
        }
        if (first)
        {
            // The ground straight under the eyes: data.groundPos is one ground contact (CharacterMovement.cs:722-751)
            // and can sit on a step or slope edge (run 6 logged 1.65 m eyes in one scene and 1.46 m in the next).
            float ground = Physics.Raycast(
                eye,
                Vector3.down,
                out var under,
                4f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
                ? under.point.y
                : d.groundPos.y;
            // The first sample only: the standing height is measured over a steady stand (MeasureStandingEye).
            var line =
                $"{Tag} Eyes {sample.X:0.00} m ahead of the body's mass centre and {sample.Y:0.00} m above the hip, "
                + $"{eye.y - ground:0.00} m above the ground on the first still stand; the player's calibrated "
                + $"eyes {RigMath.TrackedEyeHeight(rig.HeightOffset):0.00} m";
            if (blink)
            {
                // Logged when it lands, once the blink is black.
                pendingEyes = true;
                pendingEyesLog = line + "; applied behind a blink";
            }
            else
                Plugin.Log.LogInfo(line + "; applied at once (too small a change to see)");
        }
        return true;
    }

    /// <summary>Ground at most this tilted (cos 8 degrees) counts as flat for the standing eye height.</summary>
    private const float FlatGroundY = 0.99f;

    /// <summary>Body speed (m/s) under which the scout counts as standing still.</summary>
    private const float StillSpeed = 0.2f;

    /// <summary>This character's standing eye height over flat ground (<see cref="StandingEye"/>).</summary>
    private readonly StandingEye standingEye = new();

    private static bool haveStandingEye;

    /// <summary>
    /// Measures the scout's standing eye height for the Auto world scale the way the floor is judged: its eyes to the
    /// ground under them, over a steady stand (upright as for the eye offset, still, no stick, flat ground that the
    /// game's own ground contact agrees with), as a median (<see cref="StandingEye"/>). Not the animation's head
    /// target (targetHeadHeight, CharacterRagdoll.cs:213-218): run 10's physics head sat 0.08-0.09 m under it in
    /// every scene (the standing force, CharacterMovement.cs:711-719, only pushes toward it), so the floor missed by
    /// that much, and the first upright frame at the beach, soon after its wake-up fall (Character.cs:2029-2035),
    /// read the scout 0.14 m lower than at the airport with bent legs (hip 0.43 m against 0.57 m over the ground,
    /// eyes 0.99 m over the hip in both). The session keeps one value; a later scene changes it only past <see cref="StandingEye.Deadband"/>.
    /// </summary>
    private void MeasureStandingEye(Character c, bool upright, Vector3 eye)
    {
        if (c == null || standingEye.Measured)
            return;
        var d = c.data;
        float height = float.NaN;
        if (
            upright
            && d.passedOutOnTheBeach <= 0f
            && d.groundNormal.y >= FlatGroundY
            && d.avarageVelocity.sqrMagnitude < StillSpeed * StillSpeed
            && c.input.movementInput.sqrMagnitude < 0.01f
            && Physics.Raycast(
                eye,
                Vector3.down,
                out var under,
                4f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            && under.normal.y >= FlatGroundY
            && Mathf.Abs(under.point.y - d.groundPos.y) < 0.05f
        )
            height = eye.y - under.point.y;
        if (!standingEye.Update(!float.IsNaN(height), height, Time.unscaledTime))
            return;
        float measured = standingEye.Value;
        float before = avatarStandingEye;
        avatarStandingEye = StandingEye.Keep(haveStandingEye ? before : float.NaN, measured);
        string kept =
            !haveStandingEye ? "first this session"
            : avatarStandingEye == before
                ? $"kept {before:0.00} (within {StandingEye.Deadband:0.00} m)"
            : $"changed from {before:0.00}";
        haveStandingEye = true;
        float ground = eye.y - height;
        float hip = c.refs.hip != null ? c.refs.hip.Rig.position.y - ground : float.NaN;
        var head = CameraPatches.GetBodypart(c, BodypartType.Head);
        float pose =
            head != null && head.Rig != null
                ? d.targetHeadHeight + eye.y - head.Rig.position.y
                : float.NaN;
        // The scale this asks for lands at the next blink; the floor line reads this scene's stand through it.
        float want = WantScale();
        bool auto = CameraFeature.WorldScaleModeValue == WorldScaleMode.Auto;
        Plugin.Log.LogInfo(
            $"{Tag} Scout stands with its eyes {measured:0.000} m over flat ground (median of {standingEye.Count} frames after {StandingEye.SettleSeconds:0} s still, spread {standingEye.Spread:0.000} m, hip {hip:0.00} m, animation head target {pose:0.00} m); using {avatarStandingEye:0.000} m, {kept}; "
                + $"world scale {1f / want:0.000} {(auto ? $"auto = {AutoRealEye():0.00} / {avatarStandingEye:0.000}" : "manual")}, rig x{want:0.000}: "
                + $"the game floor is reached {WorldScale.FloorReach(measured, AutoRealEye(), want):0.000} m above the real one{(auto ? " (auto: within 0.02)" : "")}"
        );
    }

    /// <summary>On foot and under control: the view may ride the mass centre and avatar.local may lean.</summary>
    internal static bool Upright(Character c)
    {
        var d = c.data;
        return !d.isClimbingAnything
            && d.currentClimbHandle == null
            && !(d.currentItem != null && d.currentItem.gliderHold && !d.isGrounded)
            && d.currentRagdollControll >= RagdollControlThreshold;
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
        // A Menu the bug-report chord holds is not a recenter hold.
        bool left = VRInput.Left.Menu && !Diagnostics.DebugGesture.MenuTaken(true),
            right = VRInput.Right.Menu && !Diagnostics.DebugGesture.MenuTaken(false);
        // Both sticks clicked: the recenter every controller has (a Touch Menu may never reach the game).
        bool sticks = XRInput.StickChordInput.Held;
        // The ring goes to the controller whose button is held; with both down it stays where it is.
        if (sticks && !left && !right)
            recenterLeft = VRInput.OffHandLeft;
        else if (left != right)
            recenterLeft = left;
        bool fired = recenterPress.Update(left || right || sticks, dt);
        holdCue.Show(
            rig.Hand(recenterLeft),
            rig.Head,
            recenterPress.HeldSeconds,
            recenterPress.Progress
        );
        if (fired || rig.RecenterRequested)
        {
            rig.RecenterRequested = false;
            pendingRecenter = true;
        }
        if (rig.SeatedMode != lastSeated)
            pendingHeight = true;
        if (headTracked)
            heightSession.Watch(rig.Head.localPosition.y, rig.SeatedMode, dt);
        if (needInitialRecenter && headTracked && c != null)
        {
            // First frame with a body: nothing to fade from yet (loading just ended).
            needInitialRecenter = false;
            Recenter(c, HeightCause.SceneLoad);
            pendingRecenter = false;
        }
    }

    /// <summary>The session's height rule: measured once, on request, or after a long wrong stand (survives scenes).</summary>
    private static readonly HeightSession heightSession = new();

    private void Recenter(Character c, HeightCause cause)
    {
        var hp = rig.Head.localPosition;
        rig.RoomAnchor = new System.Numerics.Vector2(hp.x, hp.z);
        // A scene load re-places and re-yaws only: a player seated, kneeling or leaning there keeps their height.
        bool measure = heightSession.ShouldMeasure(cause);
        if (measure)
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
            $"{Tag} Recentered ({cause}): head y {hp.y:0.00} m, height offset {rig.HeightOffset:0.00} m {(measure ? "measured" : "kept")}, "
                + $"floor tracking {XRRuntimeInfo.FloorTracking}, seated {rig.SeatedMode}"
        );
        rig.RaiseRecentered();
    }

    private void CalibrateHeight()
    {
        lastSeated = rig.SeatedMode;
        pendingHeight = false;
        heightSession.Measured(rig.Head.localPosition.y);
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
        // A new world scale lands behind the same blink as a recenter (Camera.WorldScale, WorldScaleMode).
        float wantScale = WantScale();
        if (!haveScale)
        {
            // The first placement: nothing to fade from yet.
            rig.Scale = wantScale;
            ApplyScale(rig.Scale);
            haveScale = true;
            LogScale();
        }
        bool pendingScale = !Mathf.Approximately(wantScale, rig.Scale);
        if (pendingRecenter || pendingHeight || pendingScale)
            takeover.Blink();

        takeover.FadeSeconds = CameraFeature.FadeSeconds.Value;
        // The drawn fade eases at UI.FadeSeconds, slower than this ramp: switch only once it is black.
        takeover.DrawnFade = UI.Common.FadeView.Alpha;
        takeover.Update(want, dt);
        if (takeover.SwitchedThisFrame)
        {
            var newKind = takeover.Detached ? kind : ViewKind.FirstPerson;
            // A new view (not a re-placing blink, which the eye guard itself can ask for) may start in rock.
            if (newKind != shownKind || target != shownTarget)
                eyeGuard.Arm();
            shownKind = newKind;
            shownTarget = target;
            staticSource = source;
            staticPoint = source != null ? source.position : anchor;
            staticSourceLast = staticPoint;
            staticSince = Time.unscaledTime;
            snap = true;
            if (pendingEyes)
            {
                pendingEyes = false;
                massEyes.Sample(pendingMassEyes, false, dt);
                torsoEyes.Sample(pendingTorsoEyes, false, dt);
                Plugin.Log.LogInfo(pendingEyesLog);
            }
            if (pendingRecenter)
            {
                Recenter(c, HeightCause.Recenter);
                // Back on the body, the recenter's yaw replaces the one saved before the shot.
                if (shownKind != ViewKind.Static)
                    haveSavedYaw = false;
            }
            else if (pendingHeight)
                CalibrateHeight();
            pendingRecenter = false;
            // After the recenter, which would otherwise turn a still shot's viewer away from the cutscene camera.
            FaceStaticView(source);
            // The recenter or calibration just above may have measured a new height: one blink carries both.
            wantScale = WantScale();
            if (!Mathf.Approximately(wantScale, rig.Scale))
            {
                rig.Scale = wantScale;
                ApplyScale(rig.Scale);
                LogScale();
            }
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
            haveLastGoal = false;
            return;
        }
        // Warps, revives and segment jumps move the body (and the goal) metres in one frame; the chase pull-in
        // starting or stopping moves only the pulled goal, which is not a jump.
        if (shownKind != ViewKind.ThirdPerson)
            rawGoal = goal;
        if (haveLastGoal && (rawGoal - lastRawGoal).sqrMagnitude > JumpDistance * JumpDistance)
            eyeGuard.Arm();
        lastGoal = goal;
        lastRawGoal = rawGoal;
        haveLastGoal = true;
        float verticalRate = RigMath.ViewVerticalRate(
            rate,
            shownKind == ViewKind.FirstPerson && c.data.isGrounded && !c.data.isClimbingAnything,
            FollowWalking
        );
        // The standing body's idle sway stays out of the view (ViewHold); goal keeps the body's own point, so
        // ViewOffset carries the held offset like follow lag and the lean never reads it as head motion.
        var viewGoal = goal;
        if (shownKind == ViewKind.FirstPerson && !snap)
        {
            var extra = turnShift + Vector3.up * rig.CrouchLift;
            viewHoldWhy = ViewHoldGate.Why(ViewHoldNow(c, dt));
            var body = (goal - extra).N();
            // A turn (and the torso swing after it) moves the eye anchor and turnShift cancels it: the hold neither
            // holds nor releases then, or the turn pivots off the eye (vr.16: 3.2 cm in run 45, 3.8 cm in run 47).
            // A lean after a room-scale walk bleeds its held offset into its own travel, so standing up leaves none
            // for the follow to walk back to (run 50); any other lean carries it whole (run 54). The episode ends once
            // the view has been held still for a while.
            if (FollowWalking)
            {
                bleedLean = true;
                heldSinceWalk = 0f;
            }
            else if (viewHoldWhy == null)
            {
                heldSinceWalk += dt;
                if (heldSinceWalk > BleedEpisodeSeconds)
                    bleedLean = false;
            }
            else
                heldSinceWalk = 0f;
            viewGoal =
                (
                    Time.unscaledTime < turnHoldUntil ? viewHold.Carry(body)
                    : ViewHoldGate.CarriesThrough(viewHoldWhy) ? viewHold.Lean(body, bleedLean)
                    : viewHold.Update(body, viewHoldWhy == null, dt)
                ).U() + extra;
            ViewHeld = viewGoal - goal;
        }
        else
        {
            viewHold.Reset();
            viewHoldWhy = "view";
            ViewHeld = Vector3.zero;
        }
        var before = anchor;
        anchor = snap
            ? goal
            : RigMath.Follow(anchor.N(), viewGoal.N(), dt, rate, verticalRate, maxLag).U();
        // The detached chase moves the view without the player (spectating, passed out, carried, a tumble, a launch):
        // artificial locomotion, so it eases in and out at a capped acceleration and speed (ViewMotionLimit). A body
        // that gets away re-places the view behind a fade (TryGoal's Blink), never a fast glide.
        bool chasing = shownKind == ViewKind.ThirdPerson && !snap;
        bool snapped = snap;
        if (chasing)
            anchor = chaseLimit.Step(before.N(), anchor.N(), viewGoal.N(), dt).U();
        else
            chaseLimit.Reset();
        LastViewStep = new ViewStepTrace
        {
            Frame = Time.frameCount,
            Dt = dt,
            Anchor = anchor - before,
            ChaseVelocity = chaseLimit.Velocity.U(),
            Chasing = chasing,
            Snap = snapped,
        };
        if (shownKind == ViewKind.FirstPerson && !snap)
        {
            BodyAnchor = anchor;
            // Room-scale absorbs this frame's body travel before the origin is placed from its anchor.
            BodyAnchorMoved?.Invoke(anchor);
        }
        if (shownKind == ViewKind.FirstPerson)
        {
            LeanEye = BodyEye(c, yaw);
            ViewOffset =
                anchor
                - goal
                + turnShift
                + Vector3.up * rig.CrouchLift
                + rig.Origin.TransformVector(new Vector3(LeanNeutral.X, 0f, LeanNeutral.Y));
        }
        snap = false;

        var origin = RigMath.OriginPosition(
            anchor.N(),
            yaw,
            rig.RoomAnchor,
            RigMath.TrackedEyeHeight(rig.HeightOffset),
            rig.Scale
        );
        ApplyScale(rig.Scale);
        rig.Origin.SetPositionAndRotation(origin.U(), Quaternion.Euler(0f, yaw, 0f));

        // Spectating: the watched player's head and body never fill the eyes (a lean or a slow follow can carry the
        // view into them); a dead one is parked out of the world (Character.DeathPos, Character.cs:558), so skipped.
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
        var written = rig.Origin.position;
        if (LastViewStep.Frame == Time.frameCount)
            LastViewStep.Origin = haveOriginWritten ? written - lastOriginWritten : Vector3.zero;
        lastOriginWritten = written;
        haveOriginWritten = true;
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
            turnShift = RigMath.TurnShift(turnShift.N(), turnYaw, yaw, EyeOffsetNow.X).U();
            turnYaw = yaw;
            turnHoldUntil = Time.unscaledTime + TurnHoldSeconds;
        }
        else if (Time.unscaledTime < turnHoldUntil && move <= 0.1f)
        {
            // SetRotation turns the animated rig at once (Character.cs:1542); the ragdoll torso the eye hangs on
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
                $"{Tag} turn f{Time.frameCount} eye ({eye.x:0.000},{eye.z:0.000}) rig {rig.Origin.eulerAngles.y:0.0} body {bodyYaw:0.0} torso ({torso.x:0.000},{torso.z:0.000}) shift ({turnShift.x:0.000},{turnShift.z:0.000}) offset {EyeOffsetNow.X:0.000} hold {(Time.unscaledTime < turnHoldUntil ? 1 : 0)} view-hold {viewHoldWhy ?? "on"} {viewHold.Offset:0.000}"
            )
        );
    }

    /// <summary>
    /// The head can lean up to 1 m from a blocked body before room-scale recenters, so it can enter rock or a wall.
    /// Then the view fades to black and the camera stays on the body's side of the surface; the game's interact,
    /// thorn and climb-start rays start at the camera (Interaction.cs:204, :158, CharacterClimbing.cs:541), so a
    /// VR player cannot see, grab or climb through geometry. The chase view does the same against the watched
    /// body, and when its anchor still trails behind the rock it is re-placed (in front of it) behind a blink.
    /// After a jump (<see cref="EyeGuard"/>: warp, revive, segment jump, spawn, new takeover view) an eye found
    /// inside rock also holds black, without moving the view, until the body settles out of it.
    /// </summary>
    private void CheckHeadInWall(Character c, float dt)
    {
        headBlocked = false;
        bool chase = shownKind == ViewKind.ThirdPerson;
        bool onBody = shownKind == ViewKind.FirstPerson || shownKind == ViewKind.FollowFirstPerson;
        var watched = shownTarget != null ? shownTarget : c;
        // Spawns land while the loading screen still shows (yieldForCharacterSpawn): watch from when it lifts.
        bool loading = LoadingScreenHandler.loading;
        if (wasLoading && !loading && c != null)
            eyeGuard.Arm();
        wasLoading = loading;
        bool live = c != null && !rig.OriginOverride && headTracked && !loading;
        // A dead body is parked out of the world (Character.DeathPos, Character.cs:558): nothing to test against.
        bool chaseLive = chase && watched != null && !watched.data.dead;
        if (live && (shownKind == ViewKind.FirstPerson || chaseLive))
        {
            var from = chase ? watched.VirtualCenter : c.Center;
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
        if (chase)
        {
            // A body tumbling past boulders or a climber on a ledge passes behind rock for a moment all the time:
            // only a lasting block hides the view, and a trailing view re-places at most once per interval.
            if (
                chaseBlock.Update(dt, headBlocked, Vector3.Distance(anchor, lastGoal) > ChaseMargin)
            )
                takeover.Blink();
            headBlocked = chaseBlock.Blocked;
        }
        else
        {
            chaseBlock.Update(dt, false, false);
        }
        bool wasBlack = eyeGuard.Black;
        var eye = EyePosition();
        Collider rock = null;
        bool inside = live && (onBody || chase) && eyeGuard.Watching && EyeInside(eye, out rock);
        eyeGuard.Update(dt, inside);
        if (eyeGuard.Black != wasBlack)
            Plugin.Log.LogInfo(
                eyeGuard.Black
                    ? $"{Tag} Eye inside rock after a jump ({shownKind}): holding black; eye {eye}, in {(rock != null ? rock.name + " (" + LayerMask.LayerToName(rock.gameObject.layer) + ")" : "?")}"
                : inside
                    ? $"{Tag} Eye still inside rock after {eyeGuard.MaxHoldSeconds:0} s: view back anyway"
                : $"{Tag} Eye clear of rock: view back"
            );
        if (headBlocked || eyeGuard.Black)
            VRFade.Set(wallFadeOwner, 1f);
        else
            VRFade.Clear(wallFadeOwner);
        // Say why it is black: toward the body is open space for a head leaned into rock; the chase view and the
        // eye guard clear by themselves, so they get the ring alone.
        bool leaned = headBlocked && !chase && c != null;
        wallCue.Show(
            rig.Head,
            rig.HeadCamera != null ? rig.HeadCamera.transform : null,
            headBlocked || eyeGuard.Black,
            leaned ? c.Center - rig.Head.position : Vector3.zero
        );
    }

    /// <summary><paramref name="to"/>, or pulled toward <paramref name="from"/> to stop <see cref="ChaseMargin"/>
    /// short of rock between them.</summary>
    private static Vector3 ClearChase(Vector3 from, Vector3 to) =>
        Physics.Linecast(
            from,
            to,
            out var wall,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? RigMath.PullIn(from.N(), to.N(), wall.distance, ChaseMargin).U()
            : to;

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
                // Rock between the body and the chase point: the point comes in front of it, so a view re-placed
                // behind a fade (CheckHeadInWall) never lands inside the rock. Pulled in to the body (a wall at its
                // back), it rises above the body instead, so the view never sits inside the watched character.
                rawGoal = goal;
                var from = target.VirtualCenter;
                goal = ClearChase(from, goal);
                if (Vector3.Distance(from, goal) < ChaseMinDistance)
                    goal = ClearChase(
                        from,
                        from + Vector3.up * Mathf.Max(ChaseMinDistance, distance)
                    );
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

    private readonly ViewHold viewHold = new();

    /// <summary>This frame's closed <see cref="ViewHoldGate"/>, or null while the view holds. Traced.</summary>
    private string viewHoldWhy;

    private readonly LeanMotion leanMotion = new();

    /// <summary>A room-scale walk happened since the view was last held still for <see cref="BleedEpisodeSeconds"/>:
    /// leans bleed the held offset (<see cref="ViewHold.Lean"/>) instead of carrying it.</summary>
    private bool bleedLean;
    private float heldSinceWalk;

    /// <summary>Seconds of a held view that end a room-scale walk's episode: longer than a held far lean (2.5 s in
    /// vr.45), so its stand-up still bleeds.</summary>
    private const float BleedEpisodeSeconds = 3f;

    /// <summary>The local body for <see cref="ViewHoldGate"/>: the lean's body gate (grounded, upright, no stick,
    /// not carried, swimming, warping, sliding or bouncing), how fast the lean moves the eyes, the mean part speed,
    /// a jump, a room-scale walk. Only this player's view reads it; the body and what peers see are untouched.</summary>
    private ViewHoldInputs ViewHoldNow(Character c, float dt)
    {
        var d = c.data;
        var driver = AvatarPoseDriver.Of(c);
        return new ViewHoldInputs
        {
            BodyFree = HoldGate.Free(AvatarPoseDriver.GateInputs(c)),
            LeanSpeed = leanMotion.Update(
                (driver != null ? driver.EyeLeanShift : Vector3.zero).N(),
                dt
            ),
            Speed = d.avarageVelocity.magnitude,
            Jumping = d.isJumping || (c.input != null && c.input.jumpWasPressed),
            RoomWalking = FollowWalking,
        };
    }

    /// <summary>The avatar's calibrated eyes: for the local player over <see cref="ViewRoot"/>, blended toward the
    /// torso by <see cref="rootBlend"/>; for a watched player over the torso.</summary>
    private Vector3 BodyEye(Character c, float yaw)
    {
        var torso = RigMath.EyeAnchor(c.VirtualCenter.N(), yaw, torsoEyes.Offset).U();
        if (c != Character.localCharacter || rootBlend <= 0f)
            return torso;
        var mass = RigMath.EyeAnchor(ViewRoot(c).N(), yaw, massEyes.Offset).U();
        return rootBlend >= 1f ? mass : Vector3.Lerp(torso, mass, rootBlend);
    }

    private static Character rootCharacter;
    private static int rootFrame = -1;
    private static Vector3 rootCached;

    /// <summary>
    /// The point the first-person view rides: the ragdoll's level mass centre at hip height (<see cref="MassCentre"/>).
    /// Under control the parts are slippery (CharacterRagdoll.cs:93-100) and only pull on each other, with half of each
    /// pull pushed back onto the hip (Bodypart.cs:193-205), so bending forward (the game's look-down pose or
    /// avatar.local's lean) slides the hips back and the torso forward while this point stays: the view never moves
    /// unless the head or the whole body does. Height comes from the hip, which the stand force holds
    /// (CharacterMovement.cs:711-720). Dead or warping characters keep the game's stand-in point
    /// (<c>Character.VirtualCenter</c>, Character.cs:256). A held item welded to a hand (CharacterItems.cs:647-650,
    /// or avatar.local's one-hand weld) is part of the same system and counts. Outside forces do move it, as they
    /// should: walking, a push, or a wall or rock the body leans against; nothing here fights that contact.
    /// Computed once per frame (cached for the last character asked).
    /// </summary>
    internal static Vector3 ViewRoot(Character c)
    {
        if (c == rootCharacter && Time.frameCount == rootFrame)
            return rootCached;
        var parts = c.refs.ragdoll != null ? c.refs.ragdoll.partList : null;
        var hip = c.refs.hip;
        if (parts == null || hip == null)
            return c.VirtualCenter;
        var centre = new MassCentre();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var body = part != null ? part.Rig : null;
            // The interpolated pose, like everything the view shows; Rigidbody.worldCenterOfMass is the 50 Hz state.
            if (body != null)
                centre.Add(body.transform.TransformPoint(body.centerOfMass).N(), body.mass);
        }
        var item = c.data.currentItem;
        var itemBody = item != null ? item.rig : null;
        if (
            itemBody != null
            && (
                Welded(c, BodypartType.Hand_L, itemBody) || Welded(c, BodypartType.Hand_R, itemBody)
            )
        )
            centre.Add(itemBody.transform.TransformPoint(itemBody.centerOfMass).N(), itemBody.mass);
        rootCharacter = c;
        rootFrame = Time.frameCount;
        rootCached = centre.Root(hip.transform.position.y).U() + (c.VirtualCenter - c.Center);
        return rootCached;
    }

    private static bool Welded(Character c, BodypartType hand, Rigidbody item) =>
        c.refs.ragdoll.partDict.TryGetValue(hand, out var part)
        && part.Rig != null
        && part.Rig.TryGetComponent(out FixedJoint joint)
        && joint.connectedBody == item;

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
        // Real centimetres in front of the eye, at any world scale.
        float near = CameraFeature.NearClip.Value * rig.Scale;
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
            nextListenerFind = 0f;
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

        // Cutscene cameras switch on their own listeners by animation; check twice a second, not per frame. The
        // listeners (inactive ones too) are found once and every 5 s after, for any spawned later.
        float now = Time.unscaledTime;
        if (now < nextListenerScan)
            return;
        nextListenerScan = now + 0.5f;
        if (now >= nextListenerFind)
        {
            nextListenerFind = now + 5f;
            sceneListeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
        }
        foreach (var l in sceneListeners)
        {
            if (l == null || !l.isActiveAndEnabled || l.gameObject.scene.buildIndex < 0)
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
