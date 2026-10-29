using System.Collections.Generic;
using System.Reflection;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Inventory;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>
/// Body-locked anchor for the HUD: follows head position at once and head yaw lazily, so the HUD
/// stays put during small glances and re-centres after a real turn. Rock nearer than <c>UI.HudDistance</c> (a
/// cliff face while climbing) pulls it in at the same angular size, and drawn on top at the comfort floor
/// (<see cref="PanelMath.FitBeforeWall"/>), so it never sits inside the wall.
/// </summary>
internal sealed class HudLayout
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    /// <summary>Seconds between wall tests: the HUD moves with the head every frame, its 15 rays need not.</summary>
    private const float WallTestInterval = 0.1f;

    /// <summary>Seconds between re-marks of the HUD while it is drawn on top: graphics the game adds meanwhile (a new
    /// hotbar slot, a status icon) get the on-top material too.</summary>
    private const float RemarkInterval = 0.5f;

    private readonly LazyYawFollow follow = new();
    private readonly WallOnTopHold onTop = new();
    private float nextWallTest;
    private float lastWallTest;
    private float nextRemark;

    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;

    /// <summary>Metres per HUD pixel at <c>UI.HudDistance</c>; times <see cref="Scale"/> where it is drawn.</summary>
    public float MetersPerPixel = 0.001f;

    /// <summary>How far a wall pulled the HUD in, as a fraction of <c>UI.HudDistance</c> (1: not at all).</summary>
    public float Scale = 1f;

    /// <summary>A wall holds the HUD nearer than its margin: draw it (and the info canvases) on top. Held a moment
    /// past the margin (<see cref="WallOnTopHold"/>), so it does not flicker at the edge.</summary>
    public bool OnTop;

    /// <summary>This frame, walk the HUD again for graphics added while it is on top.</summary>
    public bool Remark;

    public void Update(Transform head, UIWorldSettings cfg, float dt)
    {
        follow.Threshold = cfg.HudFollowAngle.Value;
        var yaw = follow.Update(
            PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics()),
            dt
        );
        var wanted = cfg.HudDistanceWorld;
        var offset = PanelMath.InFront(yaw, wanted, cfg.HudTilt.Value).ToUnity();
        Rotation = Quaternion.LookRotation(offset, Vector3.up);
        MetersPerPixel = PanelMath.MetersPerPixel(cfg.HudWidthWorld, ReferenceWidth);
        var now = Time.unscaledTime;
        if (now >= nextWallTest)
        {
            nextWallTest = now + WallTestInterval;
            var half = 0.5f * MetersPerPixel * new Vector2(ReferenceWidth, ReferenceHeight);
            var depth = WorldCanvases.WallDepth(head.position, offset, Rotation, half);
            var (clear, _) = PanelMath.FitBeforeWall(wanted, depth);
            OnTop = onTop.Update(wanted, depth, now - lastWallTest);
            lastWallTest = now;
            Scale = clear / wanted;
        }
        Remark = OnTop && now >= nextRemark;
        if (Remark)
            nextRemark = now + RemarkInterval;
        Position = head.position + offset * Scale;
    }
}

/// <summary>
/// Moves parts of the converted HUD out of the body panel: the reticle to the aiming hand's target (or a head-locked
/// spot), in wrist mode the stamina bar (with its numb mushroom group) and hotbar to the wrist, and with
/// <c>UI.HUD.StaminaView</c> the stamina bar to a small meter high in the view (or a copy of it there, <see cref="HudMirror"/>). The parts sit in
/// <see cref="HudBox"/> containers under the HUD canvas, so the game keeps driving them; only the containers are
/// posed each frame. Everything moved is put back when the mode changes or VR stops.
/// </summary>
internal sealed class HudSplit
{
    private const float ReticleDistance = 2f;

