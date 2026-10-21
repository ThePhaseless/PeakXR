using System.Collections;
using PeakXR.UI.Keyboard;
using PeakXR.UI.Passport;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Airport UI that the rest of the run never opens, so the screenshots show it: the passport, the boarding pass
/// at the check-in kiosk and the VR keyboard. Each step opens the window through the game's own call, shoots it
/// aimed at the panel and closes it again, so 04-beach still boards from a clean airport.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator AirportScreenSteps()
    {
        yield return VRStep("vr.70-passport", 10f, PassportShot());
        MenuWindow.CloseAllWindows();
        yield return VRStep("vr.71-kiosk", 10f, KioskShot());
        MenuWindow.CloseAllWindows();
        yield return VRStep("vr.72-keyboard", 10f, KeyboardShot());
        VRKeyboard.CancelDialog();
    }

    /// <summary>vr.70: the passport through <c>PassportManager.ToggleOpen</c> (PassportManager.cs:113, what using the
    /// passport item calls, Action_Passport.cs:5); ui.passport places it at the hand.</summary>
    IEnumerator PassportShot()
    {
        var passport = PassportManager.instance;
        if (passport == null || Character.localCharacter == null)
        {
            stepSkip = "no PassportManager or character here";
            yield break;
        }
        if (!passport.isOpen)
            passport.ToggleOpen();
        yield return Until(() => HandBook.PassportOpen, 2f, "passport did not open");
        if (!stepOk)
            yield break;
        // Its open animation, and a frame for ui.world to place the canvas.
        yield return Wait(0.8f);
        var canvas = HandBook.PassportCanvas;
        stepNote =
            canvas != null ? $"passport canvas {canvas.renderMode}" : "passport canvas not placed";
        yield return canvas != null
            ? ShotAt("vr-70-passport", CentreOf(canvas.transform))
            : Shot("vr-70-passport");
        passport.ToggleOpen();
        yield return Until(() => !passport.isOpen, 2f, "passport did not close");
        // CloseRoutine ignores toggles for 0.5 s (PassportManager.cs:152).
        yield return Wait(0.6f);
    }

    /// <summary>vr.71: the kiosk's boarding pass as the kiosk opens it (<c>Interact_CastFinished</c>,
    /// AirportCheckInKiosk.cs:112); ui.lobby docks it at the kiosk, which can be metres away, so the view moves
    /// up to it.</summary>
    IEnumerator KioskShot()
    {
        var kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        var pass = GUIManager.instance != null ? GUIManager.instance.boardingPass : null;
        if (kiosk == null || pass == null || Character.localCharacter == null)
        {
            stepSkip = "no kiosk or boarding pass here";
            yield break;
        }
        kiosk.Interact_CastFinished(Character.localCharacter);
        yield return Until(() => pass.isOpen, 2f, "boarding pass did not open");
        if (!stepOk)
            yield break;
        // BoardingPass.OnOpen fades it in over 0.5 s (BoardingPass.cs:147).
        yield return Wait(0.8f);
        var at = CentreOf(pass.panel.transform);
        var eye = ViewCamera() != null ? ViewCamera().transform.position : at;
        stepNote = $"boarding pass {Vector3.Distance(eye, at):0.0} m from the eye";
        yield return ShotAt("vr-71-kiosk", at, 1.2f);
        MenuWindow.CloseAllWindows();
        yield return Until(() => !pass.isOpen, 2f, "boarding pass did not close");
        yield return Wait(0.5f);
    }

    /// <summary>vr.72: the VR keyboard as a platform text dialog opens it (<see cref="VRKeyboard.TryOpenDialog"/>),
    /// then cancelled.</summary>
    IEnumerator KeyboardShot()
    {
        bool closed = false;
        if (!VRKeyboard.TryOpenDialog("PeakXR self-test", 32, (_, _) => closed = true))
        {
            Fail("keyboard refused to open (ui.world not ready or no rig)");
            yield break;
        }
        yield return Wait(0.5f);
        yield return ShotAt("vr-72-keyboard", VRKeyboard.PanelPosition);
        VRKeyboard.CancelDialog();
        yield return Until(() => closed, 1f, "keyboard dialog did not close");
    }
}
