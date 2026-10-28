using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Framework;

/// <summary>
/// Error containment for Harmony patch bodies. An exception thrown into game code hurts whoever the patch runs for:
/// a throwing CharacterInput.Sample postfix skips the rest of CharacterMovement.Update (CharacterMovement.cs:198-213,
/// a frozen player), and a throwing prefix skips the original. Hot patch bodies catch and hand the error here
/// (prefixes that can skip the original then return true), so the site's VR logic stops at the error and vanilla
/// carries on. The first error per site is logged with its stack and marks the feature degraded; a site that keeps
/// failing (<see cref="FaultTally"/>) marks it failing. Nothing is unpatched: features read each other's state at
/// install (who owns grip, who drives movement), and pulling one out at runtime would strand the others.
/// Every caught error reaches <see cref="Caught"/>, so the self-test fails the step that caused it.
/// </summary>
public static class PatchGuard
{
    private static readonly FaultTally Tally = new();

    /// <summary>Per call site (the compiler's file path and line): its <c>File.cs:line</c> name, its tally key and its
    /// feature, built on the first error there, so a site erroring every frame allocates nothing after that.</summary>
    private static readonly Dictionary<(string File, int Line), Site> Sites = new();

    private sealed class Site
    {
        public string Name;
        public string Key;
        public Feature Feature;
        public string Id;
    }

    /// <summary>Every caught error, with its site (<c>File.cs:line</c>). Raised for repeats too.</summary>
    public static event Action<string, Exception> Caught;

    /// <summary>Call from a patch body's catch block. Never throws.</summary>
    public static void Fail<TFeature>(
        Exception error,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0
    )
        where TFeature : Feature
    {
        try
        {
            if (!Sites.TryGetValue((file, line), out var site))
            {
                var feature = FeatureManager.Get<TFeature>();
                var id = feature?.Id ?? typeof(TFeature).Name;
                var name = $"{Path.GetFileName(file)}:{line}";
                site = new Site
                {
                    Name = name,
                    Key = id + "|" + name,
                    Feature = feature,
                    Id = id,
                };
                Sites[(file, line)] = site;
            }
            Report(site, error);
            Caught?.Invoke(site.Name, error);
        }
#pragma warning disable S2486, S108, RCS1075 // the guard itself must never throw into game code
        catch { }
#pragma warning restore S2486, S108, RCS1075
    }

    private static void Report(Site at, Exception error)
    {
        var feature = at.Feature;
        var id = at.Id;
        var site = at.Name;
        switch (Tally.Record(at.Key, Time.frameCount, Time.realtimeSinceStartup))
        {
            case FaultAction.Log:
                Plugin.Log.LogError(
                    $"[{id}] patch error at {site}; the game carries on, later errors here are not logged: {error}"
                );
                if (feature is { State: FeatureState.Active, FailureReason: null })
                    feature.FailureReason =
                        $"degraded: error at {site}: {error.GetBaseException().Message}";
                break;
            case FaultAction.Persistent:
                Plugin.Log.LogError(
                    $"[{id}] keeps failing at {site} ({FaultTally.PersistentFrames}+ frames in "
                        + $"{FaultTally.WindowSeconds:0} s); its VR logic there stops at the error every time: "
                        + error.GetBaseException().Message
                );
                if (feature is { State: FeatureState.Active })
                    feature.FailureReason =
                        $"failing: keeps erroring at {site}: {error.GetBaseException().Message}";
                break;
            case FaultAction.Quiet:
                break;
        }
    }
}
