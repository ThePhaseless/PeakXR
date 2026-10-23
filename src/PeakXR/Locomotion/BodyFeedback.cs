using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// What the body feels in VR where flat play shows a reticle or a bar out of view: the stamina limit on sprint and
/// jump (the wrist bar is on the arm, not in sight), the jump taking off or refused, the landing thump, fall danger
/// building before the game's fall damage, crouch latching, footing back after a ragdoll, a slope too steep to
/// stand and the build-up to passing out. Local only, through ui.affordance; nothing goes over the network.
/// </summary>
internal static class BodyFeedback
{
    /// <summary>Take-off tick: light, both hands, like the push of the feet.</summary>
    private const float TakeOffAmplitude = 0.35f;

    /// <summary>A pal jump's boost (CharacterMovement.cs:625) is the strong version.</summary>
    private const float BoostAmplitude = 0.75f;

    private const float CrouchAmplitude = 0.15f;
    private const float FootingAmplitude = 0.3f;

    private static readonly StaminaOutCue StaminaOut = new();
    private static readonly FootingCue Footing = new();
    private static readonly Heartbeat Beat = new();
    private static readonly Shiver Chill = new();

    /// <summary>Head this close (m) to rock: the wall is felt before the view would fade (RigDriver).</summary>
    private const float WallNear = 0.3f;

    /// <summary>A further approach this much closer (m) ticks again.</summary>
    private const float WallStep = 0.03f;

    /// <summary>Seconds of the low-gravity end countdown (Affliction_LowGravity.cs:44 warns 1.5 s early).</summary>
    private const float FloatEndWarning = 1.5f;

