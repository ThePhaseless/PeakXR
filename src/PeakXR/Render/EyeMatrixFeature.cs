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
/// eye, after the camera setup and the prepasses, fixes all.
/// </summary>
internal sealed class EyeMatrixFeature : ScriptableRendererFeature
{
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
