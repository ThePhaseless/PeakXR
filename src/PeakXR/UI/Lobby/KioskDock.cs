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

        /// <summary>Width factor that makes the shown window's body text readable from where it docked.</summary>
        public float Grow = 1f;

        /// <summary>Which window <see cref="Grow"/> was measured for (the options or the pass), and whether it
        /// is final: texts fading in at open are measured again until <see cref="SettleUntil"/>.</summary>
        public bool MeasuredOptions,
            Settled;
        public float SettleUntil;

        /// <summary>The docked window is drawn over the kiosk (<see cref="OnTop"/>), on this canvas root: a new
        /// airport visit brings a new canvas, which must get the state again.</summary>
        public bool OnTop;
        public Transform OnTopRoot;
    }

    /// <summary>BoardingPass.OnOpen fades the pass in over 0.5 s (A/BoardingPass.cs:147): measure until then.</summary>
    private const float SettleSeconds = 0.6f;

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

    public KioskDock(UILobbySettings cfg)
    {
        this.cfg = cfg;
        // The canvases of the old scene are gone; their slots must not think a new one is already drawn on top.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) =>
        {
            Forget(passSlot);
            Forget(optionsSlot);
        };
    }

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
        {
            ClearOnTop(passSlot);
            ClearOnTop(optionsSlot);
            return PlacementResult.Default;
        }

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
            ClearOnTop(slot);
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
            Dock(slot, kiosk, head, nudge: !hasPass);
        slot.WasOpen = open;
        // Drawn over the kiosk while docked and open: the window floats a hand's breadth off the kiosk face, where
        // the kiosk's own screen can cut a corner from the eyes (run 53: 1/4 corners hidden). It is the thing in
        // use, so the kiosk it sits on may not cover it.
        SetOnTop(slot, root, open && slot.Docked);
        if (!slot.Docked)
            return PlacementResult.Default;

        var width = optionsOpen ? cfg.OptionsWidth.Value : cfg.PassWidth.Value;
        if (slot.MeasuredOptions != optionsOpen)
        {
            slot.MeasuredOptions = optionsOpen;
            Unsettle(slot);
        }
        if (open && !slot.Settled)
        {
            slot.Settled = Time.unscaledTime >= slot.SettleUntil;
            slot.Grow = TextGrowth(
                canvas,
                optionsOpen ? options.transform : pass.transform,
                width,
                Vector3.Distance(head, slot.Position),
                slot.Settled
            );
        }
        position = slot.Position;
        rotation = slot.Rotation;
        widthMeters = width * slot.Grow;
        return PlacementResult.Placed;
    }

    /// <summary>Draws <paramref name="root"/> over the kiosk or not, applying it again to a new canvas root and taking
    /// it off the one it was on before.</summary>
    private static void SetOnTop(Slot slot, Transform root, bool on)
    {
        if (ReferenceEquals(slot.OnTopRoot, root) && slot.OnTop == on)
            return;
        if (slot.OnTop && slot.OnTopRoot != null && !ReferenceEquals(slot.OnTopRoot, root))
            OnTop.Set(slot.OnTopRoot, false);
        OnTop.Set(root, on);
        slot.OnTop = on;
        slot.OnTopRoot = root;
    }

    private static void ClearOnTop(Slot slot)
    {
        if (slot.OnTop && slot.OnTopRoot != null)
            OnTop.Set(slot.OnTopRoot, false);
        Forget(slot);
    }

    private static void Forget(Slot slot)
    {
        slot.OnTop = false;
        slot.OnTopRoot = null;
    }

    /// <summary>The window may sit on the canvas, under it, or (like PassportManager) above it.</summary>
    private static bool Holds(Transform canvas, Transform window) =>
        window.IsChildOf(canvas) || canvas.IsChildOf(window);

    private static void Unsettle(Slot slot)
    {
        slot.Settled = false;
        slot.SettleUntil = Time.unscaledTime + SettleSeconds;
    }

    private void Dock(Slot slot, AirportCheckInKiosk kiosk, Vector3 head, bool nudge)
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
        Unsettle(slot);
    }

    /// <summary>Width factor that brings the median font size of <paramref name="window"/> to
    /// <see cref="ReadableText.MinDegrees"/> at <paramref name="distance"/> when its canvas is
    /// <paramref name="width"/> metres wide. Only while a window opens, never once it has settled.</summary>
    private float TextGrowth(Canvas canvas, Transform window, float width, float distance, bool log)
    {
        var root = (RectTransform)canvas.transform;
        var rootScale = Mathf.Abs(root.lossyScale.y);
        if (root.rect.width <= 0f || rootScale < 1e-9f)
            return 1f;
        window.GetComponentsInChildren(false, texts);
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
        if (log)
            Plugin.Log.LogInfo(
                $"[ui.lobby] {window.name} docked {distance:0.00} m away, {count} text(s), width x{grow:0.00} to read"
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
