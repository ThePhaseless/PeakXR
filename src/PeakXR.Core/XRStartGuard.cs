using System.Globalization;

namespace PeakXR.Core;

/// <summary>
/// Crash-loop guard for the XR start. The plugin records each launch's XR start in a file right before the first
/// runtime's native init and deletes it once a runtime runs and reports a frame shown (<see cref="CloseAt"/>), or every
/// runtime failed cleanly, so the file only survives a launch whose process ended during the start. Two such launches
/// in a row make launches flat until the player asks to try VR again, so a native crash in the runtime cannot keep the
/// game from starting.
/// </summary>
public static class XRStartGuard
{
    public const int FlatAfter = 2;

    /// <summary>Frames rendered after the session is set up before the guard may close: the first ones wait on the
    /// display and submit to the compositor, native calls that can still take the process down.</summary>
    public const int MinFrames = 3;

    /// <summary>How long a running session may go without the runtime reporting a frame shown before the guard
    /// closes anyway (a runtime that keeps the app hidden, or reports nothing). The process survived that long.</summary>
    public const float ShownTimeoutSeconds = 10f;

    public enum Close
    {
        Open,

        /// <summary>The runtime reported a frame shown.</summary>
        Shown,

        /// <summary>No report within <see cref="ShownTimeoutSeconds"/>; the frames were only rendered.</summary>
        TimedOut,
    }

    /// <summary>Whether the start is over, <paramref name="frames"/> rendered and <paramref name="seconds"/> after the
    /// session was set up; <paramref name="shown"/>: the runtime reports a frame of ours presented or visible.</summary>
    public static Close CloseAt(bool shown, int frames, float seconds) =>
        frames < MinFrames ? Close.Open
        : shown ? Close.Shown
        : seconds >= ShownTimeoutSeconds ? Close.TimedOut
        : Close.Open;

    /// <summary>OpenXR session VISIBLE (4) or FOCUSED (5): the runtime shows the app's frames to the user.</summary>
    public static bool OpenXRShown(int state) => state is 4 or 5;

    /// <summary>Unfinished starts recorded in the file's <paramref name="text"/>; 0 when missing or unreadable.</summary>
    public static int Parse(string text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;

    public static bool StartFlat(int unfinished) => unfinished >= FlatAfter;

    /// <summary>The file's text for one more start.</summary>
    public static string Next(int unfinished) =>
        (unfinished + 1).ToString(CultureInfo.InvariantCulture);
}
