namespace PeakXR.Core;

/// <summary><c>UI.ColorBlindNames</c>: when a target's name shows although <c>UI.TargetNames</c> is off.</summary>
public enum ColorBlindNames
{
    /// <summary>While the game's own colour-blind setting is on.</summary>
    FollowGame,
    On,
    Off,
}

/// <summary>
/// Target names are hidden in VR by default (the glow says what the hand would take), but some items, such as the
/// berries, differ only by colour, and their name is the only other clue. In colour-blind mode every item shows its
/// name. shortcut: all items, not only the colour-only ones; narrow it if the extra names feel noisy.
/// </summary>
public static class TargetNameRule
{
    public static bool Show(
        bool targetNames,
        ColorBlindNames mode,
        bool gameColorBlind,
        bool isItem
    ) =>
        targetNames
        || (
            isItem
            && (
                mode == ColorBlindNames.On || (mode == ColorBlindNames.FollowGame && gameColorBlind)
            )
        );
}
