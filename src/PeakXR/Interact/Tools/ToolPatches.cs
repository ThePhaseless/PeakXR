using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>Harmony patches of <see cref="ToolsFeature"/>. All act on the local VR player only.</summary>
internal static class ToolPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal; scroll read at :233-237): adds twist
    /// and pull gestures to the scroll fields. RopeSpool.Update reads scrollForward/BackwardIsPressed and
    /// scrollInput (RopeSpool.cs:113-124); CharacterItems.DoUsing turns scrollForward/BackwardWasPressed into
    /// Item.OnScrollForward/BackwardPressed (CharacterItems.cs:229-236), which the scout cannon's angle action
    /// subscribes to (Action_ConstructableScoutCannonScroll.cs:12-49). Runs after interact.physical.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.VeryLow)]
        private static void Postfix(CharacterInput __instance, bool playerMovementActive)
        {
            if (!VRSession.Active)
                return;
            var c = Character.localCharacter;
            if (c != null && c.input == __instance)
                ToolInput.Tick(c, __instance, playerMovementActive);
        }
    }

    /// <summary>CharacterItems.UpdateClimbingSpikeUse (CharacterItems.cs:981, private): while a spike cast runs
    /// (climbingSpikeTick &gt; 0) the game adds the frame time and places the spike at climbingSpikeHit once the
    /// timer reaches the slot prefab's usingTimePrimary (CharacterItems.cs:987-1004). The prefix sets the timer
    /// from the strikes landed so far, so only the last strike completes the cast; the button-held and range
    /// checks before it stay vanilla. The postfix (camera still at the aiming hand: interact.aim restores it in a
    /// finalizer) feeds <see cref="SpikeCue"/> the range cue, a refused press and a cast lost to range
    /// (currentClimbingSpikeComponent, CharacterItems.cs:66).</summary>
    [HarmonyPatch]
    internal static class SpikeUse
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "UpdateClimbingSpikeUse");

        private static void Prefix(
            Character ___character,
            ref float ___climbingSpikeTick,
            ItemSlot ___currentClimbingSpikeItemSlot,
            RaycastHit ___climbingSpikeHit,
            out float __state
        )
        {
            SpikeHammer.BeforeUpdate(
                ___character,
                ref ___climbingSpikeTick,
                ___currentClimbingSpikeItemSlot,
                in ___climbingSpikeHit
            );
            __state = ___climbingSpikeTick;
        }

        private static void Postfix(
            Character ___character,
            float ___climbingSpikeTick,
            ItemSlot ___currentClimbingSpikeItemSlot,
            ClimbingSpikeComponent ___currentClimbingSpikeComponent,
            RaycastHit ___climbingSpikeHit,
            float __state
        ) =>
            SpikeCue.After(
                ___character,
                __state,
                ___climbingSpikeTick,
                ___currentClimbingSpikeItemSlot,
                ___currentClimbingSpikeComponent,
                in ___climbingSpikeHit
            );
    }

    /// <summary>CharacterMovement.ApplyGlider (CharacterMovement.cs:374, public, called from Glider.FixedUpdate,
    /// Glider.cs:72-76): pushes every ragdoll part along the flattened hip forward with soarForwardForce
    /// (CharacterMovement.cs:380-388). The postfix adds the difference to the hand heading, so the net push follows
    /// the arms. Local physics only; nothing synced changes and the camera is never touched.</summary>
    [HarmonyPatch]
    internal static class Glide
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterMovement), nameof(CharacterMovement.ApplyGlider));

        private static void Postfix(CharacterMovement __instance, float soarForwardForce) =>
            GliderHands.AfterApply(__instance, soarForwardForce);
    }
}
