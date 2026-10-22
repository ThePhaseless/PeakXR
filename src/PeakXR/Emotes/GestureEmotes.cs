using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Emotes;

/// <summary>
/// Gesture emotes (D5): wave a raised open hand side to side to play the game's wave emote, hold both open hands
/// over your head to cheer. They play through vanilla CharacterAnimations.PlayEmote (A/CharacterAnimations.cs:410,
/// the call the emote wheel makes, EmoteWheel.cs:91), so every peer sees them. The emotes are found by name in the
/// wheel's own data (EmoteWheel.data), so nothing new is hard-coded. Not tracked while climbing, passed out, dead, in
/// a menu or with a wheel open (a climber's reaching hands look like a cheer); an emote the game only plays on the
/// ground (EmoteWheelData.requireGrounded, greyed on the wheel by EmoteWheelSlice.cs:31-34) gets a NeedsGround cue.
/// </summary>
[DefaultExecutionOrder(31060)]
internal sealed class GestureEmotes : MonoBehaviour
{
    /// <summary>A wave counts from about chin height: 15 cm under the eyes.</summary>
    private const float WaveMargin = -0.15f;

    /// <summary>A cheer needs both hands over the head.</summary>
    private const float CheerMargin = 0.1f;

    /// <summary>An open hand: the grip let go.</summary>
    private const float OpenGrip = 0.3f;

    /// <summary>Vanilla ends an emote after 2 s (CharacterAnimations.cs:278); no second one meanwhile.</summary>
    private const float Cooldown = 2f;

    private static Action<CharacterAnimations, string> playEmote;

    private readonly WaveGesture leftWave = new();
    private readonly WaveGesture rightWave = new();
    private readonly CheerGesture cheer = new();
    private GUIManager gui;
    private Emote wave,
        cheerEmote;
    private float nextAt;

    /// <summary>The last emote a gesture played (its animation name), for the self-test.</summary>
    public static string LastPlayed { get; private set; }

    /// <summary>Emotes played by a gesture since start, for the self-test.</summary>
    public static int PlayCount { get; private set; }

    /// <summary>The wave emote's animation found in the wheel data, or null.</summary>
    public static string WaveAnim { get; private set; }

    private struct Emote
    {
        public string Anim;
        public bool Grounded;
    }

    public static void Create()
    {
        playEmote = GameMember.Delegate<Action<CharacterAnimations, string>>(
            typeof(CharacterAnimations),
            "PlayEmote",
            new[] { typeof(string) }
        );
        var go = new GameObject("PeakXR_GestureEmotes");
        DontDestroyOnLoad(go);
        go.AddComponent<GestureEmotes>();
    }

    private void LateUpdate()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var current = GUIManager.instance;
        if (
            !VRSession.Active
            || !EmotesFeature.Gestures.Value
            || c == null
            || current == null
            || rig == null
            || rig.Head == null
            || rig.LeftHand == null
            || rig.RightHand == null
            || c.data.dead
            || c.data.passedOut
            || c.data.fullyPassedOut
            || c.data.isClimbingAnything
            || c.refs.stats.won
            || current.wheelActive
            || current.windowBlockingInput
            || GUIManager.InPauseMenu
        )
        {
            Reset();
            return;
        }
        if (current != gui)
        {
            gui = current;
            Find(current);
        }

        float dt = Time.deltaTime;
        var head = rig.Head.position;
        var side = rig.Head.right;
        side.y = 0f;
        side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
        bool holding = c.data.currentItem != null;
        bool leftFree = !holding || (!LocalHold.TwoHand && !LocalHold.ItemInLeftHand);
        bool rightFree = !holding || (!LocalHold.TwoHand && LocalHold.ItemInLeftHand);
        var l = rig.LeftHand.position;
        var r = rig.RightHand.position;
        bool leftOpen = leftFree && VRInput.Left.Tracked && VRInput.Left.Grip < OpenGrip;
        bool rightOpen = rightFree && VRInput.Right.Tracked && VRInput.Right.Grip < OpenGrip;

        bool cheered = cheer.Update(
            leftOpen
                && rightOpen
                && EmoteGestures.Raised(l.y, head.y, CheerMargin)
                && EmoteGestures.Raised(r.y, head.y, CheerMargin),
            dt
        );
        bool wavedLeft = leftWave.Update(
            leftOpen && EmoteGestures.Raised(l.y, head.y, WaveMargin),
            Vector3.Dot(l - head, side),
            dt
        );
        bool wavedRight = rightWave.Update(
            rightOpen && EmoteGestures.Raised(r.y, head.y, WaveMargin),
            Vector3.Dot(r - head, side),
            dt
        );
        if (Time.time < nextAt)
            return;
        if (cheered)
            Play(c, cheerEmote, !VRInput.LeftHanded);
        else if (wavedLeft || wavedRight)
            Play(c, wave, wavedLeft);
    }

    private void Play(Character c, Emote emote, bool left)
    {
        if (emote.Anim == null)
            return;
        if (emote.Grounded && !c.data.isGrounded)
        {
            Affordance.DenyAtHand(left, DenyReason.NeedsGround);
            return;
        }
        nextAt = Time.time + Cooldown;
        LastPlayed = emote.Anim;
        PlayCount++;
        playEmote(c.refs.animations, emote.Anim);
    }

    private void Find(GUIManager current)
    {
        wave = cheerEmote = default;
        WaveAnim = null;
        var wheel =
            current.emoteWheel != null
                ? current.emoteWheel.GetComponentInChildren<EmoteWheel>(true)
                : null;
        if (wheel == null || wheel.data == null)
            return;
        foreach (var d in wheel.data)
        {
            if (d == null || string.IsNullOrEmpty(d.anim))
                continue;
            if (wave.Anim == null && Names(d, "wave"))
                wave = new Emote { Anim = d.anim, Grounded = d.requireGrounded };
            if (cheerEmote.Anim == null && Names(d, "cheer"))
                cheerEmote = new Emote { Anim = d.anim, Grounded = d.requireGrounded };
        }
        WaveAnim = wave.Anim;
        Plugin.Log.LogInfo(
            $"[avatar.emotes] gesture emotes: wave '{wave.Anim ?? "none"}', cheer '{cheerEmote.Anim ?? "none"}'"
        );
    }

    private static bool Names(EmoteWheelData d, string word) =>
        d.anim.Contains(word, StringComparison.OrdinalIgnoreCase)
        || (d.emoteName != null && d.emoteName.Contains(word, StringComparison.OrdinalIgnoreCase));

    private void Reset()
    {
        leftWave.Reset();
        rightWave.Reset();
        cheer.Reset();
    }
}
