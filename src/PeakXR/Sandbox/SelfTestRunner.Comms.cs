using System.Collections;
using PeakXR.Core;
using PeakXR.Emotes;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.Wheels;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>Steps owned by WP6 (talk, ping, menus, prompts). Already wired into <c>HandSteps</c> after the
/// affordance steps, so the package adds steps here and never edits the step list.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator CommsSteps()
    {
        // ---- WP6 slice B: emotes, pause, spectate
        yield return HandStep("vr.114-emote-refused", 20f, EmoteRefusedStep());
        yield return HandStep("vr.115-gesture-emote", 20f, GestureEmoteStep());
        yield return HandStep("vr.117-pause-refused", 20f, PauseRefusedStep());
        yield return HandStep("vr.122-spectate-single", 30f, SpectateSingleStep());
    }

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
        if (openedPaused)
        {
            Fail("emote wheel opened over the pause menu");
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
        else
            stepNote = $"paused: {refused}; centre release: {cueNote}";
    }

    /// <summary>
    /// vr.115: an open hand raised to the side of the head and swung side to side (WaveGesture) plays the game's wave
    /// emote through CharacterAnimations.PlayEmote (CharacterAnimations.cs:410; emoting :432), with no refusal cue.
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
        yield return ExpectNoCue(hand, mark, 0f);
        if (stepOk)
            stepNote = $"waved: played {GestureEmotes.LastPlayed}";
    }

    /// <summary>
    /// vr.117: a menu tap while the emote wheel is open is refused by the game (GUIManager.cs:424); the hand that
    /// pressed it gets NotNow and the pause menu stays shut.
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
        bool paused = GUIManager.InPauseMenu;
        Script(hand).Secondary = false;
        if (stepOk && paused)
            Fail("pause menu opened over the emote wheel");
        else if (stepOk)
            stepNote = cueNote;
    }

    /// <summary>
    /// vr.122: solo and fully passed out, vanilla spectates yourself (MainCameraMovement.cs:257-259) and ignores a
    /// next flick (:189-191); the flicking (dominant) hand gets NeedsFriend.
    /// </summary>
    IEnumerator SpectateSingleStep()
    {
        var c = Character.localCharacter;
        if (!SandboxActions.PassOut())
        {
            stepSkip = "sandbox pass-out unavailable: " + SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => c.data.fullyPassedOut, 8f, "did not fully pass out");
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);
        bool hand = VRInput.LeftHanded;
        float mark = CueMark();
        Script(hand).Stick = new Vector2(0f, 1f);
        yield return Wait(0.2f);
        Script(hand).Stick = Vector2.zero;
        yield return ExpectCue(hand, DenyReason.NeedsFriend, mark, 1f);
        if (stepOk)
            stepNote =
                $"spectating {(MainCameraMovement.specCharacter == c ? "self" : "other")}: {cueNote}";
        SandboxActions.Revive();
        yield return Until(() => !c.data.fullyPassedOut && !c.data.dead, 6f, "did not revive");
    }
}
