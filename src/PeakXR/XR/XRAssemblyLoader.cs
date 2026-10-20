using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PeakXR.XR;

/// <summary>
/// Loads the managed XR assemblies (Unity.XR.Management, Unity.XR.OpenXR, Unity.XR.OpenVR) from
/// <c>BepInEx/plugins/PeakXR/RuntimeDeps/</c> (layout from tools/package.sh). They are not in Mono's
/// probing path, so they must be loaded, and an <see cref="AppDomain.AssemblyResolve"/> handler must
/// answer for them, before the JIT compiles any method that touches their types.
/// </summary>
static class XRAssemblyLoader
{
    public const string Management = "Unity.XR.Management";
    public const string OpenXR = "Unity.XR.OpenXR";
    public const string OpenVR = "Unity.XR.OpenVR";

    static readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
    static string depsDir;
    static bool hooked;

    public static string DepsDir =>
        depsDir ??= Path.Combine(
            Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".",
            "RuntimeDeps"
        );

    public static bool IsLoaded(string name) => loaded.ContainsKey(name);

    /// <summary>Loads <paramref name="name"/>.dll once. Returns false (and logs) when the file is missing
    /// or cannot be loaded; never throws.</summary>
    public static bool TryLoad(string name)
    {
        if (loaded.ContainsKey(name))
            return true;
        Hook();

        // The game or another mod may already have it (stack.md: prefer the game's copy if it ever ships one).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                loaded[name] = asm;
                Plugin.Log.LogInfo(
                    $"[xr.bootstrap] {name} already loaded from {SafeLocation(asm)}"
                );
                return true;
            }
        }

        var path = Path.Combine(DepsDir, name + ".dll");
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] {name}.dll not found in {DepsDir}");
            return false;
        }
        try
        {
            var asm = Assembly.LoadFile(path);
            loaded[name] = asm;
            Plugin.Log.LogInfo(
                $"[xr.bootstrap] Loaded {name} {asm.GetName().Version} from RuntimeDeps"
            );
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[xr.bootstrap] Could not load {path}: {e.Message}");
            return false;
        }
    }

    /// <summary>Installs the resolve handler. Plugin.Awake calls it before feature discovery, whose
    /// <c>GetTypes</c> already needs the XR assemblies for the backend types.</summary>
    public static void Hook()
    {
        if (hooked)
            return;
        hooked = true;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (loaded.TryGetValue(name, out var asm))
            return asm;
        // Only answer for files we ship; everything else keeps the default resolution.
        var path = Path.Combine(DepsDir, name + ".dll");
        if (!File.Exists(path))
            return null;
        try
        {
            asm = Assembly.LoadFile(path);
            loaded[name] = asm;
            return asm;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[xr.bootstrap] Could not resolve {name}: {e.Message}");
            return null;
        }
    }

    static string SafeLocation(Assembly asm)
    {
        try
        {
            return string.IsNullOrEmpty(asm.Location) ? "(dynamic)" : asm.Location;
        }
        catch (NotSupportedException)
        {
            return "(dynamic)";
        }
    }
}
