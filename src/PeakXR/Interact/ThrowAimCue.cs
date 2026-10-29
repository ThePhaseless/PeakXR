using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Where a button throw will go, while it charges: the drop button held past the game's delay charges a throw
/// (CharacterItems.cs:299-306), which the game marks with its throw reticle (GUIManager.cs:690). VR hides that
/// reticle, and the throw leaves along the holding hand's aim (<see cref="InteractPatches.Dropping"/>), so a
/// dotted arc from the held item follows the flight the release would give now (<see cref="ThrowPath"/>) to a dot
/// where it first meets the ground, in the ping preview's colours. Local only.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class ThrowAimCue : MonoBehaviour
{
    private const int Steps = 60;
    private const float StepSeconds = 0.05f;
    private const int DotCount = 40;
    private const float DotDegrees = 0.3f;
    private const float LandDegrees = 1.2f;

    private static readonly Color ArcColor = new(0.45f, 0.9f, 0.82f, 0.6f);
    private static readonly Color LandColor = new(0.45f, 0.9f, 0.82f, 0.9f);

    private static ThrowAimCue instance;

    private readonly Vector3[] path = new Vector3[Steps];
    private readonly System.Numerics.Vector3[] trace = new System.Numerics.Vector3[Steps];
    private readonly MeshRenderer[] dots = new MeshRenderer[DotCount];
    private MeshRenderer land;
    private int shown;

    /// <summary>Dots drawn along the arc this frame, for the self-test.</summary>
    public static int Dots => instance != null ? instance.shown : 0;

    /// <summary>Where the arc meets the ground, when it does, for the self-test.</summary>
    public static Vector3? Landing =>
        instance != null && instance.land.enabled ? instance.land.transform.position : null;

    /// <summary>The launch velocity the arc was drawn for, for the self-test.</summary>
    public static Vector3 Launch { get; private set; }

    public static void Create()
    {
        if (instance != null)
            return;
        var go = new GameObject("PeakXR_ThrowAimCue");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ThrowAimCue>();
    }

    /// <summary>The master's throw direction: <c>HelperFunctions.LookToDirection(lookValues, forward)</c>
    /// (CharacterItems.cs:414, HelperFunctions.cs:183-195).</summary>
    public static Vector3 LookDirection(Character c)
    {
        var look = c.data.lookValues;
        return Quaternion.Euler(-look.y, look.x, 0f) * Vector3.forward;
    }

    /// <summary>The holding hand's aim, which a VR button throw follows. False when that hand is untracked.</summary>
    public static bool TryAim(out Vector3 direction)
    {
        bool ok = HandAim.TryHand(LocalHold.ItemInLeftHand, out _, out var rotation);
        direction = rotation * Vector3.forward;
        return ok;
    }

    private void Awake()
    {
        var sphere = UIMaterials.SphereMesh();
        var arc = UIMaterials.Create(ArcColor, false, 3000);
        for (int i = 0; i < DotCount; i++)
        {
            dots[i] = UIMaterials
                .MeshObject("ThrowArcDot", sphere, arc, transform)
                .GetComponent<MeshRenderer>();
            dots[i].enabled = false;
        }
        land = UIMaterials
            .MeshObject(
                "ThrowLandDot",
                sphere,
                UIMaterials.Create(LandColor, true, 3001),
                transform
            )
            .GetComponent<MeshRenderer>();
        land.enabled = false;
    }

    private void LateUpdate()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var item = c != null ? c.data.currentItem : null;
        if (
            !VRSession.Active
            || rig == null
            || rig.Head == null
            || item == null
            || item.rig == null
            || c.refs.items.throwChargeLevel <= 0f
            || !TryAim(out var aim)
        )
        {
            Show(0, false);
            return;
        }
        var items = c.refs.items;
        float speed = HandThrow.LookSpeed(
            items.throwChargeLevel,
            items.minThrowForce,
            items.maxThrowForce,
            item.throwForceMultiplier
        );
        // As DoDropping sends it (CharacterItems.cs:266-272): a backpack leaves still, 0.25 m up, and so only along the
        // look, since there is no item velocity to aim with.
        bool backpack = item is Backpack;
        var start = item.transform.position + (backpack ? 0.25f * Vector3.up : Vector3.zero);
        var velocity = backpack
            ? LookDirection(c).normalized * speed
            : item.rig.linearVelocity + aim * speed;
        Launch = velocity;
        ThrowPath.Trace(
            start.N(),
            velocity.N(),
            item.rig.useGravity ? Physics.gravity.N() : System.Numerics.Vector3.Zero,
            item.rig.linearDamping,
            StepSeconds,
            trace
        );
        int count = Steps;
        bool hit = false;
        RaycastHit info = default;
        int mask = HelperFunctions.LayerType.TerrainMap.ToLayerMask();
        path[0] = start;
        for (int i = 1; i < Steps; i++)
        {
            path[i] = trace[i].U();
            var from = path[i - 1];
            var segment = path[i] - from;
            if (
                Physics.Raycast(
                    from,
                    segment,
                    out info,
                    segment.magnitude,
                    mask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                path[i] = info.point;
                count = i + 1;
                hit = true;
                break;
            }
        }
        var eye = rig.Head.position;
        Draw(count, eye);
        if (hit)
        {
            float size = VoiceIndicator.WorldSize(Vector3.Distance(eye, info.point), LandDegrees);
            land.transform.position = info.point;
            land.transform.localScale = new Vector3(size, size, size);
        }
        Show(shown, hit);
    }

    /// <summary>Dots spread evenly along the path's points, each the same size in view.</summary>
    private void Draw(int count, Vector3 eye)
    {
        int n = Math.Min(DotCount, (count - 1) * 2);
        for (int i = 0; i < n; i++)
        {
            float f = (i + 0.5f) / n * (count - 1);
            int k = Math.Min((int)f, count - 2);
            var at = Vector3.Lerp(path[k], path[k + 1], f - k);
            float size = VoiceIndicator.WorldSize(Vector3.Distance(eye, at), DotDegrees);
            var t = dots[i].transform;
            t.position = at;
            t.localScale = new Vector3(size, size, size);
        }
        shown = n;
    }

    private void Show(int count, bool landing)
    {
        shown = count;
        for (int i = 0; i < DotCount; i++)
        {
            bool on = i < count;
            if (dots[i].enabled != on)
                dots[i].enabled = on;
        }
        if (land.enabled != landing)
            land.enabled = landing;
    }
}
