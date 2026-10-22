using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;

namespace PeakXR.UI.Menu;

/// <summary>Leaves the loading space when the game destroys its loading screen mid-load (kick, ban).
/// A/LoadingScreenHandler.cs:46 <c>public static void KillCurrentLoadingScreen()</c>.</summary>
[HarmonyPatch]
internal static class KillLoadingScreenPatch
{
    internal static UIMenuRuntime Runtime;

    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(LoadingScreenHandler),
            "KillCurrentLoadingScreen",
            Type.EmptyTypes
        );

    private static void Postfix()
    {
        if (Runtime != null)
            Runtime.OnLoadingScreenKilled();
    }
}

/// <summary>Learns where a load is heading, so a black or white loading screen gets a room that fits the
/// destination. Callers build this process as a Load argument (A/AirportCheckInKiosk.cs:158, A/GameHandler.cs:375,
/// A/EndScreen.cs:718), so it runs just before the loading screen appears.
/// A/LoadingScreenHandler.cs:132 <c>public IEnumerator LoadSceneProcess(string sceneName, bool networked,
/// bool yieldForCharacterSpawn = false, float extraYieldTimeOnEnd = 3f)</c>.</summary>
[HarmonyPatch]
internal static class LoadSceneProcessPatch
{
    internal static UIMenuRuntime Runtime;

    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(LoadingScreenHandler),
            "LoadSceneProcess",
            new[] { typeof(string), typeof(bool), typeof(bool), typeof(float) }
        );

    private static void Prefix(string sceneName)
    {
        if (Runtime != null)
            Runtime.OnLoadDestination(sceneName);
    }
}

/// <summary>Sees a load the game refuses because one is running (it only logs an error), so the destination
/// built for it is dropped. A/LoadingScreenHandler.cs:87 <c>public void Load(LoadingScreen.LoadingScreenType type,
/// Action runAfter, params IEnumerator[] processes)</c> and :100 <c>LoadWithoutDisablingQueue</c> (same parameters).</summary>
[HarmonyPatch]
internal static class LoadRejectedPatch
{
    internal static UIMenuRuntime Runtime;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var parameters = new[]
        {
            typeof(LoadingScreen.LoadingScreenType),
            typeof(Action),
            typeof(IEnumerator[]),
        };
        yield return GameMember.Method(typeof(LoadingScreenHandler), "Load", parameters);
        yield return GameMember.Method(
            typeof(LoadingScreenHandler),
            "LoadWithoutDisablingQueue",
            parameters
        );
    }

    private static void Prefix()
    {
        if (Runtime != null && LoadingScreenHandler.loading)
            Runtime.OnLoadRejected();
    }
}
