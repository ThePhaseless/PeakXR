#if PEAKXR_OPENXR || PEAKXR_OPENVR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Management;

namespace PeakXR.XR;

/// <summary>
/// XR Management plumbing shared by both providers. The game ships no XR settings asset, so the
/// general settings, the manager and the loader are created at runtime; the manager's
/// "registered loaders" set is filled by reflection because <see cref="XRManagerSettings.TryAddLoader"/>
/// only accepts editor-registered loaders in a player. Manual init as documented in
/// https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/EndUser.html
/// </summary>
abstract class ManagedXRBackend : XRBackend
{
    XRGeneralSettings general;
    XRManagerSettings manager;
    XRLoader loader;

    protected abstract XRLoader CreateLoader(XRStartOptions options);

    public override bool Initialize(XRStartOptions options)
    {
        general = XRGeneralSettings.Instance;
        if (general == null)
            general = ScriptableObject.CreateInstance<XRGeneralSettings>(); // Awake registers it as Instance
        manager = ScriptableObject.CreateInstance<XRManagerSettings>();
        manager.automaticLoading = false;
        manager.automaticRunning = false;
        general.Manager = manager;

        loader = CreateLoader(options);
        var registeredField =
            typeof(XRManagerSettings).GetField(
                "m_RegisteredLoaders",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new MissingFieldException(nameof(XRManagerSettings), "m_RegisteredLoaders");
        ((HashSet<XRLoader>)registeredField.GetValue(manager)).Add(loader);
        if (!manager.TryAddLoader(loader))
            throw new InvalidOperationException("XRManagerSettings refused the loader");

        manager.InitializeLoaderSync();
        return manager.activeLoader != null;
    }

    public override void StartSubsystems()
    {
        if (manager != null)
            manager.StartSubsystems();
    }

    public override void Shutdown()
    {
        if (manager == null)
            return;
        try
        {
            if (manager.isInitializationComplete)
                manager.DeinitializeLoader();
        }
        finally
        {
            if (general != null && general.Manager == manager)
                general.Manager = null;
            UnityEngine.Object.Destroy(manager);
            if (loader != null)
                UnityEngine.Object.Destroy(loader);
            manager = null;
            loader = null;
        }
    }

    /// <summary>Sets an internal field the provider only exposes to its editor tooling.</summary>
    protected static void SetInternal(object target, string field, object value)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(
                field,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (f != null)
            {
                f.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().FullName, field);
    }
}
#endif
