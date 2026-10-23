using System;
using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Interact.Climb;

/// <summary>
/// Hands on ropes, vines and piton handles for <c>interact.physical</c>. While the game has the player on a rope or
/// vine, a grip that closes on that line anchors the hand, and pulling anchored hands along the line becomes the
/// stick Y the game's own rope and vine climbs read, so speed, stamina and sync stay vanilla. While hanging from a
/// piton (or pickaxe) handle, grips on the handle hold it. When the hands that held on all let go for longer than the
/// regrip grace, the player lets go through the game's own stop. Jump still lets go, and the stick still climbs.
/// The first grab still goes through the grip interact (<c>RopeSegment.Interact</c>, <c>JungleVine.Interact</c>,
/// <c>ClimbHandle.Interact</c>); the hand that made it counts as holding on.
/// Along the way the hands feel the line: a knock at either end of a rope, a slow fill while a handle rests them, a
/// growing warning while a fragile piton cracks, and a hard beat when the game tears them off. Leaning the head
/// along a swing pumps it (decision D7, <see cref="RopePump"/>).
/// </summary>
internal static class ClimbHands
{
    private enum Mode
    {
        None,
        Rope,
        Vine,
        Handle,
    }

    private sealed class Hand
    {
        public readonly bool Left;
        public readonly AnalogButton Grip = new(0.6f, 0.4f);
        public bool Anchored;

        public Hand(bool left) => Left = left;
    }

    /// <summary>Distance (metres) from the rope or vine within which a closing grip holds it.</summary>
    internal const float LineRadius = 0.12f;

    /// <summary>Distance (metres) from a handle within which a closing grip holds it.</summary>
    internal const float HandleRadius = 0.1f;

    /// <summary>Rope pump tuning: head speed deadzone (m/s), gain (m/s² per m/s), cap (m/s²). The game's own climber
    /// weight on the segment is 1 m/s² (Rope.cs:23, :167-171).</summary>
    private const float PumpDeadzone = 0.25f,
        PumpGain = 4f,
        PumpMax = 3f;

    /// <summary>Percent step either side of the climb point used to read the line's direction.</summary>
    private const float PercentProbe = 0.02f;

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly ArmLocomotion Arms = new();
    private static readonly ClimbGrace Grace = new();
    private static readonly StaminaHaptics StaminaBeat = new();

    private static ConfigEntry<bool> ropeHands;
    private static ConfigEntry<bool> handleHands;
    private static ConfigEntry<bool> ropePump;

    private static Mode mode;
    private static UnityEngine.Object held;
    private static bool engaged;
    private static float stickY;
    private static float lastSign;

    /// <summary>The rope's end was reached on the last frame (one knock per arrival).</summary>
    private static bool atEnd;

    /// <summary>The rope's percent rate at stick +1 last frame (signed: which end the arms pull toward).</summary>
    private static float ropeRate;
    private static Vector3 lastHead;
    private static bool headValid;
    private static Rope pumpRope;
    private static int pumpSegments;
    private static int pumpCount;
    private static Rigidbody[] pumpBodies = Array.Empty<Rigidbody>();

    private static readonly PumpIntent Intent = new();
    private static Vector3 lastSwingPoint;
    private static bool swingValid;

    /// <summary>This hold already got its refusal for pumping a rope someone else owns.</summary>
    private static bool pumpRefused;

    private static ShittyPiton piton;
    private static BreakableRopeAnchor breakable;
    private static float whirrAt;

    /// <summary><c>BreakableRopeAnchor.willBreakInTime</c> and <c>isBreaking</c> (BreakableRopeAnchor.cs:15, :21,
    /// private), resolved in Install. Only the anchor's owner counts the timer down (:36-52).</summary>
    internal static HarmonyLib.AccessTools.FieldRef<BreakableRopeAnchor, float> AnchorTimer;
    internal static HarmonyLib.AccessTools.FieldRef<BreakableRopeAnchor, bool> AnchorBreaking;

    /// <summary>Seconds before an anchor gives way that the gripping hands start to feel it.</summary>
    private const float TremorSeconds = 3f;

    /// <summary>An exception was logged for the current rope, vine or handle; quiet until the next one.</summary>
    private static bool faulted;

