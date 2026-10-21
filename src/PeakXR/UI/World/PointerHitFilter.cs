using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Pointer hits on converted (world-space) canvases land only on what you can see or use. A uGUI raycast hits a
/// graphic's whole rect, invisible ones included, so on a world panel the laser stopped on empty air: hidden
/// dimmers, transparent hit pads, a wheel slice's huge mask. A hit now counts when its graphic is visibly drawn,
/// or is invisible but part of something you can click, drag or scroll; anything else is dropped and the ray
/// goes on to what is behind. Radial wheel slices count only on their drawn wedge (<see cref="WheelSegment"/>).
/// Filtering the raycaster serves the laser and the game's own input module (which also raycasts through the
/// laser camera) alike. Outside VR, and on canvases left as screen overlays, nothing changes.
/// UnityEngine.UI <c>GraphicRaycaster.Raycast(PointerEventData, List&lt;RaycastResult&gt;)</c> (public override).
/// </summary>
[HarmonyPatch(
    typeof(GraphicRaycaster),
    nameof(GraphicRaycaster.Raycast),
    new[] { typeof(PointerEventData), typeof(List<RaycastResult>) }
)]
internal static class PointerHitFilter
{
    // Below this effective alpha a graphic is not visibly drawn (faded pages, dimmers hidden by alpha).
    private const float MinAlpha = 0.02f;

    private static void Prefix(List<RaycastResult> resultAppendList, out int __state) =>
        __state = resultAppendList.Count;

    private static void Postfix(
        Canvas ___m_Canvas,
        List<RaycastResult> resultAppendList,
        int __state
    )
    {
        if (
            !UIWorldRuntime.Ready
            || resultAppendList.Count <= __state
            || ___m_Canvas == null
            || !WorldCanvases.IsConverted(___m_Canvas.rootCanvas)
        )
            return;
        for (int i = resultAppendList.Count - 1; i >= __state; i--)
        {
            var r = resultAppendList[i];
            if (r.gameObject == null || !Accept(r.gameObject, r.worldPosition))
                resultAppendList.RemoveAt(i);
        }
    }

    /// <summary>Whether a hit on <paramref name="hit"/> at <paramref name="worldPoint"/> is on something drawn or usable.</summary>
    internal static bool Accept(GameObject hit, Vector3 worldPoint)
    {
        // Wheel slices: only the drawn wedge, whichever of the slice's graphics was hit.
        var slice = hit.GetComponentInParent<UIWheelSlice>();
        if (slice != null && slice.transform is RectTransform sliceRect)
        {
            var local = sliceRect.InverseTransformPoint(worldPoint);
            return WheelSegment.Contains(local.x, local.y, sliceRect.rect.width);
        }

        if (!hit.TryGetComponent<Graphic>(out var graphic))
            return true;
        return Visible(graphic) || Usable(hit);
    }

    private static bool Visible(Graphic g)
    {
        var renderer = g.canvasRenderer;
        if (g.color.a * renderer.GetAlpha() * renderer.GetInheritedAlpha() < MinAlpha)
            return false;
        // A mask that hides its own graphic (scroll viewports) draws nothing.
        return !(g.TryGetComponent<Mask>(out var mask) && mask.enabled && !mask.showMaskGraphic);
    }

    /// <summary>An invisible hit pad still counts when it belongs to something interactable: a button's padding,
    /// a scroll view's gaps, a dropdown's click-outside blocker, a settings row's hover preview.</summary>
    private static bool Usable(GameObject hit)
    {
        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
        if (handler == null)
            handler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hit);
        if (handler == null)
            handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit);
        if (handler == null)
            handler = ExecuteEvents.GetEventHandler<IDragHandler>(hit);
        if (handler == null)
            handler = ExecuteEvents.GetEventHandler<IScrollHandler>(hit);
        if (handler == null)
            return false;
        return !handler.TryGetComponent<Selectable>(out var selectable)
            || selectable.IsInteractable();
    }
}
