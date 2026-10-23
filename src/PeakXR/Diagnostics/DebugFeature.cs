using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.UI.Settings;
using PeakXR.XRInput;
using UnityEngine;

namespace PeakXR.Diagnostics;

/// <summary>
/// Debug and bug-report mode (feature id <c>debug.report</c>, docs/debugging.md). Always on and cheap: the log,
/// refusal, marker and grip-note rings (<see cref="DebugLog"/>), the report gesture (<see cref="DebugGesture"/>) and
/// the VR tab's "Save bug report" and "Drop a marker" buttons. <c>Debug.Mode</c> adds the verbose traces, the trace
/// file, the 10 Hz pose recorder and, with <c>Debug.ShowFrameStats</c>, a frame readout on the wrist. Runs in flat
/// play too (the self-test saves a report there); everything VR-only checks <c>VRSession.Active</c>.
/// </summary>
public sealed class DebugFeature : Feature
{
    public override string Id => "debug.report";

    // First, so the traces are on before the other features install and log.
    public override int Order => -2000;

    public override bool VROnly => false;

    internal static ConfigEntry<bool> Mode;
    internal static ConfigEntry<bool> ShowFrameStats;

    protected internal override void Install()
    {
        const string section = "Debug";
        Mode = Plugin.Settings.Bind(
            section,
            "Mode",
            false,
            "Debug mode for bug reports: turns on the detailed traces, writes BepInEx/PeakXR-trace.log and records "
                + "the last 30 s of head and hand poses, buttons and body state into each report. Costs a little "
                + "frame time; leave it off unless you are chasing a bug."
        );
        ShowFrameStats = Plugin.Settings.Bind(
            section,
            "ShowFrameStats",
            false,
            "With debug mode on, show frames per second and frame time on your off-hand wrist."
        );
        DebugLog.SetVerbose(Mode.Value);
        Mode.SettingChanged += (_, _) =>
        {
            DebugLog.SetVerbose(Mode.Value);
            Plugin.Log.LogInfo($"[debug] debug mode {(Mode.Value ? "on" : "off")}");
        };
        VRTab.AddButtonAfter(
            Mode,
            "PEAKXR_SAVE_BUG_REPORT",
            "Bug report",
            "Save bug report",
            (at, left) => BugReport.Save(left, at, "VR tab")
        );
        VRTab.AddButtonAfter(
            ShowFrameStats,
            "PEAKXR_DROP_MARKER",
            "Marker",
            "Drop a marker",
            (at, left) => DebugGesture.DropMarker(left, at, "VR tab")
        );
        XRInputSampler.AfterSample += DebugGesture.Update;
        var go = new GameObject("PeakXR_Debug");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DebugDriver>();
    }
}