    /// <summary>Top-of-view meter: real metres from the eyes, its width there (about 11 degrees: small), degrees
    /// above the gaze, and the pitch past which it stops following a look up or down.</summary>
    private const float TopDistance = 1f;
    private const float TopWidth = 0.2f;
    private const float TopRaise = 22f;
    private const float TopMaxPitch = 50f;

    /// <summary>Seconds between the top meter's wall tests (like the body HUD's).</summary>
    private const float TopWallInterval = 0.1f;

    private static FieldInfo[] reticleFields = System.Array.Empty<FieldInfo>();

    private readonly HudBox reticleBox = new("PeakXR_Reticle");
    private readonly HudBox wristBox = new("PeakXR_Wrist");
    private readonly HudBox topBox = new("PeakXR_TopStamina");
    private static readonly HudPlate wristPanel = new();
    private readonly HudPlate topPanel = new();
    private readonly HudMirror mirror = new();
    private readonly List<Transform> barGroups = new();

    /// <summary>The top meter follows the head lazily in yaw and pitch with a small dead zone: a soft head lock that
    /// never swims with small motions.</summary>
    private readonly LazyFollow topFollow = new()
    {
        Threshold = 12f,
        Dwell = 0.25f,
        SmoothTime = 0.3f,
    };
    private bool topPlaced;
    private float topScale = 1f;
    private float topWallAt;

    private GUIManager gui;
    private bool inventoryActive;
    private RectTransform hudRoot;
    private bool applied,
        appliedWrist,
        appliedPlate,
        appliedBodyHotbar;
    private ReticleMode appliedReticle;
    private StaminaView appliedView;

    /// <summary>0..1 bump that briefly grows the wrist HUD (ui.spatial status alerts).</summary>
    public static float WristPulse;

    /// <summary>The wrist HUD was posed this frame, at <see cref="WristPosition"/> (centre of the status bar) and
    /// <see cref="WristRotation"/> (up toward the fingers), so other wrist parts can sit beside it.</summary>
    public static bool WristValid;
    public static Vector3 WristPosition;
    public static Quaternion WristRotation = Quaternion.identity;

    /// <summary>The watch-face rotation on the wrist before the turn toward the eye (self-test).</summary>
    public static Quaternion WristRest = Quaternion.identity;

    /// <summary>Width of the posed wrist HUD in metres and how far (0..1) it is turned toward the eye.</summary>
    public static float WristWidthMeters,
        WristFacing;

    /// <summary>The wrist container while the HUD is split onto the wrist (self-test).</summary>
    public static RectTransform WristRoot { get; private set; }

    /// <summary>The top-of-view stamina meter while shown, its plate and its width in metres (self-test).</summary>
    public static RectTransform TopRoot { get; private set; }
    public static UnityEngine.UI.Graphic TopPlate { get; private set; }
    public static float TopWidthMeters;

    /// <summary>Plate margin round the wrist parts, in HUD pixels.</summary>
    internal const float WristPlatePad = HudPlate.Pad;

    /// <summary>Drawn wrist content the plate was last fitted to, box pixels (self-test).</summary>
    public static Vector2 WristPlateContentMin => wristPanel.ContentMin;
    public static Vector2 WristPlateContentMax => wristPanel.ContentMax;

    /// <summary>The wrist plate's centre and size, box pixels (self-test).</summary>
    public static Vector2 WristPlateCenter => wristPanel.PlateCenter;
    public static Vector2 WristPlateSize => wristPanel.PlateSize;

    /// <summary>The wrist plate, for the self-test's own measurement (null without a wrist display).</summary>
    public static UnityEngine.UI.Graphic WristPlate => wristPanel.Plate;

    /// <summary>Size of the moved groups' rects, which set the wrist layout and scale (self-test).</summary>
    public static Vector2 WristGroupsSize => wristPanel.GroupsSize;

    /// <summary>The hand reticle's container (UI.HUD.Reticle Hand, else null), whether it is shown now and where
    /// (self-test).</summary>
    public static RectTransform ReticleRoot { get; private set; }
    public static bool ReticleShown { get; private set; }
    public static Vector3 ReticlePoint { get; private set; }

