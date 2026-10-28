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
/// slice in MultiPass) and drawn whole, upright and letterboxed into the window. API:
/// https://docs.unity3d.com/6000.3/Documentation/ScriptReference/XR.XRDisplaySubsystem.GetMirrorViewBlitDesc.html
/// </summary>
internal sealed class DesktopMirror
{
    // Static: one mirror per process, so the buffer lives as long as the game (no dispose owner needed).
    private static readonly CommandBuffer cmd = new() { name = "PeakXR desktop mirror" };
    private static Material blit;
    private static bool blitLooked;
    private RenderTexture slice;
    private string lastPath;
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
        // Only after the headset render: manual camera renders (guidebook, title card) are contexts too.
        if (
            eye == MirrorEye.Off
            || !VRSession.Active
            || display == null
            || !display.running
            || !HeadsetContext.Renders(cameras)
        )
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
                    Note("provider native blit");
                    return;
                }
            }
            if (desc.blitParamsCount > 0)
            {
                desc.GetBlitParameter(0, out var p);
                // The provider's srcRect already crops the eye to the window's aspect; draw the whole eye instead.
                Blit(p.srcTex, p.srcTexArraySlice, new Rect(0f, 0f, 1f, 1f));
                Note("blit params", p.srcRect);
                return;
            }
        }

        // No mirror description: take the eye's render pass texture directly (MultiPass: pass 0 left, 1 right).
        int pass = eye == MirrorEye.Right && display.GetRenderPassCount() > 1 ? 1 : 0;
        var rt = display.GetRenderTextureForRenderPass(pass);
        Blit(rt, rt != null && rt.volumeDepth > 1 ? pass : 0, new Rect(0f, 0f, 1f, 1f));
        Note("render pass texture");
    }

    /// <summary>Logs the mirror path once per change, so a log shows which one drew the window.</summary>
    private void Note(string path, Rect providerRect = default)
    {
        if (ReferenceEquals(path, lastPath))
            return;
        lastPath = path;
        Plugin.Log.LogInfo(
            $"[rig.camera] desktop mirror via {path} (flip {SystemInfo.graphicsUVStartsAtTop}, copy shader {blit != null}, provider rect {providerRect})"
        );
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
        // XR eye textures are stored top row first, like the backbuffer, while Unity samples v = 0 as the bottom
        // row; on D3D, Metal and Vulkan (UV starts at top) the eye has to be flipped to land upright on screen.
        bool flip = SystemInfo.graphicsUVStartsAtTop;
        float v0 = flip ? srcRect.yMax : srcRect.y;
        float v1 = flip ? srcRect.y : srcRect.yMax;

        if (!blitLooked)
        {
            // Look once: a failed Shader.Find is a native search we must not repeat every frame.
            blitLooked = true;
            blit = Shader.Find("Hidden/BlitCopy") is { } shader ? new Material(shader) : null;
        }
        if (blit == null)
        {
            // No copy shader to draw a quad with: stretch over the whole window, upright at least.
            Graphics.Blit(
                tex,
                (RenderTexture)null,
                new Vector2(srcRect.width, v1 - v0),
                new Vector2(srcRect.x, v0)
            );
            return;
        }

        // Whole eye, aspect kept, black bars: the hands at the bottom of the eye stay in view.
        var r = MirrorMath.FitRect(srcAspect, dstAspect);
        RenderTexture.active = null;
        GL.Clear(false, true, Color.black);
        blit.mainTexture = tex;
        blit.SetPass(0);
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        GL.TexCoord2(srcRect.x, v0);
        GL.Vertex3(r.X, r.Y, 0f);
        GL.TexCoord2(srcRect.x, v1);
        GL.Vertex3(r.X, r.Y + r.W, 0f);
        GL.TexCoord2(srcRect.xMax, v1);
        GL.Vertex3(r.X + r.Z, r.Y + r.W, 0f);
        GL.TexCoord2(srcRect.xMax, v0);
        GL.Vertex3(r.X + r.Z, r.Y, 0f);
        GL.End();
        GL.PopMatrix();
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
