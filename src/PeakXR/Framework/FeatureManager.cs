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

    public static T Get<T>()
        where T : Feature => features.OfType<T>().FirstOrDefault();

    public static bool IsActive<T>()
        where T : Feature => Get<T>()?.State == FeatureState.Active;

    internal static void InstallAll(bool vrEnabled)
    {
        foreach (var type in LoadableTypes())
        {
            if (
                type.IsAbstract
                || !typeof(Feature).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) == null
            )
                continue;
            try
            {
                features.Add((Feature)Activator.CreateInstance(type));
            }
            catch (Exception e)
            {
                features.Add(new LoadFailure(type.Name, e.GetBaseException().Message));
                Plugin.Log.LogError($"Feature type {type.FullName} could not be created: {e}");
            }
        }

        foreach (
            var feature in features.OrderBy(f => f.Order).ThenBy(f => f.Id, StringComparer.Ordinal)
        )
        {
            if (feature.State == FeatureState.Failed)
                continue;
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

    /// <summary>
    /// Every type in PeakXR.dll that the runtime could load. A type whose base or field type lives in a
    /// missing or changed assembly (Zorro.Settings after a game update, Unity.XR.* without RuntimeDeps)
    /// makes <see cref="Assembly.GetTypes"/> throw; the other features must still install.
    /// </summary>
    private static Type[] LoadableTypes()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var le in e.LoaderExceptions)
                if (le != null && reasons.Add(le.Message))
                    Plugin.Log.LogError($"[compat] type load failed: {le.Message}");
            features.Add(
                new LoadFailure(
                    "plugin.types",
                    string.Join("; ", reasons) + " (features using these types are missing)"
                )
            );
            return e.Types.Where(t => t != null).ToArray();
        }
    }

    /// <summary>Report row for code that could not even be loaded, so the VR tab and the log show it.</summary>
    private sealed class LoadFailure : Feature
    {
        public LoadFailure(string id, string reason)
        {
            Id = id;
            State = FeatureState.Failed;
            FailureReason = reason;
        }

        public override string Id { get; }
        public override bool VROnly => false;

        protected internal override void Install() { }
    }

    private static void LogReport()
    {
        foreach (var f in features.OrderBy(f => f.Order).ThenBy(f => f.Id, StringComparer.Ordinal))
        {
            var reason = f.FailureReason == null ? "" : $" ({f.FailureReason})";
            Plugin.Log.LogInfo($"[compat] {f.Id, -24} {f.State}{reason}");
        }
    }
}
