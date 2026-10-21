using System.Collections.Generic;
using PeakXR.Core;
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
/// </summary>
internal sealed class LaserPointer
{
    private const int PointerId = -7100;
    private const float DragAngle = 1.2f;
    private const float MissLength = 1.5f;
    private const float TickSeconds = 0.015f;

    public static Camera EventCamera { get; private set; }

    /// <summary>Clicks under this transform keep the current selection, so the VR keyboard can type into the
    /// focused input field without the press deselecting it (which would end the edit).</summary>
    public static Transform KeepSelectionRoot;

    /// <summary>Show the laser although no interactive panel is open (an open radial wheel on the HUD).</summary>
    public static bool ForceShow;

    private readonly PointerHands hands = new();
    private readonly List<RaycastResult> results = new();
    private readonly LineRenderer line;
    private readonly Transform cursor;
    private PointerEventData data;
    private EventSystem dataSystem;
    private Vector3 pressForward;
    private bool visible = true;
    private GameObject hoverObject;
    private Selectable hoverSelectable;

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
        line.sharedMaterial = UIMaterials.Create(
            new Color(1f, 1f, 1f, 0.55f),
            drawOnTop: false,
            renderQueue: 3100
        );
        line.sortingOrder = short.MaxValue;

        var dot = UIMaterials.MeshObject(
            "PeakXR_Cursor",
            UIMaterials.SphereMesh(),
            UIMaterials.Create(new Color(1f, 1f, 1f, 0.95f), drawOnTop: true, renderQueue: 3200),
            parent
        );
        dot.GetComponent<MeshRenderer>().sortingOrder = short.MaxValue;
        cursor = dot.transform;

        SetVisible(false, false);
    }

    /// <summary>Hide the laser and release anything it was pressing.</summary>
    public void Hide()
    {
        Cancel();
        SetVisible(false, false);
        hoverObject = null;
        hoverSelectable = null;
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
        var hit = FirstOnWorldCanvas();
        results.Clear();
        data.pointerCurrentRaycast = hit;
        var over = hit.gameObject;

        HandleEnterExit(over);
        TickOnHover(over, cfg.HoverHaptics.Value);
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
        var end = over != null ? hit.worldPosition : origin + aim.forward * MissLength;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        if (over != null)
        {
            var distance = Vector3.Distance(origin, end);
            cursor.position = end - aim.forward * 0.002f;
            cursor.localScale = Vector3.one * (0.008f * Mathf.Max(1f, distance / 1.5f));
        }
        SetVisible(true, over != null);
    }

    /// <summary>One tick when the laser lands on a new interactable control. The lookup runs only when
    /// the hovered graphic changes, and moving between a button's own graphics does not tick again.</summary>
    private void TickOnHover(GameObject over, float strength)
    {
        if (over == hoverObject)
            return;
        hoverObject = over;
        var selectable = over != null ? over.GetComponentInParent<Selectable>() : null;
        if (selectable != null && !selectable.IsInteractable())
            selectable = null;
        if (selectable == hoverSelectable)
            return;
        hoverSelectable = selectable;
        if (selectable != null)
            UIHaptics.Tick(hands.ActiveLeft, strength, TickSeconds);
    }

    private RaycastResult FirstOnWorldCanvas()
    {
        foreach (var r in results)
        {
            if (r.gameObject == null || r.module is not GraphicRaycaster raycaster)
                continue;
            var canvas = raycaster.GetComponent<Canvas>();
            if (canvas != null && WorldCanvases.IsConverted(canvas.rootCanvas))
                return r;
        }
        return default;
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
        if (over == null)
            return;

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

    private void SetVisible(bool laser, bool dot)
    {
        Showing = laser;
        if (visible == laser && cursor.gameObject.activeSelf == dot)
            return;
        visible = laser;
        line.enabled = laser;
        cursor.gameObject.SetActive(dot);
    }
}
