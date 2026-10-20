using PeakXR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Locomotion;

/// <summary>
/// Comfort vignette as one head-locked ring mesh 1 m in front of the eyes: clear inside, a soft edge, black out to
/// 89 degrees. Angles are from the view centre, so the ring covers the same part of the view on any headset FOV
/// (a screen-space overlay would not). At 1 m the left/right eye parallax is under 2 degrees. Drawn last with depth
/// test off, so it covers fog, glass and particles. Only vertex positions change, in a preallocated array.
/// </summary>
internal sealed class VignetteMesh
{
    private const int Segments = 48;
    private const float Distance = 1f;
    private const float OuterAngle = 89f;

    private readonly Vector3[] vertices = new Vector3[Segments * 3];
    private readonly float[] cos = new float[Segments];
    private readonly float[] sin = new float[Segments];
    private Mesh mesh;
    private Material material;
    private GameObject go;
    private Transform parent;
    private float shownAngle = -1f;

    /// <param name="head">Head-locked transform (the stereo camera's).</param>
    /// <param name="intensity">0 = hidden, 1 = closed to <see cref="VignetteShape.ClosedAngle"/>.</param>
    public void Show(Transform head, float intensity)
    {
        bool visible = head != null && intensity > 0.005f;
        if (!visible)
        {
            if (go != null && go.activeSelf)
                go.SetActive(false);
            return;
        }
        if (go == null || parent != head)
            Attach(head);
        if (!go.activeSelf)
            go.SetActive(true);

        float inner = VignetteShape.InnerAngle(intensity);
        if (Mathf.Abs(inner - shownAngle) < 0.05f)
            return;
        shownAngle = inner;
        float feather = Mathf.Min(inner + VignetteShape.Feather, OuterAngle - 1f);
        SetRing(0, inner);
        SetRing(1, feather);
        SetRing(2, OuterAngle);
        mesh.SetVertices(vertices);
    }

    public void Destroy()
    {
        if (go != null)
            Object.Destroy(go);
        if (mesh != null)
            Object.Destroy(mesh);
        if (material != null)
            Object.Destroy(material);
    }

    private void SetRing(int ring, float angle)
    {
        float r = Distance * Mathf.Tan(angle * Mathf.Deg2Rad);
        int o = ring * Segments;
        for (int i = 0; i < Segments; i++)
            vertices[o + i] = new Vector3(cos[i] * r, sin[i] * r, Distance);
    }

    private void Attach(Transform head)
    {
        if (mesh == null)
            Build();
        if (go != null)
            Object.Destroy(go);
        parent = head;
        go = new GameObject("PeakXR.ComfortVignette");
        go.layer = head.gameObject.layer;
        go.transform.SetParent(head, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void Build()
    {
        var colors = new Color32[vertices.Length];
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            cos[i] = Mathf.Cos(a);
            sin[i] = Mathf.Sin(a);
            colors[i] = new Color32(0, 0, 0, 0);
            colors[Segments + i] = new Color32(0, 0, 0, 255);
            colors[2 * Segments + i] = new Color32(0, 0, 0, 255);
        }
        var triangles = new int[2 * Segments * 6];
        int t = 0;
        for (int ring = 0; ring < 2; ring++)
        {
            for (int i = 0; i < Segments; i++)
            {
                int a = ring * Segments + i;
                int b = ring * Segments + (i + 1) % Segments;
                triangles[t++] = a;
                triangles[t++] = a + Segments;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = a + Segments;
                triangles[t++] = b + Segments;
            }
        }
        SetRing(0, VignetteShape.OpenAngle);
        SetRing(1, VignetteShape.OpenAngle + VignetteShape.Feather);
        SetRing(2, OuterAngle);
        mesh = new Mesh { name = "PeakXR.ComfortVignette", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);

        // Built-in shaders that ship with every player and multiply vertex colour; URP shaders may be stripped.
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("UI/Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        material = new Material(shader)
        {
            name = "PeakXR.ComfortVignette",
            color = Color.white,
            renderQueue = 4000,
            hideFlags = HideFlags.HideAndDontSave,
        };
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)CompareFunction.Always);
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
    }
}
