using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.UI.Status;

/// <summary>
/// Per-frame status cues. In Peripheral mode the game's ScreenVFX quads keep animating (so their timing and
/// photosensitivity scaling stay vanilla) but are not drawn; their current intensity, plus the dying state,
/// drives one <see cref="PeripheralRim"/> tint instead. Runs after ui.world has placed the HUD.
/// </summary>
[DefaultExecutionOrder(31100)]
internal sealed class StatusRuntime : MonoBehaviour
{
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private readonly PeripheralCue cue = new();
    private GUIManager gui;
    private ScreenVFX[] effects = System.Array.Empty<ScreenVFX>();
    private Material[] materials = System.Array.Empty<Material>();
    private NVector3[] colors = System.Array.Empty<NVector3>();
    private bool hiding;
    private PeripheralRim rim;
    private VRRig rimRig;

    public static void Create()
    {
        var go = new GameObject("PeakXR_StatusHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<StatusRuntime>();
    }

    private void LateUpdate()
    {
        var rig = VRRig.Instance;
        var peripheral =
            UIWorldRuntime.Ready
            && rig != null
            && UIWorldFeature.Settings.StatusOverlays.Value == StatusOverlayMode.Peripheral;
        Bind(GUIManager.instance);
        SetHidden(peripheral);
        if (!peripheral)
        {
            if (rim != null && rim.Alive)
                rim.Show(Color.clear);
            return;
        }

        if (rim == null || !rim.Alive || rimRig != rig)
        {
            rim?.Destroy();
            rim = new PeripheralRim(
                rig.Head,
                rig.HeadCamera != null ? rig.HeadCamera.nearClipPlane : 0.05f
            );
            rimRig = rig;
        }

        cue.Reset();
        for (int i = 0; i < effects.Length; i++)
        {
            var fx = effects[i];
            if (fx == null || fx.renderer == null || !fx.gameObject.activeInHierarchy)
                continue;
            // ScreenVFX tweens its own material instance (renderer.material); reading it back is the effect's strength.
            var material =
                materials[i] != null ? materials[i] : materials[i] = fx.renderer.material;
            cue.Add(colors[i], material.GetFloat(IntensityId));
        }
        AddDying();

        var tint = cue.Color;
        rim.Show(new Color(tint.X, tint.Y, tint.Z, cue.Alpha(UIStatusFeature.Intensity.Value)));
    }

    /// <summary>Passed out and waiting for help: the dying bar's colour (A/GUIManager.cs:578) at the edge of the view.</summary>
    private void AddDying()
    {
        var c = Character.localCharacter;
        if (
            !UIStatusFeature.DyingTint.Value
            || c == null
            || gui == null
            || gui.dyingBarGradient == null
        )
            return;
        if (!c.data.fullyPassedOut || c.data.dead)
            return;
        var timer = c.data.deathTimer;
        var color = gui.dyingBarGradient.Evaluate(1f - timer);
        cue.Add(new NVector3(color.r, color.g, color.b), PeripheralCue.DyingWeight(timer));
    }

    private void Bind(GUIManager current)
    {
        if (current == gui)
            return;
        gui = current;
        hiding = false;
        var fields = UIStatusFeature.ScreenFxFields;
        effects = new ScreenVFX[current != null ? fields.Length : 0];
        materials = new Material[effects.Length];
        colors = new NVector3[effects.Length];
        for (int i = 0; i < effects.Length; i++)
        {
            effects[i] = fields[i].GetValue(current) as ScreenVFX;
            colors[i] = PeripheralCue.ColorFor(fields[i].Name);
        }
    }

    private void SetHidden(bool hide)
    {
        if (hide == hiding && !hide)
            return;
        hiding = hide;
        // Re-applied every frame while hiding: effects that spawn or re-enable keep their renderer state.
        foreach (var fx in effects)
            if (fx != null && fx.renderer != null && fx.renderer.forceRenderingOff != hide)
                fx.renderer.forceRenderingOff = hide;
    }

    private void OnDestroy() => rim?.Destroy();
}
