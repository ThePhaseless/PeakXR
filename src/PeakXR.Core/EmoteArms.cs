using System;

namespace PeakXR.Core;

/// <summary>Who drives a tracked avatar's arms while it plays an emote.</summary>
public enum EmoteArmsMode
{
    /// <summary>Tracked arms stay on, except for emotes listed as full-body (default).</summary>
    TrackedUnlessFullBody,

    /// <summary>Tracked arms always win; emotes only move the rest of the body.</summary>
    AlwaysTracked,

    /// <summary>Every emote plays its own arm animation.</summary>
    AlwaysAnimated,
}

/// <summary>Decides whether an emote suspends the tracked arm override of an avatar.</summary>
public static class EmoteArms
{
    /// <summary>Animator states that move the whole body; tracked arms would fight them.</summary>
    public const string DefaultFullBody = "A_Scout_Emote_BackFlip";

    public static readonly string[] ModeNames = Enum.GetNames(typeof(EmoteArmsMode));

    /// <summary>Parses a mode name; unknown names fall back to <see cref="EmoteArmsMode.TrackedUnlessFullBody"/>.</summary>
    public static EmoteArmsMode ParseMode(string name) =>
        Enum.TryParse(name, out EmoteArmsMode mode) && Enum.IsDefined(typeof(EmoteArmsMode), mode)
            ? mode
            : EmoteArmsMode.TrackedUnlessFullBody;

    /// <summary>
    /// True when the arms should follow the emote animation instead of the controllers.
    /// <paramref name="fullBodyList"/> is a comma-separated list of animator state names (case-insensitive).
    /// </summary>
    public static bool SuspendArms(EmoteArmsMode mode, string emote, string fullBodyList)
    {
        switch (mode)
        {
            case EmoteArmsMode.AlwaysAnimated:
                return true;
            case EmoteArmsMode.AlwaysTracked:
                return false;
            default:
                return IsListed(emote, fullBodyList);
        }
    }

    static bool IsListed(string emote, string list)
    {
        if (string.IsNullOrEmpty(emote) || string.IsNullOrEmpty(list))
            return false;
        int start = 0;
        while (start <= list.Length)
        {
            int end = list.IndexOf(',', start);
            if (end < 0)
                end = list.Length;
            int a = start,
                b = end;
            while (a < b && char.IsWhiteSpace(list[a]))
                a++;
            while (b > a && char.IsWhiteSpace(list[b - 1]))
                b--;
            if (
                b - a == emote.Length
                && string.Compare(
                    list,
                    a,
                    emote,
                    0,
                    emote.Length,
                    StringComparison.OrdinalIgnoreCase
                ) == 0
            )
                return true;
            start = end + 1;
        }
        return false;
    }
}
