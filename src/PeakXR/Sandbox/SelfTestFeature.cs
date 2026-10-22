using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Peak;
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
        Patch(typeof(SelfTestUseProbe));
        Patch(typeof(SelfTestEndGamePatch));
        Patch(typeof(SelfTestCampfirePatch));
        foreach (var guard in SelfTestProgressGuard.Patches)
            Patch(guard);
        SelfTestProgressGuard.Arm(Id);
        SelfTestRunner.BingBongRef = GameMember.Field<BingBongForceAbilities, Transform>(
            "bingbong"
        );
        SelfTestRunner.TornadoCaughtRef = GameMember.Field<Tornado, List<Character>>(
            "caughtCharacters"
        );
        SelfTestRunner.SpawnTornadoMethod = GameMember.Method(
            typeof(TornadoSpawner),
            "SpawnTornado"
        );

        SandboxActions.Forced = true;
        var go = new GameObject("PeakXR.SelfTest") { hideFlags = HideFlags.HideAndDontSave };
        go.AddComponent<SelfTestRunner>();
    }
}

/// <summary>
/// Keeps a self-test run from granting the player's real profile anything. Every unlock goes through four
/// <c>AchievementManager</c> entry points (decomp Assembly-CSharp/AchievementManager.cs): achievements
/// (<c>ThrowAchievement</c>, :570, the only <c>Platform.UnlockAchievement</c> call, :596), stats such as MaxAscent,
/// BestTime, guidebook pages and cosmetic picks (<c>SetSteamStat</c>, :447, and <c>IncrementSteamStat</c>, :500, the
/// only <c>SetStat</c>/<c>SetProgress</c> calls, :518-524), and ascent completion (<c>TryCompleteAscent</c>, :957, which
/// also queues the end screen's promotion popups). Badges and cosmetics follow from achievements earned this run
/// (EndScreen.cs:480-492, :508-570). The game's own guard, a custom run (<c>RunSettings.blockingAchievements</c>,
/// RunSettings.cs:136-146), covers the same four but never in the airport, where the self-test also reads the
/// guidebook and uses items, so the self-test blocks them itself for the whole run.
/// <para>The player's quicksave (<c>quicksave.peak</c> in the persistent data path, Peak/Quicksave.cs:253-265) is
/// kept too: a campfire light writes it (<c>SaveNow</c>, :333-347, from Campfire.cs:485) and the run's end deletes it
/// (<c>DestroySaveData</c>, :424-434, from Character.cs:896). Both are skipped and logged; nothing in a test run loads
/// the save back. <see cref="Arm"/> records the file's fingerprint so the run's last step can prove it unchanged.
/// Used by <c>sandbox.selftest</c> and <c>sandbox.mptest</c>.</para>
/// </summary>
static class SelfTestProgressGuard
{
    /// <summary>Every guard patch; a feature applies them all in its Install.</summary>
    internal static readonly Type[] Patches =
    {
        typeof(Achievement),
        typeof(StatSet),
        typeof(StatIncrement),
        typeof(AscentComplete),
        typeof(SaveWrite),
        typeof(SaveDelete),
    };

    /// <summary>All guards are patched: steps that would grant progress or touch the save may run.</summary>
    internal static bool Active;

    /// <summary>The quicksave's path and how it looked when the guard was armed.</summary>
    internal static string SavePath;
    internal static FileFingerprint SaveAtStart;

    /// <summary>Called by a feature once all <see cref="Patches"/> are applied.</summary>
    internal static void Arm(string feature)
    {
        SavePath = Quicksave.SaveFile.FullName;
        SaveAtStart = FileFingerprint.Of(SavePath);
        Active = true;
        Plugin.Log.LogInfo(
            $"[{feature}] progress and save guard on; quicksave {SavePath}: {SaveAtStart}"
        );
    }

    [HarmonyPatch]
    internal static class SaveWrite
    {
        static MethodBase TargetMethod() => GameMember.Method(typeof(Quicksave), "SaveNow");

        static bool Prefix()
        {
            Plugin.Log.LogInfo(
                "[selftest] guarded Quicksave.SaveNow: the player's quicksave is kept"
            );
            return false;
        }
    }

    [HarmonyPatch]
    internal static class SaveDelete
    {
        static MethodBase TargetMethod() => GameMember.Method(typeof(Quicksave), "DestroySaveData");