    /// <summary>The grip extension that claims grips on the held rope, vine or handle.</summary>
    public static readonly IGripExtension Extension = new Claimer();

    /// <summary>Arm-pulled stick input for the game's rope or vine climb this frame; zero otherwise.</summary>
    public static Vector2 StickInput => new(0f, stickY);

    /// <summary>This hand holds the current rope, vine or handle (<see cref="GripToggle"/> latches it).</summary>
    internal static bool HandAnchored(bool left) =>
        mode != Mode.None && (left ? LeftHand : RightHand).Anchored;

    internal static bool RopeHands => ropeHands?.Value ?? true;
    internal static bool HandleHands => handleHands?.Value ?? true;
    internal static bool RopePumping => ropePump?.Value ?? true;

    /// <summary>Last pump acceleration put on the held rope segment (self-test vr.42b).</summary>
    internal static float LastPump { get; private set; }

    /// <summary>Self-test only (vr.42d): treat the held rope as someone else's, which offline play never has.</summary>
    internal static bool SelfTestNotMine;

    public static void Bind(ConfigFile file)
    {
        const string s = "Interaction";
        ropeHands = file.Bind(
            s,
            "RopeHandOverHand",
            true,
            "On a rope or vine, grip it and pull hand over hand to climb. Letting go with both hands lets go of the "
                + "rope. Off: climb ropes and vines with the stick, jump to let go."
        );
        handleHands = file.Bind(
            s,
            "PitonGripHang",
            true,
            "Hang from a piton or pickaxe handle while your grip holds it; open the hand to let go. Off: interact again "
                + "or jump to let go."
        );
        ropePump = file.Bind(
            s,
            "RopeSwingPump",
            true,
            "On a rope you put out yourself, lean your head along the swing to pump it higher, as shifting your weight "
                + "would. Still: the rope swings as in the flat game."
        );
    }

    /// <summary>
    /// Per frame, from <c>PhysicalInteraction.Tick</c> after the hands have seen their grip edges. Runs inside the
    /// game's input sampling, so a failure here must never reach the rest of the hand input: it is logged once per
    /// rope, vine or handle and the arm input drops to zero.
    /// </summary>
    /// <param name="active">The game's <c>playerMovementActive</c>: no menu or wheel blocks input.</param>
    public static void Update(Character c, VRRig rig, InteractConfig cfg, bool active, float dt)
    {
        try
        {
            Step(c, rig, cfg, active, dt);
        }
        catch (Exception e)
        {
            stickY = 0f;
            if (!faulted)
                Plugin.Log.LogWarning($"[interact.climb] {mode} hands stopped: {e}");
            faulted = true;
        }
    }

