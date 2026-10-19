using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PeakXR.Framework;

public static class FeatureManager
{
    private static readonly List<Feature> features = new();

    public static IReadOnlyList<Feature> Features => features;

    public static T Get<T>() where T : Feature => features.OfType<T>().FirstOrDefault();

    public static bool IsActive<T>() where T : Feature => Get<T>()?.State == FeatureState.Active;

    internal static void InstallAll(bool vrEnabled)
    {
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => !t.IsAbstract && typeof(Feature).IsAssignableFrom(t));

        foreach (var type in types)
            features.Add((Feature)Activator.CreateInstance(type));

        foreach (var feature in features.OrderBy(f => f.Order).ThenBy(f => f.Id))
        {
            if (feature.VROnly && !vrEnabled)
            {
                feature.State = FeatureState.Skipped;
                feature.FailureReason = "VR disabled";
                continue;
            }

            feature.Harmony = new Harmony($"{MyPluginInfo.PLUGIN_GUID}.{feature.Id}");
            try
            {
                feature.Install();
                feature.State = FeatureState.Active;
            }
            catch (Exception e)
            {
                feature.Harmony.UnpatchSelf();
                feature.State = FeatureState.Failed;
                feature.FailureReason = e.GetBaseException().Message;
                Plugin.Log.LogError($"Feature '{feature.Id}' disabled: {e}");
            }
        }

        LogReport();
    }

    private static void LogReport()
    {
        foreach (var f in features.OrderBy(f => f.Order).ThenBy(f => f.Id))
        {
            var reason = f.FailureReason == null ? "" : $" ({f.FailureReason})";
            Plugin.Log.LogInfo($"[compat] {f.Id,-24} {f.State}{reason}");
        }
    }
}
