using System.Collections;
using System.IO;
using PeakXR.Core;
using PeakXR.Diagnostics;
using PeakXR.Framework;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary><c>04b-bug-report</c>: debug.report saves a complete, well-formed report folder.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// Drops a marker, turns <c>Debug.Mode</c> on for 1.5 s (so the pose recorder fills in VR), then saves a report
    /// from code and waits for it. The folder must hold report.txt (its header, the build section and the marker),
    /// log.txt (the marker line), config.cfg (the Debug section), a PNG screenshot and, in VR, poses.csv with the
    /// recorder's header and full rows. No file may carry the local player's nickname. The setting goes back.
    /// </summary>
    IEnumerator BugReportCheck()
    {
        if (!FeatureManager.IsActive<DebugFeature>())
        {
            Fail("debug.report not active");
            yield break;
        }
        bool modeBefore = DebugFeature.Mode.Value;
        DebugFeature.Mode.Value = true;
        try
        {
            int marker = DebugLog.Mark("self-test");
            yield return Wait(1.5f);
            int before = BugReport.Finished;
            if (!BugReport.Save(false, Vector3.zero, "self-test"))
            {
                Fail($"the report did not start: {BugReport.LastError ?? "busy"}");
                yield break;
            }
            yield return Until(
                () => BugReport.Finished > before,
                10f,
                "the report was not written within 10 s"
            );
            if (!stepOk)
                yield break;
            var dir = BugReport.LastFolder;
            if (BugReport.LastError != null)
            {
                Fail($"{dir}: {BugReport.LastError}");
                yield break;
            }
            var shot = Path.Combine(dir, "screenshot.png");
            yield return WaitFor(() => File.Exists(shot) && new FileInfo(shot).Length > 8, 5f);
            CheckReportFiles(dir, marker);
        }
        finally
        {
            DebugFeature.Mode.Value = modeBefore;
        }
    }

    void CheckReportFiles(string dir, int marker)
    {
        string Read(string name)
        {
            var p = Path.Combine(dir, name);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }
        var text = Read("report.txt");
        var log = Read("log.txt");
        var config = Read("config.cfg");
        var poses = Read("poses.csv");
        var shot = Path.Combine(dir, "screenshot.png");
        if (
            text == null
            || !text.StartsWith("PeakXR bug report\n", System.StringComparison.Ordinal)
        )
            Fail("report.txt missing or without its header");
        else if (!text.Contains("[Build]\nmod: PeakXR ") || !text.Contains($"#{marker} "))
            Fail("report.txt lacks the build section or the marker");
        if (log == null || !log.Contains($"marker #{marker}"))
            Fail("log.txt missing or without the marker line");
        if (config == null || !config.Contains("[Debug]"))
            Fail("config.cfg missing or without the Debug section");
        if (!IsPng(shot))
            Fail("screenshot.png missing or not a PNG");
        if (VRSession.Active)
        {
            var lines = poses?.Split('\n');
            int columns = PoseCsv.Header.Split(',').Length;
            if (lines == null || lines.Length < 6 || lines[0] != PoseCsv.Header)
                Fail(
                    $"poses.csv missing, without its header or under 5 rows ({lines?.Length ?? 0} lines)"
                );
            else if (lines[1].Split(',').Length < columns)
                Fail($"poses.csv row has {lines[1].Split(',').Length} columns, header {columns}");
        }
        var nick = PhotonNetwork.LocalPlayer?.NickName;
        if (
            nick is { Length: >= 3 }
            && (
                (text?.Contains(nick) ?? false)
                || (log?.Contains(nick) ?? false)
                || (poses?.Contains(nick) ?? false)
            )
        )
            Fail("a report file carries the local player's nickname");
        Note(
            $"{dir}: report {text?.Length ?? 0} B, log {log?.Length ?? 0} B, poses {poses?.Length ?? 0} B"
        );
    }

    static bool IsPng(string path)
    {
        if (!File.Exists(path))
            return false;
        var head = new byte[8];
        using (var f = File.OpenRead(path))
            if (f.Read(head, 0, 8) != 8)
                return false;
        return head[0] == 0x89
            && head[1] == (byte)'P'
            && head[2] == (byte)'N'
            && head[3] == (byte)'G';
    }
}
