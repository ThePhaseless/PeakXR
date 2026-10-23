using System.Collections;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.Guide;
using PeakXR.UI.Keyboard;
using PeakXR.UI.Lobby;
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
        // The kiosk steps stand at the check-in kiosk, each opening it by hand as a player does; back to spawn after.
        yield return ScreenHandStep("vr.71-kiosk", 25f, KioskShot());
        MenuWindow.CloseAllWindows();
        // WP6 refusals on these screens: scripted hands, each step torn down whatever its verdict.
        yield return ScreenHandStep("vr.116-disabled-control", 30f, DisabledControlStep());
        MenuWindow.CloseAllWindows();
        yield return ScreenHandStep("vr.130-connecting-wait", 25f, LoadingWaitStep());
        if (fakeLoading)
            LoadingFlag(false);
        MenuWindow.CloseAllWindows();
        yield return WarpBack();
        yield return VRStep("vr.72-keyboard", 10f, KeyboardShot());
        VRKeyboard.CancelDialog();
        yield return ScreenHandStep("vr.118-passport-locked", 25f, PassportLockedStep());
        ClosePassport();
        yield return Wait(0.6f);
        yield return ScreenHandStep("vr.187-passport-swipe", 20f, PassportSwipeStep());
        ClosePassport();
        yield return Wait(0.6f);
        yield return ScreenHandStep("vr.119-keyboard-type", 25f, KeyboardTypeStep());
        VRKeyboard.CancelDialog();
        yield return ScreenHandStep("vr.120-invite-offline", 10f, InviteOfflineStep());
        // WP6 gap round: try-on by touch, the wait refusal, the modal bell and the communication guide rows.
        yield return ScreenHandStep("vr.129-passport-try-on", 20f, PassportTryOnStep());
        ClosePassport();
        yield return Wait(0.6f);
        yield return ScreenHandStep("vr.131-modal-bell", 15f, ModalBellStep());
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

    static void ClosePassport()
    {
        if (PassportManager.instance != null && PassportManager.instance.isOpen)
            PassportManager.instance.ToggleOpen();
    }

    IEnumerator PullRight() => LaserClick(false);

    /// <summary>A short trigger pull: the laser click. The Airport hands the passport to the right hand, so the
    /// pull must stay a UI click and not also use the passport (Action_Passport.cs:5 opens it): a step fails if it
    /// opened.</summary>
    IEnumerator LaserClick(bool left)
    {
        bool passportShut = PassportManager.instance == null || !PassportManager.instance.isOpen;
        Script(left).Trigger = 1f;
        yield return Wait(0.15f);
        Script(left).Trigger = 0f;
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
        yield return OpenKioskByHand(kiosk, pass);
        if (!stepOk)
            yield break;
        yield return Wait(0.8f);
        var arrow = pass.decrementAscentButton;
        var rect = (RectTransform)arrow.transform;
        // Down to the lowest ascent by laser clicks on the arrow (its onClick, BoardingPass.cs:108) until UpdateAscent
        // disables it; in a custom run it is disabled already.
        for (int i = 0; i < 12 && arrow.interactable; i++)
        {
            AimLasers(rect.TransformPoint(rect.rect.center));
            yield return Wait(0.2f);
            yield return PullRight();
            if (!stepOk)
                yield break;
        }
        if (arrow.interactable)
        {
            Fail("lower-ascent arrow still enabled after 12 laser clicks");
            yield break;
        }
        int ascent = pass.ascentIndex;
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
        float swiped = CueMark();
        yield return SwipeForward(free);
        yield return ExpectFeel(free, CueFeel.EndStop, swiped, 1.5f);
        if (!stepOk)
            yield break;
        notes.Add($"last page: {cueNote}");
        stepNote = string.Join("; ", notes);
    }

    /// <summary>The free hand strokes right to left across the open passport, a little in front of the page: a forward
    /// page swipe (ui.passport's PageSwipe).</summary>
    IEnumerator SwipeForward(bool free)
    {
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
        yield return MoveTrack(free, Along(-half), 0.15f);
    }

    /// <summary>The passport's page: its tab (<c>activeType</c>, PassportManager.cs:20) and page in it (<c>page</c>,
    /// :60, turned by PageNext :316).</summary>
    static string PassportPage(PassportManager passport) =>
        $"{passport.activeType} p{GameMember.Field<PassportManager, int>("page")(passport)}";

    /// <summary>vr.187: on the passport's first tab and page, a forward swipe of the free hand turns the page (a next
    /// page or the next tab, HandBook.FlipPassport) with no EndStop bump; a swipe that turned nothing fails.</summary>
    IEnumerator PassportSwipeStep()
    {
        var passport = PassportManager.instance;
        if (passport == null || Character.localCharacter == null)
        {
            stepSkip = "no PassportManager or character here";
            yield break;
        }
        if (!PassportFeature.Settings.SwipeToFlip.Value)
        {
            stepSkip = "UI.Passport.SwipeToFlip off";
            yield break;
        }
        if (passport.tabs == null || passport.tabs.Length < 2 || passport.tabs[0] == null)
        {
            stepSkip = "the passport has fewer than two tabs to turn through";
            yield break;
        }
        if (!passport.isOpen)
            passport.ToggleOpen();
        yield return Until(() => HandBook.PassportOpen, 2f, "passport did not open");
        if (!stepOk)
            yield break;
        passport.OpenTab(passport.tabs[0].type);
        yield return Wait(0.8f);
        bool free = !HandBook.BookInLeftHand();
        string before = PassportPage(passport);
        float swiped = CueMark();
        yield return SwipeForward(free);
        yield return Wait(0.3f);
        string after = PassportPage(passport);
        bool bumped = CueLog.Capture.Felt(free, CueFeel.EndStop, swiped, out _);
        if (after == before)
            Fail($"the forward swipe left the passport on {before}{(bumped ? " and bumped" : "")}");
        else if (bumped)
            Fail($"the swipe turned {before} to {after} but also gave the EndStop bump");
        else
            stepNote = $"swiped {before} to {after}";
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
        // Offline the kiosk hides itself (AirportInviteFriendsKiosk.cs:36), so no hand can press it: the call stands in.
        if (stepOk)
            stepNote =
                $"kiosk {(shown ? "shown" : "hidden offline")}: {cueNote}; shortcut: Interact called directly, no player press offline";
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
        yield return OpenKioskByHand(kiosk, pass);
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
            stepNote =
                $"{cueNote}; shortcut: the load flag is set by the test (LoadingScreenHandler._loading), no real load runs";
    }

    /// <summary>
    /// vr.131: a game modal opening rings the notice (NotNow) once ui.world has placed it, the ring on its first button,
    /// on the hand whose laser pressed last (ModalBellPatch rings <c>LaserPointer.PressLeft</c>): the hand that just
    /// answered a question hears the next one. Each hand in turn answers the open modal with a laser click (a modal
    /// button closes it, ModalButtonsField.cs:24), then a new modal must ring that hand and not the other.
    /// shortcut: the modals come from the game's console test (Modal.cs:84-87, Open :64); offline nothing else asks.
    /// </summary>
    IEnumerator ModalBellStep()
    {
        var ui = UIWorldFeature.Settings;
        if (ui != null && ui.PointerHand.Value != PointerHandMode.LastUsed)
        {
            stepSkip = $"UI.PointerHand is {ui.PointerHand.Value}: the laser never changes hands";
            yield break;
        }
        Zorro.UI.Modal.Modal.TestModal();
        yield return Until(() => Zorro.UI.Modal.Modal.IsOpen, 1f, "modal did not open");
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);
        var notes = new System.Collections.Generic.List<string>();
        foreach (bool left in new[] { true, false })
        {
            string hand = left ? "left" : "right";
            var body = Zorro.UI.Modal.Modal.Instance.bodyParent;
            var button = body != null ? body.GetComponentInChildren<UnityEngine.UI.Button>() : null;
            if (button == null)
            {
                Fail("the modal has no button to answer");
                yield break;
            }
            var rect = (RectTransform)button.transform;
            AimLaser(left, rect.TransformPoint(rect.rect.center));
            yield return Wait(0.3f);
            yield return LaserClick(left);
            yield return Until(
                () => !Zorro.UI.Modal.Modal.IsOpen,
                1f,
                $"the {hand} laser click did not answer the modal"
            );
            if (!stepOk)
                yield break;
            // Both lasers down at the floor, so the next modal's buttons do not appear under a hovering beam.
            HeadFrame(out var head, out var forward, out _);
            AimLasers(VRRig.Instance.Origin.TransformPoint(head + forward - Vector3.up * 1.5f));
            yield return Wait(0.3f);
            float since = CueMark();
            Zorro.UI.Modal.Modal.TestModal();
            yield return Until(() => Zorro.UI.Modal.Modal.IsOpen, 1f, "modal did not reopen");
            if (!stepOk)
                yield break;
            yield return ExpectCue(left, DenyReason.NotNow, since, false, 1.5f);
            if (!stepOk)
                yield break;
            notes.Add($"after a {hand} answer: {cueNote}");
            yield return ExpectNoCue(!left, since, 0f);
            if (!stepOk)
                yield break;
        }
        stepNote = string.Join("; ", notes);
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
        ControllerLabels.ForAction(
            VRAction.SpectateNext,
            VRInput.LeftHanded,
            LocalHold.ItemInLeftHand,
            out bool spectateLeft,
            out _
        );
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
            var check = new GhostCheck();
            float until = Now + GuideTable.Get(topic).Loop + 0.2f;
            while (Now < until)
            {
                demo.Tick(Time.unscaledDeltaTime, 1f, rig.Head);
                check.Sample(demo, spectateLeft);
                yield return null;
            }
            notes.Add($"{topic}: {demo.SoundsPlayed} cue sounds");
            if (!demo.Alive || demo.Topic != topic)
                fails.Add($"{topic} demo not showing");
            else if (demo.SoundsPlayed < 1)
                fails.Add($"{topic} played no cue sound in a loop");
            else if (check.Wrong(topic) is { } wrong)
                fails.Add($"{topic} demo shows the wrong gesture: {wrong}");
        }
        demo.Destroy();
        Conclude(fails, notes);
    }

    /// <summary>What the ghost must show for <paramref name="topic"/>, checked against the mechanic itself rather
    /// than against GuideTable: the finger gun (grip and trigger lit together), a closed hand inside the talk gesture's
    /// mouth radius (MouthGesture), an open hand there for mute, and the stick lit on the hand the spectate binding
    /// reads (BindingTable.Default) and never on the other. Accumulates one loop's frames.</summary>
    sealed class GhostCheck
    {
        public bool FingerGun,
            FistAtMouth,
            OpenAtMouth,
            StickOnBound,
            StickOnOther;

        public void Sample(GhostDemo demo, bool spectateLeft)
        {
            float radius = new MouthGesture().Radius * GhostDemo.StageScale;
            var mouth = demo.MouthPosition;
            foreach (bool left in new[] { true, false })
            {
                bool grip = demo.PadLit(left, GuideButton.Grip);
                if (grip && demo.PadLit(left, GuideButton.Trigger))
                    FingerGun = true;
                if (Vector3.Distance(demo.PadPosition(left), mouth) <= radius)
                {
                    FistAtMouth |= grip;
                    OpenAtMouth |= !grip;
                }
                if (demo.PadLit(left, GuideButton.Stick))
                {
                    StickOnBound |= left == spectateLeft;
                    StickOnOther |= left != spectateLeft;
                }
            }
        }

        public string Wrong(GuideTopic topic) =>
            topic switch
            {
                GuideTopic.Ping when !FingerGun => "no ghost hand squeezes the finger gun",
                GuideTopic.Mic when !FistAtMouth => "no closed ghost hand at the mouth",
                GuideTopic.Mute when !OpenAtMouth => "no open ghost hand over the mouth",
                GuideTopic.Mute when FistAtMouth => "a closed hand at the mouth (that talks)",
                GuideTopic.Spectate when !StickOnBound =>
                    "the stick never lit on the spectate hand",
                GuideTopic.Spectate when StickOnOther =>
                    "the stick lit on the hand spectate ignores",
                _ => null,
            };
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

    /// <summary>vr.71: the boarding pass opened by hand at the check-in kiosk (<see cref="OpenKioskByHand"/>); ui.lobby
    /// docks it at the kiosk (KioskDock), where the eyes must see all of it: a corner hidden by geometry fails.</summary>
    IEnumerator KioskShot()
    {
        var kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        var pass = GUIManager.instance != null ? GUIManager.instance.boardingPass : null;
        if (kiosk == null || pass == null || Character.localCharacter == null)
        {
            stepSkip = "no kiosk or boarding pass here";
            yield break;
        }
        yield return OpenKioskByHand(kiosk, pass);
        if (!stepOk)
            yield break;
        // BoardingPass.OnOpen fades it in over 0.5 s (BoardingPass.cs:147).
        yield return Wait(0.8f);
        var at = CentreOf(pass.panel.transform);
        var eye = ViewCamera() != null ? ViewCamera().transform.position : at;
        int hidden = 0;
        var corners = new Vector3[4];
        ((RectTransform)pass.panel.transform).GetWorldCorners(corners);
        foreach (var corner in corners)
            if (Blocked(eye, Vector3.Lerp(corner, at, 0.05f), null))
                hidden++;
        stepNote =
            $"{kioskNote}; boarding pass {Vector3.Distance(eye, at):0.0} m from the eye, {hidden}/4 corners hidden by geometry";
        yield return ShotAt("vr-71-kiosk", at);
        if (hidden > 0)
            Fail($"the docked boarding pass is partly hidden: {stepNote}");
        MenuWindow.CloseAllWindows();
        yield return Until(() => !pass.isOpen, 2f, "boarding pass did not close");
        yield return Wait(0.5f);
    }

    /// <summary>Horizontal gap from the kiosk's bounds to where the player stands at it: about an arm's length, so a
    /// hand reaches its face and the pass docks between kiosk and eyes (LobbyDockLimits.StandOff is 0.45 m too).</summary>
    const float KioskStandOff = 0.45f;

    /// <summary>How the kiosk steps stood and opened the kiosk, for the step note.</summary>
    string kioskNote;

    /// <summary>Geometry other than <paramref name="ignore"/>'s (and the player's own body) lies on the line.</summary>
    static bool Blocked(Vector3 from, Vector3 to, AirportCheckInKiosk ignore)
    {
        if (
            !Physics.Linecast(
                from,
                to,
                out var hit,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            )
        )
            return false;
        return ignore == null || hit.collider.GetComponentInParent<AirportCheckInKiosk>() != ignore;
    }

    /// <summary>The kiosk's collider bounds, the shape the hand touches (PhysicalInteraction's grab search).</summary>
    static Bounds KioskBounds(AirportCheckInKiosk kiosk)
    {
        var bounds = new Bounds(kiosk.transform.position, Vector3.zero);
        foreach (var col in kiosk.GetComponentsInChildren<Collider>())
            bounds.Encapsulate(col.bounds);
        return bounds;
    }

    /// <summary>
    /// Stands in front of the check-in kiosk as a player who walks up to it does: on the floor
    /// <see cref="KioskStandOff"/> out from its bounds, facing it, on the first of eight headings (from the kiosk's
    /// forward) where the eyes see the kiosk and the spot ui.lobby docks the pass (LobbyDock.Pose) with nothing in
    /// the way. No warp when already standing there. Sets <see cref="warpedForProp"/>, so <see cref="WarpBack"/>
    /// returns to spawn.
    /// </summary>
    IEnumerator ToKiosk(AirportCheckInKiosk kiosk)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        KioskDock.Measure(kiosk.transform, out var centre, out var top);
        var bounds = KioskBounds(kiosk);
        // The eye's height over the floor now, kept at the new spot.
        float eyeUp = Physics.Raycast(
            c.Center,
            Vector3.down,
            out var floorNow,
            3f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? rig.Head.position.y - floorNow.point.y
            : 1.6f;
        var forward = Flat(kiosk.transform.forward);
        var tried = new System.Collections.Generic.List<string>();
        Vector3? stand = null;
        float heading = 0f;
        for (int i = 0; i < 8 && stand == null; i++)
        {
            heading = i * 45f;
            var dir = Quaternion.Euler(0f, heading, 0f) * forward;
            float reach =
                Mathf.Abs(dir.x) * bounds.extents.x
                + Mathf.Abs(dir.z) * bounds.extents.z
                + KioskStandOff;
            var spot = new Vector3(bounds.center.x, top, bounds.center.z) + dir * reach;
            // Down from the kiosk's top: the floor under the airport roof, never the roof.
            if (
                !Physics.Raycast(
                    spot,
                    Vector3.down,
                    out var floor,
                    top - bounds.min.y + 1f,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                tried.Add($"{heading:0} deg: no floor");
                continue;
            }
            var eye = floor.point + Vector3.up * eyeUp;
            var dock = LobbyDock
                .Pose(
                    centre.N(),
                    top,
                    kiosk.transform.forward.N(),
                    eye.N(),
                    new LobbyDockLimits(),
                    out _
                )
                .U();
            if (Blocked(eye, centre, kiosk) || Blocked(eye, dock, null))
            {
                tried.Add($"{heading:0} deg: view blocked");
                continue;
            }
            stand = floor.point;
        }
        if (stand == null)
        {
            Fail("no clear spot in front of the kiosk: " + string.Join(", ", tried));
            yield break;
        }
        if (Vector3.Distance(Flat(c.Center), Flat(stand.Value)) > 0.3f)
        {
            // Not WarpTo: its ground ray starts 3 m up, which can be above the airport roof.
            warpedForProp = true;
            c.photonView.RPC(
                "WarpPlayerRPC",
                Photon.Pun.RpcTarget.All,
                stand.Value + Vector3.up * 0.1f,
                false
            );
            yield return Wait(0.5f);
            yield return WaitFor(() => !c.warping, 3f);
            yield return Wait(1.5f);
        }
        rig.RotateYaw(
            Vector3.SignedAngle(
                Flat(rig.Head.forward),
                Flat(centre - rig.Head.position),
                Vector3.up
            )
        );
        yield return Wait(0.6f);
        kioskNote =
            $"stood {Vector3.Distance(Flat(c.Center), Flat(centre)):0.00} m from the kiosk centre, {heading:0} deg from its forward";
    }

    /// <summary>
    /// The boarding pass opened the way a player opens it: standing at the kiosk (<see cref="ToKiosk"/>), the dominant
    /// hand on the kiosk's near face and the grip held (a grab of an interactible, PhysicalInteraction
    /// GrabInteractible) through the kiosk's own hold (<c>interactTime</c>, AirportCheckInKiosk.cs:7, counted at
    /// Interaction.cs:120-129) until <c>Interact_CastFinished</c> (AirportCheckInKiosk.cs:112) opens the pass. Fails
    /// past arm's reach or when the pass stays shut.
    /// </summary>
    IEnumerator OpenKioskByHand(AirportCheckInKiosk kiosk, BoardingPass pass)
    {
        yield return ToKiosk(kiosk);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        KioskDock.Measure(kiosk.transform, out var centre, out var top);
        var eye = rig.Head.position;
        // A hand held out at chest height (or just under the kiosk top) toward its centre: where it meets the kiosk.
        float y = Mathf.Min(eye.y - 0.35f, top - 0.1f);
        if (
            !Physics.Linecast(
                new Vector3(eye.x, y, eye.z),
                new Vector3(centre.x, y, centre.z),
                out var face,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Collide
            )
            || face.collider.GetComponentInParent<AirportCheckInKiosk>() != kiosk
        )
        {
            Fail(
                $"no kiosk face at hand height ({(face.collider != null ? face.collider.name : "nothing")} on the line); {kioskNote}"
            );
            yield break;
        }
        var touch = face.point + face.normal * 0.02f;
        bool left = VRInput.LeftHanded;
        yield return MoveTo(left, () => touch, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        float reach = Vector3.Distance(Script(left).Position, VRInput.HeadPosition);
        yield return GripTo(left, 1f, 0.1f);
        float hold = GameTiming.InteractSeconds(kiosk, c);
        yield return Until(
            () => pass.isOpen,
            GameTiming.Budget(hold, 1f),
            $"holding the grip on the kiosk did not open the boarding pass ({PeakXR.Interact.PhysicalInteraction.LastGripNote})"
        );
        yield return GripTo(left, 0f, 0.1f);
        if (stepOk)
            kioskNote +=
                $", opened by a {(left ? "left" : "right")} grip {reach:0.00} m from the eyes held {hold:0.0} s";
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

    /// <summary>vr.72: the VR keyboard as a platform text dialog opens it (<see cref="VRKeyboard.TryOpenDialog"/>):
    /// placed within poking reach of the eyes (<see cref="PokeReach"/>), its face toward them and in plain view; then
    /// cancelled, which closes the dialog without submitting.</summary>
    IEnumerator KeyboardShot()
    {
        bool closed = false,
            submitted = false;
        if (
            !VRKeyboard.TryOpenDialog(
                "PeakXR self-test",
                32,
                (_, ok) =>
                {
                    closed = true;
                    submitted = ok;
                }
            )
        )
        {
            Fail("keyboard refused to open (ui.world not ready or no rig)");
            yield break;
        }
        // The idle pose holds the hands up in front of the face for the other screenshots, right over the
        // keyboard; a player about to type rests them at its near edge instead.
        var idle = VRInput.TestOverride;
        VRInput.TestOverride = KeyboardHands;
        yield return Until(() => VRKeyboard.HasPose, 2f, "keyboard not placed");
        yield return Wait(0.5f);
        var panel = VRKeyboard.PanelPosition;
        var eye = VRRig.Instance.Head.position;
        float away = Vector3.Distance(eye, panel);
        // +Z goes into the keys (PokeKey): a panel that faces the eyes points it away from them.
        bool faces = Vector3.Dot(VRKeyboard.PanelRotation * Vector3.forward, panel - eye) > 0f;
        bool hidden = Blocked(eye, Vector3.Lerp(panel, eye, 0.05f), null);
        yield return ShotAt("vr-72-keyboard", panel);
        VRInput.TestOverride = idle;
        VRKeyboard.CancelDialog();
        yield return Until(() => closed, 1f, "keyboard dialog did not close");
        if (!stepOk)
            yield break;
        stepNote = $"keyboard {away:0.00} m from the eyes, facing them {faces}, hidden {hidden}";
        if (away > PokeReach)
            Fail($"keyboard {away:0.00} m from the eyes, past poking reach ({PokeReach:0.00} m)");
        else if (!faces)
            Fail("the keyboard's keys face away from the eyes");
        else if (hidden)
            Fail("geometry hides the keyboard from the eyes");
        else if (submitted)
            Fail("cancelling the keyboard submitted the text");
    }
}
