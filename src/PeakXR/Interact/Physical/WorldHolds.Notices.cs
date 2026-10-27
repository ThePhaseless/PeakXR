using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Things the game does on its own that the hands should feel, each a notice (a contact tick on both hands and the
/// HappenedHere notice at the spot: a pin in a teal ring and the notice note), never a refusal:
/// <list type="bullet">
/// <item>Scout cannon (WS-C1): from the light to the shot (<c>fireTime</c>, ScoutCannon.cs:170-182) a rising
/// progress ramp on both hands of anyone within 4 m; standing by it unlaunched when it fires gets the notice at
/// the barrel's entry.</item>
/// <item>Checkpoint flag (WS-D6): a death the flag saved (Character.TryCheckpoint, Character.cs:641) gets the notice
/// at the flag once the warp has landed.</item>
/// <item>Summit flare (WS-R12): the helicopter summoned by your held flare (Flare.TriggerHelicopter, Flare.cs:43)
/// gets the notice at the flare; the last 10 s of the summit countdown tick on the wrist hand.</item>
/// </list>
/// </summary>
internal static partial class WorldHolds
{
    private const float CannonNear = 4f;

    /// <summary>After the cannon's shot, how long to wait for this player's launch before the notice.</summary>
    private const float LaunchGrace = 0.5f;

    /// <summary>The checkpoint warp fades (ui.runflow); the notice waits for it.</summary>
    private const float FlagNoticeDelay = 0.6f;

    private const int CountdownTicks = 10;

    internal static AccessTools.FieldRef<ScoutCannon, Transform> CannonEntry;

    private static ScoutCannon litCannon;
    private static float cannonLitAt;
    private static bool launched;
    private static bool flagPending;
    private static Vector3 flagAt;
    private static float flagNoticeAt;

    /// <summary>Notices given so far, for the self-test.</summary>
    internal static int Notices { get; private set; }

    private static void TickNotices(Character c)
    {
        float now = Time.time;
        if (litCannon != null)
        {
            var entry = CannonEntry(litCannon);
            var at = entry != null ? entry.position : litCannon.transform.position;
            bool near = (c.Center - at).sqrMagnitude < CannonNear * CannonNear;
            float t = now - cannonLitAt;
            float fire = Mathf.Max(0.1f, litCannon.fireTime);
            if (t < fire)
            {
                if (near)
                {
                    Affordance.Progress(true, t / fire, hold: false);
                    Affordance.Progress(false, t / fire, hold: false);
                }
            }
            else if (t >= fire + LaunchGrace)
            {
                if (near && !launched)
                    Notice(at);
                litCannon = null;
            }
        }
        if (flagPending && now >= flagNoticeAt)
        {
            flagPending = false;
            Notice(flagAt);
        }
    }

    private static void Notice(Vector3 at)
    {
        Notices++;
        Affordance.Contact(true);
        Affordance.Contact(false);
        Affordance.Inform(VRInput.OffHandLeft, at, DenyReason.HappenedHere, default);
    }

    /// <summary>ScoutCannon.RPCA_Light (ScoutCannon.cs:170, PunRPC on every client): the fuse caught; the shot
    /// follows after <c>fireTime</c>.</summary>
    [HarmonyPatch]
    internal static class CannonLit
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(ScoutCannon), "RPCA_Light");

        private static void Postfix(ScoutCannon __instance)
        {
            if (!VRSession.Active)
                return;
            litCannon = __instance;
            cannonLitAt = Time.time;
            launched = false;
        }
    }

    /// <summary>ScoutCannon.RPCA_LaunchTarget(int) (ScoutCannon.cs:268, PunRPC on every client): a character goes
    /// out of the barrel.</summary>
    [HarmonyPatch]
    internal static class CannonLaunch
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(ScoutCannon), "RPCA_LaunchTarget", new[] { typeof(int) });

        private static void Postfix(int targetID)
        {
            var c = Character.localCharacter;
            if (c != null && c.photonView != null && c.photonView.ViewID == targetID)
                launched = true;
        }
    }

    /// <summary>Character.TryCheckpoint (Character.cs:641): the prefix notes the flag (WillDoCheckpoint, :623) before
    /// it is destroyed; the postfix schedules the notice when it saved the local player.</summary>
    [HarmonyPatch]
    internal static class CheckpointUsed
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Character), "TryCheckpoint", System.Type.EmptyTypes);

        private static void Prefix(Character __instance)
        {
            if (VRSession.Active && __instance.IsLocal && __instance.WillDoCheckpoint(out var flag))
                flagAt = flag.transform.position;
        }

        private static void Postfix(Character __instance, bool __result)
        {
            if (!__result || !VRSession.Active || !__instance.IsLocal)
                return;
            flagPending = true;
            flagNoticeAt = Time.time + FlagNoticeDelay;
        }
    }

    /// <summary>Flare.TriggerHelicopter (Flare.cs:43, PunRPC on every client): sent by the flare its holder lit at
    /// the peak (Flare.cs:36-38). Every client's Flare.Update sends it until its own PeakHandler.summonedHelicopter
    /// is set (Flare.cs:36, set by SummonHelicopter, PeakHandler.cs:61), so it arrives once per lobby player: only the
    /// first call, the one that summons, gets the notice.</summary>
    [HarmonyPatch]
    internal static class FlareSummon
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Flare), "TriggerHelicopter");

        private static void Prefix(out bool __state)
        {
            var peak = Zorro.Core.Singleton<PeakHandler>.Instance;
            __state = peak != null && peak.summonedHelicopter;
        }

        private static void Postfix(Flare __instance, bool __state)
        {
            var item = __instance.item;
            if (
                __state
                || !VRSession.Active
                || item == null
                || item.holderCharacter == null
                || !item.holderCharacter.IsLocal
            )
                return;
            Notices++;
            Affordance.Contact(LocalHold.ItemInLeftHand);
            Affordance.Inform(
                LocalHold.ItemInLeftHand,
                item.Center(),
                DenyReason.HappenedHere,
                default
            );
        }
    }

    /// <summary>PeakSequence.RPCUpdateTimer(int, bool) (PeakSequence.cs:166, private PunRPC): one countdown second;
    /// the last ten tick on the wrist hand.</summary>
    [HarmonyPatch]
    internal static class SummitCountdown
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(PeakSequence),
                "RPCUpdateTimer",
                new[] { typeof(int), typeof(bool) }
            );

        private static void Postfix(PeakSequence __instance, int seconds)
        {
            int left = __instance.totalSeconds - seconds;
            if (!VRSession.Active || seconds < 0 || left <= 0 || left > CountdownTicks)
                return;
            var ui = PeakXR.UI.World.UIWorldFeature.Settings;
            Affordance.Contact(ui == null || ui.WristLeft);
        }
    }
}