        static bool Prefix()
        {
            // The rest of DestroySaveData's effect that a test run relies on: no load from the save.
            Quicksave.ShouldUseSaveData = false;
            Plugin.Log.LogInfo(
                "[selftest] guarded Quicksave.DestroySaveData: the player's quicksave is kept"
            );
            return false;
        }
    }

    [HarmonyPatch]
    internal static class Achievement
    {
        static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(AchievementManager),
                "ThrowAchievement",
                new[] { typeof(ACHIEVEMENTTYPE) }
            );

        static bool Prefix() => false;
    }

    [HarmonyPatch]
    internal static class StatSet
    {
        static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(AchievementManager),
                "SetSteamStat",
                new[] { typeof(STEAMSTATTYPE), typeof(int) }
            );

        static bool Prefix() => false;
    }

    [HarmonyPatch]
    internal static class StatIncrement
    {
        static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(AchievementManager),
                "IncrementSteamStat",
                new[] { typeof(STEAMSTATTYPE), typeof(int) }
            );

        static bool Prefix(ref int __result)
        {
            __result = 0;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AscentComplete
    {
        static MethodBase TargetMethod() =>
            GameMember.Method(typeof(AchievementManager), "TryCompleteAscent");

        static bool Prefix() => false;
    }
}

/// <summary>
/// Lets vr.26 finish a campfire light hold without the run moving on. A finished hold on an unlit campfire sends
/// <c>Light_Rpc(true)</c> (decomp Assembly-CSharp/Campfire.cs:309-325), which advances the map segment and quicksaves
/// (:478-486). While <see cref="Hold"/> is set the fire is lit the way <c>LightWithoutReveal</c> (:456) lights it
/// instead, so the step still sees the game's own hold complete and the fire go <c>Lit</c>.
/// </summary>
[HarmonyPatch]
static class SelfTestCampfirePatch
{
    internal static bool Hold;

    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(Campfire), "Interact_CastFinished", new[] { typeof(Character) });

    static bool Prefix(Campfire __instance)
    {
        if (!Hold || __instance.Lit)
            return true;
        __instance.LightWithoutReveal();
        return false;
    }
}

/// <summary>
/// Holds back the solo game over while a self-test death step runs. <c>Character.CheckEndGame</c> (decomp
/// Assembly-CSharp/Character.cs:838) ends the run the moment every character is dead, which solo is the moment the
/// step kills the player, and <c>RPCEndGame</c> (:893) then deletes the quicksave and opens the end screen for good.
/// </summary>
[HarmonyPatch]
static class SelfTestEndGamePatch
{
    internal static bool Hold;

    static MethodBase TargetMethod() => GameMember.Method(typeof(Character), "CheckEndGame");

    static bool Prefix() => !Hold;
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

/// <summary>
/// vr.12 diagnosis: while scripted hands run, logs (Debug, at most 120 lines per run) every
/// <c>Item.StartUsePrimary</c> (Item.cs:849) and every cancel that zeroes a running primary cast:
/// <c>CancelUsePrimary</c> (:918) and <c>CancelUseSecondary</c> (:1062, it resets castProgress too), with the caller.
/// </summary>
[HarmonyPatch]
static class SelfTestUseProbe
{
    static int lines;

    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return GameMember.Method(typeof(Item), "StartUsePrimary");
        yield return GameMember.Method(typeof(Item), "CancelUsePrimary");
        yield return GameMember.Method(typeof(Item), "CancelUseSecondary");
    }

    static void Prefix(Item __instance, MethodBase __originalMethod)
    {
        var hands = VR.VRInput.TestOverride;
        if (hands == null || hands == SelfTestRunner.IdleHands || lines >= 120)
            return;
        bool start = __originalMethod.Name == "StartUsePrimary";
        if (!start && !__instance.isUsingPrimary)
            return;
        lines++;
        Plugin.Log.LogDebug(
            $"[selftest.use] f{Time.frameCount} {__originalMethod.Name} {__instance.name} using {__instance.isUsingPrimary} "
                + $"cast {__instance.castProgress:0.000} from {Callers()}"
        );
    }

    static string Callers()
    {
        var frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
        var names = new List<string>(3);
        for (int i = 0; frames != null && i < frames.Length && names.Count < 3; i++)
        {
            var m = frames[i].GetMethod();
            if (m != null)
                names.Add(m.DeclaringType?.Name + "." + m.Name);
        }
        return string.Join(" < ", names);
    }
}
