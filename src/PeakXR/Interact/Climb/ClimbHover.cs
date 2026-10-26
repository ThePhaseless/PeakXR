using PeakXR.UI.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Interact.Climb;

/// <summary>
/// The chalk ring where a grip would catch: the VR stand-in for the flat game's highlighted climb reticle
/// (<c>sinceCanClimb</c>, CharacterClimbing.cs:566), drawn on the rock itself so the eye reads "a hold is here" without
/// text. Solid where a grip now catches; faint on climbable rock just past the reach, so the change from faint to
/// solid (with its tick) is the moment the grip starts to work. Wider than a hand, so the fist over it never hides it.
/// One small line loop per hand, built on first use; no allocation per frame. Local only.
/// </summary>
internal static class ClimbHover
{
    private const int Points = 24;
    private const float Radius = 0.065f;

    /// <summary>Lift off the surface along its normal: 1 cm sank into rough rock, and the ring stays depth tested (a
    /// mark on the rock, not hand UI) so it never shows through a ledge.</summary>
    public const float Lift = 0.025f;

    /// <summary>A hand must stay off a catchable hold this long before its next one ticks again.</summary>
    private const float RetouchAfter = 0.3f;

    private static readonly Color Chalk = new(0.97f, 0.97f, 0.92f, 0.85f);
    private static readonly Color FaintChalk = new(0.97f, 0.97f, 0.92f, 0.3f);
    private static readonly LineRenderer[] Rings = new LineRenderer[2];
    private static readonly bool[] Ready = new bool[2];
    private static readonly float[] ReadyEndedAt =
    {
        float.NegativeInfinity,
        float.NegativeInfinity,
    };
    private static Material readyMaterial,
        faintMaterial;

    /// <summary>The solid ring of this hand is drawn: a grip now catches (self-test vr.50).</summary>
    public static bool Drawn(bool left)
    {
        int i = left ? 0 : 1;
        var ring = Rings[i];
        return ring != null && ring.enabled && Ready[i];
    }

    /// <summary>Where the ring of this hand sits (self-test vr.50).</summary>
    public static Vector3 Position(bool left)
    {
        var ring = Rings[left ? 0 : 1];
        return ring != null ? ring.transform.position : default;
    }

    /// <summary>Draws the ring at <paramref name="point"/> on a surface facing <paramref name="normal"/>, solid when
    /// <paramref name="ready"/> (a grip catches), faint otherwise. True on the first ready frame of a new hold, for the
    /// contact tick.</summary>
    public static bool Show(bool left, Vector3 point, Vector3 normal, bool ready, float now)
    {
        int i = left ? 0 : 1;
        var ring = Rings[i] != null ? Rings[i] : Rings[i] = Build(left);
        bool fresh = ready && !(ring.enabled && Ready[i]) && now - ReadyEndedAt[i] >= RetouchAfter;
        if (Ready[i] && ring.enabled && !ready)
            ReadyEndedAt[i] = now;
        if (Ready[i] != ready || !ring.enabled)
        {
            ring.sharedMaterial = ready ? readyMaterial : faintMaterial;
            ring.widthMultiplier = ready ? 0.009f : 0.005f;
        }
        Ready[i] = ready;
        ring.transform.SetPositionAndRotation(
            point + normal * Lift,
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
        if (Ready[i])
            ReadyEndedAt[i] = Time.time;
    }

    private static LineRenderer Build(bool left)
    {
        readyMaterial ??= UIMaterials.Create(Chalk, drawOnTop: false, renderQueue: 3100);
        faintMaterial ??= UIMaterials.Create(FaintChalk, drawOnTop: false, renderQueue: 3100);
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
        line.widthMultiplier = 0.009f;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = readyMaterial;
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }
}
