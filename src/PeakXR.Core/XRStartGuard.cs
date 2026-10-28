using System.Globalization;

namespace PeakXR.Core;

/// <summary>
/// Crash-loop guard for the XR start. The plugin records each XR start in a file right before the runtime's native
/// init calls and deletes it as soon as they return, so the file only survives a start that took the process down
/// inside them. Two such starts in a row make launches flat until the player asks to try VR again, so a native
/// crash in the runtime cannot keep the game from starting.
/// </summary>
public static class XRStartGuard
{
    public const int FlatAfter = 2;

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
