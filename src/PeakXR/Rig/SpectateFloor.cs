using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>
/// A faint grid disc at your real floor while a detached spectate view hangs over a void (no ground within
/// <see cref="RigMath.FloorCueDrop"/> below the eye): a still floor under your feet keeps a floating view from
/// feeling like a fall. Parented to the tracking origin, so it costs nothing per frame beyond a downward ray four
/// times a second.
/// </summary>
internal sealed class SpectateFloor
{
    private const float Radius = 1.2f;
    private const float Cell = 0.3f;
    private const int CircleSegments = 48;
    private const float RecheckSeconds = 0.25f;
    private const float RayLength = 50f;
    private static readonly Color Tint = new(1f, 1f, 1f, 0.16f);

    private Mesh mesh;
    private Material material;
    private GameObject go;
    private Transform parent;
    private float nextCheck;
    private bool overVoid;

    /// <param name="watching">A detached spectate view is showing.</param>
    public void Tick(bool watching, VRRig rig)
    {
        if (!watching || rig == null || rig.Head == null)
        {
            overVoid = false;
            nextCheck = 0f;
            Hide();
            return;
        }
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + RecheckSeconds;
            float ground = Physics.Raycast(
                rig.Head.position,
                Vector3.down,
                out var hit,
                RayLength,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
                ? hit.distance
                : -1f;
            overVoid = RigMath.NeedsFloorCue(ground);
        }
        if (!overVoid)
        {
            Hide();
            return;
        }
        if (go == null || parent != rig.Origin)
            Build(rig.Origin);
        if (!go.activeSelf)
            go.SetActive(true);
    }

    public void Hide()
    {
        if (go != null && go.activeSelf)
            go.SetActive(false);
    }

    public void Destroy()
    {
        if (go != null)
            Object.Destroy(go);
        if (mesh != null)
            Object.Destroy(mesh);
        if (material != null)
            Object.Destroy(material);
        go = null;
        mesh = null;
        material = null;
    }

    private void Build(Transform origin)
    {
        if (go != null)
            Object.Destroy(go);
        parent = origin;
        if (mesh == null)
            mesh = GridMesh();
        if (material == null)
            material = UIMaterials.Create(Tint, drawOnTop: false, renderQueue: 3050);
        // The tracking origin sits on your real floor (RigMath.OriginPosition), a hair above it to avoid flicker.
        go = UIMaterials.MeshObject("PeakXR_SpectateFloor", mesh, material, origin);
        go.transform.localPosition = new Vector3(0f, 0.005f, 0f);
    }

    /// <summary>Grid lines clipped to a disc, plus its rim, as a line mesh.</summary>
    private static Mesh GridMesh()
    {
        var points = new List<Vector3>();
        int n = Mathf.FloorToInt(Radius / Cell);
        for (int i = -n; i <= n; i++)
        {
            float a = i * Cell;
            float h = Mathf.Sqrt(Radius * Radius - a * a);
            points.Add(new Vector3(a, 0f, -h));
            points.Add(new Vector3(a, 0f, h));
            points.Add(new Vector3(-h, 0f, a));
            points.Add(new Vector3(h, 0f, a));
        }
        for (int i = 0; i < CircleSegments; i++)
        {
            float a0 = i * Mathf.PI * 2f / CircleSegments;
            float a1 = (i + 1) * Mathf.PI * 2f / CircleSegments;
            points.Add(new Vector3(Mathf.Cos(a0) * Radius, 0f, Mathf.Sin(a0) * Radius));
            points.Add(new Vector3(Mathf.Cos(a1) * Radius, 0f, Mathf.Sin(a1) * Radius));
        }
        var indices = new int[points.Count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        var m = new Mesh { name = "PeakXR.SpectateFloor" };
        m.SetVertices(points);
        m.SetIndices(indices, MeshTopology.Lines, 0);
        m.RecalculateBounds();
        return m;
    }
}
