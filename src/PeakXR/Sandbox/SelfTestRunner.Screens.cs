using System.Collections;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Guide;
using PeakXR.UI.Keyboard;
using PeakXR.UI.Passport;
using PeakXR.UI.World;
using PeakXR.VR;
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
        // WP6 refusals on these screens: scripted hands, each step torn down whatever its verdict.
        yield return ScreenHandStep("vr.116-disabled-control", 15f, DisabledControlStep());
        MenuWindow.CloseAllWindows();
        yield return ScreenHandStep("vr.118-passport-locked", 25f, PassportLockedStep());
        if (PassportManager.instance != null && PassportManager.instance.isOpen)
            PassportManager.instance.ToggleOpen();
        yield return Wait(0.6f);
        yield return ScreenHandStep("vr.119-keyboard-type", 25f, KeyboardTypeStep());
        VRKeyboard.CancelDialog();
        yield return ScreenHandStep("vr.120-invite-offline", 10f, InviteOfflineStep());
        // WP6 gap round: try-on by touch, the wait refusal, the modal bell and the communication guide rows.
        yield return ScreenHandStep("vr.129-passport-try-on", 20f, PassportTryOnStep());
        if (PassportManager.instance != null && PassportManager.instance.isOpen)
            PassportManager.instance.ToggleOpen();
        yield return Wait(0.6f);
        yield return ScreenHandStep("vr.130-connecting-wait", 15f, LoadingWaitStep());
        if (fakeLoading)
            LoadingFlag(false);
        MenuWindow.CloseAllWindows();
        yield return ScreenHandStep("vr.131-modal-bell", 10f, ModalBellStep());
        if (Zorro.UI.Modal.Modal.IsOpen)
            Zorro.UI.Modal.Modal.CloseModal();
        yield return VRStep("vr.132-guide-comms-rows", 25f, GuideCommsStep());
        if (guideStage != null)
            Destroy(guideStage);
    }

    /// <summary>A screen step driven by scripted hands, which go back to the idle pose afterwards; no settle or
    /// item cleanup, since nothing here touches the body.</summary>
    IEnumerator ScreenHandStep(string id, float timeout, IEnumerator body)
    {
        yield return VRStep(id, timeout, Hands(body));
        EndHands();
    }

    IEnumerator Hands(IEnumerator body)
    {
        BeginHands();
        Affordance.ResetGate();
        yield return Wait(0.3f);
        yield return body;
    }

    /// <summary>Aims both lasers from beside the head at <paramref name="target"/> (as vr.19 does).</summary>
    void AimLasers(Vector3 target)
    {
        AimLaser(true, target);
        AimLaser(false, target);
    }

    /// <summary>Aims one laser from beside the head at <paramref name="target"/>; the other hand stays put (it may
    /// hold the passport, which moves with it).</summary>
    void AimLaser(bool left, Vector3 target)
    {
        var rig = VRRig.Instance;
        HeadFrame(out var head, out var forward, out var right);
        var p = head + (left ? -right : right) * 0.15f + Vector3.down * 0.2f + forward * 0.2f;
        var dir = rig.Origin.InverseTransformDirection(target - rig.Origin.TransformPoint(p));
        Script(left).Position = p;
        Script(left).Rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    /// <summary>
    /// A hand's fingertip (4 cm ahead of the aim pose, as the keyboard and passport pokes use) relative to the scripted
    /// grip, in tracking space, for the current scripted rotation. The rig shows a scripted pose only two frames after
    /// the step sets it (the sampler copies it before the step runs, the rig poses in LateUpdate), so a per-frame
    /// closed loop on the tip overshoots and oscillates; set the rotation, wait, measure once, then place open loop.
    /// </summary>
    IEnumerator MeasureTip(bool left, Quaternion trackingRotation)
    {
        tipLeft = left;
        Script(left).Rotation = trackingRotation;
        for (int i = 0; i < 3; i++)
            yield return null;
        var rig = VRRig.Instance;
        var aim = rig.Aim(left);
        tipOffset =
            rig.Origin.InverseTransformPoint(aim.position + aim.forward * 0.04f)
            - Script(left).Position;
    }

    Vector3 tipOffset;
    bool tipLeft;

    /// <summary>
    /// One frame of passport evidence for vr.118 and vr.129 (run 34 failed both without saying why): the window and
    /// canvas state, where <paramref name="cell"/> is on the canvas, the right aim ray and fingertip, what the laser
    /// is over and what the poke last saw.
    /// </summary>
    static void LogPassport(string tag, PassportButton cell, bool left)
    {
        var passport = PassportManager.instance;
        var canvas = HandBook.PassportCanvas;
        var aim = VRRig.Instance != null ? VRRig.Instance.Aim(left) : null;
        if (passport == null || canvas == null || aim == null || cell == null)
        {
            Plugin.Log.LogInfo(
                $"[selftest] {tag}: passport {passport != null}, canvas {canvas != null}, aim {aim != null}, cell {cell != null}"
            );
            return;
        }
        var rect = (RectTransform)cell.transform;
        var centre = rect.TransformPoint(rect.rect.center);
        var onCanvas = canvas.transform.InverseTransformPoint(centre);
        var canvasRect = ((RectTransform)canvas.transform).rect;
        var tip = aim.position + aim.forward * 0.04f;
        float offRay = Vector3.Cross(aim.forward, centre - aim.position).magnitude;
        var over = LaserPointer.Over;
        Plugin.Log.LogInfo(
            $"[selftest] {tag}: open {passport.isOpen} ui {passport.uiObject.activeInHierarchy} canvas {canvas.renderMode} at {canvas.transform.position:F3} "
                + $"rect {canvasRect.size:F0} | cell '{cell.name}' at {centre:F3} = canvas {onCanvas:F0} active {cell.gameObject.activeInHierarchy} "
                + $"| aim {aim.position:F3} fwd {aim.forward:F2} ray miss {offRay:F3} m | laser over '{(over != null ? over.name : "-")}' "
                + $"({(LaserPointer.OverLeft ? "L" : "R")}, showing {LaserPointer.Showing}) | tip {tip:F3} poke front {PassportPoke.LastFront:F3} "
                + $"hit '{(PassportPoke.LastHit != null ? PassportPoke.LastHit.name : "-")}' pointing {PassportPoke.LastPointing}"
        );
    }

    /// <summary>Puts the measured hand's fingertip on <paramref name="world"/> (after <see cref="MeasureTip"/>).</summary>
    void TipTo(Vector3 world) =>
        Script(tipLeft).Position = VRRig.Instance.Origin.InverseTransformPoint(world) - tipOffset;

    /// <summary>Eye to grip an adult reaches without stepping: 0.6-0.75 m from the shoulder, which sits about 0.25 m
    /// below and beside the eyes (as MpArmReach). A poke placed farther is a pose no player makes.</summary>
    const float PokeReach = 0.85f;

    /// <summary>Fails the step when the poking hand was put past <see cref="PokeReach"/>.</summary>
    void CheckPokeReach(bool left, string what)
    {
        float d = Vector3.Distance(Script(left).Position, VRInput.HeadPosition);
        if (d > PokeReach)
            Fail($"{what} is {d:0.00} m from the eyes, past arm's reach ({PokeReach:0.00} m)");
    }

    /// <summary>A short right-trigger pull: the laser click. The Airport hands the passport to the right hand, so the
    /// pull must stay a UI click and not also use the passport (Action_Passport.cs:5 opens it): a step fails if it
    /// opened.</summary>
    IEnumerator PullRight()
    {
        bool passportShut = PassportManager.instance == null || !PassportManager.instance.isOpen;
        Script(false).Trigger = 1f;
        yield return Wait(0.15f);
        Script(false).Trigger = 0f;
        yield return Wait(0.1f);
        if (passportShut && PassportManager.instance != null && PassportManager.instance.isOpen)
            Fail("the laser click also used the held passport");
    }

    /// <summary>vr.116: the boarding pass's lower-ascent arrow at its limit is disabled (BoardingPass.cs:186); a laser
    /// press on it refuses with Locked and the arrow turned red, and the ascent stays.</summary>
    IEnumerator DisabledControlStep()
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
        yield return Wait(0.8f);
        var arrow = pass.decrementAscentButton;
        // Down to the lowest ascent through the arrow's own click (BoardingPass.cs:108) until UpdateAscent
        // disables it; in a custom run it is disabled already.
        for (int i = 0; i < 12 && arrow.interactable; i++)
        {
            arrow.onClick.Invoke();
            yield return null;
        }
        if (arrow.interactable)
        {
            Fail("lower-ascent arrow still enabled after 12 clicks");
            yield break;
        }
        int ascent = pass.ascentIndex;
        var rect = (RectTransform)arrow.transform;
        AimLasers(rect.TransformPoint(rect.rect.center));
        yield return Wait(0.3f);
        float since = CueMark();
        yield return PullRight();
        // The right trigger pulled, so the right hand must get the cue (not whichever laser last pressed).
        yield return ExpectCue(false, DenyReason.Locked, since, true);
        if (!stepOk)
            yield break;
        if (pass.ascentIndex != ascent)
            Fail($"the disabled arrow changed the ascent {ascent} to {pass.ascentIndex}");
        else
            stepNote = $"ascent {ascent}: {cueNote}";
    }

    /// <summary>vr.118: a locked cosmetic (its lock icon shown, PassportButton.cs:27-29) pressed by laser refuses with
    /// Locked and its border red; a forward swipe on the last page bumps (EndStop) instead of flipping.</summary>
    IEnumerator PassportLockedStep()
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
        yield return Wait(0.8f);
        var notes = new System.Collections.Generic.List<string>();

        PassportButton locked = null;
        for (int page = 0; page < 30 && locked == null; page++)
        {
            foreach (var b in passport.GetComponentsInChildren<PassportButton>(false))
                if (b.lockedIcon != null && b.lockedIcon.gameObject.activeSelf)
                {
                    locked = b;
                    break;
                }
            if (locked == null && !HandBook.FlipPage(1))
                break;
            yield return Wait(0.1f);
        }
        bool free = !HandBook.BookInLeftHand();
        if (locked != null)
        {
            // Only the free hand aims. The book rides on the hand that holds the passport item
            // (HandBook.BookInLeftHand: the right hand in vr35), so aiming that hand carried the cell along with it.
            var rect = (RectTransform)locked.transform;
            AimLaser(free, rect.TransformPoint(rect.rect.center));
            for (float until = Now + 0.3f; Now < until; )
            {
                LogPassport("vr.118 aim", locked, free);
                yield return null;
            }
            float since = CueMark();
            Script(free).Trigger = 1f;
            for (float until = Now + 0.15f; Now < until; )
            {
                LogPassport("vr.118 pull", locked, free);
                yield return null;
            }
            Script(free).Trigger = 0f;
            yield return Wait(0.1f);
            LogPassport("vr.118 after", locked, free);
            yield return ExpectCue(free, DenyReason.Locked, since, true);
            if (!stepOk)
                yield break;
            notes.Add($"locked '{locked.name}': {cueNote}");
        }
        else
            notes.Add("no locked cosmetic in this save");

        if (!PassportFeature.Settings.SwipeToFlip.Value)
        {
            notes.Add("SwipeToFlip off: page end not tried");
            stepNote = string.Join("; ", notes);
            yield break;
        }
        for (int i = 0; i < 60 && HandBook.FlipPage(1); i++)
            yield return Wait(0.05f);
        // The free hand strokes right to left across the book, a little in front of the page.
        var canvas = HandBook.PassportCanvas;
        var origin = VRRig.Instance.Origin;
        float half = HandBook.HalfExtents(true).x * 0.7f;
        Vector3 Along(float x) =>
            origin.InverseTransformPoint(
                canvas.transform.position + canvas.transform.rotation * new Vector3(x, 0f, -0.06f)
            );
        Script(free).Trigger = 0f;
        Script(free).Position = Along(half);
        yield return Wait(0.2f);
        float swiped = CueMark();
        yield return MoveTrack(free, Along(-half), 0.15f);
        yield return ExpectFeel(free, CueFeel.EndStop, swiped, 1.5f);
        if (!stepOk)
            yield break;
        notes.Add($"last page: {cueNote}");
        stepNote = string.Join("; ", notes);
    }

    /// <summary>vr.119: a 4-character dialog typed by poking with the right fingertip ("ab12"), a fifth key refused
    /// as full (PackFull, key red) with the text unchanged, then Done submits it.</summary>
    IEnumerator KeyboardTypeStep()
    {
        string result = null;
        bool submitted = false;
        if (
            !VRKeyboard.TryOpenDialog(
                "PeakXR self-test",
                4,
                (text, ok) =>
                {
                    result = text;
                    submitted = ok;
                }
            )
        )
        {
            Fail("keyboard refused to open (ui.world not ready or no rig)");
            yield break;
        }
        // The left hand well below the desk, so only the right one pokes.
        HeadFrame(out var head, out var forward, out var right);
        Script(true).Position = head - right * 0.3f + Vector3.down * 0.7f;
        yield return Until(() => VRKeyboard.HasPose, 2f, "keyboard not placed");
        yield return Wait(0.4f);
        foreach (var c in "ab12")
        {
            yield return PokeKey(c, KeyKind.Character);
            if (!stepOk)
                yield break;
        }
        if (VRKeyboard.DialogText != "ab12")
        {
            Fail($"typed '{VRKeyboard.DialogText}', not 'ab12'");
            yield break;
        }
        float since = CueMark();
        yield return PokeKey('x', KeyKind.Character);
        yield return ExpectCue(false, DenyReason.PackFull, since, true);
        if (!stepOk)
            yield break;
        if (VRKeyboard.DialogText != "ab12")
        {
            Fail($"the refused key typed: '{VRKeyboard.DialogText}'");
            yield break;
        }
        var note = cueNote;
        yield return PokeKey('\0', KeyKind.Done);
        yield return Until(() => result != null, 1f, "Done did not close the dialog");
        if (!stepOk)
            yield break;
        if (!submitted || result != "ab12")
            Fail($"submitted {submitted} '{result}'");
        else
            stepNote = $"typed and submitted 'ab12'; fifth key: {note}";
    }

    /// <summary>One poke of the right fingertip (<c>VRKeyboard</c>'s tip: 4 cm ahead of the aim pose) on a key: from
    /// 5 cm in front of it to 1.5 cm past the face, then back. Placed open loop from the measured tip offset
    /// (<see cref="MeasureTip"/>): a per-frame closed loop overshot by the rig's two-frame lag and pressed 'a' twice.</summary>
    IEnumerator PokeKey(char c, KeyKind kind)
    {
        if (!VRKeyboard.TryKey(c, kind, out var key))
        {
            Fail($"no key '{c}' {kind}");
            yield break;
        }
        var rig = VRRig.Instance;
        var into = VRKeyboard.PanelRotation * Vector3.forward;
        yield return MeasureTip(
            false,
            Quaternion.Inverse(rig.Origin.rotation) * VRKeyboard.PanelRotation
        );
        float[] depths =
        {
            -0.05f,
            -0.05f,
            -0.05f,
            -0.03f,
            -0.01f,
            0.005f,
            0.015f,
            0.015f,
            -0.05f,
            -0.05f,
        };
        foreach (var d in depths)
        {
            TipTo(key + into * d);
            yield return null;
            yield return null;
        }
        CheckPokeReach(false, $"key '{c}'");
        yield return Wait(0.1f);
    }

    /// <summary>vr.120: the invite kiosk pressed while not in a Steam lobby (AirportInviteFriendsKiosk.cs:39-43 opens
    /// nothing) refuses with Blocked on the nearer hand and, where the kiosk shows, tints it. Offline the kiosk is
    /// hidden (:36) and is pressed through the same call.</summary>
    IEnumerator InviteOfflineStep()
    {
        var kiosk = FindAnyObjectByType<AirportInviteFriendsKiosk>(FindObjectsInactive.Include);
        if (kiosk == null || Character.localCharacter == null)
        {
            stepSkip = "no invite kiosk here";
            yield break;
        }
        var matchmaking = Peak.Network.NetCode.Matchmaking;
        if (matchmaking != null && matchmaking.InLobby)
        {
            stepSkip = "in a Steam lobby: pressing would open the overlay";
            yield break;
        }
        var rig = VRRig.Instance;
        var at = kiosk.transform.position;
        bool left =
            (rig.LeftHand.position - at).sqrMagnitude < (rig.RightHand.position - at).sqrMagnitude;
        bool shown = kiosk.gameObject.activeInHierarchy;
        float since = CueMark();
        kiosk.Interact(Character.localCharacter);
        yield return ExpectCue(left, DenyReason.Blocked, since, shown);
        if (stepOk)
            stepNote = $"kiosk {(shown ? "shown" : "hidden offline")}: {cueNote}";
    }

    // ---------------------------------------------------------------- WP6 gap round

    /// <summary>The fake load flag vr.130 set, cleared again whatever the step's verdict.</summary>
    bool fakeLoading;

    /// <summary>vr.132's demo stage, destroyed whatever the step's verdict.</summary>
    GameObject guideStage;

    /// <summary>LoadingScreenHandler's private load flag (A/LoadingScreenHandler.cs:26, read by <c>loading</c> :30).</summary>
    void LoadingFlag(bool on)
    {
        Traverse.Create<LoadingScreenHandler>().Field("_loading").SetValue(on);
        fakeLoading = on;
    }

    /// <summary>The local player's worn skin (what PassportManager.SetActiveButton reads, PassportManager.cs:239-240).</summary>
    static int WornSkin() =>
        GameHandler
            .GetService<PersistentPlayerDataService>()
            .GetPlayerData(Photon.Pun.PhotonNetwork.LocalPlayer)
            .customizationData.currentSkin;

    /// <summary>vr.129: the free hand (not the one holding the book) points and pokes an unlocked, unworn skin cell of the hand-held passport;
    /// the cell's own Click equips it (PassportButton.cs:63-68, PassportManager.SetOption :277), the worn skin changes
    /// and the hand gets a Contact tick. The original skin is put back.</summary>
    IEnumerator PassportTryOnStep()
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
        passport.OpenTab(Customization.Type.Skin);
        yield return Wait(0.8f);
        int before = WornSkin();
        PassportButton cell = null;
        foreach (var b in passport.buttons)
            if (
                b != null
                && b.gameObject.activeInHierarchy
                && (b.lockedIcon == null || !b.lockedIcon.gameObject.activeSelf)
                && b.border != null
                && b.border.color != passport.activeBorderColor
            )
            {
                cell = b;
                break;
            }
        if (cell == null || HandBook.PassportCanvas == null)
        {
            stepSkip = "no unlocked, unworn skin on the first page";
            yield break;
        }
        int equips = PassportPoke.Equips;
        bool free = !HandBook.BookInLeftHand();
        float since = CueMark();
        yield return PokeCell(cell, free);
        bool pressed = PassportPoke.Equips > equips;
        int after = WornSkin();
        CharacterCustomization.SetCharacterSkinColor(before);
        if (!stepOk)
            yield break;
        if (!pressed)
        {
            Fail("the poke did not press the cell");
            yield break;
        }
        yield return ExpectFeel(free, CueFeel.Contact, since);
        if (!stepOk)
            yield break;
        if (after == before)
            Fail($"worn skin stayed {before}");
        else
            stepNote = $"skin {before} to {after} by poke, restored; {cueNote}";
    }

    /// <summary>One poke of the free hand's fingertip (4 cm ahead of the aim pose, as <see cref="PassportPoke"/>) on a passport
    /// cell, with the index straight (grip closed, no trigger touch): from 5 cm in front of the page to 1.5 cm past it.
    /// Placed open loop from the measured tip offset (<see cref="MeasureTip"/>), on the cell re-read each step.</summary>
    IEnumerator PokeCell(PassportButton cell, bool left)
    {
        var rig = VRRig.Instance;
        var canvas = HandBook.PassportCanvas.transform;
        Script(left).TriggerTouch = false;
        Script(left).Trigger = 0f;
        yield return GripTo(left, 1f, 0.1f);
        float[] depths =
        {
            -0.05f,
            -0.05f,
            -0.05f,
            -0.03f,
            -0.01f,
            0.005f,
            0.015f,
            0.015f,
            -0.05f,
            -0.05f,
        };
        yield return MeasureTip(left, Quaternion.Inverse(rig.Origin.rotation) * canvas.rotation);
        var rect = (RectTransform)cell.transform;
        foreach (var d in depths)
        {
            // Re-read each step: the book rides on the other hand. The page faces the eyes along -Z: +Z goes into it.
            TipTo(rect.TransformPoint(rect.rect.center) + canvas.forward * d);
            yield return null;
            LogPassport($"vr.129 d {d:0.000}", cell, left);
            yield return null;
            LogPassport($"vr.129 d {d:0.000}", cell, left);
        }
        CheckPokeReach(left, $"passport cell '{cell.name}'");
        yield return GripTo(left, 0f, 0.1f);
        Script(left).TriggerTouch = true;
        yield return Wait(0.1f);
    }

    /// <summary>vr.130: the boarding pass's Start pressed by laser while a load runs (the load flag set for the press)
    /// does nothing in vanilla (AirportCheckInKiosk.cs:117-123) and refuses with NotNow, the button red.</summary>
    IEnumerator LoadingWaitStep()
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
        yield return Wait(0.8f);
        var start = pass.startGameButton;
        if (start == null || !start.interactable)
        {
            stepSkip = "Start not pressable here";
            yield break;
        }
        var rect = (RectTransform)start.transform;
        AimLasers(rect.TransformPoint(rect.rect.center));
        yield return Wait(0.3f);
        LoadingFlag(true);
        float since = CueMark();
        yield return PullRight();
        LoadingFlag(false);
        yield return ExpectCue(false, DenyReason.NotNow, since, true);
        if (stepOk)
            stepNote = cueNote;
    }

    /// <summary>vr.131: a game modal opened through its own console test (Modal.cs:84-87, Open :64) rings the notice
    /// (NotNow) once ui.world has placed it, the ring on its first button.</summary>
    IEnumerator ModalBellStep()
    {
        float since = CueMark();
        Zorro.UI.Modal.Modal.TestModal();
        yield return Until(() => Zorro.UI.Modal.Modal.IsOpen, 1f, "modal did not open");
        if (!stepOk)
            yield break;
        yield return ExpectCue(LaserPointer.PressLeft, DenyReason.NotNow, since, false, 1.5f);
        if (stepOk)
            stepNote = cueNote;
    }

    /// <summary>vr.132: the VR Guide demos for ping, talk, mute and spectate each play a loop on a ghost stage in front
    /// of you (the stage a guide row opens, <see cref="GhostDemo"/>) and sound their cue at least once.</summary>
    IEnumerator GuideCommsStep()
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
        {
            Fail("no rig");
            yield break;
        }
        guideStage = new GameObject("PeakXR_SelfTestGuide");
        var flat = Vector3.ProjectOnPlane(rig.Head.forward, Vector3.up).normalized;
        guideStage.transform.SetPositionAndRotation(
            rig.Head.position + flat * 0.8f,
            Quaternion.LookRotation(-flat, Vector3.up)
        );
        var demo = new GhostDemo("Demo", guideStage.transform);
        var fails = new System.Collections.Generic.List<string>();
        var notes = new System.Collections.Generic.List<string>();
        foreach (
            var topic in new[]
            {
                GuideTopic.Ping,
                GuideTopic.Mic,
                GuideTopic.Mute,
                GuideTopic.Spectate,
            }
        )
        {
            demo.Play(topic);
            float until = Now + GuideTable.Get(topic).Loop + 0.2f;
            while (Now < until)
            {
                demo.Tick(Time.unscaledDeltaTime, 1f, rig.Head);
                yield return null;
            }
            notes.Add($"{topic}: {demo.SoundsPlayed} cue sounds");
            if (!demo.Alive || demo.Topic != topic)
                fails.Add($"{topic} demo not showing");
            else if (demo.SoundsPlayed < 1)
                fails.Add($"{topic} played no cue sound in a loop");
        }
        demo.Destroy();
        Conclude(fails, notes);
    }

    /// <summary>vr.70: the passport through <c>PassportManager.ToggleOpen</c> (PassportManager.cs:113, what using the
    /// passport item calls, Action_Passport.cs:5); ui.passport places it at the hand: the page centre within the book's
    /// lift and side shift (BookPose) of the holding hand, plus slack for the cropped canvas.</summary>
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
        // Aimed between the book and the hand under it, so the frame shows the book held, not floating.
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(HandBook.BookInLeftHand()) : null;
        if (canvas == null || hand == null)
            Fail($"passport canvas placed {canvas != null}, book hand {hand != null}");
        else if (PassportFeature.Settings.PassportAtHand.Value)
        {
            var cfg = PassportFeature.Settings;
            float gap = Vector3.Distance(CentreOf(canvas.transform), hand.position);
            float most = cfg.BookLift.Value + cfg.BookSide.Value + 0.15f;
            stepNote =
                $"passport canvas {canvas.renderMode}, page centre {gap:0.00} m from the book hand";
            if (gap > most)
                Fail(
                    $"the passport page is {gap:0.00} m from the hand holding it (at most {most:0.00} m)"
                );
        }
        else
            stepNote = $"passport canvas {canvas.renderMode} (PassportAtHand off)";
        yield return canvas != null
            ? ShotAt(
                "vr-70-passport",
                hand != null
                    ? Vector3.Lerp(CentreOf(canvas.transform), hand.position, 0.4f)
                    : CentreOf(canvas.transform)
            )
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
        // A player opens the kiosk from within the game's interact reach (Interaction.distance 2 m, Interaction.cs:7);
        // this step opens it from wherever the run stands, so say how far that was and what blocks the view.
        float kioskAway = Vector3.Distance(
            Character.localCharacter.Center,
            kiosk.transform.position
        );
        int hidden = 0;
        var corners = new Vector3[4];
        ((RectTransform)pass.panel.transform).GetWorldCorners(corners);
        foreach (var corner in corners)
            if (
                Physics.Linecast(
                    eye,
                    Vector3.Lerp(corner, at, 0.05f),
                    HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                    QueryTriggerInteraction.Ignore
                )
            )
                hidden++;
        stepNote =
            $"boarding pass {Vector3.Distance(eye, at):0.0} m from the eye, kiosk {kioskAway:0.0} m from the body, {hidden}/4 corners hidden by geometry";
        yield return ShotAt("vr-71-kiosk", at, 1.2f);
        MenuWindow.CloseAllWindows();
        yield return Until(() => !pass.isOpen, 2f, "boarding pass did not close");
        yield return Wait(0.5f);
    }

    /// <summary>Both hands just past the keyboard's near edge (half its height is about 0.17 m), pointing along it.</summary>
    static void KeyboardHands()
    {
        var origin = VRRig.Instance != null ? VRRig.Instance.Origin : null;
        if (origin == null)
            return;
        var rotation = VRKeyboard.PanelRotation;
        var edge = VRKeyboard.PanelPosition + rotation * new Vector3(0f, -0.22f, -0.03f);
        var aim = Quaternion.Inverse(origin.rotation) * rotation;
        VRInput.Left = KeyboardHand(
            origin.InverseTransformPoint(edge + rotation * Vector3.left * 0.2f),
            aim
        );
        VRInput.Right = KeyboardHand(
            origin.InverseTransformPoint(edge + rotation * Vector3.right * 0.2f),
            aim
        );
    }

    static HandState KeyboardHand(Vector3 position, Quaternion rotation) =>
        new()
        {
            Tracked = true,
            Position = position,
            Rotation = rotation,
        };

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
        // The idle pose holds the hands up in front of the face for the other screenshots, right over the
        // keyboard; a player about to type rests them at its near edge instead.
        var idle = VRInput.TestOverride;
        VRInput.TestOverride = KeyboardHands;
        yield return Wait(0.5f);
        yield return ShotAt("vr-72-keyboard", VRKeyboard.PanelPosition);
        VRInput.TestOverride = idle;
        VRKeyboard.CancelDialog();
        yield return Until(() => closed, 1f, "keyboard dialog did not close");
    }
}
