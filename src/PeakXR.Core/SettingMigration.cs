using System;

namespace PeakXR.Core;

/// <summary>
/// When a default changes, a saved config still holds the old one and the player never gets the fix. A value
/// still equal to the old default was never chosen, so it moves to the new default; anything else was the
/// player's choice and stays.
/// </summary>
public static class SettingMigration
{
    /// <summary>Whether <paramref name="saved"/> should move from <paramref name="oldDefault"/> to
    /// <paramref name="newDefault"/>.</summary>
    public static bool ShouldMove(float saved, float oldDefault, float newDefault) =>
        MathF.Abs(saved - oldDefault) < 1e-4f && MathF.Abs(saved - newDefault) >= 1e-4f;
}
