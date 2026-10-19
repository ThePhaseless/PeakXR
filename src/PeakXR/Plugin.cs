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

        VRSession.Requested = VRSession.ReadRequestedRuntime();
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION}, game {UnityEngine.Application.version}, " +
                    $"Unity {UnityEngine.Application.unityVersion}, runtime {VRSession.Requested}");

        FeatureManager.InstallAll(VRSession.Requested != VRRuntime.Disabled);
    }
}
