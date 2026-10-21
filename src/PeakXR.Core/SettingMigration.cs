using System;
using System.Globalization;

namespace PeakXR.Core;

/// <summary>
/// When a default changes, a saved config still holds the old one and the player never gets the fix. A value
/// still equal to the old default was never chosen, so it moves to the new default once; anything else was the
/// player's choice and stays, and so does the old value if the player sets it back after the move.
/// </summary>
public static class SettingMigration
{
    /// <summary>Whether <paramref name="saved"/> should move from <paramref name="oldDefault"/> to
    /// <paramref name="newDefault"/>; never again once this migration <paramref name="alreadyRan"/>.</summary>
    public static bool ShouldMove(
        float saved,
        float oldDefault,
        float newDefault,
        bool alreadyRan
    ) =>
        !alreadyRan
        && MathF.Abs(saved - oldDefault) < 1e-4f
        && MathF.Abs(saved - newDefault) >= 1e-4f;

    /// <summary>Config key that records one migration ran: the setting and the default it moved off, so a later
    /// default change of the same setting gets its own marker.</summary>
    public static string MarkerKey(string section, string key, float oldDefault) =>
        section + "." + key + ".From" + oldDefault.ToString(CultureInfo.InvariantCulture);
}
