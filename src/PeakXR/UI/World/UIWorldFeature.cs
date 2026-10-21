using PeakXR.Framework;

namespace PeakXR.UI.World;

/// <summary>
/// ui.world: every screen-space canvas becomes a world-space panel in VR (menus in front of you, HUD on
/// your body or wrist), a controller laser drives uGUI, and fades are drawn head-locked. Overlay canvases
/// are invisible in a headset, so without this nothing of the game's UI would be seen.
/// </summary>
public sealed class UIWorldFeature : Feature
{
    public override string Id => "ui.world";

    public override int Order => 50;

    internal static UIWorldSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = UIWorldSettings.Bind(Plugin.Settings);
        HudSplit.Resolve();

        Patch(typeof(CanvasScalerEnablePatch));
        Patch(typeof(MenuWindowOpenPatch));
        Patch(typeof(EventCameraPatch));
        Patch(typeof(PointerHitFilter));
        Patch(typeof(CameraQuadPatch));
        Patch(typeof(PlayerNamePatch));

        UIWorldRuntime.Create();
    }
}
