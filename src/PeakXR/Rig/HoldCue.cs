using PeakXR.UI.Common;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>
/// Small head-locked radial progress ring shown while the menu button is held toward a recenter, so the player
/// sees that letting go now pauses and holding on recenters. A fixed ring mesh 0.8 m ahead and a little below the
/// view centre; progress collapses the unfilled segments onto the fill edge, so only vertex positions change.
/// </summary>
internal sealed class HoldCue
{
    private const int Segments = 40;
    private const float Distance = 0.8f;
    private const float Radius = 0.025f;
    private const float Width = 0.006f;

    /// <summary>Hidden for the first part of a press so a quick tap (pause) shows nothing.</summary>
    private const float ShowAfter = 0.15f;

    private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
    private Mesh mesh;
    private Material material;
    private GameObject go;
    private Transform parent;
    private float shown = -1f;

    /// <param name="head">Head-locked transform.</param>
    /// <param name="heldSeconds">Seconds the button has been down.</param>
    /// <param name="progress">0..1 toward the recenter.</param>
    public void Show(Transform head, float heldSeconds, float progress)
    {
        bool visible = head != null && heldSeconds >= ShowAfter && progress < 1f;
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
        if (Mathf.Abs(progress - shown) < 0.005f)
            return;
        shown = progress;
        SetArc(progress);
        mesh.SetVertices(vertices);
        mesh.RecalculateBounds();
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

    /// <summary>Clockwise from twelve o'clock up to <paramref name="progress"/>.</summary>
    private void SetArc(float progress)
    {
        float end = Mathf.Clamp01(progress) * Mathf.PI * 2f;
        for (int i = 0; i <= Segments; i++)
        {
            float a = Mathf.Min(i * Mathf.PI * 2f / Segments, end);
            float x = Mathf.Sin(a);
            float y = Mathf.Cos(a);
            vertices[2 * i] = new Vector3(x * Radius, y * Radius, 0f);
            vertices[2 * i + 1] = new Vector3(x * (Radius + Width), y * (Radius + Width), 0f);
        }
    }

    private void Attach(Transform head)
    {
        if (mesh == null)
            Build();
        if (go != null)
            Object.Destroy(go);
        parent = head;
        go = UIMaterials.MeshObject("PeakXR.RecenterCue", mesh, material, head);
        go.transform.localPosition = new Vector3(0f, -0.08f, Distance);
        go.layer = head.gameObject.layer;
    }

    private void Build()
    {
        var triangles = new int[Segments * 6];
        int t = 0;
        for (int i = 0; i < Segments; i++)
        {
            int a = 2 * i;
            triangles[t++] = a;
            triangles[t++] = a + 1;
            triangles[t++] = a + 2;
            triangles[t++] = a + 2;
            triangles[t++] = a + 1;
            triangles[t++] = a + 3;
        }
        SetArc(0f);
        mesh = new Mesh { name = "PeakXR.RecenterCue", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        // Above the comfort vignette (4000), below the VR fade.
        material = UIMaterials.Create(
            new Color(1f, 1f, 1f, 0.85f),
            drawOnTop: true,
            renderQueue: 4001
        );
    }
}
