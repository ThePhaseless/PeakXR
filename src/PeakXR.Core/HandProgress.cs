namespace PeakXR.Core;

/// <summary>What the game's hold-progress ring is currently showing (UI_UseItemProgress priority order).</summary>
public enum ProgressSource
{
    None,
    ClimbingSpike,
    Item,
    Interactable,
}

public enum ProgressPulse
{
    None,

    /// <summary>The fill crossed a step boundary (a quarter by default).</summary>
    Step,

    /// <summary>The hold finished.</summary>
    Complete,
}

/// <summary>
/// Picks the hand that carries the progress ring: the hand doing the action, so the ring sits where the
/// player is looking anyway instead of at the centre of the view.
/// </summary>
public static class ProgressHand
{
    /// <param name="aimHand">Hand whose ray drives interaction: -1 none, 0 left, 1 right.</param>
    public static bool PickLeft(ProgressSource source, bool hasItem, bool itemInLeft, int aimHand)
    {
        if (source is ProgressSource.Item or ProgressSource.ClimbingSpike)
            return itemInLeft;
        if (aimHand >= 0)
            return aimHand == 0;
        // Interact button without a hand ray: the free hand is the one reaching out.
        return hasItem && !itemInLeft;
    }
}

/// <summary>
/// Haptic cues for a hold-to-use fill: a light tick at each step and a stronger one on completion, so
/// a long hold can be timed without looking at the ring. A hide right after the fill was nearly full
/// counts as completion because the game resets progress in the frame the cast finishes.
/// </summary>
public sealed class ProgressPulseTracker
{
    public int Steps = 4;
    public float CompleteAt = 0.95f;

    private float last;
    private bool visible;
    private bool completed;

    public ProgressPulse Update(bool shown, float progress)
    {
        if (!shown)
        {
            var done = visible && !completed && last >= CompleteAt;
            visible = false;
            completed = false;
            last = 0f;
            return done ? ProgressPulse.Complete : ProgressPulse.None;
        }

        if (!visible || progress < last)
        {
            // Fresh cast (first frame shown, or the fill restarted).
            visible = true;
            completed = false;
            last = 0f;
        }

        var previous = last;
        last = progress;
        if (completed)
            return ProgressPulse.None;
        if (progress >= 1f)
        {
            completed = true;
            return ProgressPulse.Complete;
        }
        var steps = Steps < 1 ? 1 : Steps;
        return (int)(progress * steps) > (int)(previous * steps)
            ? ProgressPulse.Step
            : ProgressPulse.None;
    }
}
