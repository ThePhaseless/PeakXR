using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using NMatrix = System.Numerics.Matrix4x4;

namespace PeakXR.Rig;

/// <summary>
/// The eye separation for <see cref="VRRig.Scale"/>. The game's camera is posed by hand (auto XR tracking off), so the
/// runtime's per-eye views are its real eye offsets around that pose and a scaled rig would move the head s times
/// further while the eyes stayed a real IPD apart: near things would read a different size than head motion says.
/// URP builds each eye's view in <c>XRSystem.CreateDefaultLayout</c>/<c>ReconfigurePass</c> and then calls the layout
/// override (Unity.RenderPipelines.Core XRSystem.s_LayoutOverride, invoked after the views are built); this override
/// widens every view of the head camera's passes (<see cref="StereoScale.ScaleEye"/>). The views live in
/// <c>XRPass.m_Views</c> as the internal <c>XRView</c> struct, rewritten through a delegate compiled once. If any of
/// that is missing (a URP update), the scale still applies to head and hands and the log says the IPD stays real.
/// </summary>
internal static class StereoWidth
{
    /// <summary>An eye further than this (m) from the camera's own pose means the views are not built around it.</summary>
    private const float MaxEyeOffset = 0.1f;

    private static bool tried;
    private static bool working;
    private static bool warned;
    private static Action<XRLayout, Camera> previous;
    private static Action<XRPass, int, Matrix4x4, Matrix4x4> setView;
    private static readonly Dictionary<int, Matrix4x4> lastCenter = new();

