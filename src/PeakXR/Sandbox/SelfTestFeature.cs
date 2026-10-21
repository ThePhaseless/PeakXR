using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Unattended creative-mode run (feature id <c>sandbox.selftest</c>) for when nobody can watch the screen or
/// touch input. Only with the launch option <c>-peakxr-selftest</c>: it forces the sandbox on for this run
/// (never written to the config), plays solo, spawns and uses items, spawns every entity, visits every segment,
/// saves screenshots and a result log under BepInEx, then quits. See docs/testing.md and <see cref="SelfTestRunner"/>.
/// </summary>
public sealed class SelfTestFeature : Feature
{
    public const string LaunchFlag = "-peakxr-selftest";

    /// <summary>Stop the script after <c>04-beach</c> and keep the game running for live-input tests.</summary>
    public const string HoldFlag = "-peakxr-selftest-hold";

    internal static bool Hold => Array.IndexOf(Environment.GetCommandLineArgs(), HoldFlag) >= 0;

    public override string Id => "sandbox.selftest";

    // After sandbox (Order 0), which it drives.
    public override int Order => 1;

    public override bool VROnly => false;

    public override bool Requested =>
        Array.IndexOf(Environment.GetCommandLineArgs(), LaunchFlag) >= 0;

    protected internal override void Install()
    {
        if (!FeatureManager.IsActive<SandboxFeature>())
            throw new InvalidOperationException("the sandbox feature is not active");
        Patch(typeof(SelfTestInputPatch));
        Patch(typeof(InputEdgeCharacterPatch));
        InputEdgeLog.Install();

        SandboxActions.Forced = true;
        var go = new GameObject("PeakXR.SelfTest") { hideFlags = HideFlags.HideAndDontSave };
        go.AddComponent<SelfTestRunner>();
    }
}

/// <summary>
/// Scripted primary-use hold and one-frame drop, written the way the mod's own input features do it: a
/// postfix on <c>CharacterInput.Sample(bool)</c> (internal, decomp Assembly-CSharp/CharacterInput.cs:212, which
/// starts with <c>ResetInput()</c> at :214), OR-merged into usePrimary* (:101-105) and drop* (:95-99) so
/// <c>CharacterItems.DoUsing</c> (CharacterItems.cs:201) and <c>DoDropping</c> (:252) read them the same frame.
/// </summary>
[HarmonyPatch]
static class SelfTestInputPatch
{
    internal static bool HoldPrimary;
    internal static bool DropOnce;

    static readonly ButtonEdge primary = new();

    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

    [HarmonyPriority(Priority.Last)]
    static void Postfix(CharacterInput __instance)
    {
        var c = Character.localCharacter;
        if (c == null || c.input != __instance)
            return;

        primary.Update(HoldPrimary, Time.unscaledDeltaTime);
        __instance.usePrimaryWasPressed |= primary.Pressed;
        __instance.usePrimaryIsPressed |= primary.Held;
        __instance.usePrimaryWasReleased |= primary.Released;

        if (DropOnce)
        {
            // Press and release in one frame: DoDropping drops with no throw charge.
            DropOnce = false;
            __instance.dropWasPressed = true;
            __instance.dropWasReleased = true;
            __instance.dropIsPressed = false;
        }
    }
}
