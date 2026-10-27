using System.Collections.Generic;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Avatar;

/// <summary>
/// Draws the local VR player's held item smaller (<c>Interaction.HeldItemScale</c>) about the grip the hand holds,
/// so the game's oversized props stop filling the view (tester: "shrink items while they are held"). Render time
/// only: the item's parts are scaled in <c>beginContextRendering</c> and put back in <c>endContextRendering</c>, so
/// physics, colliders, the network and every peer keep the game's size. Gestures that test a point on the item map
/// it through <see cref="ToVisual"/> so they trigger where the item is drawn.
/// Left out: items another feature presents at a set size (passport and guidebook books, binocular lenses, a
/// parasol's canopy and a glider overhead) and held lights, whose flame lights fuses and fires.
/// </summary>
internal static class HeldItemScale
{
    private static readonly List<Transform> parts = new();
    private static readonly List<Vector3> positions = new();
    private static readonly List<Vector3> scales = new();
    private static Item bound;
    private static bool eligible;
    private static Vector3 pivot;
    private static int depth;
    private static bool applied;

    public static void Install()
    {
        RenderPipelineManager.beginContextRendering -= Begin;
        RenderPipelineManager.endContextRendering -= End;
        RenderPipelineManager.beginContextRendering += Begin;
        RenderPipelineManager.endContextRendering += End;
    }

    /// <summary>How much smaller <paramref name="item"/> is drawn: 1 unless it is the local VR player's held item
    /// and may be scaled.</summary>
    public static float Factor(Item item)
    {
        if (!VRSession.Active || item == null || !Bind(item) || !eligible)
            return 1f;
        var c = Character.localCharacter;
        return c != null && ReferenceEquals(c.data.currentItem, item)
            ? AvatarConfig.Instance.HeldItemScale
            : 1f;
    }

    /// <summary>Where a world point on <paramref name="item"/> is drawn: pulled toward its grip by
    /// <see cref="Factor"/>.</summary>
    public static Vector3 ToVisual(Item item, Vector3 world)
    {
        float f = Factor(item);
        if (f >= 1f)
            return world;
        var grip = item.transform.TransformPoint(pivot);
        return grip + (world - grip) * f;
    }

    /// <summary>The inverse of <see cref="ToVisual"/>: where on the item at game size a point by its drawn shape
    /// lies, so a test against its colliders matches what is drawn.</summary>
    public static Vector3 FromVisual(Item item, Vector3 world)
    {
        float f = Factor(item);
        if (f >= 1f)
            return world;
        var grip = item.transform.TransformPoint(pivot);
        return grip + (world - grip) / f;
    }

    private static bool Bind(Item item)
    {
        if (ReferenceEquals(item, bound) && item != null)
            return true;
        Restore();
        bound = item;
        parts.Clear();
        positions.Clear();
        scales.Clear();
        if (item == null)
            return false;
        eligible =
            !item.gliderHold
            && item.GetComponentInChildren<Action_Passport>() == null
            && item.GetComponentInChildren<Action_Guidebook>() == null
            && item is not Guidebook
            && item.GetComponentInChildren<Action_ShowBinocularOverlay>() == null
            && item.GetComponent<Parasol>() == null
            // Held lights keep their size: their flame is where fuses and fires are lit (FireGestures, HandLights)
            // and where their light shines from.
            && item.GetComponent<Lantern>() == null
            && item.GetComponent<Candle>() == null
            && item.GetComponent<Flare>() == null
            && item.GetComponentInChildren<Action_Torch>() == null;
        var root = item.transform;
        var grip = root.Find("Hand_R");
        if (grip == null)
            grip = root.Find("Hand_L");
        // The grip the hand bone is put on (AvatarPoseDriver.ItemInController) stays put.
        pivot = grip != null ? grip.localPosition : Vector3.zero;
        for (int i = 0; i < root.childCount; i++)
        {
            var part = root.GetChild(i);
            if (part.name is not ("Hand_R" or "Hand_L"))
                parts.Add(part);
        }
        return true;
    }

    private static void Begin(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (depth++ > 0)
            return;
        var c = Character.localCharacter;
        var item = c != null && c.data != null ? c.data.currentItem : null;
        float f = Factor(item);
        if (f >= 1f)
            return;
        positions.Clear();
        scales.Clear();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part == null)
            {
                positions.Add(default);
                scales.Add(default);
                continue;
            }
            positions.Add(part.localPosition);
            scales.Add(part.localScale);
            part.localScale *= f;
            part.localPosition = pivot + (part.localPosition - pivot) * f;
        }
        applied = true;
    }

    private static void End(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (depth > 0)
            depth--;
        if (depth == 0)
            Restore();
    }

    private static void Restore()
    {
        if (!applied)
            return;
        applied = false;
        for (int i = 0; i < parts.Count && i < positions.Count; i++)
        {
            // A despawned item takes its parts with it.
            if (parts[i] == null)
                continue;
            parts[i].localScale = scales[i];
            parts[i].localPosition = positions[i];
        }
    }
}
