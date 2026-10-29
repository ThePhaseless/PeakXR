using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Controller laser for world-space uGUI. A disabled, 1-degree camera sits on the aim pose so the
/// laser hit is always the centre of its "screen"; converted canvases use it as their event camera, so
/// the game's own GraphicRaycasters, sliders, scroll views, dropdowns and input fields work unchanged.
/// Events are dispatched here alongside the game's input module, the way PointerInputModule does it.
/// Events go to the front hit, as in vanilla, but the beam and dot end on the first hit that is drawn or usable
/// (<see cref="PointerPick"/>, <see cref="PointerHitRule"/>), so they pass through empty transparent areas; a control
/// seen through an invisible blocker gets a red dot and the Blocked cue instead of a click that silently fails.
/// </summary>
internal sealed class LaserPointer
{
    private const int PointerId = -7100;
    private const float DragAngle = 1.2f;
    private const float MissLength = 1.5f;
    private const float TickSeconds = 0.015f;

    // Hits looked at per ray for the beam; the first drawn or usable one usually is the first or second.
    private const int MaxCandidates = 8;
    private static readonly Color CursorColor = new(1f, 1f, 1f, 0.95f);

    // Dark rim around the dot so it reads on white text and bright sky; the dot takes the hint colour on a control.
    private static readonly Color RimColor = new(0f, 0f, 0f, 0.7f);
    private static readonly Color HoverColor = new(
        Affordance.HintColor.r,
        Affordance.HintColor.g,
        Affordance.HintColor.b,
        0.95f
    );
    private const float RimScale = 1.6f;
    private static readonly Color BeamColor = new(1f, 1f, 1f, 0.55f);
    private static readonly Color BeamHoverColor = new(1f, 1f, 1f, 0.85f);

    public static Camera EventCamera { get; private set; }

    /// <summary>Clicks under this transform keep the current selection, so the VR keyboard can type into the
    /// focused input field without the press deselecting it (which would end the edit).</summary>
    public static Transform KeepSelectionRoot;

    /// <summary>Show the laser although no interactive panel is open (an open radial wheel on the HUD).</summary>
    public static bool ForceShow;

    /// <summary>The hand whose trigger made the last laser press, so a click handler can cue on that hand.</summary>
    public static bool PressLeft { get; private set; }

    /// <summary>
    /// The control under the laser press being handled (null over none or once released), for a refusal that tints it.
    /// </summary>
    public static Selectable PressedControl { get; private set; }

    /// <summary>What the laser is over this frame (null over nothing) and from which hand, for self-test logs.</summary>
    public static GameObject Over { get; private set; }

    public static bool OverLeft { get; private set; }

    /// <summary>The laser is on a panel or its trigger is held (a slider drag can leave the panel): lazily following
    /// menus stay put meanwhile (<see cref="Core.LazyFollow"/>).</summary>
    public static bool Engaged { get; private set; }

    // A dim dot over a disabled control: it is there, but it will not take the press.
    private static readonly Color LockedColor = new(0.55f, 0.55f, 0.55f, 0.7f);

    private readonly PointerHands hands = new();
    private readonly List<RaycastResult> results = new();
    private readonly LineRenderer line;
    private readonly Transform cursor;
    private readonly Material cursorMaterial;
    private readonly Material beamMaterial;
    private bool hoverLook;
    private readonly RaycastResult[] candidates = new RaycastResult[MaxCandidates];
    private readonly PointerPick.Hit[] kinds = new PointerPick.Hit[MaxCandidates];
    private bool blocked;
    private Vector3 visualPoint;
    private PointerEventData data;
    private EventSystem dataSystem;
    private Vector3 pressForward;
    private bool visible = true;
    private GameObject hoverObject;
    private Selectable overSelectable;
    private Selectable hoverSelectable;

    // The hovered control is disabled (a boarding-pass arrow at its limit, kick for a non-host, a locked setting).
    private Selectable lockedSelectable;
    private bool lockedLook;

