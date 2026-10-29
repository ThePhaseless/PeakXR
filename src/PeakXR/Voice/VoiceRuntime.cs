using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Voice;

/// <summary>
/// Per-frame driver for VR voice: the hand-to-mouth talk gesture, the palm-over-mouth mute, the optional
/// push-to-talk button, the feedback for your own mic (a radio click and a tick when it opens or closes, a muffled
/// click when muted, the mic icon at the hand and wrist), the one-time headset mic choice and the speaking icons.
/// Runs late, after the rig is posed, and does nothing outside VR, so flat play keeps the vanilla voice chat
/// untouched.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class VoiceRuntime : MonoBehaviour
{
    private readonly MouthGesture leftMouth = new();
    private readonly MouthGesture rightMouth = new();
    private readonly PalmMute leftPalm = new();
    private readonly PalmMute rightPalm = new();
    private readonly HeadsetMic headsetMic = new();
    private SpeakingIndicators indicators;
    private MicIcon icon;
    private PushToTalkSetting modeSetting;
    private AnimatedMouth mouth;
    private Character mouthOwner;
    private MicGlyph lastGlyph;
    private bool lastAsk;
    private bool lastMute;

    private static VoiceRuntime instance;

    /// <summary>The Voice Activation notice was shown this session (it shows once).</summary>
    private static bool toldVoiceActivation;

    /// <summary>The mic glyph drawn now, for the self-test.</summary>
    public static MicGlyph GlyphShown => instance != null ? instance.icon.Shown : MicGlyph.Off;

    /// <summary>The game's microphone mode (PushToTalkSetting, read by CharacterVoiceHandler.cs:269).</summary>
    public static MicMode Mode => instance != null ? instance.ReadMode() : MicMode.VoiceActivation;

    /// <summary>Lets the self-test see the Voice Activation notice again.</summary>
    public static void ResetNotice() => toldVoiceActivation = false;

    /// <summary>The headset mic switch, for the self-test.</summary>
    internal static HeadsetMic Mic => instance != null ? instance.headsetMic : null;

    public static void Create()
    {
        var go = new GameObject("PeakXR_Voice");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VoiceRuntime>();
    }

    private void Awake()
    {
        indicators = new SpeakingIndicators(transform);
        icon = new MicIcon(transform);
        var button = VoiceFeature.Settings.PushToTalkButton;
        button.SettingChanged += (_, _) => LogConflict(button.Value);
        LogConflict(button.Value);
    }

    /// <summary>Logs which job the picked button gives up while it talks (Bindings.cs BindingTable.Conflict;
    /// LocomotionInput reserves the control).</summary>
    private static void LogConflict(string name)
    {
        bool dominantLeft = VRInput.LeftHanded;
        if (
            ButtonName.TryParse(name, dominantLeft, out bool left, out var control)
            && BindingTable.Conflict(
                BindingTable.Active,
                left,
                control,
                dominantLeft,
                out var action
            )
        )
            Plugin.Log.LogInfo(
                $"[voice] Voice.PushToTalkButton {name} replaces {action} while that button talks"
            );
    }

    private void LateUpdate()
    {
        var cfg = VoiceFeature.Settings;
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null)
        {
            if (!VRSession.Active)
                headsetMic.Restore();
            ClearGesture();
            icon.Hide();
            indicators.HideAll();
            return;
        }

        var dt = Time.unscaledDeltaTime;
        TickControls(cfg, rig, dt);
        headsetMic.Tick(cfg.PreferHeadsetMic.Value);
        indicators.Tick(cfg, rig, dt);
    }

    private MicMode ReadMode()
    {
        if (modeSetting == null)
        {
            var settings =
                GameHandler.Instance != null ? GameHandler.Instance.SettingsHandler : null;
            modeSetting = settings != null ? settings.GetSetting<PushToTalkSetting>() : null;
        }
        return modeSetting != null ? (MicMode)(int)modeSetting.Value : MicMode.VoiceActivation;
    }

    private void TickControls(VoiceSettings cfg, VRRig rig, float dt)
    {
        var local = Character.localCharacter;
        if (
            local == null
            || local.data.dead
            || rig.Head == null
            || rig.LeftHand == null
            || rig.RightHand == null
        )
        {
            ClearGesture();
            icon.Hide();
            return;
        }

        // Only an empty hand talks or mutes: a held item at the mouth means eating (interact.physical).
        var holding = local.data.currentItem != null;
        var leftFree = !holding || (!LocalHold.TwoHand && !LocalHold.ItemInLeftHand);
        var rightFree = !holding || (!LocalHold.TwoHand && LocalHold.ItemInLeftHand);
        var headPos = rig.Head.position.N();
        var headRot = rig.Head.rotation.N();
        bool talkOn = cfg.HandToMouthTalk.Value;
        bool muteOn = cfg.PalmMute.Value;
        bool leftMute = Palm(leftPalm, muteOn && leftFree, true, rig, headPos, headRot, dt);
        bool rightMute = Palm(rightPalm, muteOn && rightFree, false, rig, headPos, headRot, dt);
        // An untracked hand keeps its last pose (RigDriver.UpdateHand): lost at the mouth, it would hold the mic open.
        bool leftTalk =
            Track(
                leftMouth,
                talkOn && leftFree && VRInput.Left.Tracked,
                headPos,
                headRot,
                Rig.RealScale.Real(rig.LeftHand.position, rig.Head.position),
                dt
            ) && !leftMute;
        bool rightTalk =
            Track(
                rightMouth,
                talkOn && rightFree && VRInput.Right.Tracked,
                headPos,
                headRot,
                Rig.RealScale.Real(rig.RightHand.position, rig.Head.position),
                dt
            ) && !rightMute;
        bool talk = leftTalk || rightTalk;
        bool mute = leftMute || rightMute;
        bool button = ButtonHeld(cfg.PushToTalkButton.Value);

        var mode = ReadMode();
        bool key = VoiceKeys.Key(mode, button, talk, mute);
        VoiceInput.GestureHeld = key;
        VoiceInput.MuteHeld = mute;
        VoiceInput.Gate = VoiceKeys.Gate(mode, mute);

        if (mouthOwner != local)
        {
            mouthOwner = local;
            mouth = local.GetComponent<AnimatedMouth>();
        }
        bool speaking = mouth != null && mouth.isSpeaking;
        var glyph = VoiceKeys.Glyph(mode, key || VoiceInput.PushToTalkHeld, mute, speaking);

        // The hand the cue belongs to: the one at the mouth, else the wrist.
        var ui = PeakXR.UI.World.UIWorldFeature.Settings;
        bool wristLeft = ui != null ? ui.WristLeft : VRInput.OffHandLeft;
        bool cueLeft = leftMute || leftTalk || (!rightMute && !rightTalk && wristLeft);
        var cueHand = rig.Hand(cueLeft);
        Transform atMouth =
            leftMute || leftTalk ? rig.LeftHand
            : rightMute || rightTalk ? rig.RightHand
            : null;
        Cues(mode, glyph, mute, talk || button, cueLeft, cueHand);
        icon.Tick(
            glyph,
            atMouth,
            rig.Hand(wristLeft),
            rig.Head.position,
            mouth != null ? mouth.amplitudeIndex : 0
        );
    }

    /// <summary>The radio click and tick when Push To Talk opens and closes the mic, the muffled click when the palm
    /// mutes, and once a session the notice that Voice Activation needs no key.</summary>
    private void Cues(MicMode mode, MicGlyph glyph, bool mute, bool ask, bool left, Transform hand)
    {
        var at = hand != null ? hand.position : Vector3.zero;
        if (mode == MicMode.PushToTalk)
        {
            bool open = glyph == MicGlyph.Open;
            bool wasOpen = lastGlyph == MicGlyph.Open;
            if (open != wasOpen && !mute)
            {
                Affordance.Play(CueSound.Transmit, at);
                if (open)
                    Affordance.Contact(left);
            }
        }
        if (mute && !lastMute)
            Affordance.Cancel(left);
        if (mode == MicMode.VoiceActivation && ask && !lastAsk && !mute && !toldVoiceActivation)
            // The key does nothing in this mode: the mic is always open.
            toldVoiceActivation = Affordance.Inform(
                left,
                at,
                DenyReason.MicAlwaysOn,
                DenyHint.On(hand)
            );
        lastGlyph = glyph;
        lastMute = mute;
        lastAsk = ask;
    }

    private static bool ButtonHeld(string name)
    {
        if (!ButtonName.TryParse(name, VRInput.LeftHanded, out bool left, out var control))
            return false;
        ref var h = ref VRInput.Hand(left);
        return control switch
        {
            VRControl.Primary => h.Primary,
            VRControl.Secondary => h.Secondary,
            VRControl.StickClick => h.StickClick,
            _ => false,
        };
    }

    private static bool Palm(
        PalmMute gesture,
        bool enabled,
        bool left,
        VRRig rig,
        System.Numerics.Vector3 headPos,
        System.Numerics.Quaternion headRot,
        float dt
    )
    {
        var t = rig.Hand(left);
        if (!enabled || t == null || !VRInput.Hand(left).Tracked)
        {
            gesture.Reset();
            return false;
        }
        // Palm normal: +X of the left grip pose, -X of the right (HandRig.cs:62).
        var palm = left ? t.right : -t.right;
        var c = LocalAvatar.ControllerCurls(left);
        float curl = (c.Index + c.Middle + c.Ring + c.Pinky) * 0.25f;
        // Your own mouth: the hand in real metres around the eyes (Camera.WorldScale).
        var hand = Rig.RealScale.Real(t.position, rig.Head.position);
        return gesture.Update(headPos, headRot, hand.N(), palm.N(), curl, dt);
    }

    private static bool Track(
        MouthGesture gesture,
        bool free,
        System.Numerics.Vector3 headPos,
        System.Numerics.Quaternion headRot,
        Vector3 hand,
        float dt
    )
    {
        if (free)
            return gesture.Update(headPos, headRot, hand.N(), dt);
        gesture.Reset();
        return false;
    }

    private void ClearGesture()
    {
        leftMouth.Reset();
        rightMouth.Reset();
        leftPalm.Reset();
        rightPalm.Reset();
        VoiceInput.GestureHeld = false;
        VoiceInput.MuteHeld = false;
        VoiceInput.Gate = false;
        lastGlyph = MicGlyph.Off;
        lastMute = lastAsk = false;
    }
}
