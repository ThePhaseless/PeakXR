using System;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using NQuat = System.Numerics.Quaternion;
using NVec = System.Numerics.Vector3;

namespace PeakXR.Render;

/// <summary>
/// Culls both MultiPass eyes with one frustum (Core <see cref="StereoCull"/>), so shadows match between the eyes.
/// Unity's XR display gives each MultiPass render pass its own culling pass (XRSystem.CreateDefaultLayout and
/// ReconfigurePass read <c>GetCullingParameters(camera, renderPass.cullingPassIndex)</c>, Core RP XRSystem.cs:254 and
/// :290), URP culls each eye with it (UniversalRenderPipeline.TryGetCullingParameters, URP UniversalRenderPipeline.cs:604)
/// and fits the directional shadow cascades to that eye's culling camera (ShadowUtils.ExtractDirectionalLightMatrix,
/// ShadowUtils.cs:37, called per cull from ShadowCulling.cs:50). Each eye's cascades then cover a different volume and
/// snap to a different texel grid, and casters, LODs and lights are chosen per eye. This XR layout override (the hook
/// <see cref="Rig.StereoWidth"/> also uses, chained after it so it sees the widened eyes) gives both passes the same
/// culling parameters: each eye's own (mask, layer distances, options) with the geometry of a hidden camera fitted
/// around both eyes. Rendering stays per eye; only culling is shared.
/// </summary>
internal static class EyeCulling
{
    private const string Tag = "[render.fixes]";

    private static AccessTools.FieldRef<Action<XRLayout, Camera>> layoutOverride;
    private static Action<XRPass, int, ScriptableCullingParameters> assign;
    private static readonly Action<XRLayout, Camera> Hook = OnLayout;
    private static Action<XRLayout, Camera> previous;
    private static bool installed;
    private static bool failed;
    private static bool logged;

    private static Camera cullCam;
    private static int frame = -1;
    private static int cameraId;
    private static bool ready;
    private static ScriptableCullingParameters shared;

    /// <summary>Eyes (bit per multipassId) that URP reset to their own culling pass this frame and the hook shared
    /// again before their cull.</summary>
    private static int reshared;

    /// <summary>Finished frames whose two eyes both culled with the shared parameters (self-test vr.277).</summary>
    internal static int MatchedFrames { get; private set; }

    /// <summary>Finished frames that had two eye passes to share culling between.</summary>
    internal static int MultiPassFrames { get; private set; }

    /// <summary>Why culling stays per eye, or null.</summary>
    internal static string Problem { get; private set; }

    internal static bool Active => installed && !failed;

    /// <summary>Resolves the URP internals; called from <see cref="RenderFixesFeature.Install"/>. A URP update that drops
    /// one leaves the eyes culling separately and says so, without rolling the whole feature back.</summary>
    internal static void Resolve()
    {
        try
        {
            // Core RP XRSystem.cs:28 (private static; XRSystem is a static class, so no GameMember.StaticField<T>),
            // XRPass.cs:258 (internal).
            layoutOverride = AccessTools.StaticFieldRefAccess<Action<XRLayout, Camera>>(
                AccessTools.Field(typeof(XRSystem), "s_LayoutOverride")
                    ?? throw new MissingFieldException(
                        typeof(XRSystem).FullName,
                        "s_LayoutOverride"
                    )
            );
            assign = GameMember.Delegate<Action<XRPass, int, ScriptableCullingParameters>>(
                typeof(XRPass),
                "AssignCullingParams"
            );
        }
        catch (Exception e)
        {
            failed = true;
            Problem = $"URP internals missing ({e.GetType().Name}: {e.Message})";
            Plugin.Log.LogWarning(
                $"{Tag} shared eye culling off: {Problem}; the eyes cull separately"
            );
        }
    }

    /// <summary>Chains the hook after whatever override is set now; RenderDriver calls it once VR has started, so
    /// StereoWidth (installed on <see cref="VRSession.Started"/>) is already in place and runs first.</summary>
    internal static void Install()
    {
        if (installed || failed || !RenderFixesFeature.SharedEyeCulling.Value)
            return;
        previous = layoutOverride();
        layoutOverride() = Hook;
        installed = true;
    }

    internal static void Uninstall()
    {
        if (!installed)
            return;
        installed = false;
        if (layoutOverride() == Hook)
            layoutOverride() = previous;
        previous = null;
        frame = -1;
    }

    private static void OnLayout(XRLayout layout, Camera camera)
    {
        previous?.Invoke(layout, camera);
        if (!installed || failed || camera == null || !VRSession.Active)
            return;
        try
        {
            Share(layout, camera);
        }
        catch (Exception e)
        {
            failed = true;
            Problem = $"stopped: {e.GetType().Name}: {e.Message}";
            Plugin.Log.LogWarning($"{Tag} shared eye culling {Problem}; the eyes cull separately");
        }
    }