    public LaserPointer(Transform parent)
    {
        var camGo = new GameObject("PeakXR_PointerCamera");
        camGo.transform.SetParent(parent, false);
        EventCamera = camGo.AddComponent<Camera>();
        EventCamera.enabled = false;
        EventCamera.stereoTargetEye = StereoTargetEyeMask.None;
        EventCamera.fieldOfView = 1f;
        EventCamera.nearClipPlane = 0.01f;
        EventCamera.farClipPlane = 50f;
        EventCamera.cullingMask = 0;
        EventCamera.clearFlags = CameraClearFlags.Nothing;

        var lineGo = new GameObject("PeakXR_Laser");
        lineGo.transform.SetParent(parent, false);
        line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.004f;
        line.endWidth = 0.0015f;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        beamMaterial = UIMaterials.Create(BeamColor, drawOnTop: false, renderQueue: 3100);
        line.sharedMaterial = beamMaterial;
        line.sortingOrder = DrawOrder.LaserBeam;

        cursorMaterial = UIMaterials.Create(CursorColor, drawOnTop: true, renderQueue: 3200);
        var dot = UIMaterials.MeshObject(
            "PeakXR_Cursor",
            UIMaterials.SphereMesh(),
            cursorMaterial,
            parent
        );
        dot.GetComponent<MeshRenderer>().sortingOrder = DrawOrder.LaserCursor;
        cursor = dot.transform;
        // Child of the dot, drawn first: the part outside the dot shows as a dark ring.
        var ring = UIMaterials.MeshObject(
            "PeakXR_CursorRim",
            UIMaterials.SphereMesh(),
            UIMaterials.Create(RimColor, drawOnTop: true, renderQueue: 3199),
            cursor
        );
        ring.GetComponent<MeshRenderer>().sortingOrder = DrawOrder.LaserBeam;
        ring.transform.localScale = Vector3.one * RimScale;

        SetVisible(false, false);
    }

    /// <summary>Hide the laser and release anything it was pressing.</summary>
    public void Hide()
    {
        Engaged = false;
        SetBlocked(false);
        Cancel();
        SetVisible(false, false);
        hoverObject = null;
        overSelectable = null;
        hoverSelectable = null;
        lockedSelectable = null;
        SetHoverLook(false);
        SetLockedLook(false);
    }

    public void Process(bool show, UIWorldSettings cfg, VRRig rig, float dt)
    {
        hands.Mode = cfg.PointerHand.Value;
        hands.Update(VRInput.Left.Trigger, VRInput.Right.Trigger, dt);

        var aim = rig.Aim(hands.ActiveLeft);
        var system = EventSystem.current;
        if (!show || aim == null || system == null)
        {
            Hide();
            return;
        }
        if (data == null || dataSystem != system)
        {
            Cancel();
            data = new PointerEventData(system) { pointerId = PointerId };
            dataSystem = system;
        }

        EventCamera.transform.SetPositionAndRotation(aim.position, aim.rotation);
        data.Reset();
        data.button = PointerEventData.InputButton.Left;
        data.position = new Vector2(EventCamera.pixelWidth * 0.5f, EventCamera.pixelHeight * 0.5f);
        data.delta = Vector2.zero;
        data.scrollDelta = Vector2.zero;

        results.Clear();
        system.RaycastAll(data, results);
        var pick = PickOnWorldCanvases(out var hit);
        results.Clear();
        data.pointerCurrentRaycast = hit;
        var over = hit.gameObject;
        var shown = pick.Visual >= 0 ? candidates[pick.Visual].worldPosition : Vector3.zero;
        SetBlocked(pick.Blocked);
        visualPoint = shown;
        if (blocked)
            Affordance.Hover(hands.ActiveLeft, shown, DenyReason.Blocked);

        Over = over;
        OverLeft = hands.ActiveLeft;
        Engaged = over != null || hands.Held;
        HandleEnterExit(over);
        TickOnHover(over, cfg.HoverHaptics.Value);
        SetHoverLook(hoverSelectable != null && !blocked);
        bool locked = lockedSelectable != null && !blocked && IsLocked(lockedSelectable);
        SetLockedLook(locked);
        if (locked)
            Affordance.Hover(hands.ActiveLeft, shown, DenyReason.Locked);
        if (hands.Pressed)
            Press(system, over, aim);
        if (hands.Held)
            Drag(aim);
        if (hands.Released)
            Release(over);

        var stick = VRInput.Hand(hands.ActiveLeft).Stick.y;
        if (over != null && !hands.Held && Mathf.Abs(stick) > 0.2f)
        {
            data.scrollDelta = new Vector2(0f, stick * cfg.ScrollSpeed.Value * dt);
            ExecuteEvents.ExecuteHierarchy(
                ExecuteEvents.GetEventHandler<IScrollHandler>(over),
                data,
                ExecuteEvents.scrollHandler
            );
        }

        var origin = aim.position;
        bool landed = pick.Visual >= 0;
        var end = landed ? shown : origin + aim.forward * MissLength;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        var eye = rig.Head != null ? rig.Head.position : origin;
        if (landed)
        {
            // Constant angular size from the eye, a step bigger over a usable control.
            cursor.position = end - aim.forward * 0.002f;
            cursor.localScale =
                Vector3.one * LaserLook.CursorDiameter(Vector3.Distance(eye, end), hoverLook);
        }
        // A hand at the face would draw the beam across the view; the dot alone still shows the target.
        SetVisible(
            true,
            landed,
            rig.Head == null || !LaserLook.CrossesFace(origin.N(), end.N(), eye.N())
        );
        for (int i = 0; i < MaxCandidates; i++)
            candidates[i] = default;
    }

