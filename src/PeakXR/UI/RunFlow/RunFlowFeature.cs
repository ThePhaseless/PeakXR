using PeakXR.Framework;
using PeakXR.UI.World;
using UnityEngine;

namespace PeakXR.UI.RunFlow;

/// <summary>
/// ui.runflow: every scripted run transition (teleports, campfire segment swaps, summit portal, run
/// end, beach wake-up, credits) becomes fade-out, stable black hold, fade-in, so the game never moves
/// or cuts the view on its own; camera shakes become controller rumble; the end cutscene and credits
/// play on a world-locked cinema screen; the end-of-run report is a wide world panel; any controller
/// button ends the credits.
/// </summary>
public sealed class RunFlowFeature : Feature
{
    public override string Id => "ui.runflow";

    public override int Order => 54;

    /// <summary>
    /// Set by the camera/cutscene feature while it shows end cutscenes on a VR-safe surface (cinema
    /// screen, fixed seat). Run flow then stops holding black during the cutscene and credits.
    /// </summary>
    public static bool CutsceneViewHandled { get; set; }

    /// <summary>Frame in which a camera shake that run flow rumbles itself (rising lava/gloom, freed soul,
    /// portal) is being added, or -1. comfort.events skips its own rumble for that shake, so each source has one
    /// owner and <c>ShakeHaptics</c> = 0 silences it.</summary>
    internal static int ShakeOwnedFrame = -1;

    /// <summary>True once if the shake being added right now is run flow's (see <see cref="ShakeOwnedFrame"/>).</summary>
    internal static bool TakeShake()
    {
        bool owned = ShakeOwnedFrame == Time.frameCount;
        ShakeOwnedFrame = -1;
        return owned;
    }

    internal static RunFlowSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = RunFlowSettings.Bind(Plugin.Settings);
        EyeBlinkPatch.Resolve();

        Patch(typeof(WarpPlayerPatch));
        Patch(typeof(GoToSegmentPatch));
        Patch(typeof(SegmentTitlePatch));
        Patch(typeof(EndGamePatch));
        Patch(typeof(CreditsStartPatch));
        Patch(typeof(CreditsAnyKeyPatch));
        Patch(typeof(PortalPatch));
        Patch(typeof(RisingShakePatch));
        Patch(typeof(SoulShakePatch));
        Patch(typeof(SoulShakeOwnerPatch));
        Patch(typeof(BeachWakeupPatch));
        Patch(typeof(BeachFallPatch));
        Patch(typeof(EyeBlinkPatch));

        // Last, so a missing patch target above leaves no runtime object or canvas rule behind.
        RunFlowRuntime.Create(Settings);
        // Canvas_End holds only the EndScreen window and its children (popups, WaitingForPlayersUI).
        CanvasRules.AddCustom("EndScreen", new EndScreenPlacement(Settings));
    }
}