    private static void Step(Character c, VRRig rig, InteractConfig cfg, bool active, float dt)
    {
        // An untracked hand keeps its last grip: a tracking blink must not open it.
        if (VRInput.Left.Tracked)
            LeftHand.Grip.Update(VRInput.Left.Grip, dt);
        if (VRInput.Right.Tracked)
            RightHand.Grip.Update(VRInput.Right.Grip, dt);

        var now = Current(c, out var obj);
        if (now != mode || obj != held)
        {
            // The game let go of the line or handle while a hand still held it (stamina, a ragdoll, a broken piton,
            // the rope gone): torn off. A hand-made release goes through LetGo, which has already left the mode.
            if (now == Mode.None)
            {
                TornIfHeld(LeftHand);
                TornIfHeld(RightHand);
            }
            Enter(c, rig, now, obj);
        }
        if (mode == Mode.None || dt <= 0f)
            return;

        // With a menu or wheel open, or a controller lost, the hands' state is frozen: nobody falls off a rope
        // because a menu ate the grip or tracking blinked.
        bool frozen = !active || !VRInput.Left.Tracked || !VRInput.Right.Tracked;
        if (frozen)
            headValid = false;
        else
        {
            Release(LeftHand);
            Release(RightHand);
        }
        bool l = LeftHand.Anchored;
        bool r = RightHand.Anchored;
        if (engaged && !frozen)
        {
            Grace.Grace = cfg.ClimbRegripGrace;
            if (!Grace.Update(l || r, dt))
            {
                LetGo(c);
                return;
            }
        }
        if (mode == Mode.Handle)
        {
            if (!frozen)
                HandleFeel(c, l, r);
            return;
        }

        var d = Arms.Update(
                l && VRInput.Left.Tracked,
                VRInput.Left.Position.N(),
                r && VRInput.Right.Tracked,
                VRInput.Right.Position.N(),
                dt
            )
            .U();
        var world = (rig.Origin != null ? rig.Origin.TransformVector(d) : d) * cfg.ClimbGain;
        float target = Target(c, (world / dt).N());
        float smoothed = ClimbInput.Smooth(new(0f, stickY), new(0f, target), 12f, dt).Y;
        // Smoothing never reaches zero, and any residue would keep climbing (and paying up-stamina) with still hands.
        stickY = Mathf.Abs(smoothed) < 0.05f ? 0f : smoothed;
        if (mode == Mode.Rope && !frozen)
        {
            RopeEnd(c, l, r);
            Pump(c, rig, l || r, dt);
            AnchorTremor(l, r);
        }
        else if (mode == Mode.Vine && !frozen)
            VineSlide(c, cfg, l, r);

        if (cfg.StaminaHaptics && (l || r))
        {
            float amplitude = StaminaBeat.Update(
                c.GetTotalStamina(),
                c.data.outOfStaminaFor > 0f,
                0f,
                dt
            );
            if (amplitude > 0f)
            {
                if (l)
                    Haptics.Pulse(true, amplitude, 0.04f, HapticPriority.Ambient);
                if (r)
                    Haptics.Pulse(false, amplitude, 0.04f, HapticPriority.Ambient);
            }
        }
    }

    private static Mode Current(Character c, out UnityEngine.Object obj)
    {
        var d = c.data;
        obj = null;
        if (d.isRopeClimbing && d.heldRope != null && RopeHands)
        {
            obj = d.heldRope;
            return Mode.Rope;
        }
        if (d.isVineClimbing && d.heldVine != null && RopeHands)
        {
            obj = d.heldVine;
            return Mode.Vine;
        }
        if (d.currentClimbHandle != null && HandleHands)
        {
            obj = d.currentClimbHandle;
            return Mode.Handle;
        }
        return Mode.None;
    }

    /// <summary>
    /// The game put the player on a line or handle (or a different one). A hand whose grip is still closed near it
    /// made the grab, so it holds on: generous radius, since the body has already started moving to the line.
    /// </summary>
    private static void Enter(Character c, VRRig rig, Mode next, UnityEngine.Object obj)
    {
        mode = next;
        held = obj;
        engaged = false;
        faulted = false;
        stickY = 0f;
        lastSign = 0f;
        LeftHand.Anchored = RightHand.Anchored = false;
        atEnd = false;
        headValid = false;
        pumpRope = null;
        pumpRefused = false;
        Intent.Reset();
        swingValid = false;

        LastPump = 0f;
        piton = next == Mode.Handle && obj is ClimbHandle h ? h.GetComponent<ShittyPiton>() : null;
        breakable = next == Mode.Rope ? Breakable(obj as Rope) : null;
        Arms.Reset();
        Grace.Reset();
        StaminaBeat.Reset();
        if (mode == Mode.None)
            return;
        Adopt(c, rig, LeftHand);
        Adopt(c, rig, RightHand);
    }

    private static void Adopt(Character c, VRRig rig, Hand hand)
    {
        var t = rig.Hand(hand.Left);
        // On a handle only the free hand holds on; the item hand keeps its item.
        if (
            mode == Mode.Handle
            && c.data.currentItem != null
            && LocalHold.ItemInLeftHand == hand.Left
        )
            return;
        if (hand.Grip.Held && t != null && Near(c, t.position, 2f))
            Anchor(hand);
    }

    private static void Anchor(Hand hand)
    {
        hand.Anchored = true;
        engaged = true;
        // Rope texture: a soft, longer buzz; a handle: one firm knock.
        if (mode == Mode.Handle)
            Haptics.Pulse(hand.Left, 0.7f, 0.05f);
        else
            Haptics.Pulse(hand.Left, 0.35f, 0.12f);
    }

