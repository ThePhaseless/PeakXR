using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
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
    private const float LineRadius = 0.12f;

    /// <summary>Distance (metres) from a handle within which a closing grip holds it.</summary>
    private const float HandleRadius = 0.1f;

    /// <summary>Percent step either side of the climb point used to read the line's direction.</summary>
    private const float PercentProbe = 0.02f;

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly ArmLocomotion Arms = new();
    private static readonly ClimbGrace Grace = new();
    private static readonly StaminaHaptics StaminaBeat = new();

    private static ConfigEntry<bool> ropeHands;
    private static ConfigEntry<bool> handleHands;

    private static Mode mode;
    private static Object held;
    private static bool engaged;
    private static float stickY;

    /// <summary>The grip extension that claims grips on the held rope, vine or handle.</summary>
    public static readonly IGripExtension Extension = new Claimer();

    /// <summary>Arm-pulled stick input for the game's rope or vine climb this frame; zero otherwise.</summary>
    public static Vector2 StickInput => new(0f, stickY);

    private static bool RopeHands => ropeHands?.Value ?? true;
    private static bool HandleHands => handleHands?.Value ?? true;

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
    }

    /// <summary>Per frame, from <c>PhysicalInteraction.Tick</c> after the hands have seen their grip edges.</summary>
    public static void Update(Character c, VRRig rig, InteractConfig cfg, float dt)
    {
        LeftHand.Grip.Update(VRInput.Left.Tracked ? VRInput.Left.Grip : 0f, dt);
        RightHand.Grip.Update(VRInput.Right.Tracked ? VRInput.Right.Grip : 0f, dt);

        var now = Current(c, out var obj);
        if (now != mode || obj != held)
            Enter(c, rig, now, obj);
        if (mode == Mode.None || dt <= 0f)
            return;

        Release(LeftHand);
        Release(RightHand);
        bool l = LeftHand.Anchored;
        bool r = RightHand.Anchored;
        if (engaged)
        {
            Grace.Grace = cfg.ClimbRegripGrace;
            if (!Grace.Update(l || r, dt))
            {
                LetGo(c);
                return;
            }
        }
        if (mode == Mode.Handle)
            return;

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
                    Haptics.Pulse(true, amplitude, 0.04f);
                if (r)
                    Haptics.Pulse(false, amplitude, 0.04f);
            }
        }
    }

    private static Mode Current(Character c, out Object obj)
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
    private static void Enter(Character c, VRRig rig, Mode next, Object obj)
    {
        mode = next;
        held = obj;
        engaged = false;
        stickY = 0f;
        LeftHand.Anchored = RightHand.Anchored = false;
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
            lo = Mathf.Clamp01(d.ropePercent - PercentProbe);
            hi = Mathf.Clamp01(d.ropePercent + PercentProbe);
            a = api.GetPosition(lo);
            b = api.GetPosition(hi);
            rate = api.GetMove() * rope.climbSpeed * rope.climbSpeedMod * api.UpMult(d.ropePercent);
            negative = 3f;
        }
        else
        {
            var vine = d.heldVine;
            var climb = c.refs.vineClimbing;
            lo = Mathf.Clamp01(d.vinePercent - PercentProbe);
            hi = Mathf.Clamp01(d.vinePercent + PercentProbe);
            a = vine.GetPosition(lo, 1, 1);
            b = vine.GetPosition(hi, 1, 1);
            rate =
                vine.LengthFactor()
                * climb.climbSpeed
                * climb.climbSpeedMod
                * vine.GetSign(d.lookDirection_Flat, d.vinePercent);
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
        float r2 = r * r;
        var p = pos.N();
        if (mode == Mode.Rope)
        {
            var segments = c.data.heldRope.GetRopeSegments();
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
        var vine = c.data.heldVine;
        var root = vine.colliderRoot;
        if (root == null)
            return false;
        var down = Vector3.down * vine.colliderOffset;
        for (int i = 1; i < root.childCount; i++)
        {
            if (
                RopeClimb.SegmentDistanceSq(
                    p,
                    (root.GetChild(i - 1).position + down).N(),
                    (root.GetChild(i).position + down).N()
                ) <= r2
            )
                return true;
        }
        return false;
    }

    /// <summary>
    /// Takes a grip that closes on the held line or handle, so it neither interacts with what the head aims at
    /// (vanilla interact on a handle lets go of it, CharacterClimbing.cs:168) nor starts a wall climb.
    /// </summary>
    private sealed class Claimer : IGripExtension
    {
        public bool ClaimPress(in GripHookContext ctx)
        {
            var next = Current(ctx.Character, out var obj);
            return next != Mode.None
                && next == mode
                && obj == held
                && Near(ctx.Character, ctx.HandPosition, 1f);
        }

        public void BeginPress(in GripHookContext ctx)
        {
            var hand = ctx.Left ? LeftHand : RightHand;
            if (!hand.Anchored)
                Anchor(hand);
        }

        public bool EndPress(in GripHookContext ctx)
        {
            (ctx.Left ? LeftHand : RightHand).Anchored = false;
            return true;
        }

        public bool TryHeldRelease(in GripHookContext ctx) => false;
    }
}
