using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Guide;

/// <summary>
/// A small orb you rest a hand in to choose: it grows while it fills and fires once per visit (<see cref="DwellButton"/>).
/// The glyph says what it does without text: a chevron goes on, a cross skips or leaves.
/// </summary>
internal sealed class TouchOrb
{
    public const float Radius = 0.045f;
    private const float Reach = 0.09f;

    private readonly Transform orb;
    private readonly Material material;
    private readonly DwellButton dwell;

    /// <summary>Neutral chalk white for every orb: teal and red mean "would work" and "refused" in the cue system.</summary>
    private static readonly Color Chalk = new(0.92f, 0.92f, 0.88f, 0.8f);

    public TouchOrb(string name, bool cross, float seconds)
    {
        dwell = new DwellButton(seconds);
        material = UIMaterials.Create(Chalk, drawOnTop: false, renderQueue: 3100);
        orb = UIMaterials.MeshObject(name, UIMaterials.SphereMesh(), material, null).transform;
        Object.DontDestroyOnLoad(orb.gameObject);
        orb.localScale = Vector3.one * (Radius * 2f);
        if (cross)
        {
            Glyph(new Vector3(-0.45f, 0.45f, -1.1f), new Vector3(0.45f, -0.45f, -1.1f));
            Glyph(new Vector3(0.45f, 0.45f, -1.1f), new Vector3(-0.45f, -0.45f, -1.1f));
        }
        else
            Glyph(
                new Vector3(-0.4f, 0.5f, -1.1f),
                new Vector3(0.4f, 0f, -1.1f),
                new Vector3(-0.4f, -0.5f, -1.1f)
            );
    }

    public Vector3 Position => orb != null ? orb.position : Vector3.zero;

    /// <summary>The orb object; its glyph lines are its children.</summary>
    public Transform Root => orb;

    public void Place(Vector3 position, Quaternion rotation) =>
        orb.SetPositionAndRotation(position, rotation);

    public void SetActive(bool on)
    {
        if (orb != null && orb.gameObject.activeSelf != on)
            orb.gameObject.SetActive(on);
    }

    public void Reset() => dwell.Reset();

    /// <summary>Either hand inside fills it; true on the frame it fires. A faint tick when a hand enters.</summary>
    public bool Tick(VRRig rig, float dt)
    {
        var l = rig.Hand(true);
        var r = rig.Hand(false);
        bool inLeft = l != null && Vector3.Distance(l.position, orb.position) < Reach;
        bool inRight = r != null && Vector3.Distance(r.position, orb.position) < Reach;
        bool wasEmpty = dwell.Progress <= 0f;
        bool fired = dwell.Tick(inLeft || inRight, dt);
        if (wasEmpty && dwell.Progress > 0f)
            UIHaptics.Tick(inLeft, 0.15f, 0.02f);
        orb.localScale = Vector3.one * (Radius * 2f * (1f + 0.4f * dwell.Progress));
        return fired;
    }

    public void Destroy()
    {
        if (orb != null)
            Object.Destroy(orb.gameObject);
        if (material != null)
            Object.Destroy(material);
    }

    private void Glyph(params Vector3[] points)
    {
        var go = new GameObject("Glyph");
        go.transform.SetParent(orb, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = line.endWidth = 0.004f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.sharedMaterial = material;
    }
}
