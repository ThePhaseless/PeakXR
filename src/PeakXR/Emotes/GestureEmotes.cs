using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Rig;
using PeakXR.Social;
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
/// held still out in front, its thumb just lifted off a button (EmoteGestures.ThumbIntent). Gestures track only on
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
    private readonly DwellGesture cheer = new() { Dwell = CheerDwell };
    private readonly DwellGesture leftThumb = new() { Dwell = EmoteGestures.ThumbDwell };
    private readonly DwellGesture rightThumb = new() { Dwell = EmoteGestures.ThumbDwell };

    /// <summary>Per hand (left, right): when the thumb was last on a button (<c>Time.time</c>), never on a controller
    /// with no thumb sensors (<see cref="EmoteGestures.ThumbJustLifted"/>).</summary>
    private static readonly float[] thumbOnAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>Gestures were tracked last frame (none of the early outs in <see cref="LateUpdate"/>).</summary>
    private static bool tracking;

    private static readonly float[] playedAt = { float.NegativeInfinity, float.NegativeInfinity };
    private static readonly float[] tookAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>
    /// This empty hand's grip may be the thumbs-up's fist, not a grab (interact.physical, #82): gestures are tracked,
    /// nothing is held, the hand is out in front and short of a reach (<see cref="EmoteGestures.ThumbOutFront"/>) and the thumb is lifted
    /// and up (<see cref="EmoteGestures.ThumbLifted"/>), just now (<see cref="EmoteGestures.ThumbJustLifted"/>). Read live, so it holds on the
    /// frame the grip closes, still or not.
    /// </summary>
    internal static bool ThumbPosed(bool left)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        if (
            !tracking
            || c == null
            || c.data.currentItem != null
            || hand == null
            || rig.Head == null
            || !ThumbJustLifted(left)
            || !VRInput.Hand(left).Tracked
        )
            return false;
        var head = rig.Head.position;
        var p = Rig.RealScale.Real(hand.position, head);
        var flat = rig.Head.forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        return EmoteGestures.ThumbOutFront(Vector3.Dot(p - head, flat), head.y - p.y, Stretch(left))
            && EmoteGestures.ThumbLifted(
                LocalAvatar.ControllerCurls(left),
                Vector3.Dot(hand.ThumbSide(), Vector3.up)
            );
    }

    /// <summary>When a gesture emote last played on this hand (<c>Time.time</c>).</summary>
    internal static float PlayedAt(bool left) => playedAt[left ? 0 : 1];

    /// <summary>When the thumbs-up last fired on this hand (<c>Time.time</c>), played or held back by the cooldown: its
    /// fist is the gesture's either way, so it presses no interact (interact.physical, #82).</summary>
    internal static float TookAt(bool left) => tookAt[left ? 0 : 1];

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
        // Before the early outs: a thumb on the stick through a climb or a menu still came off it just now.
        NoteThumb(true);
        NoteThumb(false);
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
            || EmoteGestures.Busy(
                c.data.isClimbingAnything,
                c.data.IsCarryingCharacter,
                false,
                c.refs.afflictions.isStruggling
            )
            || c.refs.stats.won
            || current.wheelActive
            || current.windowBlockingInput
            || GUIManager.InPauseMenu
        )
        {
            Reset();
            return;
        }
        tracking = true;
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
        // A grip that took or started something (a handle, a friend, a lid), or a hand reaching for something (rock
        // at it, the climb ring or the grab glow on it) is busy, not gesturing: reaching up a rock face or for a rope
        // overhead looks like a cheer.
        bool leftBusy = HandBusy(true, rig.LeftHand.position);
        bool rightBusy = HandBusy(false, rig.RightHand.position);
        // The gesture thresholds (EmoteGestures, WaveGesture) are the player's real metres around the head.
        var l = Rig.RealScale.Real(rig.LeftHand.position, head);
        var r = Rig.RealScale.Real(rig.RightHand.position, head);
        bool leftOpen =
            leftFree && !leftBusy && VRInput.Left.Tracked && VRInput.Left.Grip < OpenGrip;
        bool rightOpen =
            rightFree && !rightBusy && VRInput.Right.Tracked && VRInput.Right.Grip < OpenGrip;

        bool cheered = cheer.Update(
            leftOpen
                && EmoteGestures.Raised(l.y, head.y, CheerMargin)
                && rightOpen
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
        var forward = rig.Head.forward;
        leftThumb.Dwell = EmoteGestures.ThumbDwellAt(Stretch(true));
        rightThumb.Dwell = EmoteGestures.ThumbDwellAt(Stretch(false));
        bool thumbLeft = leftThumb.Update(
            Thumb(true, rig.LeftHand, head, forward, holding, leftBusy),
            dt
        );
        bool thumbRight = rightThumb.Update(
            Thumb(false, rig.RightHand, head, forward, holding, rightBusy),
            dt
        );
        if (thumbLeft)
            tookAt[0] = Time.time;
        if (thumbRight)
            tookAt[1] = Time.time;
        if (Time.time < nextAt)
        {
            // A dwell gesture fires once per hold: one light notice that the cooldown held it back, never silence.
            if (cheered || thumbLeft || thumbRight)
            {
                bool left = cheered ? VRInput.OffHandLeft : thumbLeft;
                Affordance.Inform(left, rig.Hand(left).position, DenyReason.TooSoon, default);
            }
            return;
        }
        if (cheered)
            Play(c, cheerEmote, VRInput.OffHandLeft, rig.Hand(VRInput.OffHandLeft));
        else if (wavedLeft || wavedRight)
            Play(c, wave, wavedLeft, rig.Hand(wavedLeft));
        else if (thumbLeft || thumbRight)
            Play(c, thumbsUp, thumbLeft, rig.Hand(thumbLeft));
    }

    /// <summary>A deliberate thumbs-up (EmoteGestures.ThumbIntent): a tracked fist, thumb lifted and up, held still out
    /// in front and short of a reach, empty-handed and not busy, the thumb just lifted off a button.</summary>
    private static bool Thumb(
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
        var p = Rig.RealScale.Real(hand.position, head);
        var flat = forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        return EmoteGestures.ThumbIntent(
            EmoteGestures.ThumbsUp(
                LocalAvatar.ControllerCurls(left),
                Vector3.Dot(hand.ThumbSide(), Vector3.up)
            ),
            ThumbJustLifted(left),
            holding,
            busy,
            Vector3.Dot(p - head, flat),
            head.y - p.y,
            state.Velocity.magnitude,
            Stretch(left)
        );
    }

    private static void NoteThumb(bool left)
    {
        ref var state = ref VRInput.Hand(left);
        if (
            state.PrimaryTouch
            || state.SecondaryTouch
            || state.StickTouch
            || state.ThumbrestTouch
            || state.Primary
            || state.Secondary
        )
            thumbOnAt[left ? 0 : 1] = Time.time;
    }

    private static bool ThumbJustLifted(bool left) =>
        EmoteGestures.ThumbJustLifted(Time.time - thumbOnAt[left ? 0 : 1]);

    /// <summary>How far this hand is out, on the reach-out's own measure (Core <see cref="ReachOut"/>, tracking space),
    /// against the measured arm or the nominal one if that is longer: a learner that shrank the arm must not shut the
    /// thumbs-up out (<see cref="EmoteGestures.ThumbMaxStretch"/>, #146).</summary>
    private static float Stretch(bool left)
    {
        var headRotation = VRInput.HeadRotation.N();
        return ReachOut.Stretch(
            ReachOut.Shoulder(
                BodySlotLayout.Neck(VRInput.HeadPosition.N(), headRotation),
                BodySlotLayout.HeadYawDeg(headRotation),
                left
            ),
            VRInput.Hand(left).Position.N(),
            Mathf.Max(SocialHands.Instance.ReachOutArm, ReachOut.NominalArm)
        );
    }

    private static bool HandBusy(bool left, Vector3 hand) =>
        EmoteGestures.HandBusy(
            MidGrab(left),
            AtHold(hand),
            Interact.Climb.ClimbHover.Shown(left),
            PhysicalInteraction.GripPreview(left) != null
        );

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
        playedAt[left ? 0 : 1] = Time.time;
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
        tracking = false;
        leftWave.Reset();
        rightWave.Reset();
        cheer.Reset();
        leftThumb.Reset();
        rightThumb.Reset();
    }
}
