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
        Settings.MenuPanels.SettingChanged += (_, _) => WorldCanvases.OnMenuPanelsChanged();
        Settings.MenuBackplate.SettingChanged += (_, _) =>
        {
            WorldCanvases.OnBackplateChanged();
            // Text on a panel plate goes without a halo while plates show (BackdropStrip.OnKeptBacking): restrip.
            WorldCanvases.OnMenuPanelsChanged();
        };
        Settings.MenuBackplateOpacity.SettingChanged += (_, _) =>
            WorldCanvases.OnBackplateChanged();
        // Off, the plates must drop the last measured opacity and return to the fixed one.
        Settings.MenuBackplateAdaptive.SettingChanged += (_, _) =>
            WorldCanvases.OnBackplateChanged();
        HudSplit.Resolve();

        Patch(typeof(CanvasScalerEnablePatch));
        Patch(typeof(MenuWindowOpenPatch));
        Patch(typeof(EventCameraPatch));
        Patch(typeof(CameraQuadPatch));
        Patch(typeof(PlayerNamePatch));

        UIWorldRuntime.Create();
    }
}
