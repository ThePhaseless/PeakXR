using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact.Ping;
using PeakXR.UI.Affordances;
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
        // ---- end WP6 slice A
    }

    // ================================================================ WP6 slice A: ping and voice

    /// <summary>
    /// vr.110: the finger gun on an empty hand aimed at the ground ahead. While the pose is held the aim preview draws
    /// its dot near the aimed spot (<see cref="PingPreview"/>); the squeeze pings: PointPinger stamps
    /// <c>_timeLastPinged</c> only on a hit (PointPinger.cs:68-71). A hit gets a contact tick and no refusal.
    /// </summary>
    IEnumerator PingAimStep()
    {
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
        yield return FingerGunSqueeze(left, aim);
        yield return Until(
            () => PingInput.LastPinged(pinger) >= gameMark,
            2f,
            "the squeeze did not ping (PointPinger._timeLastPinged unchanged)"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(left, CueFeel.Contact, mark);
        string feel = cueNote;
        yield return ExpectNoCue(left, mark, 0.5f);
        if (!stepOk)
            yield break;
        if (!shown)
            Fail("no aim preview dot while the finger gun was held");
        else if (off > 1.5f)
            Fail($"preview dot {off:0.00} m from the aimed spot");
        else
            stepNote = $"preview dot {off:0.00} m from the aim, pinged; {feel}";
    }

    /// <summary>
    /// vr.111: pings the game drops silently get a cue. The sky (TryGetPingHit misses, PointPinger.cs:68) gives NoWall
    /// at the ray end with the red marker; a second ping inside <c>PointPinger.coolDown</c> (a prefab value, read here)
    /// gives TooSoon; the finger gun on a hand that holds an unusable item gives the free hand a HandsFull notice.
    /// </summary>
    IEnumerator PingRefusedStep()
    {
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
        yield return FingerGunAim(left, sky, 0.3f);
        bool skyPreview = PingPreview.DotRed;
        Affordance.ResetGate();
        float mark = CueMark();
        yield return FingerGunSqueeze(left, sky);
        yield return ExpectCue(left, DenyReason.NoWall, mark, tint: true);
        if (!stepOk)
            yield break;
        notes.Add("sky: " + cueNote);
        if (!skyPreview)
            Fail("the preview dot was not red while aimed at the sky");
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
        if (Time.time - PingInput.LastPinged(pinger) >= pinger.coolDown)
            notes.Add($"cooldown {pinger.coolDown:0.00} s too short to test");
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
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        var usability = GameMember.Field<Item, Optionable<bool>>("overrideUsability");
        usability(HeldItem()) = Optionable<bool>.Some(false);
        bool holdLeft = LocalHold.ItemInLeftHand;
        Affordance.ResetGate();
        mark = CueMark();
        yield return FingerGunAim(holdLeft, ground, 0.3f);
        yield return FingerGunSqueeze(holdLeft, ground);
        yield return ExpectCue(!holdLeft, DenyReason.HandsFull, mark);
        if (HeldItem() != null)
            usability(HeldItem()) = Optionable<bool>.None;
        if (!stepOk)
            yield break;
        notes.Add("holding: " + cueNote);
        stepNote = string.Join("; ", notes);
    }

    /// <summary>
    /// vr.112: Push To Talk. A closed hand held at the mouth presses the game's key (<c>pushToTalkPressed</c>, read by
    /// CharacterVoiceHandler.cs:250), plays the radio click, ticks the hand and lights the mic icon; taking it away
    /// plays the closing click and hides the icon. In Voice Activation the same move gets the one-time notice
    /// (CantUse: the key does nothing there). The game's mode is set without saving and restored.
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
            yield return ExpectFeel(left, CueFeel.Contact, mark);
            if (!stepOk)
                yield break;
            notes.Add("open: click, icon, " + cueNote);
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
            notes.Add("closed: click, icon off");

            SetMicMode(setting, PushToTalkSetting.PushToTalkType.VoiceActivation);
            VoiceRuntime.ResetNotice();
            Affordance.ResetGate();
            mark = CueMark();
            yield return HandToMouth(left, palm: false);
            yield return ExpectCue(left, DenyReason.CantUse, mark, within: 1.5f);
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
            if (voice.transmitting)
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
                () => voice.transmitting,
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
            if (voice.transmitting)
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

    static PointPinger LocalPinger()
    {
        var c = Character.localCharacter;
        return c != null ? c.GetComponent<PointPinger>() : null;
    }

    /// <summary>Ground about 4 m ahead, where a ping lands.</summary>
    static Vector3 PingGround()
    {
        var aim = SandboxActions.FrontPoint(4f);
        return Physics.Raycast(
            aim + Vector3.up * 2f,
            Vector3.down,
            out var ground,
            6f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? ground.point
            : aim;
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
            Script(left).Rotation = Quaternion.LookRotation(
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
