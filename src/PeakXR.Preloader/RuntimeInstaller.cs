using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace PeakXR.Preloader;

/// <summary>
/// Puts one runtime's native plugin DLLs into &lt;Game&gt;_Data/Plugins/x86_64/ and its
/// UnitySubsystemsManifest.json into &lt;Game&gt;_Data/UnitySubsystems/&lt;libraryName&gt;/.
///
/// Every file it writes is recorded with its SHA-256 in a state file inside the data folder (shared
/// by all mod-manager profiles, like the game folder itself). It only overwrites or deletes a file
/// whose current hash matches the recorded one, so files from the game or other mods are never
/// touched.
/// </summary>
public sealed class RuntimeInstaller
{
    public const string StateFileName = "PeakXR.Preloader.state";
    public const string ManifestFileName = "UnitySubsystemsManifest.json";
    private const string SubsystemsFolder = "UnitySubsystems";
    private const string PluginsFolder = "Plugins/x86_64";

    private static readonly Regex LibraryNameRegex = new(
        "\"libraryName\"\\s*:\\s*\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1)
    );

    private readonly string _dataDir;
    private readonly string _runtimeDepsDir;
    private readonly ManualLogSource _log;
    private readonly string _statePath;
    private readonly Dictionary<string, string> _state = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeInstaller(string dataDir, string runtimeDepsDir, ManualLogSource log)
    {
        _dataDir = Path.GetFullPath(dataDir);
        _runtimeDepsDir = Path.GetFullPath(runtimeDepsDir);
        _log = log;
        _statePath = Path.Combine(_dataDir, StateFileName);
    }

    /// <summary>Installs <paramref name="mode"/> and removes PeakXR manifests of any other runtime.
    /// With <paramref name="openVRFallback"/> and mode OpenXR, the OpenVR plugin is installed next to
    /// it so the plugin can fall back to it when OpenXR does not start (it only registers subsystem
    /// descriptors; nothing starts until a loader asks). Returns the mode that is actually usable
    /// (Disabled when the runtime files are missing).</summary>
    public RuntimeMode Apply(RuntimeMode mode, bool openVRFallback = false)
    {
        _log.LogInfo($"Game data folder: {_dataDir}");
        _log.LogInfo($"Runtime files: {_runtimeDepsDir}");
        LoadState();

        string activeManifest = null;
        string fallbackManifest = null;
        if (mode != RuntimeMode.Disabled)
        {
            activeManifest = Install(mode);
            if (activeManifest == null)
            {
                _log.LogError(
                    $"{mode} runtime files are missing or incomplete; starting without VR"
                );
                mode = RuntimeMode.Disabled;
            }
        }
        if (mode == RuntimeMode.OpenXR && openVRFallback)
        {
            fallbackManifest = Install(RuntimeMode.OpenVR);
            _log.LogInfo(
                fallbackManifest == null
                    ? "OpenVR fallback files not found; no fallback after OpenXR"
                    : "OpenVR plugin installed as fallback"
            );
        }

        // Disabled, or switched runtime: drop every manifest PeakXR wrote except the active one.
        foreach (var rel in _state.Keys.ToList())
        {
            if (
                rel.StartsWith(SubsystemsFolder + "/", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rel, activeManifest, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rel, fallbackManifest, StringComparison.OrdinalIgnoreCase)
            )
                RemoveTracked(rel);
        }

        ReportForeignManifests(activeManifest);
        SaveState();
        return mode;
    }

