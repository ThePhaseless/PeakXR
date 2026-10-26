using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Render;

/// <summary>
/// Gives each MultiPass eye its own <c>unity_CameraInvProjection</c>. URP sets the eye's view, projection and
/// <c>unity_CameraToWorld</c> per XR pass (UniversalCameraData.PushBuiltinShaderConstantsXR) but never this one, so it
/// keeps the value <c>SetupCameraProperties</c> takes from the mono camera. Shaders that turn a screen position into a
/// view ray with it then point the ray differently from what the eye sees, and their effect lands in different places
/// in each eye. In this build that is the distance fog quads (GD/ScreenFog, GD/SphereFog), SC Post Effects Fog, Cloud
/// Shadows, Caustics and Sketch, FogOrb, DarkFade, FireHaze, LavaPost, RainPost, NickPost/Injury and Decal (the shader
/// parameter tables read from the game data). One pass per eye, after the camera setup and the prepasses, fixes all.
/// </summary>
internal sealed class EyeMatrixFeature : ScriptableRendererFeature
{
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
            // Unity's convention for this matrix: the inverse of the eye's (non-GPU) projection.
            data.InvProjection = camera.GetProjectionMatrix(0).inverse;
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(
                static (PassData d, UnsafeGraphContext context) =>
                    context.cmd.SetGlobalMatrix(InvProjectionId, d.InvProjection)
            );
        }
    }
}
