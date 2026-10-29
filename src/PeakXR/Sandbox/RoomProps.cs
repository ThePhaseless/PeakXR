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

    /// <summary>A table under <paramref name="parent"/>: a 4 cm top whose surface is <paramref name="height"/> above
    /// <paramref name="floor"/>, on four 5 cm legs. No colliders: items lie kinematic on their spots. Only the top
    /// casts a shadow; the legs' would cost a shadow draw each for nothing a player sees.</summary>
    public static void Table(
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
        const float Thick = 0.04f;
        const float Leg = 0.05f;
        const float Inset = 0.08f;
        Box(
            parent,
            "PeakXR_ItemRoomTable",
            floor + Vector3.up * (height - Thick * 0.5f),
            rotation,
            new Vector3(width, Thick, depth),
            top
        ).shadowCastingMode = ShadowCastingMode.On;
        float legHeight = height - Thick;
        float x = Mathf.Max(0f, width * 0.5f - Inset);
        float z = Mathf.Max(0f, depth * 0.5f - Inset);
        for (int i = 0; i < 4; i++)
        {
            var offset =
                rotation
                * new Vector3((i & 1) == 0 ? -x : x, legHeight * 0.5f, (i & 2) == 0 ? -z : z);
            Box(
                parent,
                "PeakXR_ItemRoomTableLeg",
                floor + offset,
                rotation,
                new Vector3(Leg, legHeight, Leg),
                legs
            ).shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static MeshRenderer Box(
        Transform parent,
        string name,
        Vector3 at,
        Quaternion rotation,
        Vector3 size,
        Material material
    )
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        Object.Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.SetPositionAndRotation(at, rotation);
        box.transform.localScale = size;
        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
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