    private void SetBlocked(bool value)
    {
        if (blocked == value)
            return;
        blocked = value;
        ApplyLook();
    }

    /// <summary>Over an interactable control: the dot takes the hint colour and the beam brightens, so the target
    /// reads as live even where the game's own hover is subtle (the pause ribbons only grow by 15%).</summary>
    private void SetHoverLook(bool value)
    {
        if (hoverLook == value)
            return;
        hoverLook = value;
        ApplyLook();
    }

    private static bool IsLocked(Selectable s) =>
        s != null && s.isActiveAndEnabled && !s.IsInteractable();

    /// <summary>What turns red on a locked control: its own target graphic, else any graphic on it.</summary>
    private static Component LockTint(Selectable s) =>
        s.targetGraphic != null ? s.targetGraphic : s.GetComponent<Graphic>();

    private void SetLockedLook(bool value)
    {
        if (lockedLook == value)
            return;
        lockedLook = value;
        ApplyLook();
    }

    private void ApplyLook()
    {
        cursorMaterial.color =
            blocked ? Affordance.DenyColor
            : hoverLook ? HoverColor
            : lockedLook ? LockedColor
            : CursorColor;
        beamMaterial.color = hoverLook && !blocked ? BeamHoverColor : BeamColor;
    }

    /// <summary>One tick when the laser lands on a new interactable control. The lookup runs only when
    /// the hovered graphic changes, and moving between a button's own graphics does not tick again. Whether the
    /// control is locked is read every frame: a click that disables it (an ascent arrow at its last step) leaves the
    /// beam on it, and the next press must still get Locked (run 50, vr.116: it got nothing).</summary>
    private void TickOnHover(GameObject over, float strength)
    {
        if (over != hoverObject)
        {
            hoverObject = over;
            overSelectable = over != null ? over.GetComponentInParent<Selectable>() : null;
        }
        var selectable = overSelectable;
        lockedSelectable = selectable != null && !selectable.IsInteractable() ? selectable : null;
        if (lockedSelectable != null)
            selectable = null;
        if (selectable == hoverSelectable)
            return;
        hoverSelectable = selectable;
        if (selectable != null)
            UIHaptics.Tick(hands.ActiveLeft, strength, TickSeconds);
    }

    /// <summary>
    /// The hits on converted canvases, front first, up to the first drawn or usable one. <paramref name="routed"/>,
    /// the front one, gets the events (vanilla blocking); the result says where the beam ends.
    /// </summary>
    private PointerPick.Result PickOnWorldCanvases(out RaycastResult routed)
    {
        int count = 0;
        foreach (var r in results)
        {
            if (r.gameObject == null || r.module is not GraphicRaycaster raycaster)
                continue;
            var canvas = raycaster.GetComponent<Canvas>();
            if (canvas == null || !WorldCanvases.IsConverted(canvas.rootCanvas))
                continue;
            candidates[count] = r;
            kinds[count] = PointerHitRule.Classify(r.gameObject);
            if (kinds[count++] != PointerPick.Hit.None || count == MaxCandidates)
                break;
        }
        routed = count > 0 ? candidates[0] : default;
        return PointerPick.Resolve(kinds, count);
    }

    private void Press(EventSystem system, GameObject over, Transform aim)
    {
        data.eligibleForClick = true;
        data.delta = Vector2.zero;
        data.dragging = false;
        data.useDragThreshold = true;
        data.pressPosition = data.position;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        pressForward = aim.forward;
        PressLeft = hands.ActiveLeft;
        PressedControl = over != null ? over.GetComponentInParent<Selectable>() : null;
        // The blocker in front takes the press, as in vanilla; say why the control behind does not react.
        if (blocked)
            Affordance.Deny(hands.ActiveLeft, visualPoint, DenyReason.Blocked);
        // A disabled control ignores the press in vanilla too; the cue says it is locked, on the control itself.
        else if (lockedSelectable != null && IsLocked(lockedSelectable))
            Affordance.Deny(
                hands.ActiveLeft,
                visualPoint,
                DenyReason.Locked,
                default,
                LockTint(lockedSelectable)
            );
        if (over == null)
        {
            // Evidence for presses that did nothing (a pause menu near a wall ignored two clicks): log the miss.
            Plugin.Log.LogInfo(
                $"[ui.world] laser press hit no panel (blocked {blocked}, aim {aim.position}, forward {aim.forward})"
            );
            return;
        }

        if (
            ExecuteEvents.GetEventHandler<ISelectHandler>(over) != system.currentSelectedGameObject
            && (KeepSelectionRoot == null || !over.transform.IsChildOf(KeepSelectionRoot))
        )
            system.SetSelectedGameObject(null, data);

        var pressed = ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.pointerDownHandler);
        var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
        if (pressed == null)
            pressed = click;

