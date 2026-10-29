using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Menu;

/// <summary>
/// Title scene: stands the player where the title camera looks out over the beach and floats the
/// main menu in front of them. The pose comes from the title scene's <see cref="MainCamera"/> at load,
/// before anything moves it; the research doc's (31.70, 4.63, -405.84), yaw -46.4 is only a sanity check.
/// The panel is world-locked at the eye height measured once the headset tracks: one big screen
/// (<see cref="ScreenWidth"/> times its distance wide) with the game's own intro, or, with <c>UI.TitleReach</c> set
/// nearer than the screen, <see cref="TitleMenuDepth"/> brings its button groups to hand.
/// </summary>
internal sealed class TitleStage : ICanvasPlacement
{
    private static readonly Vector3 ResearchPosition = new(31.70f, 4.63f, -405.84f);
    private const float StandingEyeHeight = 1.65f;
    private const float MenuTilt = 8f;

    /// <summary>Screen width per metre of distance: 1.1 spans about 58 degrees, a big screen that still fits the view
    /// without turning the head (was 0.85, about 46 degrees, "too small").</summary>
    private const float ScreenWidth = 1.1f;

    private readonly UIMenuSettings cfg;
    private bool active;
    private bool originFixed;
    private Vector3 feet;
    private float yaw;
    private Vector3 originPosition;
    private Quaternion originRotation = Quaternion.identity;
    private VRRig overridden;
    private float eyeHeight = StandingEyeHeight;
    private readonly TitleMenuDepth depth = new();

    /// <summary>The screen's heading under <c>UI.MenuFollow</c> LazyFollow: it turns about the fixed eye once you
    /// look past its edge (it spans about 29 degrees each side), back toward the stage when you look there.</summary>
    private readonly LazyFollow follow = new();

    public TitleStage(UIMenuSettings cfg)
    {
        this.cfg = cfg;
        VRSession.Stopped += depth.Restore;
        // A restarted session may track from a new origin (device-relative fallback, a restarted runtime): re-stand
        // the player over the stage from the new head pose, as a recenter does.
        VRSession.Started += Unlatch;
        Instance = this;
    }

    /// <summary>The installed stage, for the self-test.</summary>
    public static TitleStage Instance { get; private set; }

    public bool Interactive => true;

    /// <summary>The fixed eye point the title is laid out for (feet plus the eye height measured at arrival).</summary>
    public Vector3 Eye => feet + Vector3.up * eyeHeight;

    public TitleMenuDepth Depth => depth;

    public bool Active => active;

    /// <summary>Call on every scene load; arms the stage only when the title menu is present.</summary>
    public void OnSceneLoaded()
    {
        active = false;
        originFixed = false;
        eyeHeight = StandingEyeHeight;
        depth.Restore();
        Release();
        if (
            !cfg.TitleOnBeach.Value
            || Object.FindAnyObjectByType<MainMenu>() == null
            || MainCamera.instance == null
        )
            return;

        var cam = MainCamera.instance.transform;
        var eye = cam.position;
        yaw = cam.eulerAngles.y;
        var ground = Physics.Raycast(
            eye + Vector3.up * 0.2f,
            Vector3.down,
            out var hit,
            30f,
            ~0,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point.y
            : float.NegativeInfinity;
        var height = eye.y - ground;
        feet = height is > 0.5f and < 3f
            ? new Vector3(eye.x, ground, eye.z)
            : eye - Vector3.up * StandingEyeHeight;
        active = true;
        follow.Snap(yaw);

        var offDoc = Vector3.Distance(eye, ResearchPosition);
        Plugin.Log.LogInfo(
            $"[ui.menu] title stage at {feet} yaw {yaw:0.0} (camera {eye}, {offDoc:0.0} m from the researched pose)"
        );
    }

    /// <summary>Places the rig origin so the player's head stands over <see cref="feet"/> facing the
    /// title camera's direction. Fixed once the headset is tracked, so walking around still works, until a recenter.
    /// Holds <see cref="VRRig.OriginOverride"/> while the stage is active so the camera feature keeps out.</summary>
    public void ApplyOrigin(VRRig rig)
    {
        if (!active || rig == null || rig.Origin == null)
        {
            Release();
            return;
        }
        if (overridden != rig)
        {
            Release();
            rig.OriginOverride = true;
            rig.Recentered += Unlatch;
            overridden = rig;
        }
        if (!originFixed)
        {
            var head = VRInput.HeadTracked ? VRInput.HeadPosition : Vector3.zero;
            var headYaw = VRInput.HeadTracked
                ? (VRInput.HeadRotation * Vector3.forward).HeadingDegrees()
                : 0f;
            originRotation = Quaternion.Euler(0f, yaw - headYaw, 0f);
            originPosition = feet - originRotation * new Vector3(head.x, 0f, head.z);
            originFixed = VRInput.HeadTracked;
            // Latched with the origin: a panel that follows the head's height while crouching is head-locked.
            if (originFixed)
                eyeHeight = Mathf.Clamp(head.y, 0.8f, 2.2f);
        }
        rig.Origin.SetPositionAndRotation(originPosition, originRotation);
    }

    /// <summary>A recenter re-stands the player over the stage from where the head is now.</summary>
    private void Unlatch() => originFixed = false;

    private void Release()
    {
        if (overridden != null)
        {
            overridden.OriginOverride = false;
            overridden.Recentered -= Unlatch;
        }
        overridden = null;
    }

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        position = default;
        rotation = Quaternion.identity;
        widthMeters = 0f;
        if (!active)
            return PlacementResult.Default;

        var eye = Eye;
        var distance = cfg.TitleMenuDistance.Value;
        var reach = cfg.TitleReach.Value;
        depth.Tick(canvas, eye, reach, oneScreen: reach >= distance);
        rotation = Quaternion.Euler(0f, ScreenYaw(), 0f);
        var offset = PanelMath.InFront(0f, distance, MenuTilt).ToUnity();
        position = eye + rotation * offset;
        rotation = Quaternion.LookRotation(rotation * offset, Vector3.up);
        widthMeters = distance * ScreenWidth;
        return PlacementResult.Placed;
    }

    /// <summary>The stage's heading, or with <c>UI.MenuFollow</c> LazyFollow where the screen has glided to.</summary>
    private float ScreenYaw()
    {
        var rig = VRRig.Instance;
        var head = rig != null ? rig.Head : null;
        var ui = UIWorldFeature.Settings;
        if (head == null || ui == null || ui.MenuFollow.Value != MenuFollowMode.LazyFollow)
        {
            follow.Snap(yaw);
            return yaw;
        }
        float halfSpan = Mathf.Atan(0.5f * ScreenWidth) * Mathf.Rad2Deg;
        follow.Threshold = LazyFollow.ThresholdFor(ui.MenuFollowAngle.Value, halfSpan);
        follow.Update(
            PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics()),
            0f,
            LaserPointer.Engaged,
            Time.unscaledDeltaTime
        );
        return follow.Yaw;
    }
}
