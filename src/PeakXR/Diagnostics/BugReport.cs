using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Render;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using PeakXR.XR;
using PeakXR.XRInput;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakXR.Diagnostics;

/// <summary>
/// Saves one bug report folder, <c>BepInEx/PeakXR-reports/yyyyMMdd-HHmmss/</c>: report.txt, log.txt, poses.csv (debug
/// mode), config.cfg and screenshot.png. Everything the game owns is read on the main thread into plain data
/// (<see cref="BugReportInfo"/>, ring copies); a pool thread composes and writes the files, then
/// <see cref="Poll"/> (main thread) plays the notice cue. Local files only. Player names, the OS account in paths and
/// Steam IDs are replaced (<see cref="BugReportText.Redact"/>); players appear as Photon actor numbers.
/// </summary>
internal static class BugReport
{
    public static string Root => Path.Combine(Paths.BepInExRootPath, "PeakXR-reports");

    private static int busy;
    private static volatile bool finished;
    private static string error;
    private static bool cueLeft;
    private static Vector3 cueAt;

    /// <summary>The folder of the last finished report, and whether it failed, for the self-test.</summary>
    public static string LastFolder { get; private set; }
    public static string LastError { get; private set; }
    public static int Finished { get; private set; }

    public static bool Busy => System.Threading.Volatile.Read(ref busy) != 0;

    private sealed class Job
    {
        public string Folder;
        public BugReportInfo Info;
        public LogEntry[] Lines;
        public LogEntry[] Errors;
        public PoseSample[] Poses;
        public KeyValuePair<string, string>[] Names;
        public string Config;
    }

