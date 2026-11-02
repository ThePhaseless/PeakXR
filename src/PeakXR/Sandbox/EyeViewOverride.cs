using System;
using HarmonyLib;
using PeakXR.Rig;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// Self-test vr.481 only: renders the MultiPass eyes from other views, to find state that belongs to an eye rather than
/// to its view. With both passes on one view, only such state can make the images differ; with the views swapped, stale
/// data from the other eye shows too. It chains on the XR layout override above <see cref="Render.EyeCulling"/>, so it
/// sees the real eyes and the shared cull is already built from them. <c>XRSystem.ReconfigurePass</c> re-runs the chain on
/// the whole layout before each eye (XRSystem.cs:285-298) having reset only that eye, so a read-modify-write swap would
/// swap twice: the views are read once a frame and written from that copy on every call.
/// </summary>
internal static class EyeViewOverride
{
    internal enum Mode
    {
        Mono,
        Swap,
    }

    private static readonly Action<XRLayout, Camera> Hook = OnLayout;
    private static readonly Matrix4x4[] Views = new Matrix4x4[2];
    private static readonly Matrix4x4[] Projections = new Matrix4x4[2];

    private static AccessTools.FieldRef<Action<XRLayout, Camera>> layoutOverride;
    private static Action<XRPass, int, Matrix4x4, Matrix4x4> setter;
    private static Action<XRLayout, Camera> previous;
    private static Camera target;
    private static Mode mode;
    private static bool installed;
    private static int cachedFrame = -1;

    /// <summary>What the override needs, or why it cannot run on this URP.</summary>
    internal static string Resolve()
    {
        try
        {
            var field =
                AccessTools.Field(typeof(XRSystem), "s_LayoutOverride")
                ?? throw new MissingFieldException(typeof(XRSystem).FullName, "s_LayoutOverride");
            layoutOverride = AccessTools.StaticFieldRefAccess<Action<XRLayout, Camera>>(field);
            setter = StereoWidth.BuildViewProjSetter();
            return null;
        }
        catch (Exception e)
        {
            return $"URP internals missing ({e.GetType().Name}: {e.Message})";
        }
    }

    internal static void Install(Mode how, Camera camera)
    {
        if (installed)
            return;
        mode = how;
        target = camera;
        cachedFrame = -1;
        previous = layoutOverride();
        layoutOverride() = Hook;
        installed = true;
    }

    /// <summary>False when something chained above the hook, which then stays in the chain doing nothing.</summary>
    internal static bool Remove()
    {
        if (!installed)
            return true;
        installed = false;
        if (layoutOverride() != Hook)
        {
            Plugin.Log.LogWarning(
                "[selftest] EyeViewOverride is not on top of the XR layout override, so it stays chained"
            );
            return false;
        }
        layoutOverride() = previous;
        previous = null;
        return true;
    }

    private static void OnLayout(XRLayout layout, Camera camera)
    {
        previous?.Invoke(layout, camera);
        if (!installed || camera == null || camera != target)
            return;
        var passes = layout.GetActivePasses();
        XRPass a = null;
        XRPass b = null;
        for (int i = 0; i < passes.Count; i++)
        {
            var (cam, pass) = passes[i];
            if (cam != camera || pass == null || !pass.enabled || pass.viewCount != 1)
                continue;
            if (a == null)
                a = pass;
            else
                b = pass;
        }
        if (b == null)
            return;
        if (cachedFrame != Time.frameCount)
        {
            cachedFrame = Time.frameCount;
            Cache(a);
            Cache(b);
        }
        Write(a);
        Write(b);
    }

    private static void Cache(XRPass pass)
    {
        int eye = pass.multipassId & 1;
        Views[eye] = pass.GetViewMatrix(0);
        Projections[eye] = pass.GetProjMatrix(0);
    }

    private static void Write(XRPass pass)
    {
        int from = mode == Mode.Mono ? 0 : 1 - (pass.multipassId & 1);
        setter(pass, 0, Views[from], Projections[from]);
    }
}
