using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

namespace PeakXR.Preloader;

/// <summary>
/// BepInEx 5 preloader patcher. It patches no assemblies. <see cref="Initialize"/> runs before Unity
/// enumerates XR subsystems, which is the only point where the native runtime plugin and its
/// UnitySubsystemsManifest.json can still be put in place. Patcher contract:
/// https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html; manifest:
/// https://docs.unity3d.com/2023.1/Documentation/Manual/xrsdk-provider-setup.html
/// </summary>
public static class Preload
{
    public const string RuntimeEnvVar = "PEAKXR_RUNTIME";
    public const string ConfigFileName = "PeakXR.Preloader.cfg";
    public const string RuntimeDepsFolder = "RuntimeDeps";

    private static readonly ManualLogSource Log = Logger.CreateLogSource("PeakXR.Preloader");

    // Required by BepInEx 5: no target assemblies, so Patch is never called.
    public static IEnumerable<string> TargetDLLs => Enumerable.Empty<string>();

    public static void Patch(AssemblyDefinition _)
    {
        // Never called: TargetDLLs is empty.
    }

    public static void Initialize()
    {
        // Read by the finally block even when the catch itself throws.
#pragma warning disable S1854
        var mode = RuntimeMode.Disabled;
#pragma warning restore S1854
        RuntimeInstaller installer = null;
        try
        {
            mode = ResolveMode(out var openVRFallback);
            var dataDir = FindDataDir();
            var patcherDir =
                Path.GetDirectoryName(typeof(Preload).Assembly.Location) ?? Paths.PatcherPluginPath;
            installer = new RuntimeInstaller(
                dataDir,
                Path.Combine(patcherDir, RuntimeDepsFolder),
                Log
            );
            mode = installer.Apply(mode, openVRFallback);
        }
        catch (Exception e)
        {
            Log.LogError($"XR runtime setup failed, starting without VR: {e}");
            mode = RuntimeMode.Disabled;
            try
            {
                installer?.Apply(RuntimeMode.Disabled);
            }
            catch (Exception cleanup)
            {
                Log.LogError($"Cleanup after the failed setup also failed: {cleanup.Message}");
            }
        }
        finally
        {
            try
            {
                Environment.SetEnvironmentVariable(RuntimeEnvVar, mode.ToString());
            }
            catch (Exception e)
            {
                Log.LogError($"Could not set {RuntimeEnvVar}: {e.Message}");
            }
            Log.LogInfo($"{RuntimeEnvVar}={mode}");
        }
    }

    /// <summary>Config value, overridden by -peakxr-novr / -peakxr-openvr / -peakxr-openxr.</summary>
    private static RuntimeMode ResolveMode(out bool openVRFallback)
    {
        var mode = RuntimeMode.OpenXR;
        openVRFallback = true;
        try
        {
            var config = new ConfigFile(Path.Combine(Paths.ConfigPath, ConfigFileName), true);
            mode = config
                .Bind(
                    "Runtime",
                    "Mode",
                    RuntimeMode.OpenXR,
                    "XR runtime to start the game with.\n"
                        + "OpenXR: the default; uses the system's active OpenXR runtime (SteamVR, Oculus, WMR, Monado, ...).\n"
                        + "OpenVR: SteamVR through Valve's OpenVR plugin; try it when OpenXR does not start.\n"
                        + "Disabled: play flat; PeakXR removes the XR manifests it installed.\n"
                        + "Launch options -peakxr-novr, -peakxr-openvr and -peakxr-openxr override this setting."
                )
                .Value;
            openVRFallback = config
                .Bind(
                    "Runtime",
                    "OpenVRFallback",
                    true,
                    "With Mode = OpenXR, also install Valve's OpenVR plugin so the game can switch to it "
                        + "when OpenXR does not start (plugin setting XR.FallbackToOpenVR)."
                )
                .Value;
            Log.LogInfo(
                $"Config {ConfigFileName}: Mode = {mode}, OpenVRFallback = {openVRFallback}"
            );
        }
        catch (Exception e)
        {
            Log.LogWarning($"Could not read {ConfigFileName}, using {mode}: {e.Message}");
        }

        string[] args;
        try
        {
            args = Environment.GetCommandLineArgs();
        }
        catch (Exception e)
        {
            Log.LogWarning($"Could not read the command line: {e.Message}");
            return mode;
        }

        RuntimeMode? fromArgs = null;
        foreach (var arg in args)
        {
            switch (arg.Trim().ToLowerInvariant())
            {
                case "-peakxr-novr":
                    // Flat play wins over any runtime switch, so it always works as an escape hatch.
                    Log.LogInfo("Launch option -peakxr-novr: VR disabled");
                    return RuntimeMode.Disabled;
                case "-peakxr-openvr":
                    fromArgs = RuntimeMode.OpenVR;
                    break;
                case "-peakxr-openxr":
                    fromArgs = RuntimeMode.OpenXR;
                    break;
            }
        }
        if (fromArgs.HasValue)
        {
            Log.LogInfo($"Launch option override: {fromArgs.Value}");
            return fromArgs.Value;
        }
        return mode;
    }

    /// <summary>Finds &lt;Game&gt;_Data without assuming the game's name.</summary>
    private static string FindDataDir()
    {
        var managedParent = string.IsNullOrEmpty(Paths.ManagedPath)
            ? null
            : Directory.GetParent(Paths.ManagedPath)?.FullName;
        if (
            managedParent != null
            && managedParent.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(managedParent)
        )
            return managedParent;

        var root = Paths.GameRootPath;
        var preferred = Path.Combine(root, Paths.ProcessName + "_Data");
        if (Directory.Exists(Path.Combine(preferred, "Managed")))
            return preferred;

        var candidates = Directory
            .GetDirectories(root, "*_Data")
            .Where(d => Directory.Exists(Path.Combine(d, "Managed")))
            .ToArray();
        if (candidates.Length == 1)
            return candidates[0];
        throw new DirectoryNotFoundException(
            $"Could not find the game's *_Data folder (ManagedPath '{Paths.ManagedPath}', root '{root}', {candidates.Length} candidates)"
        );
    }
}
