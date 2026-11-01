using PeakXR.UI.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Render;

/// <summary>
/// Gives each MultiPass eye its own <c>unity_CameraProjection</c> and <c>unity_CameraInvProjection</c>. URP sets the
/// eye's view, projection and <c>unity_CameraToWorld</c> per XR pass (UniversalCameraData.PushBuiltinShaderConstantsXR)
/// but never these two, so they keep the values <c>SetupCameraProperties</c> takes from the mono camera. Shaders that
/// turn a screen position into a view ray with them then point the ray differently from what the eye sees, and their
/// effect lands in different places in each eye. In this build the inverse is bound by the distance fog quads
/// (GD/ScreenFog, GD/SphereFog), SC Post Effects Fog, Cloud Shadows, Caustics and Sketch, FogOrb, DarkFade, FireHaze,
/// LavaPost, RainPost, NickPost/Injury and Decal, and the forward one by SC Post Effects DepthNormals, which the outline
/// pass reconstructs normals with (SerializedPass name tables read from the game data, 2026-10-29 scan). One pass per
/// eye, after the camera setup and the prepasses, fixes all. Right before each eye's cull it also records what that eye
/// renders with (<see cref="Eyes"/>, for self-tests vr.480 to vr.483) and moves the head-locked overlays ahead of it
/// (<see cref="StatusOverlays.PoseForEye"/>).
/// </summary>
internal sealed class EyeMatrixFeature : ScriptableRendererFeature
{
    /// <summary>One MultiPass eye's last render: its matrices and viewport, the frame, and whether the depth-normal
    /// prepass ran for it, which is what gives transparents that eye's own depth (issue #5).</summary>
    internal struct EyeRecord
    {
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Rect Viewport;
        public int Frame;
        public bool NormalsPrepass;
    }

    /// <summary>By multipass id: left, right.</summary>
    internal static readonly EyeRecord[] Eyes = new EyeRecord[2];

    private static readonly int ProjectionId = Shader.PropertyToID("unity_CameraProjection");
    private static readonly int InvProjectionId = Shader.PropertyToID("unity_CameraInvProjection");

    private EyeMatrixPass pass;

    public override void Create() =>
        pass = new EyeMatrixPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData
    )
    {
        if (renderingData.cameraData.xr.enabled)
            renderer.EnqueuePass(pass);
    }

    /// <summary>Runs per XR pass with that eye's view already set (URP UniversalRenderPipeline.cs:818), right before its
    /// cull (:639, ScriptableRenderer.cs:891-897).</summary>
    public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
    {
        var xr = cameraData.xr;
        if (!xr.enabled || xr.singlePassEnabled || xr.viewCount != 1)
            return;
        var view = xr.GetViewMatrix(0);
        Eyes[xr.multipassId & 1] = new EyeRecord
        {
            View = view,
            Proj = xr.GetProjMatrix(0),
            Viewport = xr.GetViewport(0),
            Frame = Time.frameCount,
        };
        if (RenderFixesFeature.EyeLockedOverlays.Value)
            StatusOverlays.PoseForEye(view, cameraData.camera);
    }

    private sealed class EyeMatrixPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Matrix4x4 Projection;
            public Matrix4x4 InvProjection;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            if (!camera.xr.enabled)
                return;
            // Only the depth-normal prepass creates this texture (URP UniversalRenderer.cs:2176), and it ran before this pass.
            Eyes[camera.xr.multipassId & 1].NormalsPrepass = frameData
                .Get<UniversalResourceData>()
                .cameraNormalsTexture.IsValid();
            using var builder = renderGraph.AddUnsafePass<PassData>(
                "PeakXR eye inverse projection",
                out var data
            );
            // Unity's convention for these: the eye's (non-GPU) projection and its inverse.
            data.Projection = camera.GetProjectionMatrix(0);
            data.InvProjection = data.Projection.inverse;
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(
                static (PassData d, UnsafeGraphContext context) =>
                {
                    context.cmd.SetGlobalMatrix(ProjectionId, d.Projection);
                    context.cmd.SetGlobalMatrix(InvProjectionId, d.InvProjection);
                }
            );
        }
    }
}
