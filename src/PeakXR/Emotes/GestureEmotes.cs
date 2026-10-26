using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Emotes;

/// <summary>
/// Gesture emotes (D5): wave a raised open hand side to side to play the game's wave emote, hold both open hands
/// over your head to cheer, hold a fist out with the thumb up for the thumbs-up. They play through vanilla CharacterAnimations.PlayEmote (A/CharacterAnimations.cs:410,
/// the call the emote wheel makes, EmoteWheel.cs:87), so every peer sees them. The emotes are found by name in the
/// wheel's own data (EmoteWheel.data), so nothing new is hard-coded. Not tracked while climbing (rope and vine too),
/// carrying a friend, passed out, dead, in a menu or with a wheel open, nor on a hand whose grip is busy (a climber's
/// reaching hands look like a cheer, an ordinary grip like a thumbs-up). The thumbs-up also needs an empty-handed fist
/// held still out in front, on a controller that senses the thumb (EmoteGestures.ThumbIntent). Gestures track only on
/// the ground and away from holds, so noise is never refused; a played emote is confirmed on the hand (HappenedHere).
/// </summary>
[DefaultExecutionOrder(31060)]
internal sealed class GestureEmotes : MonoBehaviour
{
    /// <summary>A wave counts from about chin height: 15 cm under the eyes.</summary>
    private const float WaveMargin = -0.15f;

    /// <summary>A cheer needs both hands over the head.</summary>
    private const float CheerMargin = 0.1f;

    /// <summary>Seconds the cheer pose is held (DwellGesture's default).</summary>
    private const float CheerDwell = 0.4f;

    /// <summary>An open hand: the grip let go.</summary>
    private const float OpenGrip = 0.3f;

    /// <summary>Vanilla ends an emote after 2 s (CharacterAnimations.cs:278); no second one meanwhile.</summary>
    private const float Cooldown = 2f;

    private static Action<CharacterAnimations, string> playEmote;

    private readonly WaveGesture leftWave = new();
    private readonly WaveGesture rightWave = new();
    private readonly DwellGesture cheer = new();
    private readonly DwellGesture leftThumb = new() { Dwell = EmoteGestures.ThumbDwell };
    private readonly DwellGesture rightThumb = new() { Dwell = EmoteGestures.ThumbDwell };

    /// <summary>Per hand (left, right): the controller has reported a thumb touch or press at least once, so it has
    /// thumb sensors and a lifted thumb means something.</summary>
    private readonly bool[] thumbSensed = new bool[2];
    private GUIManager gui;
    private Emote wave,
        cheerEmote,
        thumbsUp;
    private float nextAt;

    /// <summary>The last emote a gesture played (its animation name), for the self-test.</summary>
    public static string LastPlayed { get; private set; }

    /// <summary>Emotes played by a gesture since start, for the self-test.</summary>
    public static int PlayCount { get; private set; }

    /// <summary>The wave emote's animation found in the wheel data, or null.</summary>
    public static string WaveAnim { get; private set; }

    /// <summary>The thumbs-up emote's animation found in the wheel data, or null.</summary>
    public static string ThumbsUpAnim { get; private set; }

    /// <summary>The cheer emote's animation found in the wheel data, or null.</summary>
    public static string CheerAnim { get; private set; }

    private struct Emote
    {
        public string Anim;
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
            // Airborne: a jump or a fall, where arms fly about and the ground-only emotes would only be refused.
            || !c.data.isGrounded
            || EmoteGestures.Busy(c.data.isClimbingAnything, c.data.IsCarryingCharacter, false)
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
        // A grip that took or started something (a handle, a friend, a lid), or a hand at a hold where it would
        // climb (the climb ring shows), is busy, not gesturing: reaching up a rock face looks like a cheer.
        bool leftBusy = MidGrab(true) || AtHold(l);
        bool rightBusy = MidGrab(false) || AtHold(r);
        bool leftOpen =
            leftFree && !leftBusy && VRInput.Left.Tracked && VRInput.Left.Grip < OpenGrip;
        bool rightOpen =
            rightFree && !rightBusy && VRInput.Right.Tracked && VRInput.Right.Grip < OpenGrip;