    /// <summary>Starts a report (main thread). False, with the wait cue, while one is still being written.</summary>
    public static bool Save(bool left, Vector3 at, string source)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        {
            Affordance.Deny(left, at, DenyReason.TooSoon);
            return false;
        }
        string folder = null;
        try
        {
            Directory.CreateDirectory(Root);
            Prune();
            var name = BugReportText.FolderName(
                DateTime.Now,
                n => Directory.Exists(Path.Combine(Root, n))
            );
            folder = Path.Combine(Root, name);
            Directory.CreateDirectory(folder);
            var job = Capture(folder, source);
            // The desktop mirror, once: Unity writes it at the end of this frame.
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "screenshot.png"));
            cueLeft = left;
            cueAt = at;
            Plugin.Log.LogMessage($"[debug] saving bug report {name} ({source})");
            ThreadPool.QueueUserWorkItem(_ => Write(job));
            return true;
        }
        catch (Exception e)
        {
            System.Threading.Volatile.Write(ref busy, 0);
            Plugin.Log.LogError($"[debug] bug report not saved ({folder}): {e}");
            LastError = e.Message;
            Affordance.Deny(left, at, DenyReason.CantUse);
            return false;
        }
    }

    /// <summary>Reports kept on disk, the new one included; each holds a log, a screenshot and pose history.</summary>
    private const int KeepReports = 20;

    /// <summary>Deletes the oldest report folders so the new one makes <see cref="KeepReports"/>.</summary>
    private static void Prune()
    {
        var names = new List<string>();
        foreach (var dir in Directory.GetDirectories(Root))
            names.Add(Path.GetFileName(dir));
        foreach (var stale in BugReportText.Stale(names, KeepReports - 1))
        {
            try
            {
                Directory.Delete(Path.Combine(Root, stale), recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Plugin.Log.LogWarning($"[debug] could not remove old report {stale}: {e.Message}");
            }
        }
    }

    /// <summary>Main thread, once per frame: confirms a finished report with the notice cue and its label.</summary>
    public static void Poll()
    {
        if (!finished)
            return;
        finished = false;
        Finished++;
        LastError = error;
        System.Threading.Volatile.Write(ref busy, 0);
        if (error != null)
        {
            Plugin.Log.LogError($"[debug] bug report {LastFolder} failed: {error}");
            Affordance.Deny(cueLeft, cueAt, DenyReason.CantUse);
            return;
        }
        Plugin.Log.LogMessage($"[debug] bug report saved: {LastFolder}");
        Affordance.Inform(
            cueLeft,
            cueAt,
            DenyReason.HappenedHere,
            DenyHint.At(cueAt),
            "Bug report saved",
            forceText: true
        );
    }

    private static void Write(Job job)
    {
        string failed = null;
        try
        {
            var names = job.Names;
            File.WriteAllText(
                Path.Combine(job.Folder, "report.txt"),
                BugReportText.Redact(BugReportText.Compose(job.Info), names)
            );
            File.WriteAllText(
                Path.Combine(job.Folder, "log.txt"),
                BugReportText.Redact(BugReportText.Log(job.Lines, job.Errors), names)
            );
            if (job.Poses != null && job.Poses.Length > 0)
            {
                var sb = new StringBuilder(job.Poses.Length * 400);
                sb.Append(PoseCsv.Header).Append('\n');
                foreach (var p in job.Poses)
                {
                    PoseCsv.Row(sb, p);
                    sb.Append('\n');
                }
                File.WriteAllText(Path.Combine(job.Folder, "poses.csv"), sb.ToString());
            }
            if (File.Exists(job.Config))
                File.Copy(job.Config, Path.Combine(job.Folder, "config.cfg"), true);
        }
        catch (Exception e)
        {
            failed = e.GetType().Name + ": " + e.Message;
        }
        error = failed;
        LastFolder = job.Folder;
        finished = true;
    }

    // ---------------------------------------------------------------- capture (main thread)

    private static Job Capture(string folder, string source)
    {
        var info = new BugReportInfo { Saved = DateTime.Now };
        info.Add("Report", "saved by", source);
        info.Add("Report", "debug mode", DebugLog.Verbose ? "on" : "off");
        Build(info);
        Xr(info);
        World(info);
        Player(info);
        Multiplayer(info);
        Comfort(info);
        Features(info);
        DebugLog.Snapshot(out var lines, out var errors, out var refusals, out var markers);
        foreach (var r in refusals)
            info.Refusals.Add(r);
        foreach (var m in markers)
            info.Markers.Add(m);
        for (int i = 0; i < 2; i++)
        {
            info.GripNotes[i] = DebugLog.GripNotes[i];
            info.GripNoteWalls[i] = DebugLog.GripNoteWalls[i];
        }
        return new Job
        {
            Folder = folder,
            Info = info,
            Lines = lines,
            Errors = errors,
            Poses = DebugLog.Verbose ? DebugDriver.Poses.ToArray() : null,
            Names = Names(),
            Config = Plugin.Settings.ConfigFilePath,
        };
    }

    /// <summary>One value; a game object that throws while it loads leaves "unavailable" instead of no report.</summary>
    private static void Try(BugReportInfo info, string section, string key, Func<string> value)
    {
        string v;
        try
        {
            v = value() ?? "none";
        }
        catch (Exception e)
        {
            v = "unavailable (" + e.GetType().Name + ")";
        }
        info.Add(section, key, v);
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);

    private static string Cfg(string section, string key)
    {
        var d = new ConfigDefinition(section, key);
        return Plugin.Settings.ContainsKey(d) ? Plugin.Settings[d].BoxedValue?.ToString() : "n/a";
    }

    private static void Build(BugReportInfo info)
    {
        const string s = "Build";
        Try(
            info,
            s,
            "mod",
            () =>
                MyPluginInfo.PLUGIN_NAME
                + " "
                + (
                    typeof(Plugin)
                        .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion
                    ?? MyPluginInfo.PLUGIN_VERSION
                )
        );
        Try(info, s, "game", () => Application.version);
        Try(info, s, "unity", () => Application.unityVersion);
        Try(info, s, "bepinex", () => typeof(Paths).Assembly.GetName().Version.ToString());
        Try(info, s, "os", () => SystemInfo.operatingSystem);
        Try(
            info,
            s,
            "gpu",
            () =>
                $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {SystemInfo.graphicsMemorySize} MB"
        );
        Try(
            info,
            s,
            "cpu",
            () => $"{SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB RAM"
        );
    }

    private static void Xr(BugReportInfo info)
    {
        const string s = "VR";
        Try(info, s, "requested", () => VRSession.Requested.ToString());
        Try(info, s, "running", () => $"{VRSession.Running}, active {VRSession.Active}");
        Try(info, s, "runtime", () => XRRuntimeInfo.Description);
        Try(info, s, "devices", XRInputSampler.DeviceNames);
        Try(
            info,
            s,
            "refresh",
            () =>
                F(
                    $"{FrameClock.RefreshHz:0.#} Hz from {FrameClock.Source}, headset rate {FrameClock.Trusted}"
                )
        );
        Try(
            info,
            s,
            "render scale",
            () =>
                F(
                    $"{RenderDriver.CurrentScale:0.00} now, setting {Cfg("Render", "RenderScale")}, eye texture {UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}"
                )
        );
        Try(
            info,
            s,
            "adaptive quality",
            () => $"{Cfg("Render", "AdaptiveQuality")}: {RenderDriver.AdaptiveState}"
        );
        Try(
            info,
            s,
            "frames",
            () => FrameClock.Summarize(FrameClock.Log).Line(RenderDriver.CurrentScale)
        );
        Try(info, s, "floor tracking", () => XRRuntimeInfo.FloorTracking.ToString());
    }

    private static void World(BugReportInfo info)
    {
        const string s = "Game";
        Try(info, s, "scene", () => SceneManager.GetActiveScene().name);
        Try(
            info,
            s,
            "segment",
            () =>
                Singleton<MapHandler>.Instance != null
                    ? MapHandler.CurrentSegmentNumber.ToString()
                    : "no map"
        );
        Try(info, s, "time scale", () => F($"{Time.timeScale:0.##}, frame {Time.frameCount}"));
        Try(info, s, "uptime", () => F($"{Time.realtimeSinceStartup:0} s"));
    }

    private static void Player(BugReportInfo info)
    {
        const string s = "Player";
        var c = Character.localCharacter;
        if (c == null)
        {
            info.Add(s, "character", "none");
            return;
        }
        var d = c.data;
        Try(
            info,
            s,
            "position",
            () => F($"{c.Center.x:0.00}, {c.Center.y:0.00}, {c.Center.z:0.00}")
        );
        Try(info, s, "grounded", () => d.isGrounded.ToString());
        Try(
            info,
            s,
            "climbing",
            () =>
                $"{d.isClimbingAnything} (wall {d.isClimbing}, rope {d.isRopeClimbing}, vine {d.isVineClimbing})"
        );
        Try(
            info,
            s,
            "holding",
            () =>
                d.currentItem != null
                    ? $"{d.currentItem.name} in the {(PeakXR.Avatar.LocalHold.ItemInLeftHand ? "left" : "right")} hand"
                    : "nothing"
        );
        Try(info, s, "stamina", () => F($"{d.currentStamina:0.00} (+{d.extraStamina:0.00} extra)"));
        Try(
            info,
            s,
            "state",
            () =>
                $"dead {d.dead}, passed out {d.passedOut}, fully {d.fullyPassedOut}, crouching {d.isCrouching}"
        );
        Try(
            info,
            s,
            "statuses",
            () =>
            {
                var st = c.refs.afflictions.currentStatuses;
                var sb = new StringBuilder();
                for (int i = 0; i < st.Length; i++)
                    if (st[i] > 0.001f)
                        sb.Append(sb.Length > 0 ? ", " : "")
                            .Append((CharacterAfflictions.STATUSTYPE)i)
                            .Append(' ')
                            .Append(
                                st[i]
                                    .ToString(
                                        "0.00",
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                            );
                return sb.Length > 0 ? sb.ToString() : "none";
            }
        );
    }

    private static void Multiplayer(BugReportInfo info)
    {
        const string s = "Multiplayer";
        Try(
            info,
            s,
            "room",
            () => $"in a room {PhotonNetwork.InRoom}, offline {PhotonNetwork.OfflineMode}"
        );
        Try(
            info,
            s,
            "players",
            () =>
            {
                var players = PhotonNetwork.PlayerList;
                int modded = 0,
                    vr = 0;
                foreach (var p in PhotonNetwork.PlayerListOthers)
                    if (
                        p.CustomProperties != null
                        && p.CustomProperties.TryGetValue(Handshake.Key, out var value)
                        && Handshake.TryParse(value, out _, out bool peerVr)
                    )
                    {
                        modded++;
                        if (peerVr)
                            vr++;
                    }
                var local = PhotonNetwork.LocalPlayer;
                return $"{players.Length} (you are player {(local != null ? local.ActorNumber : 0)}), PeakXR peers {modded}, in VR {vr}";
            }
        );
    }

    private static void Comfort(BugReportInfo info)
    {
        const string s = "Setup";
        Try(
            info,
            s,
            "one-handed",
            () => $"{Cfg("Accessibility", "OneHanded")} (active {VRInput.OneHanded})"
        );
        Try(info, s, "dominant hand", () => Cfg("Input", "DominantHand"));
        Try(
            info,
            s,
            "seated",
            () =>
                $"{Cfg("Comfort", "SeatedMode")} (rig {(VRRig.Instance != null && VRRig.Instance.SeatedMode)})"
        );
        Try(info, s, "comfort preset", () => Cfg("Comfort", "Preset"));
    }

    private static void Features(BugReportInfo info)
    {
        const string s = "Features";
        int active = 0;
        foreach (var f in FeatureManager.Features)
        {
            if (f.State == FeatureState.Active)
                active++;
            else
                info.Add(
                    s,
                    f.Id,
                    $"{f.State}{(f.FailureReason != null ? ": " + f.FailureReason : "")}"
                );
        }
        info.Add(s, "active", active.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Every name the files could carry, with what replaces it: Photon nicknames (Steam names) by actor
    /// number, character names by index, and the OS account.</summary>
    internal static KeyValuePair<string, string>[] Names()
    {
        var names = new List<KeyValuePair<string, string>>();
        try
        {
            foreach (var p in PhotonNetwork.PlayerList)
                names.Add(new(p.NickName, BugReportText.PlayerLabel(p.ActorNumber)));
            if (PhotonNetwork.LocalPlayer != null)
                names.Add(
                    new(
                        PhotonNetwork.LocalPlayer.NickName,
                        BugReportText.PlayerLabel(PhotonNetwork.LocalPlayer.ActorNumber)
                    )
                );
            for (int i = 0; i < Character.AllCharacters.Count; i++)
            {
                var c = Character.AllCharacters[i];
                if (c != null)
                    names.Add(new(c.characterName, $"character {i}"));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[debug] player names not all read for the privacy pass: {e.Message}"
            );
        }
        names.Add(new(Environment.UserName, "[user]"));
        return names.ToArray();
    }
}
