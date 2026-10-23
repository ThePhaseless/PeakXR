using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>
/// Where a ping would land, before the squeeze: while a hand holds the finger gun (<see cref="PingInput.Aiming"/>), or
/// the thumb rests on or presses the layout's ping button (<see cref="PingInput.ButtonAiming"/>), a dotted line from
/// the aiming hand to a small dot on the ground the ping ray would hit (the same ray and TerrainMap mask as
/// <see cref="PingPatches.PingHit"/>), like a laser pointer's spot. Where the ray finds nothing the dot sits red at a fixed distance
/// inside the refused cue's "no" sign (a ring and its slash, <see cref="CueGlyph.Slash"/>), so shape and not only
/// colour says it will fail, and a ping fired there leaves it for a moment as the refused marker. Local only.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class PingPreview : MonoBehaviour
{
    /// <summary>Where the red dot sits when the ray finds no ground.</summary>
    public const float SkyMeters = 30f;

    private const float DotDegrees = 1.2f;

    /// <summary>The ray's dots: one every 12 cm near the hand, at most 40 spread over a longer ray, each this wide.</summary>
    private const float RaySpacing = 0.12f;
    private const int RayDotCount = 40;
    private const float RayDotDegrees = 0.3f;
    private const float RefusedSeconds = 1f;

    /// <summary>The "no" sign's ring radius, in dot widths: just clear of the dot.</summary>
    private const float SignScale = 0.9f;
    private const int SignPoints = 24;

    private static readonly Color AimColor = new(0.45f, 0.9f, 0.82f, 0.6f);
    private static readonly Color DotColor = new(0.45f, 0.9f, 0.82f, 0.9f);

    private static PingPreview instance;

    private readonly MeshRenderer[] rayDots = new MeshRenderer[RayDotCount];
    private int rayShown;
    private bool fromLeft;
    private MeshRenderer dot;
    private LineRenderer signRing,
        signSlash;
    private Material dotMaterial,
        skyMaterial;
    private float refusedUntil = -1f;
    private Vector3 refusedAt;

    /// <summary>The dot is drawn this frame, for the self-test.</summary>
    public static bool DotShown => instance != null && instance.dot.enabled;

    /// <summary>The drawn dot is the red no-ground one.</summary>
    public static bool DotRed => DotShown && instance.dot.sharedMaterial == instance.skyMaterial;

    /// <summary>The red dot is drawn inside the "no" sign (ring and slash), for the self-test.</summary>
    public static bool DotSigned =>
        DotRed && instance.signRing.enabled && instance.signSlash.enabled;

    /// <summary>Dots drawn along the aim ray this frame (0 when only the landing dot shows), for the self-test.</summary>
    public static int RayDots => instance != null ? instance.rayShown : 0;

    /// <summary>The last drawn aim ray started at the left hand.</summary>
    public static bool FromLeft => instance != null && instance.fromLeft;

    /// <summary>Where the dot is now.</summary>
    public static Vector3 DotPosition =>
        instance != null ? instance.dot.transform.position : default;

    public static void Create()
    {
        var go = new GameObject("PeakXR_PingPreview");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PingPreview>();
    }

    /// <summary>
    /// A ping went into the sky: the red dot stays at <paramref name="end"/> for a moment and is returned as the thing
    /// to tint for the refusal (null outside VR).
    /// </summary>
    public static Component Refused(Vector3 end)
    {
        if (instance == null)
            return null;
        instance.refusedAt = end;
        instance.refusedUntil = Time.unscaledTime + RefusedSeconds;
        instance.Draw(false, end, end, true);
        return instance.dot;
    }

    private void Awake()
    {
        var sphere = UIMaterials.SphereMesh();
        var rayMaterial = UIMaterials.Create(AimColor, false, 3000);
        for (int i = 0; i < RayDotCount; i++)
        {
            rayDots[i] = UIMaterials
                .MeshObject("PingRayDot", sphere, rayMaterial, transform)
                .GetComponent<MeshRenderer>();
            rayDots[i].enabled = false;
        }
        dotMaterial = UIMaterials.Create(DotColor, true, 3001);
        skyMaterial = UIMaterials.Create(Affordance.DenyColor, true, 3001);
        dot = UIMaterials
            .MeshObject("PingDot", sphere, dotMaterial, transform)
            .GetComponent<MeshRenderer>();
        dot.enabled = false;
        var sign = new GameObject("PingNoSign").transform;
        sign.SetParent(transform, false);
        signRing = SignLine(sign, SignPoints);
        signRing.loop = true;
        for (int i = 0; i < SignPoints; i++)
        {
            float a = i * Mathf.PI * 2f / SignPoints;
            signRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        var slash = GlyphShape.Stroke(CueGlyph.Slash, 0);
        signSlash = SignLine(sign, slash.Length);
        for (int i = 0; i < slash.Length; i++)
            signSlash.SetPosition(i, new Vector3(slash[i].X, slash[i].Y, 0f));
    }

    private LineRenderer SignLine(Transform parent, int points)
    {
        var go = new GameObject("Stroke");
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = points;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = skyMaterial;
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }

    private void LateUpdate()
    {
        if (!VRSession.Active || !PingConfig.Instance.AimPreview || VRRig.Instance == null)
        {
            Hide();
            return;
        }
        if (Time.unscaledTime < refusedUntil)
        {
            Draw(false, refusedAt, refusedAt, true);
            return;
        }
        bool left;
        if (PingInput.Aiming(true))
            left = true;
        else if (PingInput.Aiming(false))
            left = false;
        else if (!PingInput.ButtonAiming(out left))
        {
            Hide();
            return;
        }
        fromLeft = left;
        if (!HandAim.TryHand(left, out var origin, out var rotation))
        {
            Hide();
            return;
        }
        var direction = rotation * Vector3.forward;
        bool hit = Physics.Raycast(
            origin,
            direction,
            out var info,
            float.PositiveInfinity,
            HelperFunctions.LayerType.TerrainMap.ToLayerMask()
        );
        Draw(true, origin, hit ? info.point : origin + direction * SkyMeters, !hit);
    }

    private void Draw(bool withLine, Vector3 origin, Vector3 end, bool sky)
    {
        var rig = VRRig.Instance;
        var eye = rig != null && rig.Head != null ? rig.Head.position : origin;
        DrawRay(withLine, origin, end, eye);
        float size = VoiceIndicator.WorldSize(Vector3.Distance(eye, end), DotDegrees);
        var t = dot.transform;
        t.position = end;
        t.localScale = new Vector3(size, size, size);
        dot.sharedMaterial = sky ? skyMaterial : dotMaterial;
        dot.enabled = true;
        ShowSign(sky, end, eye, size * SignScale);
    }

    /// <summary>The ring and slash around the red dot, facing the eye; widths in world units, as the cue rings.</summary>
    private void ShowSign(bool on, Vector3 at, Vector3 eye, float radius)
    {
        if (on)
        {
            var t = signRing.transform.parent;
            t.SetPositionAndRotation(at, SpatialRuntime.FaceEye(at, eye));
            t.localScale = new Vector3(radius, radius, radius);
            signRing.widthMultiplier = signSlash.widthMultiplier = radius * 0.18f;
        }
        if (signRing.enabled != on)
            signRing.enabled = signSlash.enabled = on;
    }

    /// <summary>Dots from <paramref name="origin"/> toward <paramref name="end"/>, each the same size in view.</summary>
    private void DrawRay(bool on, Vector3 origin, Vector3 end, Vector3 eye)
    {
        int count = 0;
        float step = 0f;
        var toEnd = end - origin;
        float length = toEnd.magnitude;
        if (on)
            PingRules.Dots(length, RaySpacing, RayDotCount, out count, out step);
        var direction = length > 0f ? toEnd / length : Vector3.zero;
        for (int i = 0; i < RayDotCount; i++)
        {
            var r = rayDots[i];
            if (i >= count)
            {
                if (r.enabled)
                    r.enabled = false;
                continue;
            }
            var at = origin + direction * (step * (i + 0.5f));
            float size = VoiceIndicator.WorldSize(Vector3.Distance(eye, at), RayDotDegrees);
            var t = r.transform;
            t.position = at;
            t.localScale = new Vector3(size, size, size);
            if (!r.enabled)
                r.enabled = true;
        }
        rayShown = count;
    }

    private void Hide()
    {
        DrawRay(false, default, default, default);
        dot.enabled = false;
        ShowSign(false, default, default, 0f);
    }
}
