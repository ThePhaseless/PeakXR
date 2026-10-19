using System;
using HarmonyLib;

namespace PeakXR.Framework;

public enum FeatureState
{
    Pending,
    Active,
    Skipped,
    Failed,
}

/// <summary>
/// A self-contained part of the mod. Each feature owns its Harmony instance, so when a game update
/// removes or renames something it depends on, only that feature is rolled back and reported;
/// the rest of the mod keeps working.
/// Features are discovered by reflection: adding a subclass is enough to register it.
/// </summary>
public abstract class Feature
{
    /// <summary>Stable identifier, used for the Harmony id, logs and the compatibility report.</summary>
    public abstract string Id { get; }

    /// <summary>Lower runs first.</summary>
    public virtual int Order => 0;

    /// <summary>Install only when a VR runtime is selected. Features that also matter for flat
    /// players (pose receiving, sandbox) return false.</summary>
    public virtual bool VROnly => true;

    public FeatureState State { get; internal set; } = FeatureState.Pending;
    public string FailureReason { get; internal set; }

    protected internal Harmony Harmony { get; internal set; }

    /// <summary>
    /// Bind config, resolve game members and apply patches. Throw on anything missing:
    /// the manager rolls back this feature's patches and marks it failed.
    /// </summary>
    protected internal abstract void Install();

    /// <summary>Apply every [HarmonyPatch] in a class. Throws if a target no longer exists.</summary>
    protected void Patch(Type patchClass) => Harmony.CreateClassProcessor(patchClass).Patch();
}