    private static void Release(Hand hand)
    {
        if (hand.Anchored && !hand.Grip.Held)
            hand.Anchored = false;
    }

    /// <summary>
    /// Lets go the way the game does: <c>CharacterRopeHandling.Stop</c> (CharacterRopeHandling.cs:77),
    /// <c>CharacterVineClimbing.Stop</c> (CharacterVineClimbing.cs:129), or <c>CharacterClimbing.CancelHandle</c>
    /// without grabbing the wall (CharacterClimbing.cs:198; jump does the same at :180). Each sends the game's own
    /// stop RPC. A hand already on the wall started its own wall climb, which carries on.
    /// </summary>
    private static void LetGo(Character c)
    {
        var m = mode;
        Enter(c, null, Mode.None, null);
        switch (m)
        {
            case Mode.Rope:
                c.refs.ropeHandling.Stop();
                break;
            case Mode.Vine:
                c.refs.vineClimbing.Stop();
                break;
            case Mode.Handle:
                if (c.data.currentClimbHandle != null)
                    c.refs.climbing.CancelHandle(grabWall: false);
                break;
        }
    }

    /// <summary>
    /// Stick Y that moves the climb point with the arm-pulled body. Rope: <c>ropePercent</c> changes by
    /// <c>GetMove() * y * (y &lt; 0 ? 3 : 1) * climbSpeed * climbSpeedMod * UpMult</c> per second
    /// (CharacterRopeHandling.cs:63-64). Vine: <c>vinePercent</c> by <c>LengthFactor() * climbSpeed * climbSpeedMod *
    /// GetSign(lookDirection_Flat) * y</c> (CharacterVineClimbing.cs:67-68, :118). The line's direction comes from
    /// the same <c>GetPosition</c> the game pulls the body to (CharacterRopeHandling.cs:136, CharacterVineClimbing.cs:150).
    /// </summary>
    private static float Target(Character c, NVector3 bodyVelocity)
    {
        var d = c.data;
        float lo,
            hi,
            rate,
            negative;
        Vector3 a,
            b;
        if (mode == Mode.Rope)
        {
            var api = d.heldRope.climbingAPI;
            var rope = c.refs.ropeHandling;
            // RopeClimbingAPI.GetPosition and UpMult index the segment list unchecked (RopeClimbingAPI.cs:77-83);
            // a remote rope can have none yet.
            var segments = d.heldRope.GetRopeSegments();
            if (api == null || segments == null || segments.Count < 2)
                return 0f;
            lo = Mathf.Clamp01(d.ropePercent - PercentProbe);
            hi = Mathf.Clamp01(d.ropePercent + PercentProbe);
            a = api.GetPosition(lo);
            b = api.GetPosition(hi);
            rate = api.GetMove() * rope.climbSpeed * rope.climbSpeedMod * api.UpMult(d.ropePercent);
            ropeRate = rate;
            negative = 3f;
        }
        else
        {
            var vine = d.heldVine;
            var climb = c.refs.vineClimbing;
            // GetSign and GetPosition index colliderRoot's children unchecked (JungleVine.cs:428, :456-468).
            var root = vine.colliderRoot;
            if (
                root == null
                || root.childCount < 4
                || vine.GetIndexFromPercentage(d.vinePercent) >= root.childCount
            )
                return 0f;
            lo = Mathf.Clamp01(d.vinePercent - PercentProbe);
            hi = Mathf.Clamp01(d.vinePercent + PercentProbe);
            a = vine.GetPosition(lo, 1, 1);
            b = vine.GetPosition(hi, 1, 1);
            float sign = vine.GetSign(d.lookDirection_Flat, d.vinePercent);
            // Looking across the vine flips the game's stick meaning (CharacterVineClimbing.cs:67-68). Re-express the
            // smoothed stick in the new sign so the body keeps moving the same way instead of blipping back.
            if (lastSign != 0f && sign != lastSign)
                stickY = -stickY;
            lastSign = sign;
            rate = vine.LengthFactor() * climb.climbSpeed * climb.climbSpeedMod * sign;
            negative = 1f;
        }
        if (hi - lo < 1e-4f)
            return 0f;
        return RopeClimb.StickAlong(bodyVelocity, ((b - a) / (hi - lo)).N(), rate, negative, 0.1f);
    }

