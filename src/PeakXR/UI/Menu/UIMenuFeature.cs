using PeakXR.Framework;
using PeakXR.UI.World;

namespace PeakXR.UI.Menu;

/// <summary>
/// ui.menu: the title menu as a panel on the 3D beach, the pause menu as a world panel (through
/// ui.world's default menu placement), and a head-tracked 3D loading space with fades around every
/// level switch instead of a frozen or black headset.
/// </summary>
public sealed class UIMenuFeature : Feature
{
    public override string Id => "ui.menu";

    public override int Order => 51;

    protected internal override void Install()
    {
        var cfg = UIMenuSettings.Bind(Plugin.Settings);
        // First, so a missing target leaves no runtime objects behind.
        Patch(typeof(KillLoadingScreenPatch));
        var stage = new TitleStage(cfg);
        var space = LoadingSpace.Create();
        var runtime = UIMenuRuntime.Create(stage, space, cfg);

        // Public static events on the game's loading handler; a rename fails this feature here.
        LoadingScreenHandler.OnLoadingStart += runtime.OnLoadingStart;
        LoadingScreenHandler.OnLoadingDone += runtime.OnLoadingDone;
        KillLoadingScreenPatch.Runtime = runtime;

        // Title menu canvas (MainMenu root holds MainMenuPageHandler) and every LoadingScreen prefab's canvas.
        CanvasRules.AddCustom("MainMenuPageHandler", stage);
        CanvasRules.AddCustom("LoadingScreen", space);
    }
}