    private static readonly Vector3[] WallProbes =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.right,
        Vector3.left,
    };

    private static AccessTools.FieldRef<CharacterMovement, Character> owner;
    private static AccessTools.FieldRef<CharacterMovement, float> fallDamageTime;

    private static Character tracked;
    private static bool wasCrouching;
    private static readonly SlideCue Slide = new();

    /// <summary>Seconds a hand's own refusal keeps priority over a body refusal (its ring is still up).</summary>
    private const float OwnCueSeconds = 1.5f;
    private static int jumps;
    private static float lastPassOut;
    private static int refusedFrame = -10;
    private static float wallNearest = float.PositiveInfinity;
    private static float lastBeatAt = -1f;

    /// <summary>Jumps the local body took off (self-test).</summary>
    internal static int TakeOffs => jumps;

    /// <summary>Thump amplitude of the last landing (self-test).</summary>
    internal static float LastLanding { get; private set; }

    /// <summary>Landings felt (self-test).</summary>
    internal static int Landings { get; private set; }

    /// <summary>Heartbeats felt (lub only) and the gap before the last one (self-test).</summary>
    internal static int Beats { get; private set; }

    internal static float LastBeatPeriod { get; private set; }

    /// <summary>Shiver pulses felt (self-test).</summary>
    internal static int Shivers { get; private set; }

    /// <summary>Air jumps (double-jump amulet charges) spent (self-test).</summary>
    internal static int AirJumps { get; private set; }

    /// <summary>The game's fall damage time (CharacterMovement.cs:128), 1.5 s in this build.</summary>
    internal static float FallDamageTime(Character c) =>
        fallDamageTime != null && c.refs.movement != null ? fallDamageTime(c.refs.movement) : 1.5f;

    /// <summary>Resolves the private members; called from <see cref="LocomotionFeature"/>'s Install.</summary>
    public static void Resolve()
    {
        owner = GameMember.Field<CharacterMovement, Character>("character");
        fallDamageTime = GameMember.Field<CharacterMovement, float>("fallDamageTime");
    }

    private static bool Mine(CharacterMovement m, out Character c)
    {
        c = VRSession.Active && owner != null ? owner(m) : null;
        return c != null && c.IsLocal;
    }

    private static bool DominantLeft => VRInput.LeftHanded;

    private static Vector3 Feet(Character c) =>
        c.data.isGrounded ? c.data.groundPos : c.Center + Vector3.down * 0.9f;

    /// <summary>A non-refusal feel at a chosen strength, recorded like ui.affordance's own.</summary>
    internal static void Feel(bool left, CueFeel feel, float amplitude, float seconds)
    {
        if (!Affordance.Enabled)
            return;
        bool delivered = Haptics.Pulse(
            left,
            amplitude,
            seconds,
            feel == CueFeel.Danger ? HapticPriority.Alert : HapticPriority.Cue
        );
        CueLog.Capture.Feel(left, feel, amplitude, delivered, Haptics.Muted, Time.unscaledTime);
    }

    internal static void FeelBoth(CueFeel feel, float amplitude, float seconds)
    {
        Feel(true, feel, amplitude, seconds);
        Feel(false, feel, amplitude, seconds);
    }

    /// <summary>Per frame from <see cref="LocomotionDriver"/>: the polled cues.</summary>
    public static void Tick(Character c)
    {
        if (!Affordance.Enabled || c == null || c.data.dead)
        {
            tracked = null;
            return;
        }
        var d = c.data;
        if (c != tracked)
        {
            tracked = c;
            wasCrouching = d.isCrouching;
        }

        // A6: the crouch latching or letting go, so a half-crouched or seated player knows it flipped.
        if (d.isCrouching != wasCrouching)
            FeelBoth(CueFeel.Contact, CrouchAmplitude, 0.03f);
        wasCrouching = d.isCrouching;

        // F1: control back after a ragdoll.
        if (Footing.Update(d.currentRagdollControll) && d.fullyConscious)
            FeelBoth(CueFeel.Contact, FootingAmplitude, 0.06f);

        if (d.fullyPassedOut || d.passedOut)
        {
            Beat.Reset();
            return;
        }
        var feet = Feet(c);
        BodyBeat(c);
        NearWall(c);

        // G9: low gravity about to end (Affliction_LowGravity.cs:42-46 warns 1.5 s early): progress ticks fill
        // toward the moment gravity returns. Polled, not patched: nothing to miss if the warning call is inlined.
        if (
            c.refs.afflictions.HasAfflictionType(
                Peak.Afflictions.Affliction.AfflictionType.LowGravity,
                out var low
            )
        )
        {
            float left = low.totalTime - low.timeElapsed;
            if (left > 0f && left < FloatEndWarning)
            {
                float u = BodyCues.CountdownUrgency(left, FloatEndWarning);
                Affordance.Progress(true, u);
                Affordance.Progress(false, u);
            }
        }

        // A7: a jump press the game turned down (see JumpTry), unless it became a climb grab.
        if (refusedFrame >= 0 && Time.frameCount > refusedFrame)
        {
            if (!d.isClimbingAnything && Time.frameCount - refusedFrame <= 3)
            {
                if (BodyHand(DominantLeft, out bool hand))
                    Affordance.Deny(hand, feet, DenyReason.TooSoon);
            }
            refusedFrame = -10;
        }

        // F3a: the status sum is over the top and passOutValue climbs to 1 over 5 s (Character.cs:1040-1060).
        bool rising = d.passOutValue > lastPassOut;
        lastPassOut = d.passOutValue;
        if (rising && d.passOutValue > 0.02f)
        {
            Affordance.Danger(true, c.Center, d.passOutValue);
            Affordance.Danger(false, c.Center, d.passOutValue);
        }

        // A11: falling long enough to get hurt (CharacterMovement.cs:825-875): danger builds before the damage.
        if (!d.isGrounded && !d.isClimbingAnything && c.refs.movement != null)
        {
            float u = BodyCues.FallUrgency(c.refs.movement.FallTime(), FallDamageTime(c));
            if (u > 0f)
            {
                Affordance.Danger(true, feet, u);
                Affordance.Danger(false, feet, u);
            }
        }

        // A12: ground steeper than the game's max angle resets sinceFallSlide (CharacterMovement.cs:901-914), but
        // so does a steep edge under feet that stand (run 34: a beach lip during vr.63). Only a real slide (off the
        // ground, moving down, for 0.2 s) slips the feet, once.
        if (
            Slide.Update(
                d.sinceFallSlide < 0.1f && !d.isClimbingAnything,
                d.isGrounded,
                -d.avarageVelocity.y,
                Time.deltaTime
            ) && BodyHand(DominantLeft, out bool slipHand)
        )
            Affordance.Deny(slipHand, feet, DenyReason.TooSteep);
    }

    /// <summary>
    /// The hand that shows a body refusal (<see cref="BodyCueHand"/>): one that is not holding the item or
    /// gripping or pulling a trigger, and has no refusal of its own up, so a body cue never replaces the ring of what
    /// that hand is doing. False when both hands are busy and the preferred one's own cue is showing.
    /// </summary>
    internal static bool BodyHand(bool preferLeft, out bool left)
    {
        float now = Time.unscaledTime;
        return BodyCueHand.Pick(
            preferLeft,
            Busy(true),
            Busy(false),
            now - CueLog.Capture.Hand(true).ReasonAt < OwnCueSeconds,
            now - CueLog.Capture.Hand(false).ReasonAt < OwnCueSeconds,
            out left
        );
    }

    private static bool Busy(bool left)
    {
        var c = Character.localCharacter;
        ref var hand = ref VRInput.Hand(left);
        bool holds =
            c != null && c.data.currentItem != null && Avatar.LocalHold.ItemInLeftHand == left;
        return holds || hand.Grip > 0.5f || hand.Trigger > 0.5f;
    }

    /// <summary>
    /// B1/B5: the body breathing. A lub-dub heartbeat while stamina is spent off the wall (climbing has its own),
    /// faster as the bar empties, slower and heavier under the Weight status; fine shivers while Cold rises.
    /// Plain haptics on both hands, not a refusal.
    /// </summary>
    private static void BodyBeat(Character c)
    {
        var d = c.data;
        var a = c.refs.afflictions;
        float dt = Time.deltaTime;
        bool exerting =
            BodyGestures.HeartbeatOn
            && d.sinceUseStamina < 0.5f
            && !d.isClimbingAnything
            && !c.infiniteStam;
        float max = Mathf.Max(c.GetMaxStamina(), 0.05f);
        float weight = a.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Weight);
        if (Beat.Update(exerting, d.currentStamina / max, weight, dt, out var pulse))
        {
            Haptics.Pulse(true, pulse.Amplitude, pulse.Seconds, HapticPriority.Ambient);
            Haptics.Pulse(false, pulse.Amplitude, pulse.Seconds, HapticPriority.Ambient);
            if (pulse.Beat)
            {
                float now = Time.unscaledTime;
                LastBeatPeriod = lastBeatAt > 0f ? now - lastBeatAt : 0f;
                lastBeatAt = now;
                Beats++;
            }
        }
        float shiver = Chill.Update(
            BodyGestures.ShiverOn ? a.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) : 0f,
            dt
        );
        if (shiver > 0f)
        {
            Haptics.Pulse(true, shiver, 0.03f, HapticPriority.Ambient);
            Haptics.Pulse(false, shiver, 0.03f, HapticPriority.Ambient);
            Shivers++;
        }
    }

    /// <summary>
    /// A2: the real head coming within <see cref="WallNear"/> of rock, before RigDriver's fade would black the view:
    /// a soft contact tick on the hand on that side (both for ahead or behind), again for each further approach.
    /// Only the hands react; the view never moves. Quiet while climbing, where the head is at the rock on purpose.
    /// </summary>
    private static void NearWall(Character c)
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (
            head == null
            || !BodyGestures.WallTouchOn
            || c.data.isClimbingAnything
            || c.data.currentClimbHandle != null
        )
        {
            wallNearest = float.PositiveInfinity;
            return;
        }
        var eye = head.position;
        var mask = HelperFunctions.terrainMapMask;
        if (!Physics.CheckSphere(eye, WallNear, mask, QueryTriggerInteraction.Ignore))
        {
            wallNearest = float.PositiveInfinity;
            return;
        }
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        float best = float.PositiveInfinity;
        int side = 0;
        for (int i = 0; i < WallProbes.Length; i++)
        {
            if (
                Physics.Raycast(
                    eye,
                    yaw * WallProbes[i],
                    out var hit,
                    WallNear,
                    mask,
                    QueryTriggerInteraction.Ignore
                )
                && hit.distance < best
            )
            {
                best = hit.distance;
                side = i;
            }
        }
        if (float.IsPositiveInfinity(best) || best > wallNearest - WallStep)
        {
            if (float.IsPositiveInfinity(best))
                wallNearest = float.PositiveInfinity;
            return;
        }
        wallNearest = best;
        float amplitude = Mathf.Lerp(0.08f, 0.3f, 1f - best / WallNear);
        if (side != 3)
            Feel(false, CueFeel.Contact, amplitude, 0.03f);
        if (side != 2)
            Feel(true, CueFeel.Contact, amplitude, 0.03f);
    }

    /// <summary>
    /// CharacterMovement.SetMovementState (CharacterMovement.cs:231, private): sets <c>isSprinting</c> and clears it
    /// with the toggle when regular stamina is out (:263-271). Running out mid-sprint or right after a jump, or
    /// asking to sprint with an empty bar, gives the stamina refusal at the wrist hand: bar red, wrist raised.
    /// </summary>
    [HarmonyPatch]
    internal static class SprintState
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), "SetMovementState");

        private static void Prefix(CharacterMovement __instance, out bool __state) =>
            __state = Mine(__instance, out var c) && c.data.isSprinting;

        private static void Postfix(CharacterMovement __instance, bool __state)
        {
            if (!Mine(__instance, out var c) || !Affordance.Enabled)
                return;
            var d = c.data;
            var input = c.input;
            bool empty = !c.infiniteStam && d.currentStamina < 0.005f;
            bool body = !d.isClimbingAnything && d.fullyConscious;
            bool wanted =
                body
                && d.isGrounded
                && input.movementInput.y > 0.01f
                && (input.sprintIsPressed || input.sprintToggleWasPressed);
            bool exerting = body && (__state || d.sinceJump < 0.5f);
            if (StaminaOut.Update(empty, exerting, wanted, Time.unscaledTime))
            {
                if (BodyHand(!DominantLeft, out bool hand))
                    Affordance.DenyStamina(hand);
            }
        }
    }

    /// <summary>
    /// CharacterMovement.TryToJump (CharacterMovement.cs:409, private): a press the game turns down (past the 0.2 s
    /// coyote time, within 0.3 s of the last jump) sends no JumpRpc. That refusal gets TooSoon at the feet (next
    /// frame, in <see cref="Tick"/>), so
    /// mashing jump in the air feels different from a jump. Climbing, a handle, struggling, passed out, a jetpack
    /// and a jump still charging are not refusals of a jump and stay quiet.
    /// </summary>
    [HarmonyPatch]
    internal static class JumpTry
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), "TryToJump");

        private static void Prefix(out int __state) => __state = jumps;

        private static void Postfix(CharacterMovement __instance, int __state)
        {
            if (jumps != __state || !Mine(__instance, out var c))
                return;
            var d = c.data;
            if (
                d.fullyPassedOut
                || !d.fullyConscious
                || d.isClimbingAnything
                || d.currentClimbHandle != null
                || d.chargingJump
                || c.refs.afflictions.isStruggling
                || (__instance.hasJetpack && !d.isGrounded)
            )
                return;
            // Judged next frame: the same press may start a jump-to-climb grab (CharacterClimbing.cs:68-78).
            refusedFrame = Time.frameCount;
        }
    }

    /// <summary>CharacterMovement.JumpRpc(bool) (CharacterMovement.cs:620, [PunRPC], runs at once on the sender):
    /// the local body takes off; a pal jump boost is the strong tick.</summary>
    [HarmonyPatch]
    internal static class TakeOff
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), "JumpRpc", new[] { typeof(bool) });

        // JumpRpc spends a ground jump first, then an amulet charge (CharacterMovement.cs:634-644).
        private static void Prefix(CharacterMovement __instance, out bool __state) =>
            __state =
                Mine(__instance, out var c) && c.data.jumpsRemaining <= 0 && c.data.extraJumps > 0;

        private static void Postfix(CharacterMovement __instance, bool isPalJump, bool __state)
        {
            if (!Mine(__instance, out _))
                return;
            jumps++;
            if (__state)
            {
                // A9: an air jump is a double tick, so a spent charge is felt apart from a jump.
                AirJumps++;
                FeelBoth(CueFeel.Contact, BoostAmplitude, 0.03f);
                PeakXR.UI.Affordances.Affordance.Play(
                    CueSound.WouldWork,
                    __instance.transform.position,
                    0.6f
                );
                return;
            }
            FeelBoth(CueFeel.Contact, isPalJump ? BoostAmplitude : TakeOffAmplitude, 0.04f);
        }
    }

    /// <summary>CharacterMovement.Land (CharacterMovement.cs:812, private): after half a second in the air, a thump
    /// on both hands scaled by the impact speed (the same velocity fall damage reads, :848).</summary>
    [HarmonyPatch]
    internal static class Landing
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), "Land");

        private static void Prefix(CharacterMovement __instance)
        {
            if (!Mine(__instance, out var c) || !(c.data.sinceGrounded > 0.5f))
                return;
            var d = c.data;
            float speed = Mathf.Max(-d.avarageLastFrameVelocity.y, -d.avarageVelocity.y);
            LastLanding = BodyCues.LandingAmplitude(speed);
            Landings++;
            FeelBoth(CueFeel.Contact, LastLanding, 0.06f + 0.08f * LastLanding);
        }
    }
}
