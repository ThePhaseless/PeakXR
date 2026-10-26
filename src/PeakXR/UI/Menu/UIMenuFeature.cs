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
        // 0.55 m sat in Meta's 0.5-0.8 m avoid band (vr-practices review P2-5). 0.45 brought the buttons to the face:
        // the tester found the menu "too small and too close", so the title is one big screen now.
        ConfigMigration.FromOldDefault(cfg.TitleReach, 0.55f);
        ConfigMigration.FromOldDefault(cfg.TitleReach, Core.TitleLayout.TouchReach);
        ConfigMigration.FromOldDefault(cfg.TitleMenuDistance, 2.2f);
        // First, so a missing target leaves no runtime objects behind.
        Patch(typeof(KillLoadingScreenPatch));
        Patch(typeof(LoadSceneProcessPatch));
        Patch(typeof(LoadRejectedPatch));
        Patch(typeof(EnteringRoomWaitPatch));
        Patch(typeof(LoadingWaitPatch));
        Patch(typeof(ModalBellPatch));
        // The splash's own skip flag, set by its skip keys (A/Pretitle.cs:12, set at :61).
        var skipSplash = GameMember.Field<Pretitle, bool>("allowedToSwitch");
        var stage = new TitleStage(cfg);
        var space = LoadingSpace.Create();
        var runtime = UIMenuRuntime.Create(stage, space, cfg, skipSplash);

        // Public static events on the game's loading handler; a rename fails this feature here.
        LoadingScreenHandler.OnLoadingStart += runtime.OnLoadingStart;
        LoadingScreenHandler.OnLoadingDone += runtime.OnLoadingDone;
        KillLoadingScreenPatch.Runtime = runtime;
        LoadSceneProcessPatch.Runtime = runtime;
        LoadRejectedPatch.Runtime = runtime;

        // Title menu canvas (MainMenu root holds MainMenuPageHandler) and every LoadingScreen prefab's canvas.
        CanvasRules.AddCustomMenu("MainMenuPageHandler", stage);
        CanvasRules.AddCustom("LoadingScreen", space);
        // Boot splash canvas (Pretitle/Anim/Canvas, screen space camera), shown in the loading room. Only with
        // the room on: without it the splash keeps ui.world's default panel and its own camera.
        if (cfg.LoadingSpace.Value)
            CanvasRules.AddCustom("Pretitle", space);
    }
}
