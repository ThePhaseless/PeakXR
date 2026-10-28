using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>
/// The cue over the head-in-wall black (<see cref="RigDriver"/> wall fade, the eye guard): a small teal ring in
/// the middle of the view, drawn above the fade, with a chevron toward open space (<see cref="WallCueMath"/>: up is
/// ahead, down behind), and a soft note from that side on entering. Without it the view just goes dark and the
/// player cannot tell why or where to move. The eye guard's hold has nowhere to move to, so it shows the ring alone.
/// Head-locked only while the world is hidden, so it carries no motion against the scene: it appears once the drawn
/// fade is fully black (<see cref="BlackEnough"/>), not while the scene still shows through, and it rides the eye the
/// view renders from (the held eye outside the rock, not the head inside it), so it sits centred and still.
/// </summary>
internal sealed class WallCue
{
    private const int Segments = 40;

    /// <summary>Ahead of the eyes: past the 0.5 m comfort floor for focus.</summary>
    private const float Distance = 0.6f;
    private const float Radius = 0.03f;
    private const float Width = 0.0035f;

    /// <summary>Chevron: tip height and arm spread inside the ring, stroke width.</summary>
    private const float Tip = 0.016f;
    private const float Spread = 0.011f;
    private const float Drop = 0.009f;

    /// <summary>The drawn fade (<see cref="FadeView.Alpha"/>) at or above which the world is hidden.</summary>
    private const float BlackEnough = 0.99f;

    private readonly WallCueGate gate = new();
    private Mesh ringMesh;
    private Mesh chevronMesh;
    private Material material;
    private GameObject ring;
    private GameObject chevron;

    /// <summary>The ring is drawn (self-test).</summary>
    public bool Visible => ring != null && ring.activeSelf;

    /// <param name="head">The rig head (its layer, the sound's place); null hides the cue.</param>
    /// <param name="eye">The transform the view renders from (the headset camera, at the held eye while the head is
    /// in rock); null uses <paramref name="head"/>.</param>
    /// <param name="black">The wall fade or the eye guard holds black.</param>
    /// <param name="toOpen">World direction toward open space, or zero when moving cannot help.</param>
    public void Show(Transform head, Transform eye, bool black, Vector3 toOpen)
    {
        bool visible = head != null && black && FadeView.Alpha >= BlackEnough;
        if (gate.Update(visible, Time.unscaledTime) && toOpen != Vector3.zero)
            Affordance.Play(CueSound.Notice, head.position + toOpen.normalized * 0.5f, 0.5f);
        if (!visible)
        {
            if (ring != null && ring.activeSelf)
                ring.SetActive(false);
            return;
        }
        var parent = eye != null ? eye : head;
        if (ring == null)
            Build(parent, head.gameObject.layer);
        else if (ring.transform.parent != parent)
            ring.transform.SetParent(parent, false);
        if (!ring.activeSelf)
            ring.SetActive(true);
        var forward = parent.forward;
        float degrees = WallCueMath.ArrowDegrees(
            new System.Numerics.Vector3(toOpen.x, toOpen.y, toOpen.z),
            new System.Numerics.Vector3(forward.x, forward.y, forward.z)
        );
        bool arrow = !float.IsNaN(degrees);
        if (chevron.activeSelf != arrow)
            chevron.SetActive(arrow);
        if (arrow)
            chevron.transform.localRotation = Quaternion.Euler(0f, 0f, -degrees);
    }

    public void Destroy()
    {
        if (ring != null)
            Object.Destroy(ring);
        if (ringMesh != null)
            Object.Destroy(ringMesh);
        if (chevronMesh != null)
            Object.Destroy(chevronMesh);
        if (material != null)
            Object.Destroy(material);
    }

    private void Build(Transform parent, int layer)
    {
        material = UIMaterials.Create(Affordance.HintColor, drawOnTop: true, renderQueue: 5000);
        ringMesh = RingMesh();
        chevronMesh = ChevronMesh();
        ring = UIMaterials.MeshObject("PeakXR.WallCue", ringMesh, material, parent);
        ring.transform.localPosition = new Vector3(0f, 0f, Distance);
        ring.layer = layer;
        ring.GetComponent<MeshRenderer>().sortingOrder = DrawOrder.WallCue;
        chevron = UIMaterials.MeshObject(
            "PeakXR.WallCueArrow",
            chevronMesh,
            material,
            ring.transform
        );
        chevron.layer = layer;
        chevron.GetComponent<MeshRenderer>().sortingOrder = DrawOrder.WallCue;
    }

    private static Mesh RingMesh()
    {
        var vertices = new Vector3[(Segments + 1) * 2];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            float x = Mathf.Sin(a);
            float y = Mathf.Cos(a);
            vertices[2 * i] = new Vector3(x * Radius, y * Radius, 0f);
            vertices[2 * i + 1] = new Vector3(x * (Radius + Width), y * (Radius + Width), 0f);
        }
        int t = 0;
        for (int i = 0; i < Segments; i++)
        {
            int v = 2 * i;
            triangles[t++] = v;
            triangles[t++] = v + 1;
            triangles[t++] = v + 2;
            triangles[t++] = v + 2;
            triangles[t++] = v + 1;
            triangles[t++] = v + 3;
        }
        return Finish("PeakXR.WallCue", vertices, triangles);
    }

    /// <summary>An upward chevron (two strokes meeting at the tip); the transform turns it.</summary>
    private static Mesh ChevronMesh()
    {
        var tip = new Vector3(0f, Tip, 0f);
        var left = new Vector3(-Spread, Tip - Drop, 0f);
        var right = new Vector3(Spread, Tip - Drop, 0f);
        var vertices = new Vector3[8];
        Stroke(vertices, 0, left, tip);
        Stroke(vertices, 4, tip, right);
        var triangles = new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };
        return Finish("PeakXR.WallCueArrow", vertices, triangles);
    }

    private static void Stroke(Vector3[] v, int at, Vector3 a, Vector3 b)
    {
        var d = (b - a).normalized;
        var side = new Vector3(-d.y, d.x, 0f) * (Width * 0.5f);
        v[at] = a - side;
        v[at + 1] = a + side;
        v[at + 2] = b - side;
        v[at + 3] = b + side;
    }

    private static Mesh Finish(string name, Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
