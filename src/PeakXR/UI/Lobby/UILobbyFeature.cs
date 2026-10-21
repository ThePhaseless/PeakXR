using PeakXR.Framework;
using PeakXR.UI.World;

namespace PeakXR.UI.Lobby;

/// <summary>
/// ui.lobby: the airport kiosks in VR. The boarding pass and custom run options dock as world panels
/// at the check-in kiosk that opened them, and the invite kiosk explains that the Steam invite dialog
/// is on the desktop overlay. Kick, disconnect and version modals need nothing here: ui.world shows
/// Zorro modals as menus and ui.menu fades the direct Title loads.
/// </summary>
public sealed class UILobbyFeature : Feature
{
    public override string Id => "ui.lobby";

    public override int Order => 53;

    internal static InviteNote Note { get; private set; }

    protected internal override void Install()
    {
        var cfg = UILobbySettings.Bind(Plugin.Settings);
        // Patch first: a missing target fails the feature before anything is registered.
        Patch(typeof(InviteKioskPatch));

        // Canvas_BoardingPass holds BoardingPass (A/BoardingPass.cs:12), Canvas_CustomOptionWindow holds
        // CustomOptionsWindow (A/CustomOptionsWindow.cs:5); both sit under GUIManager (R2 §2.1).
        var dock = new KioskDock(cfg);
        CanvasRules.AddCustomMenu("BoardingPass", dock);
        CanvasRules.AddCustomMenu("CustomOptionsWindow", dock);

        Note = InviteNote.Create(cfg);
    }
}
