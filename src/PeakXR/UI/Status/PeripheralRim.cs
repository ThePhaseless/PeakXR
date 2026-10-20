using PeakXR.Core;
using PeakXR.UI.Common;
using UnityEngine;

namespace PeakXR.UI.Status;

/// <summary>
/// Head-locked glow at the edge of the view: a spherical band around the camera (see <see cref="RimMesh"/>)
/// that is transparent within 35 degrees of the view centre, drawn over the world and UI but under the fade.
/// One tint at a time; the vertex alpha shapes it, the material colour sets hue and strength.
/// </summary>
internal sealed class PeripheralRim
{
    private const int Segments = 48;
    private static Mesh mesh;

    private readonly GameObject go;
    private readonly MeshRenderer meshRenderer;
    private readonly Material material;
    private Color shown = Color.clear;

    public PeripheralRim(Camera camera, Transform fallback)
    {
        if (mesh == null)
            mesh = BuildMesh();
        material = UIMaterials.Create(Color.clear, drawOnTop: true, renderQueue: 4500);
        go = UIMaterials.MeshObject(
            "PeakXR_StatusRim",
            mesh,
            material,
            camera != null ? camera.transform : fallback
        );
        meshRenderer = go.GetComponent<MeshRenderer>();
        meshRenderer.sortingOrder = short.MaxValue - 1;
        meshRenderer.enabled = false;
        var near = camera != null ? camera.nearClipPlane : 0.05f;
        go.transform.localScale = Vector3.one * Mathf.Max(0.5f, near * 8f);
    }

    public bool Alive => go != null;

    public void Show(Color color)
    {
        var visible = color.a > 0.002f;
        if (meshRenderer.enabled != visible)
            meshRenderer.enabled = visible;
        if (visible && color != shown)
        {
            material.color = color;
            shown = color;
        }
    }

    public void Destroy()
    {
        if (go != null)
            Object.Destroy(go);
        if (material != null)
            Object.Destroy(material);
    }

    private static Mesh BuildMesh()
    {
        RimMesh.Build(Segments, 1f, out var points, out var alpha, out var triangles);
        var vertices = new Vector3[points.Length];
        var colors = new Color[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            vertices[i] = points[i].ToUnity();
            colors[i] = new Color(1f, 1f, 1f, alpha[i]);
        }
        var m = new Mesh { name = "PeakXR_StatusRim" };
        m.vertices = vertices;
        m.colors = colors;
        m.triangles = triangles;
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return m;
    }
}
