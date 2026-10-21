using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Lobby;

/// <summary>
/// Places Canvas_BoardingPass and Canvas_CustomOptionWindow (R2 §2.1, sort 1000/1001) at the
/// check-in kiosk that opened them, so choosing an ascent feels like using the kiosk screen instead of
/// a menu glued to the face. The pose is fixed when a window opens (world-locked, no swimming) and
/// re-docked only on reopen, a different kiosk, or after the player ends up far away. Without a kiosk
/// reference (A/BoardingPass.cs:35 <c>kiosk</c>, set by A/AirportCheckInKiosk.cs:114 after
/// <c>Open()</c>) the canvas falls back to ui.world's default menu placement.
/// </summary>
internal sealed class KioskDock : ICanvasPlacement
{
    /// <summary>Run options float this much closer than the pass, so the fading pass never z-fights them.</summary>
    private const float OptionsNudge = 0.03f;

    /// <summary>Re-dock when the eyes are farther than 4 m from the panel (teleport, respawn).</summary>
    private const float RedockDistanceSqr = 16f;

    /// <summary>Per-canvas dock state; the pass and the options may live on separate canvases.</summary>
    private sealed class Slot
    {
        public bool WasOpen;
        public bool Docked;
        public AirportCheckInKiosk Kiosk;
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Width factor that makes the panel's body text readable from where it docked.</summary>
        public float Grow = 1f;
    }

    /// <summary>Most a kiosk panel may grow past its width setting to make its text readable.</summary>
    private const float MaxTextGrowth = 2f;

    private readonly System.Collections.Generic.List<TMPro.TMP_Text> texts = new();
    private readonly float[] textHeights = new float[64];

    private readonly UILobbySettings cfg;
    private readonly LobbyDockLimits limits = new();
    private readonly Slot passSlot = new();
    private readonly Slot optionsSlot = new();
    private AirportCheckInKiosk measured;
    private Vector3 kioskCenter;
    private float kioskTop;

    public KioskDock(UILobbySettings cfg) => this.cfg = cfg;

    public bool Interactive => true;

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

        var gui = GUIManager.instance;
        var rig = VRRig.Instance;
        if (
            !cfg.DockAtKiosk.Value
            || canvas == null
            || gui == null
            || gui.boardingPass == null
            || rig == null
            || rig.Head == null
        )
            return PlacementResult.Default;

        var pass = gui.boardingPass;
        var options = pass.customOptionsWindow;
        var root = canvas.transform;
        bool hasPass = Holds(root, pass.transform);
        bool hasOptions = options != null && Holds(root, options.transform);
        if (!hasPass && !hasOptions)
            return PlacementResult.Default;

        bool optionsOpen = hasOptions && options.isOpen;
        bool open = optionsOpen || (hasPass && pass.isOpen);
        var slot = hasPass ? passSlot : optionsSlot;
        var kiosk = pass.kiosk;
        if (kiosk == null)
        {
            slot.Docked = false;
            slot.WasOpen = open;
            return PlacementResult.Default;
        }

        var head = rig.Head.position;
        if (
            open
            && (
                !slot.WasOpen
                || !slot.Docked
                || slot.Kiosk != kiosk
                || (slot.Position - head).sqrMagnitude > RedockDistanceSqr
            )
        )
            Dock(slot, kiosk, head, nudge: !hasPass, canvas);
        slot.WasOpen = open;
        if (!slot.Docked)
            return PlacementResult.Default;

        position = slot.Position;
        rotation = slot.Rotation;
        widthMeters = (optionsOpen ? cfg.OptionsWidth.Value : cfg.PassWidth.Value) * slot.Grow;
        return PlacementResult.Placed;
    }

    /// <summary>The window may sit on the canvas, under it, or (like PassportManager) above it.</summary>
    private static bool Holds(Transform canvas, Transform window) =>
        window.IsChildOf(canvas) || canvas.IsChildOf(window);

    private void Dock(Slot slot, AirportCheckInKiosk kiosk, Vector3 head, bool nudge, Canvas canvas)
    {
        if (measured != kiosk)
        {
            Measure(kiosk.transform, out kioskCenter, out kioskTop);
            measured = kiosk;
        }
        limits.MaxDistance = cfg.MaxDistance.Value;
        var position = LobbyDock
            .Pose(
                kioskCenter.ToNumerics(),
                kioskTop,
                kiosk.transform.forward.ToNumerics(),
                head.ToNumerics(),
                limits,
                out var facing
            )
            .ToUnity();
        var forward = facing.ToUnity();
        if (nudge)
            position -= forward * OptionsNudge;
        slot.Position = position;
        slot.Rotation = Quaternion.LookRotation(forward, Vector3.up);
        slot.Kiosk = kiosk;
        slot.Docked = true;
        var width = nudge ? cfg.OptionsWidth.Value : cfg.PassWidth.Value;
        slot.Grow = TextGrowth(canvas, width, Vector3.Distance(head, position));
    }

    /// <summary>Width factor that brings the canvas's median font size to <see cref="ReadableText.MinDegrees"/>
    /// at <paramref name="distance"/> when the canvas is <paramref name="width"/> metres wide. Once per dock.</summary>
    private float TextGrowth(Canvas canvas, float width, float distance)
    {
        var root = (RectTransform)canvas.transform;
        var rootScale = Mathf.Abs(root.lossyScale.y);
        if (root.rect.width <= 0f || rootScale < 1e-9f)
            return 1f;
        canvas.GetComponentsInChildren(false, texts);
        int count = 0;
        foreach (var text in texts)
            if (text.isActiveAndEnabled && count < textHeights.Length)
                textHeights[count++] =
                    text.fontSize * Mathf.Abs(text.transform.lossyScale.y) / rootScale;
        texts.Clear();
        var metresPerUnit = width / root.rect.width;
        var grow = ReadableText.Grow(
            ReadableText.Median(textHeights, count) * metresPerUnit,
            distance,
            MaxTextGrowth
        );
        Plugin.Log.LogInfo(
            $"[ui.lobby] {canvas.name} docked {distance:0.00} m away, {count} text(s), width x{grow:0.00} to read"
        );
        return grow;
    }

    /// <summary>Kiosk centre and top from its mesh renderers (the meshes the game highlights on hover,
    /// A/AirportCheckInKiosk.cs:20). Runs once per kiosk when a panel docks, never per frame.</summary>
    internal static void Measure(Transform kiosk, out Vector3 center, out float top)
    {
        var pivot = kiosk.position;
        var min = pivot;
        var max = pivot;
        var renderers = kiosk.GetComponentsInChildren<MeshRenderer>();
        bool any = false;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled)
                continue;
            var b = r.bounds;
            if (!any)
            {
                min = b.min;
                max = b.max;
                any = true;
            }
            else
            {
                min = Vector3.Min(min, b.min);
                max = Vector3.Max(max, b.max);
            }
        }
        LobbyDock.Anchor(
            pivot.ToNumerics(),
            min.ToNumerics(),
            max.ToNumerics(),
            out var c,
            out top
        );
        center = c.ToUnity();
    }
}
