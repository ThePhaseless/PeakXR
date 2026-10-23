using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Diagnostics;

/// <summary>
/// The always-on history a bug report is made from: the last PeakXR log lines, Unity errors with their stacks, the
/// last refusals, markers and grip decisions. Every write stores a reference or a few value fields in a preallocated
/// <see cref="Ring{T}"/> (the logged string already exists), so normal play pays almost nothing. Attached from
/// <c>Plugin.Awake</c>, before any feature logs. Under <c>Debug.Mode</c> it also writes <c>PeakXR-trace.log</c> the way
/// the self-test does, one flushed line at a time, and keeps Debug-level lines.
/// </summary>
internal static class DebugLog
{
    public const int LogLines = 500;
    public const int UnityErrors = 100;
    public const int RefusalCount = 50;
    public const int MarkerCount = 50;

    private static readonly object Gate = new();
    private static readonly Ring<LogEntry> Lines = new(LogLines);
    private static readonly Ring<LogEntry> Errors = new(UnityErrors);
    public static readonly Ring<RefusalEntry> Refusals = new(RefusalCount);
    public static readonly Ring<MarkerEntry> Markers = new(MarkerCount);

    /// <summary>The last grip decision per hand (0 left, 1 right) and its wall clock.</summary>
    public static readonly string[] GripNotes = new string[2];
    public static readonly long[] GripNoteWalls = new long[2];

    // Cached level names: LogLevel.ToString() would allocate on every line.
    private static readonly string[] LevelNames =
    {
        "Fatal",
        "Error",
        "Warning",
        "Message",
        "Info",
        "Debug",
    };

    private static bool attached;
    private static StreamWriter trace;

    /// <summary><c>Debug.Mode</c>: the verbose traces run. Read on hot paths, so a plain field.</summary>
    public static bool Verbose;

    /// <summary>The self-test's own trace listener writes PeakXR-trace.log; debug mode then leaves the file to it.</summary>
    private static bool SelfTestTrace =>
        Array.IndexOf(Environment.GetCommandLineArgs(), "-peakxr-selftest") >= 0;

    public static string TracePath => Path.Combine(Paths.BepInExRootPath, "PeakXR-trace.log");

    public static void Attach()
    {
        if (attached)
            return;
        attached = true;
        BepInEx.Logging.Logger.Listeners.Add(new Listener());
        Application.logMessageReceivedThreaded += OnUnityLog;
    }

    /// <summary>Turns the verbose traces and the trace file on or off. The file opens once per session (truncated),
    /// so switching debug mode off and on again appends.</summary>
    public static void SetVerbose(bool on)
    {
        lock (Gate)
        {
            Verbose = on;
            if (on && trace == null && !SelfTestTrace)
            {
                try
                {
                    trace = new StreamWriter(TracePath, append: false) { AutoFlush = true };
                }
                catch (IOException e)
                {
                    Plugin.Log.LogWarning($"[debug] could not open {TracePath}: {e.Message}");
                }
            }
        }
    }

    public static void Refusal(bool left, DenyReason reason, Vector3 at)
    {
        var e = new RefusalEntry
        {
            Wall = DateTime.Now.Ticks,
            Left = left,
            Reason = reason,
            At = new System.Numerics.Vector3(at.x, at.y, at.z),
        };
        lock (Gate)
            Refusals.Add(e);
    }

    /// <summary>A grip decision note (the string is built once per grip press by its owner); logged in debug mode.</summary>
    public static void GripNote(bool left, string note)
    {
        int i = left ? 0 : 1;
        GripNotes[i] = note;
        GripNoteWalls[i] = DateTime.Now.Ticks;
        if (Verbose)
            Plugin.Log.LogInfo("[interact.physical] " + note);
    }

    /// <summary>Drops marker n into the ring and the log (so the trace and log.txt show it in place).</summary>
    public static int Mark(string note)
    {
        int n;
        lock (Gate)
        {
            n = (int)Markers.Total + 1;
            Markers.Add(
                new MarkerEntry
                {
                    Wall = DateTime.Now.Ticks,
                    Number = n,
                    Note = note,
                }
            );
        }
        Plugin.Log.LogMessage(
            $"[debug] ===== marker #{n}: something happened here{(note != null ? " (" + note + ")" : "")} ====="
        );
        return n;
    }

    /// <summary>Copies of the rings for a report, oldest first.</summary>
    public static void Snapshot(
        out LogEntry[] lines,
        out LogEntry[] errors,
        out RefusalEntry[] refusals,
        out MarkerEntry[] markers
    )
    {
        lock (Gate)
        {
            lines = Lines.ToArray();
            errors = Errors.ToArray();
            refusals = Refusals.ToArray();
            markers = Markers.ToArray();
        }
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type is not (LogType.Error or LogType.Exception or LogType.Assert))
            return;
        var e = new LogEntry
        {
            Wall = DateTime.Now.Ticks,
            Level =
                type == LogType.Exception ? "Exception"
                : type == LogType.Assert ? "Assert"
                : "Error",
            Data = condition,
            Stack = stackTrace,
        };
        lock (Gate)
            Errors.Add(e);
    }

    private static string LevelName(LogLevel level) =>
        (level & LogLevel.Fatal) != 0 ? LevelNames[0]
        : (level & LogLevel.Error) != 0 ? LevelNames[1]
        : (level & LogLevel.Warning) != 0 ? LevelNames[2]
        : (level & LogLevel.Message) != 0 ? LevelNames[3]
        : (level & LogLevel.Info) != 0 ? LevelNames[4]
        : LevelNames[5];

    private sealed class Listener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs.Source.SourceName != MyPluginInfo.PLUGIN_NAME)
                return;
            bool debug = (eventArgs.Level & LogLevel.Debug) != 0;
            if (debug && !Verbose)
                return;
            var e = new LogEntry
            {
                Wall = DateTime.Now.Ticks,
                Level = LevelName(eventArgs.Level),
                Data = eventArgs.Data,
            };
            lock (Gate)
            {
                Lines.Add(e);
                if (Verbose && trace != null)
                    trace.WriteLine(
                        $"{new DateTime(e.Wall):HH:mm:ss.fff} [{e.Level}] {eventArgs.Data}"
                    );
            }
        }

        public void Dispose() { }
    }
}