    /// <summary>
    /// The hand is within reach of the held rope's or vine's axis, or at the held handle. Geometry, not physics: vine
    /// colliders are 0.25 m trigger capsules on layer 21 raised by <c>colliderOffset</c> above the drawn vine
    /// (JungleVine.cs:286-297), so an overlap test would accept hands far off the vine and depends on the layer mask.
    /// Rope axis: the segment transforms (Rope.cs:186); vine axis: the collider children lowered by the offset.
    /// </summary>
    private static bool Near(Character c, Vector3 pos, float scale)
    {
        if (mode == Mode.Handle)
        {
            var handle = c.data.currentClimbHandle;
            float h = HandleRadius * scale;
            return handle != null && (handle.transform.position - pos).sqrMagnitude <= h * h;
        }
        float r = LineRadius * scale;
        return mode == Mode.Rope
            ? NearRope(c.data.heldRope, pos, r)
            : NearVine(c.data.heldVine, pos, r);
    }

    /// <summary>The point is within <paramref name="r"/> of the rope's axis (its segment transforms, Rope.cs:186).</summary>
    internal static bool NearRope(Rope rope, Vector3 pos, float r)
    {
        float r2 = r * r;
        var p = pos.N();
        var segments = rope.GetRopeSegments();
        if (segments == null)
            return false;
        for (int i = 1; i < segments.Count; i++)
        {
            if (
                segments[i - 1] != null
                && segments[i] != null
                && RopeClimb.SegmentDistanceSq(
                    p,
                    segments[i - 1].position.N(),
                    segments[i].position.N()
                ) <= r2
            )
                return true;
        }
        return false;
    }

    /// <summary>The point is within <paramref name="r"/> of the drawn vine: its collider children lowered by
    /// <c>colliderOffset</c> (JungleVine.cs:286-297).</summary>
    internal static bool NearVine(JungleVine vine, Vector3 pos, float r) =>
        VineDistance(vine, pos) <= r;

    /// <summary>Distance from <paramref name="pos"/> to the drawn vine; infinity without colliders.</summary>
    internal static float VineDistance(JungleVine vine, Vector3 pos)
    {
        var p = pos.N();
        var root = vine.colliderRoot;
        if (root == null)
            return float.PositiveInfinity;
        var down = Vector3.down * vine.colliderOffset;
        float best = float.PositiveInfinity;
        for (int i = 1; i < root.childCount; i++)
        {
            best = Mathf.Min(
                best,
                RopeClimb.SegmentDistanceSq(
                    p,
                    (root.GetChild(i - 1).position + down).N(),
                    (root.GetChild(i).position + down).N()
                )
            );
        }
        return Mathf.Sqrt(best);
    }

    /// <summary>The breakable anchor holding <paramref name="rope"/>, if any. Once per rope grab, never per frame.</summary>
    private static BreakableRopeAnchor Breakable(Rope rope)
    {
        if (rope == null || AnchorTimer == null)
            return null;
        foreach (
            var b in UnityEngine.Object.FindObjectsByType<BreakableRopeAnchor>(
                FindObjectsSortMode.None
            )
        )
        {
            if (b.TryGetComponent<RopeAnchorWithRope>(out var anchor) && anchor.rope == rope)
                return b;
        }
        return null;
    }

    /// <summary>
    /// D5: a breakable anchor (BreakableRopeAnchor.cs:34-70) gives way 3-8 s into being climbed. In its last seconds,
    /// and through the 3 s break, the gripping hands feel the danger warning at the anchor, growing as it nears.
    /// shortcut: only the anchor's owner runs the timer, so on another player's rope the warning stays silent.
    /// </summary>
    private static void AnchorTremor(bool l, bool r)
    {
        if (breakable == null)
            return;
        float left = AnchorTimer(breakable);
        float urgency =
            AnchorBreaking(breakable) ? 1f
            : left < TremorSeconds ? 1f - Mathf.Max(left, 0f) / TremorSeconds
            : 0f;
        if (urgency <= 0f)
            return;
        var at = breakable.transform.position;
        if (l)
            Affordance.Danger(true, at, urgency);
        if (r)
            Affordance.Danger(false, at, urgency);
    }

