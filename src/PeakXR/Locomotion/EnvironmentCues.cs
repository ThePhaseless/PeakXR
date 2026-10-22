using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// What the world does to the local body, felt in the hands: a bounce shroom's spring, a lava sting, a balloon tied
/// or popped, the parachute's tug, the sugar crash and a bridge trembling before it breaks. Local haptics, cue
/// sounds and rings only; nothing goes over the network, the view never moves.
/// </summary>
internal static class EnvironmentCues
{
    private static AccessTools.FieldRef<CollisionModifier, float> maxBounce;
    private static AccessTools.FieldRef<
        CollisionModifier,
        System.Collections.Generic.List<Character>
    > held;
    private static AccessTools.FieldRef<CharacterBalloons, Character> balloonOwner;
    private static AccessTools.FieldRef<CharacterBalloons, bool> parachuteOpen;
    private static AccessTools.FieldRef<BreakableBridge, bool> bridgeBreaking;
    private static AccessTools.FieldRef<BreakableBridge, bool> bridgeFallen;
    private static AccessTools.FieldRef<BreakableBridge, float> bridgeTimer;

    private static BreakableBridge bridge;
    private static float bridgeTouch = -10f;

    /// <summary>Spring amplitude of the last bounce (self-test).</summary>
    internal static float LastBounce { get; private set; }

    /// <summary>Counts per cue (self-test).</summary>
    internal static int Bounces { get; private set; }

    internal static int Stings { get; private set; }
    internal static int Tugs { get; private set; }
    internal static int Crashes { get; private set; }
    internal static int Pops { get; private set; }

    /// <summary>The bridge the local body last stood on, while it trembles (self-test).</summary>
    internal static BreakableBridge Bridge => bridge;

    /// <summary>Starts a bridge's break countdown as its owner would (BreakableBridge.cs:186-189): self-test only.</summary>
    internal static AccessTools.FieldRef<BreakableBridge, bool> BridgeBreaking => bridgeBreaking;

    /// <summary>BreakableBridge.timeUntilBreak (BreakableBridge.cs:70): self-test only.</summary>
    internal static AccessTools.FieldRef<BreakableBridge, float> BridgeTimer => bridgeTimer;

    public static void Resolve()
    {
        maxBounce = GameMember.Field<CollisionModifier, float>("maxBounce");
        held = GameMember.Field<CollisionModifier, System.Collections.Generic.List<Character>>(
            "characterList"
        );
        balloonOwner = GameMember.Field<CharacterBalloons, Character>("character");
        parachuteOpen = GameMember.Field<CharacterBalloons, bool>("_isParachuteOpen");
        bridgeBreaking = GameMember.Field<BreakableBridge, bool>("isBreaking");
        bridgeFallen = GameMember.Field<BreakableBridge, bool>("isFallen");
        bridgeTimer = GameMember.Field<BreakableBridge, float>("timeUntilBreak");
    }

    private static bool Local(Character c) => VRSession.Active && c != null && c.IsLocal;

    /// <summary>Per frame from <see cref="LocomotionDriver"/>: the bridge tremor (BM-D10). Breaking counts down
    /// 2.5-7.5 s (:186-189), then the bridge shakes for <c>fallTime</c> (:195) and falls (:250): a danger cue at
    /// the feet that grows from the countdown into the shake, while the body is still on it.</summary>
    public static void Tick(Character c)
    {
        if (bridge == null || c == null || !Affordance.Enabled)
            return;
        bool fallen = bridgeFallen(bridge);
        bool breaking = bridgeBreaking(bridge) || bridge.isShaking;
        bool onIt = Time.time - bridgeTouch < 1f;
        if (fallen || !onIt)
        {
            if (fallen || Time.time - bridgeTouch > 10f)
                bridge = null;
            return;
        }
        if (!breaking)
            return;
        float u = bridge.isShaking ? 0.8f : 0.35f;
        var feet = c.data.isGrounded ? c.data.groundPos : c.Center;
        Affordance.Danger(true, feet, u);
        Affordance.Danger(false, feet, u);
    }

