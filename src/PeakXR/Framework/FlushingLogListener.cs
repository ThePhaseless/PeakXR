using System;
using System.IO;
using BepInEx.Logging;

namespace PeakXR.Framework;

/// <summary>
/// Writes PeakXR's log lines to their own file and flushes each one. BepInEx buffers its disk log, so a native
/// crash (XR runtime, graphics driver) loses the last lines, which are exactly the ones that say where it died.
/// Only installed for test runs (<c>-peakxr-selftest</c>).
/// </summary>
internal sealed class FlushingLogListener : ILogListener
{
    private StreamWriter writer;

    public FlushingLogListener(string path)
    {
        writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (writer == null || eventArgs.Source.SourceName != MyPluginInfo.PLUGIN_NAME)
            return;
        try
        {
            writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{eventArgs.Level}] {eventArgs.Data}");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // A full disk or a lost file must not throw into every Plugin.Log call: stop writing the file.
            writer = null;
            UnityEngine.Debug.LogWarning(
                $"[PeakXR] trace file stopped (write failed: {e.Message})"
            );
        }
    }

    public void Dispose() => writer?.Dispose();
}
