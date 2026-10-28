using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;
    internal static ManualLogSource Log;

    /// <summary>All PeakXR settings. Features bind their own entries; the in-game VR settings tab
    /// lists every entry here, grouped by section.</summary>
    internal static ConfigFile Settings => Instance.Config;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        // First, so every line from here on reaches a bug report's log ring.
        Diagnostics.DebugLog.Attach();

        var args = System.Environment.GetCommandLineArgs();
        // BepInEx's Logger owns registered listeners and disposes them at shutdown.
#pragma warning disable CA2000
        if (System.Array.IndexOf(args, "-peakxr-selftest") >= 0)
            BepInEx.Logging.Logger.Listeners.Add(
                new FlushingLogListener(
                    System.IO.Path.Combine(Paths.BepInExRootPath, "PeakXR-trace.log")
                )
            );
#pragma warning restore CA2000

        VRSession.Requested = VRSession.ReadRequestedRuntime();
        VRSession.CompatProbe = System.Array.IndexOf(args, "-peakxr-compat") >= 0;
        Log.LogInfo(
            $"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION}, game {UnityEngine.Application.version}, "
                + $"Unity {UnityEngine.Application.unityVersion}, runtime {VRSession.Requested}"
                + (VRSession.CompatProbe ? ", compat probe (XR is not started)" : "")
        );

        XR.XRAssemblyLoader.Hook();
        FeatureManager.InstallAll(
            VRSession.Requested != VRRuntime.Disabled || VRSession.CompatProbe
        );
    }
}