    /// <summary>
    /// CollisionModifier.Collide(Character, ContactPoint, Collision, Bodypart) (CollisionModifier.cs:69), run for
    /// every body-part contact (CharacterMovement.OnCollision, :944-952): the prefix applies the bounce's own gate
    /// (:72, :92: effects on, not held from the last hit, knockback, contact side, 0.2 s since the last bounce,
    /// standing when it must be) and, when it bounces, a spring on both hands scaled by the game's bounce multiplier
    /// (:94-103), so a long drop onto a shroom springs harder. Collide, not the small bounce-event method it ends
    /// with: run 40's pad bounced but that patch never ran (most likely inlined).
    /// </summary>
    [HarmonyPatch]
    internal static class Bounce
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CollisionModifier),
                nameof(CollisionModifier.Collide),
                new[]
                {
                    typeof(Character),
                    typeof(ContactPoint),
                    typeof(Collision),
                    typeof(Bodypart),
                }
            );

        private static void Prefix(
            CollisionModifier __instance,
            Character character,
            ContactPoint contactPoint
        )
        {
            if (__instance.knockback <= 0f || !__instance.applyEffects || !Local(character))
                return;
            var d = character.data;
            var contact = contactPoint;
            if (
                held(__instance).Contains(character)
                || !(
                    Vector3.Dot(contact.normal, __instance.transform.forward)
                    > __instance.bounceDotMinimum
                )
                || !(d.lastBouncedTime + 0.2f < Time.time)
                || (
                    __instance.knockbackOnlyWhenStanding
                    && (d.isClimbingAnything || !d.fullyConscious)
                )
            )
                return;
            float max = maxBounce(__instance);
            float bounce = Mathf.Clamp(
                Mathf.Pow(
                    Mathf.Clamp(d.sinceGrounded + __instance.sinceGroundedOffset, 1f, 5f),
                    __instance.superBouncePow
                ),
                1f,
                max
            );
            // Ragdolled or about to pass out, the game bounces at 1 (:100-103).
            if (d.fallSeconds > 0f || character.refs.afflictions.shouldPassOut)
                bounce = 1f;
            LastBounce = BodyCues.BounceAmplitude(bounce, max);
            Bounces++;
            BodyFeedback.FeelBoth(CueFeel.Contact, LastBounce, 0.05f + 0.1f * LastBounce);
        }
    }

    /// <summary>Lava.HitPlayer(Character) (Lava.cs:105, private): the once-a-second burn while touching lava,
    /// always the local character (DoEffects, :89-97). A sharp sting on both hands and the danger ring.</summary>
    [HarmonyPatch]
    internal static class LavaSting
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Lava), "HitPlayer", new[] { typeof(Character) });

        private static void Prefix(Character item)
        {
            if (!Local(item))
                return;
            Stings++;
            BodyFeedback.FeelBoth(CueFeel.Danger, 0.9f, 0.06f);
            Affordance.Play(CueSound.Danger, item.Center);
        }
    }

    /// <summary>CharacterBalloons.TieNewBalloon(int) (CharacterBalloons.cs:99), run by the item's user
    /// (Action_Balloon.cs:11-17): a light lift tick on the hand holding the bunch.</summary>
    [HarmonyPatch]
    internal static class BalloonTie
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterBalloons),
                nameof(CharacterBalloons.TieNewBalloon),
                new[] { typeof(int) }
            );

        private static void Postfix(CharacterBalloons __instance)
        {
            if (Local(balloonOwner(__instance)))
                BodyFeedback.Feel(LocalHold.ItemInLeftHand, CueFeel.Contact, 0.2f, 0.03f);
        }
    }

    /// <summary>CharacterBalloons.RemoveBalloon(TiedBalloon) (CharacterBalloons.cs:108), on every client when a tied
    /// balloon goes (TiedBalloon.OnDestroy, TiedBalloon.cs:97): a pop snap on both hands for the local body.</summary>
    [HarmonyPatch]
    internal static class BalloonPop
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterBalloons),
                nameof(CharacterBalloons.RemoveBalloon),
                new[] { typeof(TiedBalloon) }
            );

        private static void Postfix(CharacterBalloons __instance)
        {
            if (!Local(balloonOwner(__instance)))
                return;
            Pops++;
            BodyFeedback.FeelBoth(CueFeel.Contact, 0.6f, 0.02f);
        }
    }

    /// <summary>CharacterBalloons.SetParachute(bool) (CharacterBalloons.cs:117): the auto-open after 1.5 s of
    /// falling (Character.cs:1122-1127) gives a strong tug on both hands, like the harness catching.</summary>
    [HarmonyPatch]
    internal static class ParachuteTug
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterBalloons),
                nameof(CharacterBalloons.SetParachute),
                new[] { typeof(bool) }
            );

        private static void Prefix(CharacterBalloons __instance, out bool __state) =>
            __state = parachuteOpen(__instance);

        private static void Postfix(CharacterBalloons __instance, bool shouldOpen, bool __state)
        {
            if (!shouldOpen || __state || !Local(balloonOwner(__instance)))
                return;
            Tugs++;
            BodyFeedback.FeelBoth(CueFeel.Contact, 0.8f, 0.15f);
        }
    }

    /// <summary>GUIManager.EndSugarRush() (GUIManager.cs:976), called only for the local character when infinite
    /// stamina ends (Affliction_InfiniteStamina.cs:136-140) and drowsiness follows: the crash, a soft falling cue.</summary>
    [HarmonyPatch]
    internal static class SugarCrash
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(GUIManager), nameof(GUIManager.EndSugarRush));

        private static void Postfix()
        {
            if (!Affordance.Enabled)
                return;
            Crashes++;
            Affordance.Cancel(true);
            Affordance.Cancel(false);
        }
    }

    /// <summary>BreakableBridge.OnBridgeCollision (BreakableBridge.cs:170, private), hooked to its planks'
    /// CollisionModifier.onCollide: the postfix runs even while breaking (the body returns early there), so the
    /// local body standing on it is known through the countdown.</summary>
    [HarmonyPatch]
    internal static class BridgeTouch
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(BreakableBridge), "OnBridgeCollision");

        private static void Postfix(BreakableBridge __instance, Character character)
        {
            if (!Local(character))
                return;
            bridge = __instance;
            bridgeTouch = Time.time;
        }
    }

    /// <summary>BreakableBridge.Fall_Rpc() (BreakableBridge.cs:250, [PunRPC] on every client): the planks go; a
    /// hard jolt if the local body was on it.</summary>
    [HarmonyPatch]
    internal static class BridgeFall
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(BreakableBridge), "Fall_Rpc");

        private static void Postfix(BreakableBridge __instance)
        {
            if (__instance == bridge && Time.time - bridgeTouch < 1f)
                BodyFeedback.FeelBoth(CueFeel.Contact, 1f, 0.12f);
        }
    }
}