        var now = Time.unscaledTime;
        data.clickCount =
            pressed == data.lastPress && now - data.clickTime < 0.3f ? data.clickCount + 1 : 1;
        data.clickTime = now;
        data.pointerPress = pressed;
        data.rawPointerPress = over;
        data.pointerClick = click;

        data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(over);
        if (data.pointerDrag != null)
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
    }

    private void Drag(Transform aim)
    {
        if (data.pointerDrag == null)
            return;
        if (!data.dragging)
        {
            var angle = Vector3.Angle(pressForward, aim.forward);
            if (angle > (data.useDragThreshold ? DragAngle : 0.05f))
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
            }
        }
        if (!data.dragging)
            return;
        if (data.pointerPress != data.pointerDrag)
        {
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;
        }
        ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
    }

    private void Release(GameObject over)
    {
        ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
        var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
        if (data.pointerClick != null && data.pointerClick == click && data.eligibleForClick)
            ExecuteEvents.Execute(data.pointerClick, data, ExecuteEvents.pointerClickHandler);
        else if (data.pointerClick != null)
            Plugin.Log.LogInfo(
                $"[ui.world] laser click dropped: pressed '{data.pointerClick.name}', released over "
                    + $"'{(click != null ? click.name : "nothing")}', eligible {data.eligibleForClick}, dragging "
                    + $"{data.dragging}, aim turned {Vector3.Angle(pressForward, EventCamera.transform.forward):0.0} deg"
            );
        if (data.pointerDrag != null && data.dragging && over != null)
            ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.dropHandler);

        data.eligibleForClick = false;
        data.pointerPress = null;
        data.rawPointerPress = null;
        data.pointerClick = null;
        if (data.pointerDrag != null && data.dragging)
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
        data.dragging = false;
        data.pointerDrag = null;
        // The click has run; a later refusal (keyboard Cancel, a scripted click) must not tint this control.
        PressedControl = null;
    }

    private void Cancel()
    {
        if (data == null)
            return;
        if (data.pointerPress != null)
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
        if (data.pointerDrag != null && data.dragging)
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
        data.eligibleForClick = false;
        data.pointerPress = null;
        data.rawPointerPress = null;
        data.pointerClick = null;
        data.pointerDrag = null;
        data.dragging = false;
        PressedControl = null;
        HandleEnterExit(null);
    }

    /// <summary>Enter/exit events up to the common ancestor, as BaseInputModule does.</summary>
    private void HandleEnterExit(GameObject target)
    {
        if (target == null || data.pointerEnter == null)
        {
            for (int i = 0; i < data.hovered.Count; i++)
                ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerExitHandler);
            data.hovered.Clear();
            if (target == null)
            {
                data.pointerEnter = null;
                return;
            }
        }
        if (data.pointerEnter == target)
            return;

        var common = CommonRoot(data.pointerEnter, target);
        if (data.pointerEnter != null)
        {
            for (var t = data.pointerEnter.transform; t != null && t != common; t = t.parent)
            {
                ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerExitHandler);
                data.hovered.Remove(t.gameObject);
            }
        }

        data.pointerEnter = target;
        for (var t = target.transform; t != null && t != common; t = t.parent)
        {
            ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerEnterHandler);
            data.hovered.Add(t.gameObject);
        }
    }

    private static Transform CommonRoot(GameObject a, GameObject b)
    {
        if (a == null || b == null)
            return null;
        for (var x = a.transform; x != null; x = x.parent)
        for (var y = b.transform; y != null; y = y.parent)
            if (x == y)
                return x;
        return null;
    }

    /// <summary>The menu laser is drawn this frame (ui.affordance hides its reach ray meanwhile).</summary>
    public static bool Showing { get; private set; }

    private void SetVisible(bool laser, bool dot, bool beam = true)
    {
        Showing = laser;
        if (
            visible == laser
            && cursor.gameObject.activeSelf == dot
            && line.enabled == (laser && beam)
        )
            return;
        visible = laser;
        line.enabled = laser && beam;
        cursor.gameObject.SetActive(dot);
    }
}
