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
    internal static bool EyeInside(Vector3 eye)
    {
        if (
            Physics.CheckSphere(
                eye,
                EyeRadius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return true;
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
                    return true;
            return false;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    private static readonly RaycastHit[] insideHits = new RaycastHit[16];

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

    /// <summary>The eyes over the mass-centre root (upright) and over the torso (pitched bodies), learned together.</summary>
    private readonly EyeCalibration massEyes = new(DefaultEyeOffset);
    private readonly EyeCalibration torsoEyes = new(DefaultTorsoEyeOffset);

    /// <summary>1: the view rides the mass centre (upright on foot, where the lean runs); 0: the torso, as before the
    /// lean. On a pitched body (climbing, hanging, gliding, ragdolled) a root at the hip puts the eyes 0.8 (1 - cos
    /// pitch) off, twice the torso's error, so the root blends over <see cref="RootBlendSeconds"/>, never in a frame.</summary>
    private float rootBlend = 1f;

    /// <summary>The eye offset the view uses now (forward is what a turn swings).</summary>
    private System.Numerics.Vector2 EyeOffsetNow =>
        System.Numerics.Vector2.Lerp(torsoEyes.Offset, massEyes.Offset, rootBlend);
    private Vector3 anchor;
    private readonly ViewMotionLimit chaseLimit = new();
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
        if (now >= TraceLeanUntil || now < next)
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
        eyeGuard.Reset();
        chaseBlock.Reset();
        haveLastGoal = false;
        ViewDetached = false;
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
            massEyes.Reset();
            torsoEyes.Reset();
            needInitialRecenter = c != null;
            snap = true;
            // A spawn or respawn can put the body (and the eyes) in rock.
            if (c != null)
                eyeGuard.Arm();
        }
        UpdateEyeOffset(c, dt);
        HandleRecenterInput(c, dt);
        float upright = c != null && Upright(c) ? 1f : 0f;
        rootBlend = snap ? upright : Mathf.MoveTowards(rootBlend, upright, dt / RootBlendSeconds);

        BodyAnchor = null;
        ViewOffset = Vector3.zero;
        LeanEye = null;
        if (rig.OriginOverride)
            ResetTakeover();
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

    private void UpdateHead()
    {
        // A scripted self-test head (vr.45 leans, vr.40 crouches) must move the view and the avatar's lean like a
        // real one; run 26 read the static HMD here, so the avatar never saw the lean the hands and room-scale did.
        if (VRInput.TestOverride != null && VRInput.HeadTracked)
        {
            headTracked = true;
            rig.Head.SetLocalPositionAndRotation(VRInput.HeadPosition, VRInput.HeadRotation);
        }
        else if (devices.TryGetHead(out var p, out var r))
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
    /// face's eye renderers (CustomizationRefs.EyeRenderers, CharacterCustomization.cs:282). Not the vanilla camera
    /// point <c>head.TransformPoint(up * 1 + forward * -0.5)</c> (Character.cs:1355): that sits on top of the head,
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
        // Only the unleaned eyes (EyeCalibration): any lean puts them on the headset, and sampling that would carry
        // the head's offset into the anchor and slide the view after the head. The lean waits for convergence.
        var driver = AvatarPoseDriver.Of(c);
        bool leaning = driver != null && driver.LeanWeight > 0f;
        if (leaning || !TryEyePoint(c, out var eye))
            return;
        float facing = Mathf.Atan2(d.lookDirection_Flat.x, d.lookDirection_Flat.z) * Mathf.Rad2Deg;
        var sample = RigMath.EyeOffset(eye.N(), ViewRoot(c).N(), facing);
        var torsoSample = RigMath.EyeOffset(eye.N(), c.Center.N(), facing);
        if (!(sample.Y > 0.1f && sample.Y < 2f && Mathf.Abs(sample.X) < 0.5f))
            return;
        bool first = !massEyes.Have;
        // Snap only while the view is placed anyway (a new character, a recenter); otherwise glide, never jump.
        massEyes.Sample(sample, leaning, snap, dt);
        torsoEyes.Sample(torsoSample, leaning, snap, dt);
        if (first)
            Plugin.Log.LogInfo(
                $"{Tag} Eyes {sample.X:0.00} m ahead of the body's mass centre and {sample.Y:0.00} m above the hip"
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
            // A turn (and the torso swing after it) moves the eye anchor and turnShift cancels it: the hold must not
            // cancel it again, or the turn pivots off the eye (run 45, vr.16: 3.2 cm slide in a 30 degree snap).
            if (Time.unscaledTime < turnHoldUntil)
                viewHold.Rebase(body);
            viewGoal = viewHold.Update(body, viewHoldWhy == null, dt).U() + extra;
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
        if (shownKind == ViewKind.ThirdPerson && !snap)
            anchor = chaseLimit.Step(before.N(), anchor.N(), viewGoal.N(), dt).U();
        else
            chaseLimit.Reset();
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
            RigMath.TrackedEyeHeight(rig.HeightOffset)
        );
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
                $"{Tag} turn f{Time.frameCount} eye ({eye.x:0.000},{eye.z:0.000}) rig {rig.Origin.eulerAngles.y:0.0} body {bodyYaw:0.0} torso ({torso.x:0.000},{torso.z:0.000}) shift ({turnShift.x:0.000},{turnShift.z:0.000}) offset {EyeOffsetNow.X:0.000} hold {(Time.unscaledTime < turnHoldUntil ? 1 : 0)}"
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
        bool inside = live && (onBody || chase) && eyeGuard.Watching && EyeInside(EyePosition());
        eyeGuard.Update(dt, inside);
        if (eyeGuard.Black != wasBlack)
            Plugin.Log.LogInfo(
                eyeGuard.Black ? $"{Tag} Eye inside rock after a jump ({shownKind}): holding black"
                : inside
                    ? $"{Tag} Eye still inside rock after {eyeGuard.MaxHoldSeconds:0} s: view back anyway"
                : $"{Tag} Eye clear of rock: view back"
            );
        if (headBlocked || eyeGuard.Black)
            VRFade.Set(wallFadeOwner, 1f);
        else
            VRFade.Clear(wallFadeOwner);
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
