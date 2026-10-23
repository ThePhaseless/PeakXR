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

    /// <summary>
    /// A setting whose scale changed along with its default (HapticStrength: 0.5 became 1 and every value means
    /// half as much). A saved value at <paramref name="oldDefault"/> moves to <paramref name="newDefault"/>; any other
    /// saved value is multiplied by <paramref name="factor"/> and clamped to [<paramref name="min"/>,
    /// <paramref name="max"/>], so it still means what the player chose. A value that was not
    /// <paramref name="existed"/> in the file (written fresh on the new scale) and a migration that
    /// <paramref name="alreadyRan"/> leave it as it is.
    /// </summary>
    public static float Rescaled(
        float saved,
        float oldDefault,
        float newDefault,
        float factor,
        float min,
        float max,
        bool existed,
        bool alreadyRan
    )
    {
        if (alreadyRan || !existed)
            return saved;
        if (MathF.Abs(saved - oldDefault) < 1e-4f)
            return newDefault;
        return Math.Clamp(saved * factor, min, max);
    }

    /// <summary>
    /// <see cref="Rescaled"/> from the text the config file held (<paramref name="raw"/>, null when it held none).
    /// True with the value to write when it should change. An unparseable value is left alone: BepInEx already fell
    /// back to the new default for it, and rescaling that would be wrong.
    /// </summary>
    public static bool RescaleSaved(
        string raw,
        float oldDefault,
        float newDefault,
        float factor,
        float min,
        float max,
        bool alreadyRan,
        out float value
    )
    {
        value = 0f;
        if (
            alreadyRan
            || raw == null
            || !float.TryParse(
                raw.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float saved
            )
        )
            return false;
        value = Rescaled(saved, oldDefault, newDefault, factor, min, max, true, false);
        return true;
    }

    /// <summary>Config key that records one migration ran: the setting and the default it moved off, so a later
    /// default change of the same setting gets its own marker.</summary>
    public static string MarkerKey(string section, string key, float oldDefault) =>
        section + "." + key + ".From" + oldDefault.ToString(CultureInfo.InvariantCulture);
}