    internal static void Install()
    {
        if (tried)
            return;
        tried = true;
        try
        {
            var overrideField = typeof(XRSystem).GetField(
                "s_LayoutOverride",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            var viewsField = typeof(XRPass).GetField(
                "m_Views",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (overrideField == null || viewsField == null)
                throw new MissingMemberException("XRSystem.s_LayoutOverride or XRPass.m_Views");
            setView = BuildSetter(viewsField);
            previous = overrideField.GetValue(null) as Action<XRLayout, Camera>;
            overrideField.SetValue(null, (Action<XRLayout, Camera>)OnLayout);
            working = true;
            Plugin.Log.LogInfo(
                "[rig.camera] eye separation follows Camera.WorldScale (XR layout override)"
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[rig.camera] eye separation cannot follow Camera.WorldScale, it stays real: {e.GetType().Name}: {e.Message}"
            );
        }
    }

    /// <summary>(pass, index, view, previous view) => pass.m_Views[index] = new XRView(..., view, previous, ...),
    /// every other field kept.</summary>
    private static Action<XRPass, int, Matrix4x4, Matrix4x4> BuildSetter(FieldInfo viewsField)
    {
        var listType = viewsField.FieldType;
        var viewType = listType.GetGenericArguments()[0];
        const BindingFlags any =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        var ctor =
            viewType.GetConstructor(
                any,
                null,
                new[]
                {
                    typeof(Matrix4x4),
                    typeof(Matrix4x4),
                    typeof(Matrix4x4),
                    typeof(bool),
                    typeof(Rect),
                    typeof(Mesh),
                    typeof(Mesh),
                    typeof(int),
                },
                null
            ) ?? throw new MissingMemberException("XRView constructor");
        var pass = Expression.Parameter(typeof(XRPass), "pass");
        var index = Expression.Parameter(typeof(int), "index");
        var view = Expression.Parameter(typeof(Matrix4x4), "view");
        var prev = Expression.Parameter(typeof(Matrix4x4), "prev");
        var old = Expression.Variable(viewType, "old");
        var item = Expression.MakeIndex(
            Expression.Field(pass, viewsField),
            listType.GetProperty("Item"),
            new Expression[] { index }
        );
        Expression F(string name) =>
            Expression.Field(
                old,
                viewType.GetField(name, any) ?? throw new MissingMemberException("XRView." + name)
            );
        // Block(variables, params expressions): the variables array is not one of the expressions.
#pragma warning disable S3220
        var body = Expression.Block(
            new[] { old },
            Expression.Assign(old, item),
            Expression.Assign(
                item,
                Expression.New(
                    ctor,
                    F("projMatrix"),
                    view,
                    prev,
                    F("isPrevViewMatrixValid"),
                    F("viewport"),
                    F("occlusionMesh"),
                    F("visibleMesh"),
                    F("textureArraySlice")
                )
            )
        );
#pragma warning restore S3220
        return Expression
            .Lambda<Action<XRPass, int, Matrix4x4, Matrix4x4>>(body, pass, index, view, prev)
            .Compile();
    }

    private static void OnLayout(XRLayout layout, Camera camera)
    {
        previous?.Invoke(layout, camera);
        var rig = VRRig.Instance;
        if (!working || rig == null || camera == null || camera != rig.HeadCamera)
            return;
        float scale = rig.Scale;
        var center = camera.worldToCameraMatrix;
        int id = camera.GetInstanceID();
        var before = lastCenter.TryGetValue(id, out var c) ? c : center;
        lastCenter[id] = center;
        if (Mathf.Approximately(scale, 1f))
            return;
        // A scaled camera transform is left to Unity (it may already scale the eyes): widen only around a unit one.
        if (Mathf.Abs(camera.transform.lossyScale.x - 1f) > 1e-3f)
            return;
        try
        {
            var passes = layout.GetActivePasses();
            for (int p = 0; p < passes.Count; p++)
            {
                var (cam, pass) = passes[p];
                if (cam != camera || pass == null || !pass.enabled)
                    continue;
                for (int i = 0; i < pass.viewCount; i++)
                {
                    var eye = pass.GetViewMatrix(i);
                    if (!AroundCenter(eye, center))
                        return;
                    var prevEye = pass.GetPrevViewValid(i) ? pass.GetPrevViewMatrix(i) : eye;
                    setView(pass, i, Scale(eye, center, scale), Scale(prevEye, before, scale));
                }
            }
        }
        catch (Exception e)
        {
            working = false;
            Plugin.Log.LogWarning(
                $"[rig.camera] eye separation scaling failed, the IPD stays real: {e.GetType().Name}: {e.Message}"
            );
        }
    }

    /// <summary>The runtime's eye sits within a real IPD of the camera pose (else the views are built some other way
    /// and widening them would tear the stereo apart).</summary>
    private static bool AroundCenter(Matrix4x4 eye, Matrix4x4 center)
    {
        var offset = eye * center.inverse;
        var t = new Vector3(offset.m03, offset.m13, offset.m23);
        if (t.sqrMagnitude <= MaxEyeOffset * MaxEyeOffset)
            return true;
        if (!warned)
        {
            warned = true;
            Plugin.Log.LogWarning(
                $"[rig.camera] an eye view is {t.magnitude:0.000} m from the camera pose; eye separation left real"
            );
        }
        return false;
    }

    /// <summary>Unity matrices are column-vector, Core's row-vector: transpose in, transpose out.</summary>
    private static Matrix4x4 Scale(Matrix4x4 eye, Matrix4x4 center, float scale) =>
        U(StereoScale.ScaleEye(N(eye), N(center), scale));

    private static NMatrix N(Matrix4x4 m) =>
        new(
            m.m00,
            m.m10,
            m.m20,
            m.m30,
            m.m01,
            m.m11,
            m.m21,
            m.m31,
            m.m02,
            m.m12,
            m.m22,
            m.m32,
            m.m03,
            m.m13,
            m.m23,
            m.m33
        );

    private static Matrix4x4 U(NMatrix m)
    {
        var r = new Matrix4x4();
        r.m00 = m.M11;
        r.m10 = m.M12;
        r.m20 = m.M13;
        r.m30 = m.M14;
        r.m01 = m.M21;
        r.m11 = m.M22;
        r.m21 = m.M23;
        r.m31 = m.M24;
        r.m02 = m.M31;
        r.m12 = m.M32;
        r.m22 = m.M33;
        r.m32 = m.M34;
        r.m03 = m.M41;
        r.m13 = m.M42;
        r.m23 = m.M43;
        r.m33 = m.M44;
        return r;
    }
}
