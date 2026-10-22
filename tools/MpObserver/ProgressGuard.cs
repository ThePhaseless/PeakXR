using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Peak;
using PeakXR.Core;

namespace MpObserver;

/// <summary>
/// Keeps client B's real profile as it was: no achievement, stat or ascent unlock, and the quicksave neither written
/// nor deleted. The same six skip-call prefixes as PeakXR's <c>SelfTestProgressGuard</c>
/// (src/PeakXR/Sandbox/SelfTestFeature.cs), copied because B must not reference PeakXR; keep the two lists in step.
/// Targets (decomp Assembly-CSharp): AchievementManager.ThrowAchievement (:570), SetSteamStat (:447),
/// IncrementSteamStat (:500), TryCompleteAscent (:957); Peak/Quicksave.SaveNow (:333) and DestroySaveData (:424).
/// </summary>
static class ProgressGuard
{
    /// <summary>The quicksave and how it looked when the guard was armed.</summary>
    internal static string SavePath;
    internal static FileFingerprint SaveAtStart;

    /// <summary>Resolves every target; null (and the missing ones in <paramref name="missing"/>) when any is gone,
    /// so the observer refuses to run unguarded.</summary>
    internal static MethodBase[] Targets(out string missing)
    {
        var found = new List<MethodBase>();
        var gone = new List<string>();
        void Add(Type type, string name, Type[] args)
        {
            var m = AccessTools.Method(type, name, args);
            if (m == null)
                gone.Add(type.Name + "." + name);
            else
                found.Add(m);
        }
        Add(typeof(AchievementManager), "ThrowAchievement", new[] { typeof(ACHIEVEMENTTYPE) });
        Add(
            typeof(AchievementManager),
            "SetSteamStat",
            new[] { typeof(STEAMSTATTYPE), typeof(int) }
        );
        Add(
            typeof(AchievementManager),
            "IncrementSteamStat",
            new[] { typeof(STEAMSTATTYPE), typeof(int) }
        );
        Add(typeof(AchievementManager), "TryCompleteAscent", null);
        Add(typeof(Quicksave), "SaveNow", null);
        Add(typeof(Quicksave), "DestroySaveData", null);
        missing = string.Join(", ", gone);
        return gone.Count == 0 ? found.ToArray() : null;
    }

    /// <summary>Patches all targets with <see cref="Skip"/> and records the quicksave's fingerprint.</summary>
    internal static void Arm(Harmony harmony, MethodBase[] targets)
    {
        var skip = new HarmonyMethod(AccessTools.Method(typeof(ProgressGuard), nameof(Skip)));
        foreach (var target in targets)
            harmony.Patch(target, prefix: skip);
        SavePath = Quicksave.SaveFile.FullName;
        SaveAtStart = FileFingerprint.Of(SavePath);
        ObserverPlugin.Log.LogInfo(
            $"progress and save guard on; quicksave {SavePath}: {SaveAtStart}"
        );
    }

    /// <summary>Skips the call and logs it. IncrementSteamStat returns the new total; its int result stays 0.
    /// DestroySaveData's other effect a run relies on (no load from the save) is kept.</summary>
    static bool Skip(MethodBase __originalMethod)
    {
        if (__originalMethod.Name == "DestroySaveData")
            Quicksave.ShouldUseSaveData = false;
        ObserverPlugin.Log.LogInfo(
            $"guarded {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: B's profile is kept"
        );
        return false;
    }
}