    /// <summary>
    /// D9: a fast grab onto a vine slides along it with input ignored (<c>Sliding</c>, CharacterVineClimbing.cs:102-127).
    /// The hands feel a whirr and hear it, like a line running through them; the ones holding the vine, or both
    /// after a button grab.
    /// </summary>
    private static void VineSlide(Character c, InteractConfig cfg, bool l, bool r)
    {
        if (!cfg.ClimbSurfaceFeel || !c.refs.vineClimbing.Sliding())
            return;
        float now = Time.time;
        if (now < whirrAt)
            return;
        whirrAt = now + 0.15f;
        ClimbCues.Sound(ClimbCue.Whirr, ClimbSound.Whirr, c.Center);
        bool both = !l && !r;
        if (l || both)
            ClimbCues.Pulse(ClimbCue.Whirr, true, 0.35f, 0.12f);
        if (r || both)
            ClimbCues.Pulse(ClimbCue.Whirr, false, 0.35f, 0.12f);
    }

    private static void TornIfHeld(Hand hand)
    {
        if (hand.Anchored && hand.Grip.Held)
            PhysicalInteraction.TearOff(hand.Left, Time.time);
    }

    /// <summary>
    /// While hanging from a handle: the gripping hands feel stamina refill (the game regenerates it there,
    /// CharacterClimbing.cs:160-227) as a slow progress fill, so hanging is learnt as rest. A fragile piton that has
    /// started to crack (ShittyPiton.cs:57-93; its <c>crack</c> grows 0.75 per crack, 4 cracks break it) turns that
    /// into a warning at the handle that speeds up with each crack.
    /// </summary>
    private static void HandleFeel(Character c, bool l, bool r)
    {
        var handle = c.data.currentClimbHandle;
        if (handle == null)
            return;
        if (piton != null && piton.crack != null && piton.crack.activeSelf)
        {
            // From the first crack on, never a faint warning: the crack grows from zero scale (ShittyPiton.cs:110-111).
            float urgency = 0.3f + 0.7f * Mathf.Clamp01(piton.crack.transform.localScale.x / 3f);
            var at = handle.transform.position;
            if (l)
                Affordance.Danger(true, at, urgency);
            if (r)
                Affordance.Danger(false, at, urgency);
            return;
        }
        float max = c.GetMaxStamina();
        float fill = max > 0f ? c.GetTotalStamina() / max : 1f;
        if (fill >= 0.999f)
            return;
        if (l)
            Affordance.Progress(true, fill, hold: false);
        if (r)
            Affordance.Progress(false, fill, hold: false);
    }

    /// <summary>
    /// A knock as the climb point reaches either end of the rope while the arms still pull that way:
    /// <c>ropePercent</c> clamps to 0..1 with no event (CharacterRopeHandling.cs:64-65). Which end the arms pull toward
    /// follows the sign of the stick times the rope's rate (<c>GetMove * UpMult</c>, which flips with segment order).
    /// </summary>
    private static void RopeEnd(Character c, bool l, bool r)
    {
        float p = c.data.ropePercent;
        float toward = stickY * ropeRate;
        bool end = (toward < 0f && p <= 0.001f) || (toward > 0f && p >= 0.999f);
        if (end && !atEnd)
        {
            if (l)
                Affordance.EndStop(true);
            if (r)
                Affordance.EndStop(false);
        }
        atEnd = end;
    }

