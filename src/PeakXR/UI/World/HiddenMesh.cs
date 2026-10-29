using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// Shrinks a hidden graphic's mesh to one zero-area triangle. A backdrop hidden at alpha 0 keeps
/// <c>cullTransparentMesh</c> off so it stays batched, keeps its depth and still catches the laser
/// (<see cref="BackdropStrip"/>), but its full quad was still shaded and blended: full-canvas dimmers and window
/// panels behind every open menu, each a blended layer over much of both eyes (MultiPass, MSAA). GraphicRaycaster
/// hit-tests the RectTransform, never the mesh, so raycasts stay as they were while the GPU draws nothing.
/// </summary>
internal sealed class HiddenMesh : BaseMeshEffect
{
    /// <summary>Adds the effect to <paramref name="g"/> unless it already has one; returns the one added, else null.</summary>
    public static HiddenMesh Add(Graphic g) =>
        g.TryGetComponent<HiddenMesh>(out _) ? null : g.gameObject.AddComponent<HiddenMesh>();

    /// <summary>Gives the graphic its full mesh back at once (disabling marks it dirty), then removes the effect.</summary>
    public static void Remove(HiddenMesh effect)
    {
        if (effect == null)
            return;
        effect.enabled = false;
        Destroy(effect);
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
            return;
        var v = default(UIVertex);
        vh.PopulateUIVertex(ref v, 0);
        vh.Clear();
        vh.AddVert(v);
        vh.AddVert(v);
        vh.AddVert(v);
        vh.AddTriangle(0, 1, 2);
    }
}
