using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace PeakXR.Rig;

/// <summary>
/// Draws one eye into the desktop window. URP's own mirror pass cannot run in this game: its
/// <c>XRMirrorView</c> shader has no compiled code in the build (docs/research/stack.md section 4), so
/// xr.bootstrap turns URP's mirror off and this copies the eye after all cameras have rendered. The
/// provider's native blit is used when it offers one; otherwise the eye texture is copied (one array
/// slice in MultiPass) and stretched to fill the window without distortion.
/// </summary>
internal sealed class DesktopMirror
{
    // Static: one mirror per process, so the buffer lives as long as the game (no dispose owner needed).
    private static readonly CommandBuffer cmd = new() { name = "PeakXR desktop mirror" };
    private RenderTexture slice;
    private bool hooked;

    public void Enable()
    {
        if (hooked)
            return;
        hooked = true;
        RenderPipelineManager.endContextRendering += OnEndContextRendering;
    }

    public void Disable()
    {
        if (!hooked)
            return;
        hooked = false;
        RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        if (slice != null)
        {
            slice.Release();
            Object.Destroy(slice);
            slice = null;
        }
    }

    private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        var eye = CameraFeature.Mirror.Value;
        var display = XRRuntimeInfo.Display;
        if (eye == MirrorEye.Off || !VRSession.Active || display == null || !display.running)
            return;

        int mode =
            eye == MirrorEye.Right ? XRMirrorViewBlitMode.RightEye : XRMirrorViewBlitMode.LeftEye;
        if (display.GetMirrorViewBlitDesc(null, out var desc, mode))
        {
            if (desc.nativeBlitAvailable)
            {
                cmd.Clear();
                cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                if (
                    display.AddGraphicsThreadMirrorViewBlit(cmd, desc.nativeBlitInvalidStates, mode)
                )
                {
                    Graphics.ExecuteCommandBuffer(cmd);
                    return;
                }
            }
            if (desc.blitParamsCount > 0)
            {
                desc.GetBlitParameter(0, out var p);
                Blit(p.srcTex, p.srcTexArraySlice, p.srcRect);
                return;
            }
        }

        // No mirror description: take the eye's render pass texture directly (MultiPass: pass 0 left, 1 right).
        int pass = eye == MirrorEye.Right && display.GetRenderPassCount() > 1 ? 1 : 0;
        var rt = display.GetRenderTextureForRenderPass(pass);
        Blit(rt, rt != null && rt.volumeDepth > 1 ? pass : 0, new Rect(0f, 0f, 1f, 1f));
    }

    private void Blit(RenderTexture src, int arraySlice, Rect srcRect)
    {
        if (src == null)
            return;
        Texture tex = src;
        if (src.dimension == TextureDimension.Tex2DArray)
        {
            EnsureSlice(src);
            Graphics.CopyTexture(src, arraySlice, 0, slice, 0, 0);
            tex = slice;
        }

        float srcAspect = srcRect.width * src.width / Mathf.Max(1f, srcRect.height * src.height);
        float dstAspect = Screen.width / (float)Mathf.Max(1, Screen.height);
        var uv = MirrorMath.EnvelopeUv(srcAspect, dstAspect);
        var scale = new Vector2(srcRect.width * uv.Z, srcRect.height * uv.W);
        var offset = new Vector2(
            srcRect.x + srcRect.width * uv.X,
            srcRect.y + srcRect.height * uv.Y
        );
        Graphics.Blit(tex, (RenderTexture)null, scale, offset);
    }

    private void EnsureSlice(RenderTexture src)
    {
        if (
            slice != null
            && slice.width == src.width
            && slice.height == src.height
            && slice.graphicsFormat == src.graphicsFormat
            && slice.antiAliasing == src.antiAliasing
        )
            return;
        if (slice != null)
        {
            slice.Release();
            Object.Destroy(slice);
        }
        var d = new RenderTextureDescriptor(src.width, src.height, src.graphicsFormat, 0)
        {
            dimension = TextureDimension.Tex2D,
            volumeDepth = 1,
            msaaSamples = Mathf.Max(1, src.antiAliasing),
        };
        slice = new RenderTexture(d) { name = "PeakXR mirror eye" };
        slice.Create();
    }
}
