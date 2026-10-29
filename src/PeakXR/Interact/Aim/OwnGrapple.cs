using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Aim;

/// <summary>
/// The local VR player's own rescue-hook pull to a wall or the air (<see cref="GrapplePull"/>). rig.camera and
/// comfort.events read <see cref="Covers"/>: the game's fall for the pull (RescueHook.cs:407) and the launch-speed
/// rise of the pull (:174-194) are the player's own aimed move, so they no longer detach the view into the
/// tumble or launch chase view, which also took the hands off the body until it landed. Peers see vanilla: this
/// only reads the hook's own RPCs.
/// </summary>
internal static class OwnGrapple
{
    private static readonly GrapplePull Pull = new();

    /// <summary>The local body's tumble and speed now come from its own hook pull or the recovery right after.</summary>
    internal static bool Covers(Character c) =>
        c != null && c.IsLocal && Pull.Covers(Time.time, c.data.fallSeconds > 0f);

    private static bool Mine(RescueHook hook) =>
        VRSession.Active
        && hook != null
        && hook.item != null
        && hook.item.holderCharacter != null
        && hook.item.holderCharacter.IsLocal;

    /// <summary>RescueHook.RPCA_RescueWall(bool, Vector3) (RescueHook.cs:398, [PunRPC] on every client): the
    /// holder's pull toward the hit (or the air) starts, with <c>Fall(selfFallSeconds)</c> (:407).</summary>
    [HarmonyPatch]
    internal static class RescueWall
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(RescueHook),
                nameof(RescueHook.RPCA_RescueWall),
                [typeof(bool), typeof(Vector3)]
            );

        private static void Postfix(RescueHook __instance)
        {
            if (Mine(__instance))
                Pull.Shot(Time.time);
        }
    }

    /// <summary>RescueHook.RPCA_LetGo (RescueHook.cs:362, private; called directly from FixedUpdate :203 for a wall
    /// pull, as an RPC :169 for a friend pull): clears the holder's fall timer (:372-375).</summary>
    [HarmonyPatch]
    internal static class LetGo
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(RescueHook), "RPCA_LetGo");

        private static void Postfix(RescueHook __instance)
        {
            if (Mine(__instance))
                Pull.LetGo(Time.time);
        }
    }
}
