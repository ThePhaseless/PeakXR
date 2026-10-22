using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
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

    /// <summary>A pal jump's boost (CharacterMovement.cs:631) is the strong version.</summary>
    private const float BoostAmplitude = 0.75f;

    private const float CrouchAmplitude = 0.15f;
    private const float FootingAmplitude = 0.3f;

    private static readonly StaminaOutCue StaminaOut = new();
    private static readonly FootingCue Footing = new();

    private static AccessTools.FieldRef<CharacterMovement, Character> owner;
    private static AccessTools.FieldRef<CharacterMovement, float> fallDamageTime;

    private static Character tracked;
    private static bool wasCrouching;
    private static bool wasSliding;
    private static int jumps;
    private static float lastPassOut;
    private static int refusedFrame = -10;

    /// <summary>Jumps the local body took off (self-test).</summary>
    internal static int TakeOffs => jumps;

    /// <summary>Thump amplitude of the last landing (self-test).</summary>
    internal static float LastLanding { get; private set; }

    /// <summary>Landings felt (self-test).</summary>
    internal static int Landings { get; private set; }

    /// <summary>The game's fall damage time (CharacterMovement.cs:126), 1.5 s in this build.</summary>
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
    private static void Feel(bool left, CueFeel feel, float amplitude, float seconds)
    {
        if (!Affordance.Enabled)
            return;
        bool delivered = Haptics.Pulse(left, amplitude, seconds);
        CueLog.Capture.Feel(left, feel, amplitude, delivered, Haptics.Muted, Time.unscaledTime);
    }

    private static void FeelBoth(CueFeel feel, float amplitude, float seconds)
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
            wasSliding = false;
        }

        // A6: the crouch latching or letting go, so a half-crouched or seated player knows it flipped.
        if (d.isCrouching != wasCrouching)
            FeelBoth(CueFeel.Contact, CrouchAmplitude, 0.03f);
        wasCrouching = d.isCrouching;

        // F1: control back after a ragdoll.
        if (Footing.Update(d.currentRagdollControll) && d.fullyConscious)
            FeelBoth(CueFeel.Contact, FootingAmplitude, 0.06f);

        if (d.fullyPassedOut || d.passedOut)
            return;
        var feet = Feet(c);

        // A7: a jump press the game turned down (see JumpTry), unless it became a climb grab.
        if (refusedFrame >= 0 && Time.frameCount > refusedFrame)
        {
            if (!d.isClimbingAnything && Time.frameCount - refusedFrame <= 3)
                Affordance.Deny(DominantLeft, feet, DenyReason.TooSoon);
            refusedFrame = -10;
        }

        // F3a: the status sum is over the top and passOutValue climbs to 1 over 5 s (Character.cs:1043-1063).
        bool rising = d.passOutValue > lastPassOut;
        lastPassOut = d.passOutValue;
        if (rising && d.passOutValue > 0.02f)
        {
            Affordance.Danger(true, c.Center, d.passOutValue);
            Affordance.Danger(false, c.Center, d.passOutValue);
        }

        // A11: falling long enough to get hurt (CharacterMovement.cs:830-880): danger builds before the damage.
        if (!d.isGrounded && !d.isClimbingAnything && c.refs.movement != null)
        {
            float u = BodyCues.FallUrgency(c.refs.movement.FallTime(), FallDamageTime(c));
            if (u > 0f)
            {
                Affordance.Danger(true, feet, u);
                Affordance.Danger(false, feet, u);
            }
        }

        // A12: ground steeper than the game's max angle resets sinceFallSlide every frame on it
        // (CharacterMovement.cs:906-917): the feet slip, once per slide.
        bool sliding = d.sinceFallSlide < 0.1f && !d.isClimbingAnything;
        if (sliding && !wasSliding)
        {
            Affordance.Deny(true, feet, DenyReason.TooSteep);
            Affordance.Deny(false, feet, DenyReason.TooSteep);
        }
        wasSliding = sliding;
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
                Affordance.DenyStamina(!DominantLeft);
        }
    }

    /// <summary>
    /// CharacterMovement.TryToJump (CharacterMovement.cs:415, private): a press the game turns down (past the 0.2 s
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

    /// <summary>CharacterMovement.JumpRpc(bool) (CharacterMovement.cs:626, [PunRPC], runs at once on the sender):
    /// the local body takes off; a pal jump boost is the strong tick.</summary>
    [HarmonyPatch]
    internal static class TakeOff
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), "JumpRpc", new[] { typeof(bool) });

        private static void Postfix(CharacterMovement __instance, bool isPalJump)
        {
            if (!Mine(__instance, out _))
                return;
            jumps++;
            FeelBoth(CueFeel.Contact, isPalJump ? BoostAmplitude : TakeOffAmplitude, 0.04f);
        }
    }

    /// <summary>CharacterMovement.Land (CharacterMovement.cs:817, private): after half a second in the air, a thump
    /// on both hands scaled by the impact speed (the same velocity fall damage reads, :853).</summary>
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
