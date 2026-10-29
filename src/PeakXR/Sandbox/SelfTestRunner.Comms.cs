using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Emotes;
using PeakXR.Framework;
using PeakXR.Interact.Ping;
using PeakXR.UI.Affordances;
using PeakXR.UI.Wheels;
using PeakXR.Voice;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>Steps owned by WP6 (talk, ping, menus, prompts). Already wired into <c>HandSteps</c> after the
/// affordance steps, so the package adds steps here and never edits the step list.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator CommsSteps()
    {
        // ---- WP6 slice A: ping and voice
        yield return HandStep("vr.110-ping", 25f, PingAimStep());
        yield return HandStep("vr.111-ping-refused", 40f, PingRefusedStep());
        yield return HandStep("vr.112-ptt", 25f, PushToTalkStep());
        yield return HandStep("vr.113-mute", 25f, PalmMuteStep());
        yield return HandStep("vr.188-ptt-button", 20f, PushToTalkButtonStep());
        // ---- end WP6 slice A
        // ---- WP6 slice B: emotes, pause, spectate
        yield return HandStep("vr.114-emote-refused", 20f, EmoteRefusedStep());
        yield return HandStep("vr.115-gesture-emote", 20f, GestureEmoteStep());
        yield return HandStep("vr.117-pause-refused", 20f, PauseRefusedStep());
        yield return HandStep("vr.185-menu-recenter", 20f, MenuRecenterStep());
        yield return HandStep("vr.248-menu-follow-text", 25f, MenuFollowTextStep());
        yield return HandStep("vr.186-cheer", 20f, CheerStep());
        yield return PromptGlyphSteps();
        // ---- WP6 gap round: ping
        yield return HandStep("vr.123-ping-dotted", 20f, PingDottedStep());
        yield return HandStep("vr.124-button-ping-preview", 25f, ButtonPingPreviewStep());
        // ---- WP6 gap round: wheels
        yield return WheelGapSteps();
    }

    // ================================================================ WP6 gap round: ping

    /// <summary>Ground items within this distance of the body can be in reach of a pointing grip's interact.</summary>
    const float StrayItemRadius = 3f;

    /// <summary>
    /// The finger-gun pose is a pointing grip: held past its trigger window (FingerGun.DefaultWindow) it interacts
    /// with what the hand points at, as interact.physical intends. vr37's vr.110 aimed at the ground where an earlier
    /// step's Compass lay (dropped at the end of use.Compass and never swept), picked it up, and the squeeze used the
    /// compass instead of pinging. The ping steps start from clear ground: world items near the body that no step
    /// holds are removed, each one logged.
    /// </summary>
    static void ClearStrayItems()
    {
        var c = Character.localCharacter;
        if (c == null)
            return;
        foreach (var item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (
                item.itemState != ItemState.Ground
                || Vector3.Distance(item.transform.position, c.Center) > StrayItemRadius
            )
                continue;
            var view = item.GetComponent<Photon.Pun.PhotonView>();
            if (view == null || !(view.IsMine || Photon.Pun.PhotonNetwork.IsMasterClient))
                continue;
            Plugin.Log.LogInfo(
                $"[selftest] ping steps: removed stray {item.name} {Vector3.Distance(item.transform.position, c.Center):0.0} m from the body"
            );
            Photon.Pun.PhotonNetwork.Destroy(item.gameObject);
        }
    }

    /// <summary>vr.123: the finger gun aimed at the ground ahead draws its aim as a row of dots from the hand to the
    /// landing dot (<see cref="PingPreview.RayDots"/>), not a solid line; aiming alone refuses nothing.</summary>
    IEnumerator PingDottedStep()
    {
        ClearStrayItems();
        bool left = false;
        var aim = PingGround();
        float mark = CueMark();
        yield return FingerGunAim(left, aim, 0.5f);
        int dots = PingPreview.RayDots;
        bool landing = PingPreview.DotShown && !PingPreview.DotRed;
        yield return ShotAt("vr-123-ping-dotted", aim);
        yield return ExpectNoCue(left, mark, 0f);
        if (!stepOk)
            yield break;
        var note =
            $"{dots} ray dots, landing dot {landing}, from the {(PingPreview.FromLeft ? "left" : "right")} hand";
        if (dots < 3 || !landing || PingPreview.FromLeft != left)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.124: the thumb resting on the layout's ping button (Ping, off-hand B/Y tap in BindingTable.Default) shows the
    /// aim preview from the hand a tap pings from (<c>PingRules.ButtonHandLeft</c>); the tap pings
    /// (<c>_timeLastPinged</c>, PointPinger.cs:68-71) with a contact tick and no refusal. A long press (the emote
    /// wheel on the same button) hides the preview until the thumb lets go.
    /// </summary>
    IEnumerator ButtonPingPreviewStep()
    {
        ClearStrayItems();
        var pinger = LocalPinger();
        if (pinger == null || PingInput.LastPinged == null)
        {
            stepSkip = "no PointPinger or interact.ping not installed";
            yield break;
        }
        if (
            !ControllerLabels.ForAction(
                VRAction.Ping,
                VRInput.LeftHanded,
                LocalHold.ItemInLeftHand,
                out bool button,
                out var control
            )
            || control != VRControl.Secondary
        )
        {
            stepSkip = "the layout's ping is not on an upper face button";
            yield break;
        }
        // No hand points, so the tap aims from the button's own hand.
        bool aimLeft = button;
        var rig = VRRig.Instance;
        var aim = PingGround();
        yield return PingCooldownOver(pinger);
        Script(button).SecondaryTouch = true;
        for (float until = Now + 0.4f; Now < until; )
        {
            AimHand(rig, aimLeft, aim);
            yield return null;
        }
        bool previewed = PingInput.ButtonAiming(out bool fromLeft) && PingPreview.DotShown;
        int dots = PingPreview.RayDots;
        yield return ShotAt("vr-124-button-ping-preview", aim);
        Affordance.ResetGate();
        float mark = CueMark();
        float gameMark = Time.time;
        var oldMarker = PingMarker(pinger);
        Script(button).Secondary = true;
        for (float until = Now + 0.15f; Now < until; )
        {
            AimHand(rig, aimLeft, aim);
            yield return null;
        }
        Script(button).Secondary = false;
        yield return Until(
            () => PingInput.LastPinged(pinger) >= gameMark,
            2f,
            "the button tap did not ping (PointPinger._timeLastPinged unchanged)"
        );
        if (!stepOk)
            yield break;
        var landing = PingLanding(pinger, oldMarker, aim, aimLeft, out float landed);
        if (landing != null)
        {
            Fail("button tap: " + landing);
            yield break;
        }
        yield return ExpectFeel(aimLeft, CueFeel.Contact, mark);
        string feel = cueNote;
        yield return ExpectNoCue(aimLeft, mark, 0.3f);
        if (!stepOk)
            yield break;
        float tapped = PingInput.LastPinged(pinger);

        // The long press opens the emote wheel instead: the preview goes and stays gone while the thumb rests.
        Script(button).Secondary = true;
        yield return Wait(new ActionMapper().LongPressSeconds + 0.2f);
        bool hiddenHeld = !PingInput.ButtonAiming(out _);
        Script(button).Secondary = false;
        yield return Wait(0.2f);
        bool hiddenAfter = !PingInput.ButtonAiming(out _);
        Script(button).SecondaryTouch = false;
        yield return Wait(0.3f);
        // The long press is the emote wheel's, never a second ping (cooldown 0 in the prefab, so one would show).
        if (PingInput.LastPinged(pinger) != tapped)
        {
            Fail("the emote long press on the ping button also pinged");
            yield break;
        }

        var note =
            $"preview {previewed} from the {(fromLeft ? "left" : "right")} hand ({dots} dots), pinged {landed:0.00} m from the aim; {feel}; "
            + $"long press hid it {hiddenHeld}, still hidden after {hiddenAfter}";
        if (!previewed || fromLeft != aimLeft)
            Fail(
                "no aim preview from the tap's hand while the thumb rested on the button: " + note
            );
        else if (!hiddenHeld || !hiddenAfter)
            Fail("the preview stayed on through the emote long press: " + note);
        else
            stepNote = note;
    }

    // ================================================================ end WP6 gap round: ping

    // ---------------------------------------------------------------- WP6 slice B: emotes, pause, spectate

    /// <summary>The emote binding's long press (<c>ActionMapper.LongPressSeconds</c>) plus a margin.</summary>
    static float EmoteOpenSeconds => new ActionMapper().LongPressSeconds + 0.5f;

    /// <summary>
    /// vr.114: the emote binding held while the pause menu blocks input (GUIManager.cs:565, windowBlockingInput :410)
    /// keeps the wheel shut and gives the opening hand NotNow. Then, unpaused, the wheel opens and a release with the
    /// hand still in its empty centre plays nothing and gives a Cancel tick, no refusal.
    /// </summary>
    IEnumerator EmoteRefusedStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        if (gui == null || gui.emoteWheel == null)
        {
            Fail("no GUIManager emote wheel");
            yield break;
        }
        var emoting = GameMember.Field<CharacterAnimations, bool>("emoting");
        bool hand = RadialWheels.OpeningHandLeft(VRAction.Emote);
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(() => GUIManager.InPauseMenu, 3f, "pause menu did not open");
        if (!stepOk)
            yield break;
        float mark = CueMark();
        Script(hand).Secondary = true;
        yield return ExpectCue(hand, DenyReason.NotNow, mark, EmoteOpenSeconds);
        bool openedPaused = gui.emoteWheel.activeInHierarchy;
        Script(hand).Secondary = false;
        if (!stepOk)
            yield break;
        string refused = cueNote;
        if (openedPaused || emoting(c.refs.animations))
        {
            Fail(
                openedPaused
                    ? "emote wheel opened over the pause menu"
                    : "an emote played over the pause menu"
            );
            yield break;
        }
        ClosePause();
        yield return Until(() => !GUIManager.InPauseMenu, 2f, "pause menu did not close");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);

        Script(hand).Secondary = true;
        yield return Until(() => gui.emoteWheel.activeInHierarchy, 2f, "emote wheel did not open");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        int denials = Affordance.Count;
        mark = CueMark();
        Script(hand).Secondary = false;
        yield return ExpectFeel(hand, CueFeel.Cancel, mark, 1f);
        if (!stepOk)
            yield break;
        if (Affordance.Count != denials)
            Fail($"centre release was refused ({Affordance.LastReason}), not cancelled");
        else if (emoting(c.refs.animations))
            Fail("the centre release played an emote");
        else
            stepNote = $"paused: {refused}; centre release: {cueNote}";
    }

    /// <summary>
    /// vr.115: an open hand raised to the side of the head and swung side to side (WaveGesture) plays the game's wave
    /// emote through CharacterAnimations.PlayEmote (CharacterAnimations.cs:410; emoting :432), confirmed on that hand
    /// by the notice (HappenedHere: tick, bell, pin ring), no refusal.
    /// </summary>
    IEnumerator GestureEmoteStep()
    {
        if (!EmotesFeature.Gestures.Value)
        {
            stepSkip = "Emotes.GestureEmotes off";
            yield break;
        }
        yield return Wait(0.2f);
        if (GestureEmotes.WaveAnim == null)
        {
            Fail("no wave emote in EmoteWheel.data");
            yield break;
        }
        var c = Character.localCharacter;
        var emoting = GameMember.Field<CharacterAnimations, bool>("emoting");
        bool hand = !VRInput.LeftHanded;
        ref var s = ref Script(hand);
        s.Grip = 0f;
        s.TriggerTouch = false;
        HeadFrame(out var head, out var forward, out var right);
        var side = hand ? -right : right;
        var centre = head + side * 0.3f + forward * 0.2f + Vector3.up * 0.05f;
        yield return MoveTrack(hand, centre, 0.3f);
        float mark = CueMark();
        int before = GestureEmotes.PlayCount;
        bool started = false;
        // 1.5 Hz, 10 cm each way: a relaxed wave.
        for (float t = 0f; t < 2.5f && !started; t += Time.unscaledDeltaTime)
        {
            Script(hand).Position = centre + right * (0.1f * Mathf.Sin(2f * Mathf.PI * 1.5f * t));
            yield return null;
            started =
                emoting(c.refs.animations)
                && GestureEmotes.PlayCount != before
                && GestureEmotes.LastPlayed == GestureEmotes.WaveAnim;
        }
        if (!started)
        {
            Fail(
                $"no wave emote after 2.5 s of waving (last gesture emote '{GestureEmotes.LastPlayed}')"
            );
            yield break;
        }
        yield return ExpectCue(hand, DenyReason.HappenedHere, mark);
        if (stepOk)
            stepNote = $"waved: played {GestureEmotes.LastPlayed}; confirmed: {cueNote}";
    }

    /// <summary>
    /// vr.117: a menu tap while the emote wheel is open is refused by the game (GUIManager.cs:424); the hand that
    /// pressed it gets NotNow and the pause menu stays shut. With the wheel closed the same tap pauses, so the refusal
    /// is the wheel's and not a dead menu button.
    /// </summary>
    IEnumerator PauseRefusedStep()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui.emoteWheel == null)
        {
            Fail("no GUIManager emote wheel");
            yield break;
        }
        bool hand = RadialWheels.OpeningHandLeft(VRAction.Emote);
        Script(hand).Secondary = true;
        yield return Until(() => gui.emoteWheel.activeInHierarchy, 2f, "emote wheel did not open");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        // The left controller always has a menu button; a tap is a release before the recenter hold.
        float mark = CueMark();
        Script(true).Menu = true;
        yield return Wait(0.15f);
        Script(true).Menu = false;
        yield return ExpectCue(true, DenyReason.NotNow, mark, 1f);
        // A few frames for a pause that would open late.
        yield return Wait(0.3f);
        bool paused = GUIManager.InPauseMenu;
        Script(hand).Secondary = false;
        if (stepOk && paused)
            Fail("pause menu opened over the emote wheel");
        if (!stepOk)
            yield break;
        string refused = cueNote;
        yield return Until(() => !gui.wheelActive, 1f, "emote wheel did not close");
        if (!stepOk)
            yield break;
        yield return Wait(0.2f);
        Script(true).Menu = true;
        yield return Wait(0.15f);
        Script(true).Menu = false;
        yield return Until(
            () => GUIManager.InPauseMenu,
            1.5f,
            "the same menu tap with no wheel open did not pause"
        );
        if (stepOk)
            stepNote = refused + "; unrefused tap paused";
    }

    /// <summary>
    /// vr.185: holding a menu button for <c>Camera.RecenterHoldSeconds</c> recenters the view (RigDriver's recenter
    /// hold, <c>VRRig.Recentered</c>), not before the hold time, and letting go does not also pause (the pause binding
    /// is a tap, ActionMapper.MenuHoldSeconds). Mid-hold the progress ring sits just above the held (left) controller,
    /// not head-locked.
    /// </summary>
    IEnumerator MenuRecenterStep()
    {
        var rig = VRRig.Instance;
        if (GUIManager.InPauseMenu)
        {
            ClosePause();
            yield return Until(() => !GUIManager.InPauseMenu, 2f, "pause menu did not close");
            if (!stepOk)
                yield break;
            yield return Wait(0.3f);
        }
        int recentered = 0;
        // Sonar misses that the local function writes the captured counter the waits below read.
#pragma warning disable S2123
        void Count() => recentered++;
#pragma warning restore S2123
        float hold = PeakXR.Rig.CameraFeature.RecenterSeconds;
        rig.Recentered += Count;
        try
        {
            Script(true).Menu = true;
            yield return Wait(hold * 0.6f);
            bool early = recentered > 0;
            var cue = PeakXR.Rig.RigDriver.RecenterCue;
            bool ringShown = cue != null && cue.Visible;
            float ringToHand = ringShown
                ? Vector3.Distance(cue.Position, rig.Hand(true).position)
                : -1f;
            yield return WaitFor(() => recentered > 0, hold * 0.4f + 1.5f);
            Script(true).Menu = false;
            // A pause that would open on the release.
            yield return Wait(0.4f);
            if (early)
                Fail($"the menu press recentered before its {hold:0.0} s hold");
            else if (recentered == 0)
                Fail($"holding the menu button {hold + 1.5f:0.0} s did not recenter");
            else if (GUIManager.InPauseMenu)
                Fail("the recenter hold also opened the pause menu");
            else if (!ringShown)
                Fail("no recenter hold ring mid-hold");
            else if (ringToHand > 0.15f)
                Fail($"the recenter hold ring sat {ringToHand:0.00} m from the held controller");
            else
                stepNote =
                    $"recentered after the {hold:0.0} s menu hold, no pause; ring {ringToHand:0.00} m from the left controller";
        }
        finally
        {
            rig.Recentered -= Count;
        }
    }

    /// <summary>
    /// vr.186: both open hands raised over the head and held there (GestureEmotes cheer: grip under OpenGrip, both
    /// over the eyes by CheerMargin) play the game's cheer emote through CharacterAnimations.PlayEmote
    /// (A/CharacterAnimations.cs:410; emoting :432), not the wave, confirmed on the dominant hand (HappenedHere), the
    /// other hand quiet.
    /// </summary>
    IEnumerator CheerStep()
    {
        if (!EmotesFeature.Gestures.Value)
        {
            stepSkip = "Emotes.GestureEmotes off";
            yield break;
        }
        if (GestureEmotes.CheerAnim == null)
        {
            stepSkip = "no cheer emote in EmoteWheel.data";
            yield break;
        }
        // Past the 2 s cooldown between gesture emotes after vr.115's wave.
        yield return Wait(2f);
        var c = Character.localCharacter;
        var emoting = GameMember.Field<CharacterAnimations, bool>("emoting");
        HeadFrame(out var head, out var forward, out var right);
        foreach (bool left in new[] { true, false })
        {
            Script(left).Grip = 0f;
            Script(left).TriggerTouch = false;
        }
        float mark = CueMark();
        int before = GestureEmotes.PlayCount;
        // Hands up and a little apart, 25 cm over the eyes.
        var up = head + forward * 0.1f + Vector3.up * 0.25f;
        yield return MoveTrack(true, up - right * 0.2f, 0.3f);
        yield return MoveTrack(false, up + right * 0.2f, 0.3f);
        bool started = false;
        yield return WaitFor(
            () =>
                started =
                    emoting(c.refs.animations)
                    && GestureEmotes.PlayCount != before
                    && GestureEmotes.LastPlayed == GestureEmotes.CheerAnim,
            2f
        );
        if (!started)
        {
            Fail(
                $"no cheer emote within 2 s of both hands overhead (last gesture emote '{GestureEmotes.LastPlayed}')"
            );
            yield break;
        }
        bool dominant = !VRInput.LeftHanded;
        yield return ExpectCue(dominant, DenyReason.HappenedHere, mark);
        string confirmed = cueNote;
        yield return ExpectNoCue(!dominant, mark, 0f);
        if (stepOk)
            stepNote = $"cheered: played {GestureEmotes.LastPlayed}; confirmed: {confirmed}";
    }

    /// <summary>
    /// vr.188: <c>Voice.PushToTalkButton</c> set to DominantPrimary (the jump button) talks while held in Push To Talk:
    /// the game's key (<c>pushToTalkPressed</c>, CharacterVoiceHandler.cs:269) opens the recorder that sends (:270),
    /// with the radio click and the open mic icon, and the button does not also jump (LocomotionInput reserves it);
    /// letting go closes the mic. The settings are put back.
    /// </summary>
    IEnumerator PushToTalkButtonStep()
    {
        var setting = MicSetting();
        var c = Character.localCharacter;
        var voice = c != null ? c.refs.voice : null;
        var cfg = VoiceFeature.Settings;
        if (setting == null || voice == null || cfg == null)
        {
            stepSkip = "no PushToTalkSetting, voice handler or voice feature";
            yield break;
        }
        var mode = setting.Value;
        string button = cfg.PushToTalkButton.Value;
        bool talk = cfg.HandToMouthTalk.Value;
        bool hand = VRInput.LeftHanded;
        try
        {
            // Only the button talks here: the hands rest away from the mouth anyway.
            cfg.HandToMouthTalk.Value = false;
            cfg.PushToTalkButton.Value = "DominantPrimary";
            SetMicMode(setting, PushToTalkSetting.PushToTalkType.PushToTalk);
            yield return Wait(0.3f);
            float mark = CueMark();
            bool jumped = false;
            Script(hand).Primary = true;
            for (float until = Now + 1f; Now < until; )
            {
                jumped |= c.input.jumpWasPressed || c.input.jumpIsPressed;
                if (c.input.pushToTalkPressed && voice.transmitting && RecorderIs(voice, true))
                    break;
                yield return null;
            }
            bool pressed = c.input.pushToTalkPressed;
            bool sending = voice.transmitting && RecorderIs(voice, true);
            var click = CueLog.Capture.LastSound(CueSound.Transmit);
            var glyph = VoiceRuntime.GlyphShown;
            Script(hand).Primary = false;
            for (float until = Now + 0.3f; Now < until; )
            {
                jumped |= c.input.jumpWasPressed || c.input.jumpIsPressed;
                yield return null;
            }
            if (!pressed)
                Fail("holding the push-to-talk button did not press the game's key");
            else if (!sending)
                Fail(
                    $"push-to-talk pressed but the mic did not send (transmitting {voice.transmitting})"
                );
            else if (click.At < mark || !click.Played)
                Fail("no radio click as the mic opened");
            else if (glyph != MicGlyph.Open)
                Fail($"mic icon shows {glyph}, not Open");
            else if (jumped)
                Fail("the push-to-talk button also jumped");
            if (!stepOk)
                yield break;
            yield return Until(
                () => !c.input.pushToTalkPressed && !voice.transmitting && RecorderIs(voice, false),
                1f,
                "the mic kept sending after the button let go"
            );
            if (stepOk)
                stepNote =
                    $"{(VoiceAllowed(voice) ? "recorder sent" : "platform blocks voice")} while held, click, icon, no jump; closed on release";
        }
        finally
        {
            SetMicMode(setting, mode);
            cfg.PushToTalkButton.Value = button;
            cfg.HandToMouthTalk.Value = talk;
        }
    }

    // ================================================================ WP6 slice A: ping and voice

    /// <summary>
    /// vr.110: the finger gun on an empty hand aimed at the ground ahead. While the pose is held the aim preview draws
    /// its dot near the aimed spot (<see cref="PingPreview"/>); the squeeze pings: PointPinger stamps
    /// <c>_timeLastPinged</c> only on a hit (PointPinger.cs:68-71). A hit gets a contact tick and no refusal.
    /// </summary>
    IEnumerator PingAimStep()
    {
        ClearStrayItems();
        var pinger = LocalPinger();
        if (pinger == null || PingInput.LastPinged == null)
        {
            stepSkip = "no PointPinger or interact.ping not installed";
            yield break;
        }
        bool left = false;
        var aim = PingGround();
        yield return PingCooldownOver(pinger);
        yield return FingerGunAim(left, aim, 0.5f);
        bool shown = PingPreview.DotShown && !PingPreview.DotRed;
        float off = Vector3.Distance(PingPreview.DotPosition, aim);
        yield return ShotAt("vr-110-ping", aim);
        Affordance.ResetGate();
        float mark = CueMark();
        float gameMark = Time.time;
        var oldMarker = PingMarker(pinger);
        yield return FingerGunSqueeze(left, aim);
        yield return Until(
            () => PingInput.LastPinged(pinger) >= gameMark,
            2f,
            "the squeeze did not ping (PointPinger._timeLastPinged unchanged)"
        );
        if (!stepOk)
            yield break;
        var landing = PingLanding(pinger, oldMarker, aim, left, out float landed);
        yield return ExpectFeel(left, CueFeel.Contact, mark);
        string feel = cueNote;
        yield return ExpectNoCue(left, mark, 0.5f);
        if (!stepOk)
            yield break;
        var held = HeldItem();
        if (!shown)
            Fail("no aim preview dot while the finger gun was held");
        else if (off > PingLandRadius)
            Fail($"preview dot {off:0.00} m from the aimed spot");
        else if (landing != null)
            Fail(landing);
        else if (held != null)
            Fail($"the finger-gun squeeze picked up {held.name} instead of only pinging");
        else
            stepNote =
                $"preview dot {off:0.00} m from the aim, ping marker {landed:0.00} m; {feel}";
    }

    /// <summary>
    /// vr.111: pings the game drops silently get a cue. The sky (TryGetPingHit misses, PointPinger.cs:68) gives NoWall
    /// at the ray end with the red marker; a second ping inside <c>PointPinger.coolDown</c> (a prefab value, read here)
    /// gives TooSoon; the finger gun on a hand that holds an unusable item gives the free hand a UseOtherHand notice.
    /// </summary>
    IEnumerator PingRefusedStep()
    {
        ClearStrayItems();
        var pinger = LocalPinger();
        if (pinger == null || PingInput.LastPinged == null)
        {
            stepSkip = "no PointPinger or interact.ping not installed";
            yield break;
        }
        var notes = new List<string>();
        bool left = false;
        var rig = VRRig.Instance;

        // Sky: straight up from the hand.
        yield return PingCooldownOver(pinger);
        float before = PingInput.LastPinged(pinger);
        var sky = rig.Hand(left).position + Vector3.up * 200f;
        // The player looks along the finger gun at what they ping (run 48: the ring sat 89 deg off a level view).
        LookAtHand(left, along: true);
        yield return FingerGunAim(left, sky, 0.3f);
        bool skyPreview = PingPreview.DotRed;
        bool skySign = PingPreview.DotSigned;
        Affordance.ResetGate();
        float mark = CueMark();
        yield return FingerGunSqueeze(left, sky);
        yield return ExpectCue(left, DenyReason.NoWall, mark, tint: true);
        if (!stepOk)
            yield break;
        notes.Add("sky: " + cueNote);
        if (!skyPreview)
            Fail("the preview dot was not red while aimed at the sky");
        else if (!skySign)
            Fail(
                "the red sky dot had no ring-and-slash sign: colour alone told the ping would fail"
            );
        if (PingInput.LastPinged(pinger) != before)
            Fail("a ping went out at the sky");
        if (!stepOk)
            yield break;
        yield return ShotAt("vr-111-ping-sky", PingPreview.DotPosition);

        // Cooldown: ping the ground, then again at once.
        var ground = PingGround();
        yield return PingCooldownOver(pinger);
        float gameMark = Time.time;
        yield return FingerGunAim(left, ground, 0.3f);
        yield return FingerGunSqueeze(left, ground);
        yield return Until(
            () => PingInput.LastPinged(pinger) >= gameMark,
            2f,
            "the first ping did not go out"
        );
        if (!stepOk)
            yield break;
        Affordance.ResetGate();
        mark = CueMark();
        yield return FingerGunAim(left, ground, 0.15f);
        // The prefab's coolDown is 0 (vr43), so no player meets TooSoon and this branch normally does not run.
        if (Time.time - PingInput.LastPinged(pinger) >= pinger.coolDown)
            notes.Add(
                $"TooSoon not tested: PointPinger.coolDown {pinger.coolDown:0.00} s, a second ping at once goes out"
            );
        else
        {
            yield return FingerGunSqueeze(left, ground);
            yield return ExpectCue(left, DenyReason.TooSoon, mark);
            if (!stepOk)
                yield break;
            notes.Add($"cooldown {pinger.coolDown:0.00} s: " + cueNote);
        }
        yield return GripTo(left, 0f, 0.1f);

        // A hand holding an item the game will not use (overrideUsability false, Item.cs:797-802).
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var usability = GameMember.Field<Item, Optionable<bool>>("overrideUsability");
        usability(HeldItem()) = Optionable<bool>.Some(false);
        bool holdLeft = LocalHold.ItemInLeftHand;
        Affordance.ResetGate();
        mark = CueMark();
        float heldBefore = PingInput.LastPinged(pinger);
        yield return FingerGunAim(holdLeft, ground, 0.3f);
        yield return FingerGunSqueeze(holdLeft, ground);
        yield return ExpectCue(!holdLeft, DenyReason.UseOtherHand, mark);
        if (HeldItem() != null)
            usability(HeldItem()) = Optionable<bool>.None;
        if (stepOk && PingInput.LastPinged(pinger) != heldBefore)
            Fail("the holding hand's finger gun pinged");
        if (!stepOk)
            yield break;
        notes.Add("holding: " + cueNote);
        stepNote = string.Join("; ", notes);
    }

    /// <summary>
    /// vr.112: Push To Talk. A closed hand held at the mouth presses the game's key (<c>pushToTalkPressed</c>, read by
    /// CharacterVoiceHandler.cs:269), plays the radio click, ticks the hand and lights the mic icon; taking it away
    /// plays the closing click and hides the icon. In Voice Activation the same move gets the one-time notice
    /// (MicAlwaysOn: the key does nothing there). The game's mode is set without saving and restored.
    /// </summary>
    IEnumerator PushToTalkStep()
    {
        var setting = MicSetting();
        var c = Character.localCharacter;
        if (setting == null || VoiceFeature.Settings == null)
        {
            stepSkip = "no PushToTalkSetting or voice not installed";
            yield break;
        }
        var voice = c != null ? c.refs.voice : null;
        if (voice == null)
        {
            stepSkip = "no voice handler";
            yield break;
        }
        var mode = setting.Value;
        bool talk = VoiceFeature.Settings.HandToMouthTalk.Value;
        var notes = new List<string>();
        bool left = !VRInput.LeftHanded;
        try
        {
            VoiceFeature.Settings.HandToMouthTalk.Value = true;
            SetMicMode(setting, PushToTalkSetting.PushToTalkType.PushToTalk);
            Affordance.ResetGate();
            float mark = CueMark();
            yield return HandToMouth(left, palm: false);
            yield return Until(
                () => c.input.pushToTalkPressed,
                1.5f,
                "hand at the mouth did not press push-to-talk"
            );
            if (!stepOk)
                yield break;
            var click = CueLog.Capture.LastSound(CueSound.Transmit);
            if (click.At < mark || !click.Played)
                Fail("no radio click as the mic opened");
            else if (VoiceRuntime.GlyphShown != MicGlyph.Open)
                Fail($"mic icon shows {VoiceRuntime.GlyphShown}, not Open");
            if (!stepOk)
                yield break;
            // The key alone proves nothing: the game's gate (:265) must open the recorder that sends (:270).
            yield return Until(
                () => voice.transmitting && RecorderIs(voice, true),
                0.5f,
                $"push-to-talk pressed but the mic did not send (transmitting {voice.transmitting})"
            );
            if (!stepOk)
                yield break;
            yield return ExpectFeel(left, CueFeel.Contact, mark);
            if (!stepOk)
                yield break;
            notes.Add(
                (VoiceAllowed(voice) ? "open: recorder sends, " : "open: platform blocks voice, ")
                    + "click, icon, "
                    + cueNote
            );
            yield return ShotAt("vr-112-ptt", VRRig.Instance.Hand(left).position);

            float closeMark = CueMark();
            yield return HandAway(left);
            yield return Until(
                () => !c.input.pushToTalkPressed,
                1.5f,
                "push-to-talk stuck after the hand left"
            );
            if (!stepOk)
                yield break;
            click = CueLog.Capture.LastSound(CueSound.Transmit);
            if (click.At < closeMark)
                Fail("no closing click");
            else if (VoiceRuntime.GlyphShown == MicGlyph.Open)
                Fail("mic icon still open");
            if (!stepOk)
                yield break;
            yield return Until(
                () => !voice.transmitting && RecorderIs(voice, false),
                0.5f,
                "the mic kept sending after the hand left"
            );
            if (!stepOk)
                yield break;
            notes.Add("closed: click, icon off, recorder off");

            SetMicMode(setting, PushToTalkSetting.PushToTalkType.VoiceActivation);
            VoiceRuntime.ResetNotice();
            Affordance.ResetGate();
            mark = CueMark();
            yield return HandToMouth(left, palm: false);
            yield return ExpectCue(left, DenyReason.MicAlwaysOn, mark, within: 1.5f);
            if (!stepOk)
                yield break;
            notes.Add("voice activation: " + cueNote);
            yield return HandAway(left);
            stepNote = string.Join("; ", notes);
        }
        finally
        {
            SetMicMode(setting, mode);
            VoiceFeature.Settings.HandToMouthTalk.Value = talk;
        }
    }

    /// <summary>
    /// vr.113: a flat palm over the mouth mutes. In Voice Activation the local recorder is held shut
    /// (<c>CharacterVoiceHandler.transmitting</c> false, :250) without touching the setting; in Push To Mute the palm
    /// presses the game's key. Both show the red mic icon, with a muffled click and a soft tick on that hand.
    /// </summary>
    IEnumerator PalmMuteStep()
    {
        var setting = MicSetting();
        var c = Character.localCharacter;
        var voice = c != null ? c.refs.voice : null;
        if (setting == null || voice == null || VoiceFeature.Settings == null)
        {
            stepSkip = "no PushToTalkSetting, voice handler or voice feature";
            yield break;
        }
        var mode = setting.Value;
        bool palmOn = VoiceFeature.Settings.PalmMute.Value;
        var notes = new List<string>();
        bool left = !VRInput.LeftHanded;
        try
        {
            VoiceFeature.Settings.PalmMute.Value = true;
            SetMicMode(setting, PushToTalkSetting.PushToTalkType.VoiceActivation);
            yield return Wait(0.2f);
            Affordance.ResetGate();
            float mark = CueMark();
            yield return HandToMouth(left, palm: true);
            yield return Until(() => VoiceInput.MuteHeld, 1.5f, "palm over the mouth did not mute");
            if (!stepOk)
                yield break;
            yield return Wait(0.1f);
            if (voice.transmitting || !RecorderIs(voice, false))
                Fail("voice activation still transmitting under the palm");
            else if (VoiceRuntime.GlyphShown != MicGlyph.Muted)
                Fail($"mic icon shows {VoiceRuntime.GlyphShown}, not Muted");
            else if (CueLog.Capture.LastSound(CueSound.Cancel).At < mark)
                Fail("no muffled click");
            if (!stepOk)
                yield break;
            yield return ExpectFeel(left, CueFeel.Cancel, mark);
            if (!stepOk)
                yield break;
            notes.Add("voice activation gated, red icon, " + cueNote);
            yield return ShotAt("vr-113-mute", VRRig.Instance.Hand(left).position);
            yield return HandAway(left);
            yield return Until(
                () => voice.transmitting && RecorderIs(voice, true),
                1.5f,
                "mic did not reopen after the palm left"
            );
            if (!stepOk)
                yield break;

            SetMicMode(setting, PushToTalkSetting.PushToTalkType.PushToMute);
            yield return HandToMouth(left, palm: true);
            yield return Until(
                () => c.input.pushToTalkPressed,
                1.5f,
                "palm did not press the mute key"
            );
            if (!stepOk)
                yield break;
            yield return Wait(0.1f);
            if (voice.transmitting || !RecorderIs(voice, false))
                Fail("push to mute still transmitting under the palm");
            if (!stepOk)
                yield break;
            notes.Add("push to mute key pressed");
            yield return HandAway(left);
            stepNote = string.Join("; ", notes);
        }
        finally
        {
            SetMicMode(setting, mode);
            VoiceFeature.Settings.PalmMute.Value = palmOn;
        }
    }

    /// <summary>How close the ping must land to the aimed spot: the aim pose looks straight at it, so only a bump in
    /// the ground on the way can move it.</summary>
    const float PingLandRadius = 0.3f;

    /// <summary>The marker of the local player's last ping (PointPinger.pingInstance, PointPinger.cs:14, placed at the
    /// hit point by ReceivePoint_Rpc :97), or null once it is gone (destroyed after 2 s, :104).</summary>
    static GameObject PingMarker(PointPinger pinger) =>
        GameMember.Field<PointPinger, GameObject>("pingInstance")(pinger);

    /// <summary>Why the ping just sent did not land where <paramref name="left"/>'s hand aimed (it came from the other
    /// hand, no new marker, or the marker is off the spot), or null; <paramref name="off"/> is the marker's miss.</summary>
    static string PingLanding(
        PointPinger pinger,
        GameObject before,
        Vector3 aim,
        bool left,
        out float off
    )
    {
        off = -1f;
        if (PingInput.PingLeft != left)
            return $"the ping went out from the {(left ? "right" : "left")} hand";
        var marker = PingMarker(pinger);
        if (marker == null || marker == before)
            return "no new ping marker (PointPinger.pingInstance)";
        off = Vector3.Distance(marker.transform.position, aim);
        return off > PingLandRadius
            ? $"the ping marker landed {off:0.00} m from the aimed spot"
            : null;
    }

    /// <summary>The platform lets this player talk (CharacterVoiceHandler.m_allowedToCommunicate, :66, set :149);
    /// without it the game keeps the recorder off whatever the key does (:238-247).</summary>
    static bool VoiceAllowed(CharacterVoiceHandler voice) =>
        GameMember.Field<CharacterVoiceHandler, bool>("m_allowedToCommunicate")(voice);

    /// <summary>The local recorder's send switch (CharacterVoiceHandler.m_Recorder :54, TransmitEnabled set :270 and
    /// :246) is <paramref name="send"/>: what other players hear, not the game's <c>transmitting</c> flag alone.
    /// True when the platform forbids voice, where there is nothing to send.</summary>
    static bool RecorderIs(CharacterVoiceHandler voice, bool send)
    {
        if (!VoiceAllowed(voice))
            return true;
        var recorder = MicGatePatch.Recorder != null ? MicGatePatch.Recorder(voice) : null;
        return recorder != null && recorder.TransmitEnabled == send;
    }

    static PointPinger LocalPinger()
    {
        var c = Character.localCharacter;
        return c != null ? c.GetComponent<PointPinger>() : null;
    }

    /// <summary>Yaw tries for <see cref="PingGround"/>, nearest the facing first.</summary>
    static readonly float[] PingYaws = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };

    /// <summary>
    /// Ground about 4 m ahead, where a ping lands, that both hands can see: nothing on the ping's TerrainMap layer
    /// between either finger-gun hand (as <see cref="FingerGunAim"/> places it) and the spot. vr54 aimed past a glass
    /// crate beside the right hand; the preview dot and the ping both stopped on the crate, 1.46 m short, which was
    /// right, and the step failed on its own target. Turns up to 75 degrees off the facing to find a clear spot.
    /// </summary>
    static Vector3 PingGround()
    {
        var c = Character.localCharacter;
        var head = VRRig.Instance.Head;
        var forward = Flat(head.forward);
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var start = head.position + Vector3.down * 0.2f + forward * 0.3f;
        Vector3 first = default;
        for (int i = 0; i < PingYaws.Length; i++)
        {
            var dir = Quaternion.Euler(0f, PingYaws[i], 0f) * forward;
            var aim = c.Center + dir * 4f;
            if (Terrain(aim + Vector3.up * 2f, Vector3.down, 6f, out var ground))
                aim = ground.point;
            if (i == 0)
                first = aim;
            if (Clear(start - right * 0.2f, aim) && Clear(start + right * 0.2f, aim))
                return aim;
        }
        Plugin.Log.LogInfo("[selftest] ping ground: no spot in sight of both hands; aiming ahead");
        return first;

        static bool Clear(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            return len < 0.2f
                || !Physics.Raycast(
                    from,
                    d / len,
                    len - 0.15f,
                    HelperFunctions.LayerType.TerrainMap.ToLayerMask(),
                    QueryTriggerInteraction.Ignore
                );
        }
    }

    /// <summary>Waits out what is left of <c>PointPinger.coolDown</c> (game time).</summary>
    static IEnumerator PingCooldownOver(PointPinger pinger)
    {
        float left = pinger.coolDown - (Time.time - PingInput.LastPinged(pinger));
        if (left > 0f)
            yield return Wait(GameTiming.Budget(left) + 0.1f);
    }

    /// <summary>Grip closed, index straight (no trigger touch), aimed at <paramref name="world"/>.</summary>
    IEnumerator FingerGunAim(bool left, Vector3 world, float seconds)
    {
        var rig = VRRig.Instance;
        HeadFrame(out var head, out var forward, out var right);
        Script(left).Position =
            head + (left ? -right : right) * 0.2f + Vector3.down * 0.2f + forward * 0.3f;
        Script(left).TriggerTouch = false;
        Script(left).Trigger = 0f;
        yield return GripTo(left, 1f, 0.1f);
        for (float until = Now + seconds; Now < until; )
        {
            AimHand(rig, left, world);
            yield return null;
        }
    }

    /// <summary>The index curls onto the trigger and squeezes, then lets go; the grip stays closed.</summary>
    IEnumerator FingerGunSqueeze(bool left, Vector3 world)
    {
        var rig = VRRig.Instance;
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        for (float until = Now + 0.15f; Now < until; )
        {
            AimHand(rig, left, world);
            yield return null;
        }
        Script(left).Trigger = 0f;
        yield return null;
    }

    /// <summary>The hand onto the mouth (MouthGesture.DefaultOffset in head space): a closed fist, or with
    /// <paramref name="palm"/> an open hand whose palm (+X left, -X right, HandRig.cs:62) faces the face.</summary>
    IEnumerator HandToMouth(bool left, bool palm)
    {
        var o = MouthGesture.DefaultOffset;
        var mouth = VRInput.HeadPosition + VRInput.HeadRotation * new Vector3(o.X, o.Y, o.Z);
        var back = -(VRInput.HeadRotation * Vector3.forward);
        Script(left).TriggerTouch = !palm;
        Script(left).Trigger = 0f;
        yield return GripTo(left, palm ? 0f : 1f, 0.1f);
        yield return MoveTrack(left, mouth, 0.3f);
        if (palm)
            Script(left).Rotation = HandLook(
                Vector3.Cross(left ? back : -back, Vector3.up),
                Vector3.up
            );
    }

    IEnumerator HandAway(bool left)
    {
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(
            left,
            head + (left ? -right : right) * 0.25f + Vector3.down * 0.35f + forward * 0.4f,
            0.3f
        );
        yield return GripTo(left, 0f, 0.1f);
        Script(left).TriggerTouch = true;
    }

    static PushToTalkSetting MicSetting()
    {
        var settings = GameHandler.Instance != null ? GameHandler.Instance.SettingsHandler : null;
        return settings != null ? settings.GetSetting<PushToTalkSetting>() : null;
    }

    /// <summary>The game's microphone mode for this step only: the property, not <c>SetValue</c>, so nothing is saved.</summary>
    static void SetMicMode(PushToTalkSetting setting, PushToTalkSetting.PushToTalkType mode) =>
        Traverse.Create(setting).Property("Value").SetValue(mode);

    // ================================================================ end WP6 slice A
}
