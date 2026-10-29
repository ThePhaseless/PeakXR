using PeakXR.UI.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// Lit, opaque props for the item room and the sandbox test wall. They use URP/Lit, which the game build compiles
/// (docs/research/stack.md: its main-light-shadow variants are in the build; the airport's own blockers use it), so
/// they take the hall's light and shadows and write depth like the level. The room used to draw its tables with the
/// unlit UI colour shader: flat, shadowless slabs without legs, and the near-white medicine planks read as white
/// blocks next to the other tables (tester, v0.1.1).
/// </summary>
internal static class RoomProps
{
    private const string LitShader = "Universal Render Pipeline/Lit";

    /// <summary>Shared by every test wall; never destroyed, so a placed wall always keeps it.</summary>
    private static Material wallMaterial;

    /// <summary>An opaque lit material in <paramref name="color"/>; the unlit UI colour when the build lacks URP/Lit
    /// (then <see cref="Unlit"/> names it).</summary>
    public static Material Lit(Color color)
    {
        var shader = Shader.Find(LitShader);
        if (shader == null || !shader.isSupported)
            return UIMaterials.Create(color, false, 2450);
        var m = new Material(shader) { name = "PeakXR_RoomProp" };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    /// <summary>The test wall's material: concrete grey. A primitive's default material is the built-in pipeline's,
    /// which URP does not draw as the level is drawn.</summary>
    public static Material Wall =>
        wallMaterial != null ? wallMaterial : wallMaterial = Lit(new Color(0.55f, 0.55f, 0.53f));

    /// <summary>Top thickness of every room table: a sturdy slab. A 4 cm top on a 0.30 m deep one-row table read
    /// as a plank (tester, v0.1.1).</summary>
    public const float TopThick = 0.07f;

    /// <summary>Leg width: thick enough to read from across the room.</summary>
    public const float LegWidth = 0.07f;

    /// <summary>A table at <paramref name="floor"/>, under <paramref name="parent"/>: a <see cref="TopThick"/> top
    /// whose surface is <paramref name="height"/> above the floor, on a leg at each corner and pairs between them at
    /// most 1.5 m apart. Top and legs are children of an unscaled table object, placed in its frame, so no leg takes a
    /// top's scale. No colliders: items lie kinematic on their spots. Legs cast shadows: 5 cm dark legs without one,
    /// inset under the top's own shadow, did not read at all (tester: "the tables have no legs").</summary>
    public static Transform Table(
        Transform parent,
        Vector3 floor,
        Quaternion rotation,
        float width,
        float depth,
        float height,
        Material top,
        Material legs
    )
    {
        var table = new GameObject("PeakXR_ItemRoomTable").transform;
        table.SetParent(parent, false);
        table.SetPositionAndRotation(floor, rotation);
        Box(
            table,
            "PeakXR_ItemRoomTableTop",
            Vector3.up * (height - TopThick * 0.5f),
            new Vector3(width, TopThick, depth),
            top
        );
        float legHeight = height - TopThick;
        float x = Mathf.Max(0f, width * 0.5f - LegWidth);
        float z = Mathf.Max(0f, depth * 0.5f - LegWidth);
        int across = Mathf.Max(2, Mathf.CeilToInt(2f * x / 1.5f) + 1);
        for (int i = 0; i < across; i++)
        for (int side = -1; side <= 1; side += 2)
            Box(
                table,
                "PeakXR_ItemRoomTableLeg",
                new Vector3(Mathf.Lerp(-x, x, i / (across - 1f)), legHeight * 0.5f, side * z),
                new Vector3(LegWidth, legHeight, LegWidth),
                legs
            );
        return table;
    }

    /// <summary>A lit box named <paramref name="name"/> under <paramref name="parent"/>, centred at
    /// <paramref name="at"/> in its frame; it casts and takes shadows like the level.</summary>
    public static MeshRenderer Box(
        Transform parent,
        string name,
        Vector3 at,
        Vector3 size,
        Material material
    )
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        Object.Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = at;
        box.transform.localScale = size;
        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return renderer;
    }

    /// <summary>The first mesh under <paramref name="root"/> not drawn with a supported lit URP or game (<c>W/</c>)
    /// shader, as "object (material, shader)", or null when every one is.</summary>
    public static string Unlit(Transform root)
    {
        if (root == null)
            return "nothing to check";
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        foreach (var m in renderer.sharedMaterials)
        {
            var shader = m != null ? m.shader : null;
            bool ok =
                shader != null
                && shader.isSupported
                && (
                    shader.name.StartsWith(
                        "Universal Render Pipeline/Lit",
                        System.StringComparison.Ordinal
                    ) || shader.name.StartsWith("W/", System.StringComparison.Ordinal)
                );
            if (!ok)
                return $"{renderer.name} ({(m != null ? m.name : "no material")}, {(shader != null ? shader.name : "no shader")})";
        }
        return null;
    }
}
