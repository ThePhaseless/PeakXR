namespace PeakXR.Core;

/// <summary>
/// Watches one finished cast whose game code can fail with no feedback: it aims, tries, and when no try works it just
/// returns (the vine shooter's ten path tries, VineShooter.cs:36-69). The cast opens the watch, the aim check and a
/// successful try report in, and the end says whether the player gets a refusal.
/// </summary>
public sealed class CastWatch
{
    private bool aimed;
    private bool built;

    /// <summary>Between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public bool Open { get; private set; }

    public void Begin()
    {
        Open = true;
        aimed = false;
        built = false;
    }

    /// <summary>The game's aim check passed inside the cast, so it went on to try.</summary>
    public void Aimed()
    {
        if (Open)
            aimed = true;
    }

    /// <summary>One try worked.</summary>
    public void Built()
    {
        if (Open)
            built = true;
    }

    /// <summary>Closes the watch: <paramref name="failure"/> when the cast aimed but nothing came of it, else None.</summary>
    public DenyReason End(DenyReason failure)
    {
        bool failed = Open && aimed && !built;
        Open = false;
        return failed ? failure : DenyReason.None;
    }
}