    /// <summary>Every public GameObject field on GUIManager named reticle* (A/GUIManager.cs:208-236).</summary>
    public static void Resolve()
    {
        var list = new List<FieldInfo>();
        foreach (var f in typeof(GUIManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (
                f.FieldType == typeof(GameObject)
                && f.Name.StartsWith("reticle", System.StringComparison.Ordinal)
            )
                list.Add(f);
        reticleFields = list.ToArray();
    }

    public void Sync(UIWorldSettings cfg, VRRig rig)
    {
        var current = GUIManager.instance;
        if (current != gui)
        {
            // The old HUD died with its scene, and the moved parts and copies with it.
            reticleBox.Forget();
            wristBox.Forget();
            topBox.Forget();
            mirror.Clear();
            applied = false;
            gui = current;
            // Feature states settle at startup; read here, not per frame (FeatureManager.Get walks a list).
            inventoryActive = FeatureManager.IsActive<InventoryFeature>();
        }
        if (gui == null || gui.hudCanvas == null)
            return;

        var reticle = cfg.Reticle.Value;
        var wrist = cfg.Hud.Value == HudMode.Wrist && rig.Hand(cfg.WristLeft) != null;
        var view = cfg.StaminaView.Value;
        // While spectating the status bar rides on the friend's nameplate (ui.spatial), and with the body hotbar
        // (inventory.vr) the 2D hotbar stays hidden on the panel instead of filling the wrist.
        var plate = SpectateView.ClaimsStatus;
        // Keyed on the settings, not on SlotGhosts.Showing, so focus loss (dashboard) does not re-lay the wrist.
        // A failed or skipped inventory.vr draws no body hotbar, so the 2D one keeps its wrist spot.
        var bodyHotbar =
            inventoryActive
            && InventoryConfig.Instance.BodySlots
            && InventoryConfig.Instance.Ghosts;
        if (
            applied
            && reticle == appliedReticle
            && wrist == appliedWrist
            && plate == appliedPlate
            && bodyHotbar == appliedBodyHotbar
            && view == appliedView
        )
            return;

        Restore();
        Apply(reticle, wrist, plate, bodyHotbar, view);
    }

    public void Restore()
    {
        WristValid = false;
        TopRoot = null;
        ReticleRoot = null;
        ReticleShown = false;
        if (reticleBox.Rect != null)
            OnTop.Set(reticleBox.Rect, false);
        reticleBox.Restore();
        if (wristBox.Rect != null)
            OnTop.Set(wristBox.Rect, false);
        wristBox.Restore();
        mirror.Clear();
        if (topBox.Rect != null)
            OnTop.Set(topBox.Rect, false);
        topBox.Restore();
        if (gui != null && gui.heroBG != null)
            gui.heroBG.enabled = true;
        applied = false;
    }

    private void Apply(
        ReticleMode reticle,
        bool wrist,
        bool plate,
        bool bodyHotbar,
        StaminaView view
    )
    {
        hudRoot = (RectTransform)gui.hudCanvas.transform;
        // The biome title dims the whole screen (A/GUIManager.cs:511-512); on a floating panel that is
        // just a dark rectangle, so the title text shows without it.
        if (gui.heroBG != null)
            gui.heroBG.enabled = false;
        if (reticle != ReticleMode.Hud)
        {
            reticleBox.Ensure(hudRoot);
            reticleBox.SetActive(reticle == ReticleMode.Head);
            foreach (var field in reticleFields)
                if (field.GetValue(gui) is GameObject go && go != null)
                    reticleBox.Move(go.transform);
            // At the hand it marks a spot on rock or ground, which would cut the flat reticle in half.
            if (reticle == ReticleMode.Hand)
            {
                OnTop.Set(reticleBox.Rect, true);
                ReticleRoot = reticleBox.Rect;
            }
        }

        // The bar itself goes to the top meter, else to the wrist, else stays on the body panel; spectating keeps it
        // for the friend's nameplate.
        bool top = !plate && view != StaminaView.Wrist;
        if (wrist)
            wristPanel.Ensure(wristBox.Ensure(hudRoot));
        if (top)
            topPanel.Ensure(topBox.Ensure(hudRoot));
        var barBox =
            plate ? null
            : view == StaminaView.TopOfView ? topBox
            : wrist ? wristBox
            : null;
        CollectBarGroups();
        if (barBox != null)
            foreach (var group in barGroups)
                barBox.Move(group);

        if (wrist)
        {
            var box = wristBox.Rect;
            if (!bodyHotbar)
            {
                MoveGroup(
                    gui.items != null && gui.items.Length > 0 && gui.items[0] != null
                        ? gui.items[0].transform
                        : null
                );
                MoveGroup(gui.backpack != null ? gui.backpack.transform : null);
            }
            // The dying bar (A/GUIManager.cs:578) belongs with the wrist bar, not on the body panel.
            MoveGroup(gui.dyingBarObject != null ? gui.dyingBarObject.transform : null);
            wristPanel.Measure(box, Frame());
            // Hand UI: the forearm and hand it rides on never cover it.
            OnTop.Sort(box);
            OnTop.Set(box, true);
        }

        if (top)
        {
            var box = topBox.Rect;
            // Both: the bar stays where it was put above and a copy reads at the top.
            if (view == StaminaView.Both)
                foreach (var group in barGroups)
                    mirror.Add(group, box);
            topPanel.Measure(box, TopFrame());
            // Over the view like the hand UI: rock at the brim never cuts it.
            OnTop.Sort(box);
            OnTop.Set(box, true);
            TopPlate = topPanel.Plate;
            topPlaced = false;
        }

        applied = true;
        appliedReticle = reticle;
        appliedWrist = wrist;
        appliedPlate = plate;
        appliedBodyHotbar = bodyHotbar;
        appliedView = view;
    }

    /// <summary>The stamina bar's frame (A/StaminaBar.cs:135), which sets the wrist and top layouts.</summary>
    private RectTransform Frame() =>
        gui != null && gui.bar != null ? gui.bar.staminaBarOutline : null;

    private RectTransform TopFrame() => mirror.Empty ? Frame() : mirror.CopyOf(Frame());

    /// <summary>The HUD groups of the stamina bar: its own, its affliction segments' and the numb mushrooms' (Numb
    /// swaps the bar for them, A/GUIManager.cs:1249). The bar finds its segments anywhere under its parent
    /// (A/StaminaBar.cs:85), not only under itself, so moving only the bar's group left them on the body panel,
    /// where the wrist never showed the Cold segment (VR run 25).</summary>
    private void CollectBarGroups()
    {
        barGroups.Clear();
        var bar = gui.bar;
        if (bar == null)
            return;
        AddGroup(GroupOf(bar.transform, hudRoot));
        if (bar.afflictions != null)
            foreach (var a in bar.afflictions)
                if (a != null)
                    AddGroup(AfflictionGroup(a, bar, hudRoot));
        if (gui.mushroomsCanvasGroup != null)
            AddGroup(GroupOf(gui.mushroomsCanvasGroup.transform, hudRoot));
    }

    private void AddGroup(Transform group)
    {
        if (group == null)
            return;
        foreach (var g in barGroups)
            if (group.IsChildOf(g))
                return;
        barGroups.Add(group);
    }

    /// <summary>The HUD group to move so affliction segment <paramref name="a"/> follows <paramref name="bar"/>:
    /// segments in a container of their own move with it, keeping their layout (see <see cref="GroupOf"/>).</summary>
    internal static Transform AfflictionGroup(
        BarAffliction a,
        StaminaBar bar,
        RectTransform hudRoot
    )
    {
        var parent = a.transform.parent;
        return GroupOf(
            parent != null && parent != bar.transform.parent ? parent : a.transform,
            hudRoot
        );
    }

    /// <summary>Moves the HUD group holding <paramref name="target"/> to the wrist (see <see cref="GroupOf"/>).</summary>
    private void MoveGroup(Transform target)
    {
        var group = GroupOf(target, hudRoot);
        if (group != null)
            wristBox.Move(group);
    }

    /// <summary>The HUD group holding <paramref name="target"/>: its ancestor directly under
    /// <paramref name="hudRoot"/>, or a smaller one when that ancestor is a full-screen container. Inside a
    /// full-HUD <see cref="HudBox"/> that is the box's child; null when the target is not in the HUD or sits in a
    /// small PeakXR box (ui.spatial), whose owner keeps it.</summary>
    internal static Transform GroupOf(Transform target, RectTransform hudRoot)
    {
        if (target == null || hudRoot == null)
            return null;
        var group = target;
        while (group.parent != null && group.parent != hudRoot)
            group = group.parent;
        if (group.parent != hudRoot)
            return null;
        // Runs only when the layout is re-applied, so reading the name (it allocates) is fine.
        if (
            group != target
            && group is RectTransform box
            && !CoversHud(box, hudRoot)
            && group.name.StartsWith("PeakXR_", System.StringComparison.Ordinal)
        )
            return null;

        while (group != target && group is RectTransform r && CoversHud(r, hudRoot))
        {
            var next = target;
            while (next.parent != group)
                next = next.parent;
            group = next;
        }
        return group;
    }

    private static bool CoversHud(RectTransform r, RectTransform hudRoot)
    {
        var hud = hudRoot.rect;
        return r.rect.width >= hud.width * 0.9f && r.rect.height >= hud.height * 0.9f;
    }

    /// <summary>Poses the wrist container. Called once per frame after the canvases are placed.</summary>
    public void PoseWrist(VRRig rig, UIWorldSettings cfg)
    {
        WristValid = false;
        if (!applied || !appliedWrist || wristBox.Rect == null)
            return;
        var hand = rig.Hand(cfg.WristLeft);
        if (hand == null)
            return;
        var box = wristBox.Rect;
        if (wristPanel.Stale(box))
        {
            wristPanel.Measure(box, Frame());
            // A part the game showed for the first time may carry a graphic not yet drawn on top.
            OnTop.Set(box, true);
        }
        else
            wristPanel.Fit(box);

        // Like a watch face: on the back of the wrist, the side that faces the eye when the wrist is raised to read
        // it, the bar across the wrist and text up toward the fingers (WristHud.Rest, mirrored per hand). Raised off
        // the arm and drawn on top, so the forearm never cuts it. It turns toward the eye (WristHud.Facing: halfway
        // at rest, fully when looked at or on an alert) and keeps its angular size.
        bool left = cfg.WristLeft;
        var anchor = WristHud.Anchor(hand.position.N(), hand.rotation.N(), left).U();
        var rotation = WristHud.Rest(hand.rotation.N(), left).U();
        WristRest = rotation;
        float baseWidth = WristHud.ScaledWidth(cfg.WristWidth.Value, cfg.TextScale.Value);
        var width = WristHud.Width(baseWidth, WristHud.ReadDistance, WristPulse);
        var head = rig.Head;
        var toEye = head != null ? head.position - anchor : Vector3.zero;
        float distance = toEye.magnitude;
        if (distance > 0.01f)
        {
            float gaze = Vector3.Angle(head.forward, -toEye);
            WristFacing = Mathf.MoveTowards(
                WristFacing,
                WristHud.Facing(gaze, WristPulse),
                Time.unscaledDeltaTime * 4f
            );
            var face = Quaternion.LookRotation(-toEye, head.up);
            rotation = Quaternion.Slerp(rotation, face, WristFacing);
            width = WristHud.Width(baseWidth, distance, WristPulse);
        }
        wristBox.Pose(anchor, rotation, wristPanel.Center, width / wristPanel.Span);
        WristValid = true;
        WristWidthMeters = width;
        WristRoot = box;
        WristPosition = anchor;
        WristRotation = rotation;
    }

    /// <summary>
    /// Poses the top-of-view stamina meter: high in the view, never in its centre, level with the horizon, lazily
    /// following the head (<see cref="LazyFollow"/>), pulled in front of rock at the same angular size and drawn on
    /// top. Called once per frame after the canvases are placed.
    /// </summary>
    public void PoseTop(Transform head, UIWorldSettings cfg, float dt)
    {
        TopRoot = null;
        var box = topBox.Rect;
        if (!applied || box == null || !box.gameObject.activeSelf || head == null)
            return;
        mirror.Sync();
        if (topPanel.Stale(box))
        {
            topPanel.Measure(box, TopFrame());
            OnTop.Set(box, true);
        }
        else
            topPanel.Fit(box);

        var forward = head.forward;
        float yaw = PanelMath.YawOf(forward.ToNumerics(), head.up.ToNumerics());
        float pitch = Mathf.Clamp(
            Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
            -TopMaxPitch,
            TopMaxPitch
        );
        if (!topPlaced)
        {
            topFollow.Snap(yaw, pitch);
            topPlaced = true;
            topWallAt = 0f;
        }
        else
            topFollow.Update(yaw, pitch, false, dt);

        var wanted = Rig.RealScale.World(TopDistance);
        var offset = PanelMath
            .InFront(topFollow.Yaw, wanted, -(topFollow.Pitch + TopRaise))
            .ToUnity();
        var rotation = Quaternion.LookRotation(offset, Vector3.up);
        var width = Rig.RealScale.World(TopWidth) * ReadableText.Scale(cfg.TextScale.Value);
        var now = Time.unscaledTime;
        if (now >= topWallAt)
        {
            topWallAt = now + TopWallInterval;
            var half = new Vector2(0.5f * width, 0.1f * width);
            var depth = WorldCanvases.WallDepth(head.position, offset, rotation, half);
            topScale = PanelMath.FitBeforeWall(wanted, depth).Distance / wanted;
        }
        topBox.Pose(
            head.position + offset * topScale,
            rotation,
            topPanel.Center,
            width * topScale / topPanel.Span
        );
        TopRoot = box;
        TopWidthMeters = width * topScale;
    }

    /// <summary>Poses the head-locked or hand reticle. Called late and again right before rendering. The hand one
    /// keeps the head one's angular size at any distance and faces the eye; the game still picks and tints its
    /// variant (GUIManager.UpdateReticle, GUIManager.cs:667-745).</summary>
    public void PoseReticle(Transform head, HudLayout hud, UIWorldSettings cfg)
    {
        if (!applied || reticleBox.Rect == null)
            return;
        if (appliedReticle == ReticleMode.Head)
        {
            reticleBox.SetScale(hud.MetersPerPixel * ReticleDistance / cfg.HudDistanceWorld);
            reticleBox.Rect.SetPositionAndRotation(
                head.position + head.forward * ReticleDistance,
                head.rotation
            );
            return;
        }
        if (appliedReticle != ReticleMode.Hand)
            return;
        var eye = head.position;
        bool show = HandReticle.TryPoint(out var point) && (point - eye).sqrMagnitude > 0.01f;
        ReticleShown = show;
        reticleBox.SetActive(show);
        if (!show)
            return;
        ReticlePoint = point;
        reticleBox.SetScale(
            hud.MetersPerPixel * Vector3.Distance(eye, point) / cfg.HudDistanceWorld
        );
        reticleBox.Rect.SetPositionAndRotation(
            point,
            Quaternion.LookRotation(point - eye, head.up)
        );
    }
}