        // One-handed play (Accessibility.OneHanded): one open hand held still over the head cheers, held twice as long
        // so a hand raised to wave is not taken for one.
        cheer.Dwell = VRInput.OneHanded ? 2f * CheerDwell : CheerDwell;
        bool cheered = cheer.Update(
            EmoteGestures.CheerPose(
                VRInput.OneHanded,
                leftOpen && EmoteGestures.Raised(l.y, head.y, CheerMargin),
                rightOpen && EmoteGestures.Raised(r.y, head.y, CheerMargin),
                VRInput.Hand(VRInput.LeftHanded).Velocity.magnitude
            ),
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
        var forward = rig.Head.forward;
        bool thumbLeft = leftThumb.Update(
            Thumb(true, rig.LeftHand, head, forward, holding, leftBusy),
            dt
        );
        bool thumbRight = rightThumb.Update(
            Thumb(false, rig.RightHand, head, forward, holding, rightBusy),
            dt
        );
        if (Time.time < nextAt)
            return;
        if (cheered)
            Play(c, cheerEmote, !VRInput.LeftHanded, rig.Hand(!VRInput.LeftHanded));
        else if (wavedLeft || wavedRight)
            Play(c, wave, wavedLeft, rig.Hand(wavedLeft));
        else if (thumbLeft || thumbRight)
            Play(c, thumbsUp, thumbLeft, rig.Hand(thumbLeft));
    }

    /// <summary>A deliberate thumbs-up (EmoteGestures.ThumbIntent): a tracked fist, thumb lifted and up, held still out
    /// in front, empty-handed and not busy, on a controller that has shown it senses the thumb.</summary>
    private bool Thumb(
        bool left,
        Transform hand,
        Vector3 head,
        Vector3 forward,
        bool holding,
        bool busy
    )
    {
        ref var state = ref VRInput.Hand(left);
        if (!state.Tracked)
            return false;
        ref bool sensed = ref thumbSensed[left ? 0 : 1];
        sensed |=
            state.PrimaryTouch
            || state.SecondaryTouch
            || state.StickTouch
            || state.ThumbrestTouch
            || state.Primary
            || state.Secondary;
        var p = hand.position;
        var flat = forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        return EmoteGestures.ThumbIntent(
            EmoteGestures.ThumbsUp(
                LocalAvatar.ControllerCurls(left),
                Vector3.Dot(hand.ThumbSide(), Vector3.up)
            ),
            sensed,
            holding,
            busy,
            Vector3.Dot(p - head, flat),
            head.y - p.y,
            state.Velocity.magnitude
        );
    }

    /// <summary>This hand's grip took or started something (PhysicalInteraction.HandAction: a grab, climb, second
    /// hand, extension), or holds a target. The fallback Interact of a grip on empty air is not busy.</summary>
    private static bool MidGrab(bool left)
    {
        var action = PhysicalInteraction.HandAction(left);
        return (action != GripPressAction.None && action != GripPressAction.Interact)
            || PhysicalInteraction.HandTarget(left) != null;
    }

    /// <summary>Plays the emote and confirms it on the gesturing hand: the notice tick, bell and pin ring
    /// (HappenedHere), so the sender knows the peers saw it. Gestures are only tracked on the ground, so a
    /// ground-only emote (EmoteWheelData.requireGrounded) is never refused from a gesture.</summary>
    private void Play(Character c, Emote emote, bool left, Transform hand)
    {
        if (emote.Anim == null)
            return;
        nextAt = Time.time + Cooldown;
        LastPlayed = emote.Anim;
        PlayCount++;
        playEmote(c.refs.animations, emote.Anim);
        Affordance.Inform(left, hand.position, DenyReason.HappenedHere, default);
    }

    /// <summary>The hand is within reach of terrain it would climb (interact.physical's climb probe: a sphere of
    /// <c>Interaction.ClimbRadius</c> on the terrain map).</summary>
    private static bool AtHold(Vector3 hand)
    {
        var cfg = InteractConfig.Instance;
        return cfg.PhysicalClimb
            && Physics.CheckSphere(
                hand,
                cfg.ClimbRadius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
    }

    private void Find(GUIManager current)
    {
        wave = cheerEmote = thumbsUp = default;
        WaveAnim = ThumbsUpAnim = CheerAnim = null;
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
                wave = new Emote { Anim = d.anim };
            if (cheerEmote.Anim == null && Names(d, "cheer"))
                cheerEmote = new Emote { Anim = d.anim };
            if (thumbsUp.Anim == null && Names(d, "thumb"))
                thumbsUp = new Emote { Anim = d.anim };
        }
        WaveAnim = wave.Anim;
        ThumbsUpAnim = thumbsUp.Anim;
        CheerAnim = cheerEmote.Anim;
        Plugin.Log.LogInfo(
            $"[avatar.emotes] gesture emotes: wave '{wave.Anim ?? "none"}', cheer '{cheerEmote.Anim ?? "none"}', thumbs-up '{thumbsUp.Anim ?? "none"}'"
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
        leftThumb.Reset();
        rightThumb.Reset();
    }
}
