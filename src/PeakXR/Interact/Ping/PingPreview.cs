using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Ping;

/// <summary>
/// Where a ping would land, before the squeeze: while a hand holds the finger gun (<see cref="PingInput.Aiming"/>), a
/// thin line from that hand to a small dot on the ground the ping ray would hit (the same ray and TerrainMap mask as
/// <see cref="PingPatches.PingHit"/>). Where the ray finds nothing the dot sits red at a fixed distance, and a ping
/// fired there leaves it red for a moment as the refused marker. Local only.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class PingPreview : MonoBehaviour
{
    /// <summary>Where the red dot sits when the ray finds no ground.</summary>
    public const float SkyMeters = 30f;

    private const float DotDegrees = 1.2f;
    private const float LineWidth = 0.002f;
    private const float RefusedSeconds = 1f;

    private static readonly Color AimColor = new(0.45f, 0.9f, 0.82f, 0.6f);
    private static readonly Color DotColor = new(0.45f, 0.9f, 0.82f, 0.9f);

    private static PingPreview instance;

    private LineRenderer line;
    private MeshRenderer dot;
    private Material dotMaterial,
        skyMaterial;
    private float refusedUntil = -1f;
    private Vector3 refusedAt;

    /// <summary>The dot is drawn this frame, for the self-test.</summary>
    public static bool DotShown => instance != null && instance.dot.enabled;

    /// <summary>The drawn dot is the red no-ground one.</summary>
    public static bool DotRed => DotShown && instance.dot.sharedMaterial == instance.skyMaterial;

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
        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = LineWidth;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = UIMaterials.Create(AimColor, false, 3000);
        line.enabled = false;
        dotMaterial = UIMaterials.Create(DotColor, true, 3001);
        skyMaterial = UIMaterials.Create(Affordance.DenyColor, true, 3001);
        dot = UIMaterials
            .MeshObject("PingDot", UIMaterials.SphereMesh(), dotMaterial, transform)
            .GetComponent<MeshRenderer>();
        dot.enabled = false;
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
        bool left = PingInput.Aiming(true);
        if (!left && !PingInput.Aiming(false))
        {
            Hide();
            return;
        }
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
        line.enabled = withLine;
        if (withLine)
        {
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
        }
        float size = VoiceIndicator.WorldSize(Vector3.Distance(eye, end), DotDegrees);
        var t = dot.transform;
        t.position = end;
        t.localScale = new Vector3(size, size, size);
        dot.sharedMaterial = sky ? skyMaterial : dotMaterial;
        dot.enabled = true;
    }

    private void Hide()
    {
        line.enabled = false;
        dot.enabled = false;
    }
}
