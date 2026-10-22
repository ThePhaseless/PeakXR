using PeakXR.UI.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Interact.Climb;

/// <summary>
/// The faint chalk ring under an open hand on rock the game would let it grab: the VR stand-in for the flat game's
/// highlighted climb reticle (<c>sinceCanClimb</c>, CharacterClimbing.cs:566), drawn on the rock itself so the eye
/// reads "a hold is here" without text. One small line loop per hand, built on first use; no allocation per frame.
/// Local only.
/// </summary>
internal static class ClimbHover
{
    private const int Points = 24;
    private const float Radius = 0.045f;

    /// <summary>A hand must stay off the rock this long before its next touch ticks again.</summary>
    private const float RetouchAfter = 0.3f;

    private static readonly Color Chalk = new(0.95f, 0.95f, 0.9f, 0.45f);
    private static readonly LineRenderer[] Rings = new LineRenderer[2];
    private static readonly float[] HiddenAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>The ring of this hand is drawn (self-test vr.50).</summary>
    public static bool Drawn(bool left)
    {
        var ring = Rings[left ? 0 : 1];
        return ring != null && ring.enabled;
    }

    /// <summary>Draws the ring at <paramref name="point"/> on a surface facing <paramref name="normal"/>. True on
    /// the first frame of a new touch, for the contact tick.</summary>
    public static bool Show(bool left, Vector3 point, Vector3 normal, float now)
    {
        int i = left ? 0 : 1;
        var ring = Rings[i] != null ? Rings[i] : Rings[i] = Build(left);
        bool fresh = !ring.enabled && now - HiddenAt[i] >= RetouchAfter;
        // Lifted off the rock a little so it is not lost in the surface.
        ring.transform.SetPositionAndRotation(
            point + normal * 0.01f,
            Quaternion.LookRotation(normal)
        );
        ring.enabled = true;
        return fresh;
    }

    public static void Hide(bool left)
    {
        int i = left ? 0 : 1;
        var ring = Rings[i];
        if (ring == null || !ring.enabled)
            return;
        ring.enabled = false;
        HiddenAt[i] = Time.time;
    }

    private static LineRenderer Build(bool left)
    {
        var go = new GameObject(left ? "PeakXR_ClimbHoverL" : "PeakXR_ClimbHoverR");
        Object.DontDestroyOnLoad(go);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = Points;
        for (int p = 0; p < Points; p++)
        {
            float a = p * Mathf.PI * 2f / Points;
            line.SetPosition(p, new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a) * Radius, 0f));
        }
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = UIMaterials.Create(Chalk, drawOnTop: false, renderQueue: 3100);
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }
}
