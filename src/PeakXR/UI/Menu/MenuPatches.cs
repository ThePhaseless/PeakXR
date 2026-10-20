using System;
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
