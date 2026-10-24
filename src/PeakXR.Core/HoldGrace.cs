namespace PeakXR.Core;

/// <summary>What a pointed hold's ray dropout means this frame.</summary>
public enum HoldGraceStep
{
    /// <summary>Nothing to do: no hold, or the ray is on its target.</summary>
    None,

    /// <summary>The ray left the target only a moment ago: keep the hold on it.</summary>
    Keep,

    /// <summary>The ray stayed off past the grace: let the game drop the hold, with the cancel feel. Once.</summary>
    Drop,
}

/// <summary>
/// A pointed hold (interact held on what the hand aims at) survives a brief ray dropout. The game drops a held
/// interact the frame its target leaves the ray (Interaction.cs:128-132) and never restarts it while interact stays
/// pressed, and a real hand's ray grazes off edges as it jitters. For <see cref="Grace"/> the hold keeps its target;
/// past it the game drops it as before and the hand feels the cancel. Allocation-free.
/// </summary>
public sealed class HoldGrace
{
    /// <summary>Seconds the ray may be off the held target before the hold drops.</summary>
    public const float Grace = 0.2f;

    private float lostAt = -1f;
    private bool dropped;

    /// <param name="holding">A hold runs: interact pressed on a held target.</param>
    /// <param name="seen">The ray is on that target this frame.</param>
    public HoldGraceStep Update(bool holding, bool seen, float now)
    {
        if (!holding)
        {
            lostAt = -1f;
            dropped = false;
            return HoldGraceStep.None;
        }
        if (seen)
        {
            lostAt = -1f;
            dropped = false;
            return HoldGraceStep.None;
        }
        if (dropped)
            return HoldGraceStep.None;
        if (lostAt < 0f)
            lostAt = now;
        if (now - lostAt <= Grace)
            return HoldGraceStep.Keep;
        dropped = true;
        return HoldGraceStep.Drop;
    }
}
