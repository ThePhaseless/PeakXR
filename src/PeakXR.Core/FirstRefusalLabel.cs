namespace PeakXR.Core;

/// <summary>
/// About twenty refusal reasons share the slash glyph, so a glyph alone cannot teach its cause. The first time each
/// reason fires its text label shows too (<c>UI.FirstTimeRefusalLabels</c>), then only the glyph. Which reasons were
/// seen is one bit per <see cref="DenyReason"/> in a ulong, saved in the config so it survives restarts.
/// </summary>
public static class FirstRefusalLabel
{
    /// <summary>Whether <paramref name="reason"/> fired before.</summary>
    public static bool Seen(ulong seen, DenyReason reason) =>
        Valid(reason) && (seen & Bit(reason)) != 0;

    /// <summary>Whether this firing of <paramref name="reason"/> shows its label: always with
    /// <paramref name="actionHintText"/>, else only the first time while <paramref name="firstTime"/> is on. Marks the
    /// reason seen whenever a label shows.</summary>
    public static bool Show(bool actionHintText, bool firstTime, ref ulong seen, DenyReason reason)
    {
        if (!Valid(reason))
            return actionHintText;
        bool first = (seen & Bit(reason)) == 0;
        if (actionHintText || firstTime)
            seen |= Bit(reason);
        return actionHintText || (firstTime && first);
    }

    private static bool Valid(DenyReason reason) =>
        reason > DenyReason.None && reason < DenyReason.Count && (int)reason < 64;

    private static ulong Bit(DenyReason reason) => 1ul << (int)reason;
}
