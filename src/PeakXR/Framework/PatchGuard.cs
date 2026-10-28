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
/// (prefixes that can skip the original then return true). The first error per site is logged with its stack and
/// marks the feature degraded in the compat report; a feature that keeps failing (<see cref="FaultTally"/>) is
/// unpatched on the next frame, so the game falls back to flat behaviour for it.
/// </summary>
public static class PatchGuard
{
    private static readonly FaultTally Tally = new();
    private static readonly List<Feature> PendingOff = new();

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
            Report(
                FeatureManager.Get<TFeature>(),
                typeof(TFeature).Name,
                error,
                $"{Path.GetFileName(file)}:{line}"
            );
        }
#pragma warning disable S2486, S108, RCS1075 // the guard itself must never throw into game code
        catch { }
#pragma warning restore S2486, S108, RCS1075
    }

    private static void Report(Feature feature, string owner, Exception error, string site)
    {
        var id = feature?.Id ?? owner;
        switch (Tally.Record(id, site, Time.realtimeSinceStartup))
        {
            case FaultAction.Log:
                Plugin.Log.LogError(
                    $"[{id}] patch error at {site}; the game carries on, later errors here are not logged: {error}"
                );
                if (feature is { State: FeatureState.Active, FailureReason: null })
                    feature.FailureReason =
                        $"degraded: error at {site}: {error.GetBaseException().Message}";
                break;
            case FaultAction.Disable:
                Plugin.Log.LogError(
                    $"[{id}] keeps failing at {site}; turning it off for this session: {error.GetBaseException().Message}"
                );
                if (feature is { State: FeatureState.Active })
                {
                    feature.State = FeatureState.Failed;
                    feature.FailureReason =
                        $"turned off after repeated errors at {site}: {error.GetBaseException().Message}";
                    if (PendingOff.Count == 0)
                        Application.onBeforeRender += UnpatchPending;
                    PendingOff.Add(feature);
                }
                break;
            case FaultAction.Quiet:
                break;
        }
    }

    /// <summary>Unpatch outside any patched game method: the failing one may still be on the stack when
    /// <see cref="Fail{TFeature}"/> runs.</summary>
    private static void UnpatchPending()
    {
        Application.onBeforeRender -= UnpatchPending;
        foreach (var feature in PendingOff)
        {
            try
            {
                feature.Harmony?.UnpatchSelf();
                Plugin.Log.LogWarning($"[compat] {feature.Id} unpatched: {feature.FailureReason}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[compat] could not unpatch {feature.Id}: {e}");
            }
        }
        PendingOff.Clear();
    }
}
