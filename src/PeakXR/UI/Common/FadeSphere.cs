using UnityEngine;

namespace PeakXR.UI.Common;

/// <summary>
/// Head-locked fade: an inside-out sphere around the eyes, drawn after everything (UI included) with
/// depth testing off. A sphere instead of a quad so it covers any headset FOV in both eyes. Parented to a
/// transform that is never deactivated (the rig head, not the game's camera, which cutscenes switch off).
/// </summary>
[DefaultExecutionOrder(32000)]
internal sealed class FadeSphere : MonoBehaviour
{
    private static Mesh mesh;
    private Material material;
    private MeshRenderer meshRenderer;

    public static FadeSphere Attach(Transform parent)
    {
        if (mesh == null)
            mesh = UIMaterials.SphereMesh();

        var fadeMaterial = UIMaterials.Create(Color.clear, drawOnTop: true, renderQueue: 5000);
        var go = UIMaterials.MeshObject("PeakXR_Fade", mesh, fadeMaterial, parent);
        go.GetComponent<MeshRenderer>().sortingOrder = PeakXR.Core.DrawOrder.Fade;
        // Radius 0.3 m: beyond every near plane in use (rig.camera NearClip is at most 0.1 m).
        go.transform.localScale = Vector3.one * 0.6f;

        var sphere = go.AddComponent<FadeSphere>();
        sphere.material = fadeMaterial;
        sphere.meshRenderer = go.GetComponent<MeshRenderer>();
        sphere.Apply();
        return sphere;
    }

    private void LateUpdate()
    {
        FadeView.Tick();
        Apply();
    }

    private void Apply()
    {
        var alpha = FadeView.Alpha;
        var visible = alpha > 0.001f;
        if (meshRenderer.enabled != visible)
            meshRenderer.enabled = visible;
        if (visible)
            material.color = new Color(0f, 0f, 0f, alpha);
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
