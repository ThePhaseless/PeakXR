using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.RunFlow;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Nothing in VR may depend on the play space (issues #7, #8, #9): panels and cards are placed from the head. With
/// the tracking origin turned 90 degrees from the head and 2 m off it, the end screen (vr.269), a banner (vr.270)
/// and the pause menu (vr.271) must each open in front of the head and face the eye; the top stamina meter must face
/// the eye squarely, upright in view, with the head pitched and rolled (vr.272).
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Degrees a panel may sit off the head's heading, or turn away from the eye.</summary>
    const float HeadPlacedSlack = 3f;

    /// <summary>The hero title card's raise above the gaze (WorldBanners' hero card).</summary>
    const float HeroRaise = 8f;

    /// <summary>The fog banner card's raise above the gaze (WorldBanners' banner card).</summary>
    const float BannerRaise = 3f;

    IEnumerator HeadPlacedSteps()
    {
        yield return HandStep("vr.269-end-screen-head", 20f, PlaySpaceStep(EndScreenHeadCheck));
        yield return HandStep("vr.270-banner-head-card", 20f, PlaySpaceStep(BannerHeadCheck));
        yield return HandStep("vr.271-pause-head", 20f, PlaySpaceStep(PauseHeadCheck));
        yield return HandStep("vr.272-top-meter-faces-eye", 25f, TopMeterFacesEyeStep());
    }

    /// <summary>
    /// Runs <paramref name="check"/> with the play space turned 90 degrees from the head and 2 m off it: the headset
    /// scripted 2 m out along tracking +X and turned to tracking -X, level, with the room anchor under it so the body
    /// stays where it stands. The view then looks 90 degrees off the origin's forward from 2 m beside it.
    /// </summary>
    IEnumerator PlaySpaceStep(System.Func<VRRig, IEnumerator> check)
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null || rig.Origin == null || GUIManager.instance == null)
        {
            Fail("no rig or GUIManager");
            yield break;
        }
        // vr.248 may leave its pause menu open. Hands low and pointing down, so the laser is never on a panel (it holds
        // menus still).
        ClosePause();
        yield return HandsAside();
        var anchor = rig.RoomAnchor;
        var home = VRInput.HeadPosition;
        try
        {
            var spot = new Vector3(2f, home.y, 0f);
            rig.RoomAnchor = new System.Numerics.Vector2(spot.x, spot.z);
            ScriptHead(spot, Quaternion.Euler(0f, -90f, 0f));
            yield return Wait(0.5f);
            float turned = Vector3.Angle(Flat(rig.Origin.forward), Flat(rig.Head.forward));
            float apart = Flat(rig.Head.position - rig.Origin.position).magnitude / rig.Scale;
            if (turned < 80f || apart < 1.8f)
            {
                Fail(
                    F(
                        $"play space not offset: origin {turned:0} deg off the head, {apart:0.00} m from it"
                    )
                );
                yield break;
            }
            yield return check(rig);
            if (stepOk)
                stepNote =
                    F($"origin {turned:0} deg off the head and {apart:0.0} m from it; ") + stepNote;
        }
        finally
        {
            ReleaseHead();
            rig.RoomAnchor = anchor;
        }
    }

    /// <summary>vr.269: the end-of-run screen's placement, run as if it were open (<see
    /// cref="EndScreenPlacement.SelfTestOpen"/>, not the game's end sequence, which grants progress).</summary>
    IEnumerator EndScreenHeadCheck(VRRig rig)
    {
        var root = RootCanvasOf(GUIManager.instance.endScreen);
        if (root == null)
        {
            Fail("no end screen canvas");
            yield break;
        }
        WorldCanvases.Track(root);
        EndScreenPlacement.SelfTestOpen = true;
        try
        {
            yield return Wait(0.5f);
            if (root.renderMode != RenderMode.WorldSpace)
            {
                Fail("the end screen canvas was not placed in the world");
                yield break;
            }
            ConcludePanel("end screen", rig.Head, root.transform.position, root.transform.forward);
        }
        finally
        {
            EndScreenPlacement.SelfTestOpen = false;
        }
    }

    /// <summary>vr.270: "the fog rises" (GUIManager.TheFogRises, GUIManager.cs:1195) shows on a card in front of the
    /// head, drawn on top, and goes when the game hides the banner 4 s later.</summary>
    IEnumerator BannerHeadCheck(VRRig rig)
    {
        var gui = GUIManager.instance;
        if (gui.fogRises == null)
        {
            Fail("no fog banner");
            yield break;
        }
        gui.TheFogRises();
        yield return Until(() => gui.fogRises.activeSelf, 2f, "the fog banner never showed");
        if (!stepOk)
            yield break;
        float shown = Now;
        yield return Wait(0.3f);
        var card = gui.fogRises.transform.parent;
        var fault =
            card == null || card.name != "PeakXR_BannerSign"
                ? $"fog banner under '{(card != null ? card.name : "none")}'"
                : HeadCardFault("fog card", rig.Head, card, BannerRaise, out _)
                    ?? (!OnTop.AllOnTop(card) ? "the fog card is depth tested" : null);
        yield return Shot("vr-270-banner-card");
        yield return WaitFor(() => !gui.fogRises.activeSelf, 6f);
        float gone = Now - shown;
        if (fault == null && gui.fogRises.activeSelf)
            fault = "the fog card was still up 6 s after it showed";
        if (fault != null)
            Fail(fault);
        else
            stepNote = F($"fog card in front of the head, gone {gone:0.0} s after it showed");
    }

    /// <summary>vr.271: the pause menu opens in front of the head.</summary>
    IEnumerator PauseHeadCheck(VRRig rig)
    {
        var c = Character.localCharacter;
        if (c == null)
        {
            Fail("no character");
            yield break;
        }
        c.input.pauseWasPressed = true;
        GUIManager.instance.UpdatePaused();
        yield return Until(
            () => GUIManager.InPauseMenu && FindAnyObjectByType<PauseMenuHandler>() != null,
            5f,
            "pause menu did not open"
        );
        if (!stepOk)
            yield break;
        try
        {
            // Past the page's scale-in (ScaleOnEnable, 0.25 s) and the wall retest.
            yield return Wait(1f);
            var root = RootCanvasOf(FindAnyObjectByType<PauseMenuHandler>());
            if (root == null || !WorldCanvases.TryMenuCentre(root, out var centre))
            {
                Fail("the pause menu is not a placed menu");
                yield break;
            }
            ConcludePanel("pause menu", rig.Head, centre, root.transform.forward);
        }
        finally
        {
            ClosePause();
        }
    }

    /// <summary>A level panel at <paramref name="centre"/> must sit on the head's heading and face the eye (its
    /// <paramref name="forward"/> along the line from the eye).</summary>
    void ConcludePanel(string what, Transform head, Vector3 centre, Vector3 forward)
    {
        var to = centre - head.position;
        float off = Vector3.Angle(Flat(head.forward), Flat(to));
        float face = Vector3.Angle(forward, to);
        var note = F(
            $"{what} {to.magnitude:0.00} m out, {off:0.0} deg off the head's heading, faces the eye to {face:0.0} deg"
        );
        if (off > HeadPlacedSlack)
            Fail($"{note}: not in front of the head");
        else if (face > HeadPlacedSlack)
            Fail($"{note}: turned away from the eye");
        else
            stepNote = note;
    }

    /// <summary>A head card <paramref name="raise"/> degrees above the gaze: no further off the gaze than that plus
    /// <see cref="HeadPlacedSlack"/>, facing the eye, within its 1.5 m (or pulled in, not nearer than 0.5 m). Null
    /// when it holds.</summary>
    static string HeadCardFault(
        string what,
        Transform head,
        Transform card,
        float raise,
        out string where
    )
    {
        var to = card.position - head.position;
        float d = PeakXR.Rig.RealScale.Real(to.magnitude);
        float off = Vector3.Angle(head.forward, to);
        float face = Vector3.Angle(card.forward, to);
        where = F($"{d:0.00} m out, {off:0.0} deg off the gaze, facing the eye to {face:0.0} deg");
        if (off > raise + HeadPlacedSlack)
            return $"{what} {where}: not in front of the head";
        if (face > HeadPlacedSlack)
            return $"{what} {where}: turned away from the eye";
        if (d > UI.Spatial.WorldBanners.Distance + 0.05f || d < PanelMath.ComfortFloor - 0.05f)
            return $"{what} {where}: outside 0.5-{UI.Spatial.WorldBanners.Distance:0.0} m";
        return null;
    }

    /// <summary>
    /// vr.272: <c>UI.HUD.StaminaView</c> TopOfView, the head pitched 60 degrees up, 60 down and rolled 30 degrees,
    /// each held 1.5 s (past the meter's lazy follow): the meter must face the eye squarely (its normal within 3
    /// degrees of the line from the eye), read upright in view (its up within 5 degrees of the head's up seen in its
    /// plane) and stay within 40 degrees of the gaze.
    /// </summary>
    IEnumerator TopMeterFacesEyeStep()
    {
        var cfg = UIWorldFeature.Settings;
        var rig = VRRig.Instance;
        var gui = GUIManager.instance;
        if (cfg == null || rig == null || rig.Head == null || gui == null || gui.bar == null)
        {
            Fail("no rig, settings or stamina bar");
            yield break;
        }
        var was = cfg.StaminaView.Value;
        cfg.StaminaView.Value = StaminaView.TopOfView;
        var home = VRInput.HeadPosition;
        var level = Quaternion.Euler(
            0f,
            (VRInput.HeadRotation * Vector3.forward).HeadingDegrees(),
            0f
        );
        var notes = new List<string>();
        string fault = null;
        try
        {
            foreach (var (name, pose) in TopMeterPoses)
            {
                ScriptHead(home, level * pose);
                yield return Wait(1.5f);
                var root = HudSplit.TopRoot;
                if (root == null)
                {
                    fault = $"head {name}: top stamina meter not posed";
                    break;
                }
                var frame = gui.bar.staminaBarOutline;
                var head = rig.Head;
                var to = frame.TransformPoint(frame.rect.center) - head.position;
                float face = Vector3.Angle(root.forward, to);
                float upright = Vector3.Angle(root.up, Vector3.ProjectOnPlane(head.up, to));
                float off = Vector3.Angle(head.forward, to);
                notes.Add(
                    F($"{name}: faces {face:0.0}, upright {upright:0.0}, {off:0} deg off the gaze")
                );
                fault ??=
                    face > HeadPlacedSlack
                        ? $"head {name}: the meter is turned {face:0.0} deg from the eye"
                    : upright > 5f
                        ? $"head {name}: the meter is tilted {upright:0.0} deg from the head's up"
                    : off > 40f ? $"head {name}: the meter is {off:0} deg off the gaze"
                    : null;
            }
        }
        finally
        {
            ReleaseHead();
            cfg.StaminaView.Value = was;
        }
        if (fault != null)
            Fail(fault + "; " + string.Join("; ", notes));
        else
            stepNote = string.Join("; ", notes);
    }

    static readonly (string Name, Quaternion Pose)[] TopMeterPoses =
    {
        ("pitch -60 (up)", Quaternion.Euler(-60f, 0f, 0f)),
        ("pitch +60 (down)", Quaternion.Euler(60f, 0f, 0f)),
        ("roll 30", Quaternion.Euler(0f, 0f, 30f)),
    };
}