    /// <summary>Returns the relative path of the installed manifest, or null when the runtime cannot be installed.</summary>
    private string Install(RuntimeMode mode)
    {
        var src = Path.Combine(_runtimeDepsDir, mode.ToString());
        var manifest = Path.Combine(src, ManifestFileName);
        if (!Directory.Exists(src) || !File.Exists(manifest))
        {
            _log.LogError($"Missing {manifest}");
            return null;
        }

        var libraryName = ReadLibraryName(manifest);
        if (libraryName == null)
        {
            _log.LogError($"No libraryName in {manifest}");
            return null;
        }

        var dlls = Directory.GetFiles(src, "*.dll");
        if (
            !dlls.Any(d =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(d),
                    libraryName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            _log.LogError(
                $"Manifest names native library '{libraryName}', but {src} has no {libraryName}.dll"
            );
            return null;
        }

        _log.LogInfo($"Installing {mode} runtime ({libraryName}, {dlls.Length} native DLLs)");
        foreach (var dll in dlls)
            Sync(dll, PluginsFolder + "/" + Path.GetFileName(dll));

        var manifestRel = SubsystemsFolder + "/" + libraryName + "/" + ManifestFileName;
        Sync(manifest, manifestRel);
        return manifestRel;
    }

    private static string ReadLibraryName(string manifestPath)
    {
        var match = LibraryNameRegex.Match(File.ReadAllText(manifestPath));
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    /// <summary>Copies <paramref name="src"/> to the data-relative path when missing or outdated.</summary>
    private void Sync(string src, string rel)
    {
        var dst = Full(rel);
        var srcHash = Sha256(src);

        if (File.Exists(dst))
        {
            var dstHash = Sha256(dst);
            if (dstHash == srcHash)
            {
                // Same bytes as ours (state file lost, second profile): track it so Disabled removes it.
                _state[rel] = srcHash;
                _log.LogDebug($"Up to date: {rel}");
                return;
            }

            if (!_state.TryGetValue(rel, out var recorded) || recorded != dstHash)
            {
                // Not ours (game, other mod or user edit): leave it alone.
                _state.Remove(rel);
                _log.LogWarning(
                    $"{rel} exists but was not written by PeakXR (or was changed since); leaving it as is. "
                        + "If VR fails to start, remove it or the mod that installed it."
                );
                return;
            }
            _log.LogInfo($"Updating {rel}");
        }
        else
        {
            _log.LogInfo($"Installing {rel}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        var tmp = dst + ".peakxr-tmp";
        File.Copy(src, tmp, true);
        if (File.Exists(dst))
            File.Delete(dst);
        File.Move(tmp, dst);
        _state[rel] = srcHash;
    }

    private void RemoveTracked(string rel)
    {
        var path = Full(rel);
        var recorded = _state[rel];
        _state.Remove(rel);
        if (!File.Exists(path))
            return;

        if (Sha256(path) != recorded)
        {
            _log.LogWarning($"{rel} changed since PeakXR wrote it; leaving it as is");
            return;
        }

        _log.LogInfo($"Removing {rel}");
        File.Delete(path);

        // Remove the folders we may have created, but only when empty.
        var subsystemsRoot = Full(SubsystemsFolder);
        for (
            var dir = Path.GetDirectoryName(path);
            dir != null && dir.Length >= subsystemsRoot.Length;
            dir = Path.GetDirectoryName(dir)
        )
        {
            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                break;
            Directory.Delete(dir);
        }
    }

    /// <summary>Warns about XR manifests from other sources (for example another VR mod).</summary>
    private void ReportForeignManifests(string activeManifest)
    {
        var root = Full(SubsystemsFolder);
        if (!Directory.Exists(root))
            return;
        foreach (
            var file in Directory.GetFiles(root, ManifestFileName, SearchOption.AllDirectories)
        )
        {
            var rel = Rel(file);
            if (
                _state.ContainsKey(rel)
                || string.Equals(rel, activeManifest, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            _log.LogWarning(
                $"Found XR manifest not written by PeakXR: {rel}. Another VR mod may be installed; it can conflict with PeakXR."
            );
        }
    }

    private void LoadState()
    {
        _state.Clear();
        if (!File.Exists(_statePath))
            return;
        foreach (var line in File.ReadAllLines(_statePath))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var tab = line.IndexOf('\t');
            if (tab != 64)
                continue;
            var rel = line.Substring(tab + 1).Trim().Replace('\\', '/');
            // Never follow a state entry outside the data folder.
            if (rel.Length == 0 || rel.Contains("..") || Path.IsPathRooted(rel))
                continue;
            if (File.Exists(Full(rel)))
                _state[rel] = line.Substring(0, tab).ToLowerInvariant();
        }
    }

    private void SaveState()
    {
        if (_state.Count == 0)
        {
            if (File.Exists(_statePath))
                File.Delete(_statePath);
            return;
        }

        var sb = new StringBuilder();
        sb.Append(
            "# Files written by the PeakXR preloader (sha256<TAB>path relative to this folder).\n"
        );
        sb.Append("# PeakXR only updates or deletes files listed here whose hash still matches.\n");
        foreach (var pair in _state.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append(pair.Value).Append('\t').Append(pair.Key).Append('\n');
        File.WriteAllText(_statePath, sb.ToString());
    }

    private string Full(string rel) =>
        Path.Combine(_dataDir, rel.Replace('/', Path.DirectorySeparatorChar));

    private string Rel(string full) =>
        full.Substring(_dataDir.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