    /// <summary>
    /// D7: the head leaning along the swing pushes the whole rope (<see cref="RopePump"/>). Only the rope's owner
    /// simulates it (Rope.cs:155); vanilla syncs the result, so nothing new is sent.
    /// shortcut: a rope someone else owns does not pump, since pushing it would need a message to its owner. The
    /// first clear pump on such a rope in a hold (<see cref="PumpIntent"/>: a rhythm of leans, never a look around)
    /// gets NotHere at the gripping hand instead of nothing.
    /// </summary>
    private static void Pump(Character c, VRRig rig, bool holding, float dt)
    {
        var head = VRInput.HeadPosition;
        bool valid = headValid;
        var velocity = valid ? (head - lastHead) / dt : Vector3.zero;
        lastHead = head;
        headValid = VRInput.HeadTracked;
        LastPump = 0f;
        var rope = c.data.heldRope;
        if (!RopePumping || !holding || !valid || rope.photonView == null)
        {
            swingValid = false;
            return;
        }
        if (!rope.photonView.IsMine || SelfTestNotMine)
        {
            if (rig.Origin != null)
                velocity = rig.Origin.TransformVector(velocity);
            // Refuse only a clear pump (PumpIntent): looking around or leaning to grab on a friend's rope stays quiet.
            var point =
                rope.climbingAPI != null
                    ? rope.climbingAPI.GetPosition(c.data.ropePercent)
                    : c.Center;
            var swing = swingValid ? (point - lastSwingPoint) / dt : Vector3.zero;
            lastSwingPoint = point;
            swingValid = true;
            bool pumping = Intent.Update(velocity.N(), swing.N(), Time.time, dt);
            if (RopePump.RefuseOnce(false, pumping ? 1f : 0f, ref pumpRefused))
            {
                bool left = LeftHand.Anchored || !RightHand.Anchored;
                Affordance.DenyAtHand(left, DenyReason.NotHere);
            }
            return;
        }
        var segments = rope.GetRopeSegments();
        if (segments == null || segments.Count < 2)
            return;
        if (rope != pumpRope || segments.Count != pumpSegments)
            CachePumpBodies(rope, segments);
        if (pumpCount == 0)
            return;
        if (rig.Origin != null)
            velocity = rig.Origin.TransformVector(velocity);
        var push = RopePump.Push(velocity.N(), PumpDeadzone, PumpGain, PumpMax).U();
        LastPump = push.magnitude;
        if (LastPump <= 0f)
            return;
        // The whole line, as a shift of weight swings a real rope: run 37 pushed only the held segment, which its
        // joints and the hanging body soaked up (swing 0.21 m pumped against 0.24 m still).
        var dv = push * dt;
        for (int i = 0; i < pumpCount; i++)
        {
            if (pumpBodies[i] != null)
                pumpBodies[i].AddForce(dv, ForceMode.VelocityChange);
        }
    }

    /// <summary>The simulated segment bodies of the held rope (owner only, Rope.cs:313-377); rebuilt when the rope or
    /// its segment count changes, never per frame.</summary>
    private static void CachePumpBodies(
        Rope rope,
        System.Collections.Generic.List<Transform> segments
    )
    {
        pumpRope = rope;
        pumpSegments = segments.Count;
        pumpCount = 0;
        if (pumpBodies.Length < segments.Count)
            pumpBodies = new Rigidbody[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            if (
                segments[i] != null
                && segments[i].TryGetComponent<Rigidbody>(out var body)
                && !body.isKinematic
            )
                pumpBodies[pumpCount++] = body;
        }
    }

    /// <summary>
    /// Takes a grip that closes on the held line or handle, so it neither interacts with what the head aims at
    /// (vanilla interact on a handle lets go of it, CharacterClimbing.cs:168) nor starts a wall climb.
    /// </summary>
    private sealed class Claimer : IGripExtension
    {
        public bool ClaimPress(in GripHookContext ctx)
        {
            try
            {
                var next = Current(ctx.Character, out var obj);
                // The hand that holds an item never holds the handle too (GripIntent: the free hand hangs).
                if (next == Mode.Handle && ctx.ThisHandHolds)
                    return false;
                return next != Mode.None
                    && next == mode
                    && obj == held
                    && Near(ctx.Character, ctx.HandPosition, 1f);
            }
            catch (Exception e)
            {
                if (!faulted)
                    Plugin.Log.LogWarning($"[interact.climb] {mode} grip check failed: {e}");
                faulted = true;
                return false;
            }
        }

        public void BeginPress(in GripHookContext ctx)
        {
            var hand = ctx.Left ? LeftHand : RightHand;
            if (!hand.Anchored)
                Anchor(hand);
        }

        /// <summary>Consumed; the hand itself lets go in <see cref="Step"/>, which ignores a lost controller.</summary>
        public bool EndPress(in GripHookContext ctx) => true;

        public bool TryHeldRelease(in GripHookContext ctx) => false;
    }
}
