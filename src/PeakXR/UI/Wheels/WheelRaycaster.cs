using System.Collections.Generic;
using PeakXR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakXR.UI.Wheels;

/// <summary>
/// Raycaster for the box a VR wheel sits in. Each slice's raycast graphic is an invisible 3000 px mask, so a plain
/// raycast picks a slice anywhere in its sector, far outside the wheel; here a slice is hit only on its drawn wedge
/// (<see cref="WheelSegment"/>), for the laser and the game's input module alike, so what is hovered is what is
/// under the ray. Hits outside slices (page buttons, name label) are left as they are.
/// </summary>
internal sealed class WheelRaycaster : GraphicRaycaster
{
    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        int start = resultAppendList.Count;
        base.Raycast(eventData, resultAppendList);
        for (int i = resultAppendList.Count - 1; i >= start; i--)
        {
            var r = resultAppendList[i];
            var slice =
                r.gameObject != null ? r.gameObject.GetComponentInParent<UIWheelSlice>() : null;
            if (slice == null || slice.transform is not RectTransform rect)
                continue;
            var local = rect.InverseTransformPoint(r.worldPosition);
            if (!WheelSegment.Contains(local.x, local.y, rect.rect.width))
                resultAppendList.RemoveAt(i);
        }
    }
}
