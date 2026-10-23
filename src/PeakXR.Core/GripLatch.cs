namespace PeakXR.Core;

/// <summary><c>Interaction.GripMode</c>: Hold needs the grip closed for as long as the hand holds on; Toggle latches a
/// hold so a weak or tired hand can open (an accessibility option, external review #36).</summary>
public enum GripMode : byte
{
    Hold,
    Toggle,
}

/// <summary>What a <see cref="GripLatch"/> frame did: each latch and each unlatch is felt.</summary>
public enum GripLatchEvent : byte
{
    None,
    Latched,
    Unlatched,
}

/// <summary>
/// One hand's toggle grip. In <see cref="GripMode.Toggle"/>, a grip that closed on something (a hold, a line, an
/// item, a friend: <c>engaged</c>) stays closed when the hand opens. A firm squeeze held for
/// <see cref="SqueezeSeconds"/> and then opened past the release threshold lets go, so a relaxed or twitching hand
/// never does. <see cref="Grip"/> is the value every grip consumer reads instead of the controller's. A grip that
/// closed on nothing never latches. In Hold, or once the hold ends, the controller's grip passes straight through.
/// </summary>
public sealed class GripLatch
{
    /// <summary>The hands' own grip thresholds (<c>AnalogButton(0.6, 0.4)</c>), so the latch decides on the same
    /// press and release they see.</summary>
    public const float PressThreshold = 0.6f;
    public const float ReleaseThreshold = 0.4f;

    /// <summary>The unlatching squeeze: above a resting or twitching hand's grip, held for a moment.</summary>
    public const float SqueezeThreshold = 0.75f;
    public const float SqueezeSeconds = 0.1f;

    private bool down;
    private bool squeezed;
    private float squeezeFor;

    /// <summary>The hand counts as gripping whatever the controller reports.</summary>
    public bool Latched { get; private set; }

    /// <summary>The grip value consumers read this frame.</summary>
    public float Grip { get; private set; }

    /// <param name="raw">The controller's grip, 0..1.</param>
    /// <param name="tracked">The controller is tracked. While it is not, the latch is frozen: a tracking blink
    /// neither latches nor unlatches.</param>
    /// <param name="engaged">The hand holds something through its grip (a wall or a line, a handle, an item it would
    /// drop by opening, a friend it lifts).</param>
    public GripLatchEvent Update(float raw, bool tracked, bool engaged, GripMode mode, float dt)
    {
        if (!tracked)
        {
            Grip = Latched ? 1f : raw;
            return GripLatchEvent.None;
        }
        bool wasDown = down;
        down = down ? raw > ReleaseThreshold : raw >= PressThreshold;
        var result = GripLatchEvent.None;
        if (!Latched)
        {
            if (mode == GripMode.Toggle && engaged && wasDown && !down)
            {
                Latched = true;
                squeezed = false;
                squeezeFor = 0f;
                result = GripLatchEvent.Latched;
            }
        }
        else if (!engaged)
            // The game ended the hold (torn off, eaten, put away): it has its own cue.
            Latched = false;
        else if (mode == GripMode.Hold)
        {
            // Switched to Hold: a closed grip carries the hold on as a plain grip, an open one lets go now.
            Latched = false;
            if (raw > ReleaseThreshold)
                down = true;
            else
                result = GripLatchEvent.Unlatched;
        }
        else
        {
            squeezeFor = raw >= SqueezeThreshold ? squeezeFor + dt : 0f;
            if (squeezeFor >= SqueezeSeconds)
                squeezed = true;
            if (squeezed && !down)
            {
                Latched = false;
                result = GripLatchEvent.Unlatched;
            }
        }
        Grip = Latched ? 1f : raw;
        return result;
    }

    public void Reset()
    {
        down = squeezed = Latched = false;
        squeezeFor = 0f;
        Grip = 0f;
    }
}