    private static void Share(XRLayout layout, Camera camera)
    {
        var passes = layout.GetActivePasses();
        XRPass a = null;
        XRPass b = null;
        for (int i = 0; i < passes.Count; i++)
        {
            var (cam, pass) = passes[i];
            if (cam != camera || pass == null || !pass.enabled)
                continue;
            if (pass.singlePassEnabled || pass.viewCount != 1)
                return; // single pass already culls both eyes at once
            if (a == null)
                a = pass;
            else if (b == null)
                b = pass;
            else
                return;
        }
        if (b == null)
            return;
        // The first call of a frame is CreateDefaultLayout: both passes are fresh there. Later calls (ReconfigurePass,
        // one per eye, right before that eye's cull) reset only their own pass to its display culling pass, so the shared
        // frustum is built once per frame and given back to the reset pass.
        int id = camera.GetInstanceID();
        bool fresh = Time.frameCount != frame || id != cameraId;
        if (fresh)
        {
            if (frame >= 0)
            {
                MultiPassFrames++;
                if (reshared == 3)
                    MatchedFrames++;
            }
            frame = Time.frameCount;
            cameraId = id;
            reshared = 0;
            ready = Build(a, b, camera);
        }
        if (!ready)
            return;
        if (!fresh)
        {
            if (a.cullingParams != shared)
                reshared |= 1 << (a.multipassId & 1);
            if (b.cullingParams != shared)
                reshared |= 1 << (b.multipassId & 1);
        }
        assign(a, a.cullingPassId, shared);
        assign(b, b.cullingPassId, shared);
    }

    private static bool Build(XRPass a, XRPass b, Camera camera)
    {
        if (
            !TryEye(a, out var ea)
            || !TryEye(b, out var eb)
            || !StereoCull.TryUnion(ea, eb, camera.nearClipPlane, camera.farClipPlane, out var u)
        )
        {
            Problem = "no union frustum for these eye views";
            return false;
        }
        var cull = CullCamera();
        cull.cullingMask = camera.cullingMask;
        cull.transform.SetPositionAndRotation(U(u.Position), U(u.Rotation));
        cull.nearClipPlane = u.Near;
        cull.farClipPlane = u.Far;
        cull.aspect = (u.Right - u.Left) / (u.Top - u.Bottom);
        cull.fieldOfView = 2f * Mathf.Atan(Mathf.Max(u.Top, -u.Bottom)) * Mathf.Rad2Deg;
        cull.projectionMatrix = Matrix4x4.Frustum(
            u.Left * u.Near,
            u.Right * u.Near,
            u.Bottom * u.Near,
            u.Top * u.Near,
            u.Near,
            u.Far
        );
        if (!cull.TryGetCullingParameters(false, out var union))
        {
            Problem = "the cull camera gave no culling parameters";
            return false;
        }
        // The eye's own parameters keep its mask, layer distances and options; only the geometry is the union's.
        var p = a.cullingParams;
        p.cameraProperties = union.cameraProperties;
        p.cullingMatrix = union.cullingMatrix;
        p.cullingPlaneCount = union.cullingPlaneCount;
        for (int i = 0; i < union.cullingPlaneCount; i++)
            p.SetCullingPlane(i, union.GetCullingPlane(i));
        p.origin = union.origin;
        var lod = p.lodParameters;
        lod.cameraPosition = (Pos(a) + Pos(b)) * 0.5f;
        p.lodParameters = lod;
        shared = p;
        Problem = null;
        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"{Tag} MultiPass eyes cull as one: display culling passes {a.cullingPassId} and {b.cullingPassId} -> one frustum {u.Left:0.00}/{u.Right:0.00}/{u.Bottom:0.00}/{u.Top:0.00} (eye tangents {ea.Left:0.00}/{ea.Right:0.00} and {eb.Left:0.00}/{eb.Right:0.00}), apex {Vector3.Distance(U(u.Position), lod.cameraPosition) * 1000f:0} mm behind the eyes; shadows, LODs and lights now match between the eyes"
                )
            );
        }
        return true;
    }

    private static bool TryEye(XRPass pass, out EyeView eye)
    {
        eye = default;
        var view = pass.GetViewMatrix(0);
        var proj = pass.GetProjMatrix(0);
        if (proj.m00 == 0f || proj.m11 == 0f)
            return false;
        var toWorld = view.inverse;
        // View space looks down -Z: the eye's forward is minus the third column.
        Vector3 forward = -(Vector3)toWorld.GetColumn(2);
        Vector3 up = toWorld.GetColumn(1);
        if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
            return false;
        var rot = Quaternion.LookRotation(forward, up);
        var (l, r, bottom, top) = StereoCull.Tangents(proj.m00, proj.m02, proj.m11, proj.m12);
        eye = new EyeView(N((Vector3)toWorld.GetColumn(3)), N(rot), l, r, bottom, top);
        return true;
    }

    private static Vector3 Pos(XRPass pass) => pass.GetViewMatrix(0).inverse.GetColumn(3);

    /// <summary>A disabled camera that never renders; only its culling parameters are read.</summary>
    private static Camera CullCamera()
    {
        if (cullCam != null)
            return cullCam;
        var go = new GameObject("PeakXR.EyeCulling") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(go);
        cullCam = go.AddComponent<Camera>();
        cullCam.enabled = false;
        cullCam.stereoTargetEye = StereoTargetEyeMask.None;
        return cullCam;
    }

    private static NVec N(Vector3 v) => new(v.x, v.y, v.z);

    private static NQuat N(Quaternion q) => new(q.x, q.y, q.z, q.w);

    private static Vector3 U(NVec v) => new(v.X, v.Y, v.Z);

    private static Quaternion U(NQuat q) => new(q.X, q.Y, q.Z, q.W);
}
