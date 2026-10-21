using PeakXR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Sorts one laser hit for <see cref="PointerPick"/>: visibly drawn, part of something usable, or neither (an empty
/// transparent area, or an invisible blocker). Only the laser's visuals read it; event routing stays vanilla.
/// Called for the front few hits of one ray per frame, until the first counted one.
/// </summary>
internal static class PointerHitRule
{
    // Below this effective alpha a graphic is not visibly drawn (faded pages, dimmers hidden by alpha).
    private const float MinAlpha = 0.02f;

    public static PointerPick.Hit Classify(GameObject hit)
    {
        var result = PointerPick.Hit.None;
        if (!hit.TryGetComponent<Graphic>(out var graphic) || Visible(graphic))
            result |= PointerPick.Hit.Visible;
        if (Usable(hit))
            result |= PointerPick.Hit.Usable;
        return result;
    }

    private static bool Visible(Graphic g)
    {
        var renderer = g.canvasRenderer;
        if (g.color.a * renderer.GetAlpha() * renderer.GetInheritedAlpha() < MinAlpha)
            return false;
        // A mask that hides its own graphic (scroll viewports) draws nothing.
        return !(g.TryGetComponent<Mask>(out var mask) && mask.enabled && !mask.showMaskGraphic);
    }

    /// <summary>Part of something interactable: a button's padding, a scroll view's gaps, a dropdown's
    /// click-outside blocker, a settings row's hover preview, a wheel slice's hit mask.</summary>
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
