using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.UI.Common;

/// <summary>
/// Unlit colour materials for mod-drawn geometry (fade sphere, laser, cursor). Uses shaders that
/// ship with every Unity player instead of URP shaders that the game build may have stripped.
/// </summary>
internal static class UIMaterials
{
    private static Shader shader;

    public static Material Create(Color color, bool drawOnTop, int renderQueue)
    {
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");

        var ztest = (int)(drawOnTop ? CompareFunction.Always : CompareFunction.LessEqual);
        var material = new Material(shader) { color = color, renderQueue = renderQueue };
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", ztest);
        // Read by UI/Default when Internal-Colored is missing.
        material.SetInt("unity_GUIZTestMode", ztest);
        return material;
    }

    /// <summary>Mesh of Unity's built-in sphere primitive, without keeping the primitive around.</summary>
    public static Mesh SphereMesh()
    {
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(primitive);
        return mesh;
    }

    public static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }
}
