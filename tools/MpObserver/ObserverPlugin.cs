using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PeakXR.Core;
using UnityEngine;

namespace MpObserver;

/// <summary>
/// Client B of the two-client multiplayer test: a vanilla PEAK peer that hosts a lobby through the title menu and
/// records what it sees of the PeakXR player. It sends nothing a vanilla player could not: it clicks the game's own
/// menu and turns its own camera, and on the VR side's request (<see cref="MpRequest"/>) acts on its own character
/// only through the game's console command, the game's RPCs on its own view, or its own crouch input. No custom RPC,
/// event or property. Idle unless the game starts with <c>-mpobs</c>.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class ObserverPlugin : BaseUnityPlugin
{
    public const string Flag = "-mpobs";

    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, Flag) < 0)
        {
            Log.LogInfo($"idle: no {Flag} flag");
            return;
        }
        var dir = MpDir.ToNative(
            MpDir.Resolve(
                args,
                MpDir.UnixHome(
                    Environment.GetEnvironmentVariable("HOME"),
                    Environment.GetEnvironmentVariable("WINEHOMEDIR")
                ),
                Path.Combine(Paths.BepInExRootPath, "peakxr-mp")
            ),
            Path.DirectorySeparatorChar == '\\'
        );
        Harmony.CreateAndPatchAll(typeof(RpcProbe), MyPluginInfo.PLUGIN_GUID);
        Harmony.CreateAndPatchAll(typeof(InputHold), MyPluginInfo.PLUGIN_GUID);
        var go = new GameObject("MpObserver") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<Observer>().Dir = dir;
        Log.LogInfo($"observing, files in {dir}");
    }
}

/// <summary>
/// Postfixes on the vanilla RPC receivers that carry what the VR player does. They only report the call; the game
/// has already applied it. Decomp lines (Assembly-CSharp): CharacterAnimations.cs:417, PointPinger.cs:84,
/// CharacterItems.cs:385 and :546, CharacterClimbing.cs:349, Item.cs:996 and :1096, Character.cs:993, :1646 and :1752,
/// Luggage.cs:72, CharacterMovement.cs:626 (JumpRpc, true for a pal jump), CharacterCarrying.cs:56 and :92,
/// Campfire.cs:462.
/// </summary>
[HarmonyPatch]
static class RpcProbe
{
    internal static Action<string, Component, object[]> Seen;

    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CharacterAnimations), "RPCA_PlayRemove");
        yield return AccessTools.Method(typeof(PointPinger), "ReceivePoint_Rpc");
        yield return AccessTools.Method(typeof(CharacterItems), "DropItemRpc");
        yield return AccessTools.Method(typeof(CharacterItems), "EquipSlotRpc");
        yield return AccessTools.Method(typeof(CharacterClimbing), "StartClimbRpc");
        yield return AccessTools.Method(typeof(Item), "SendFeedDataRPC");
        yield return AccessTools.Method(typeof(Item), "Consume");
        yield return AccessTools.Method(typeof(Character), "RPCA_PassOut");
        yield return AccessTools.Method(typeof(Character), "GetFedItemRPC");
        yield return AccessTools.Method(typeof(Character), "RPCA_Fall");
        yield return AccessTools.Method(typeof(Luggage), "OpenLuggageRPC");
        yield return AccessTools.Method(typeof(CharacterMovement), "JumpRpc");
        yield return AccessTools.Method(typeof(CharacterCarrying), "RPCA_StartCarry");
        yield return AccessTools.Method(typeof(CharacterCarrying), "RPCA_Drop");
        yield return AccessTools.Method(typeof(Campfire), "Light_Rpc");
    }

    static void Postfix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        try
        {
            Seen?.Invoke(__originalMethod.Name, __instance as Component, __args);
        }
        catch (Exception e)
        {
            ObserverPlugin.Log.LogWarning($"probe {__originalMethod.Name}: {e.Message}");
        }
    }
}

/// <summary>
/// CharacterInput.Sample (CharacterInput.cs:212), postfix: our own crouch held while the VR side asks for it
/// (<see cref="MpRequest.Crouch"/>), the same field the crouch key sets (:245). Our own character only.
/// </summary>
[HarmonyPatch(typeof(CharacterInput), "Sample")]
static class InputHold
{
    internal static bool Crouch;

    static void Postfix(CharacterInput __instance)
    {
        var c = Character.localCharacter;
        if (Crouch && c != null && c.input == __instance)
            __instance.crouchIsPressed = true;
    }
}
