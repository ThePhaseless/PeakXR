namespace PeakXR.Core;

/// <summary>What one <see cref="CapPull.Update"/> saw.</summary>
public enum CapPullEvent
{
    None,

    /// <summary>The free hand closed on an end of the item: the pull is armed.</summary>
    Gripped,

    /// <summary>The hand pulled the end far enough away in time: light it.</summary>
    Lit,

    /// <summary>The grip opened or the time ran out after a real but short pull.</summary>
    TooShort,
}

/// <summary>
/// Lighting a flare as a real one: the free hand closes on its cap (either end) and pulls it off along the flare.
/// <c>apart</c> is the free hand's offset from the holding hand along the flare, outward through the gripped end, so
/// both hands pulling apart count and a sideways wave does not.
/// </summary>
public sealed class CapPull
{
    /// <summary>Metres the end must come away from where it was gripped.</summary>
    public float Distance = 0.1f;

    /// <summary>Seconds from the grip for the pull to finish.</summary>
    public float Seconds = 1f;

    /// <summary>Share of <see cref="Distance"/> past which a pull that falls short is reported, not ignored.</summary>
    public float ShortShare = 0.3f;

    bool _wasHeld;
    float _start;
    float _elapsed;
    float _best;

    /// <summary>Gripped and not yet lit, released or timed out.</summary>
    public bool Armed { get; private set; }

    /// <summary>0..1 of <see cref="Distance"/> pulled so far while armed.</summary>
    public float Progress =>
        !Armed || Distance <= 0f ? 0f
        : _best >= Distance ? 1f
        : _best / Distance;

    /// <param name="gripHeld">The free hand's grip is closed.</param>
    /// <param name="atEnd">The free hand is at an end of the flare (read on the grip's closing frame only).</param>
    /// <param name="apart">Free hand along the flare from the holding hand, outward through the gripped end, m.</param>
    /// <param name="dt">Frame time, seconds.</param>
    public CapPullEvent Update(bool gripHeld, bool atEnd, float apart, float dt)
    {
        bool pressed = gripHeld && !_wasHeld;
        _wasHeld = gripHeld;
        if (pressed && atEnd)
        {
            Armed = true;
            _start = apart;
            _elapsed = 0f;
            _best = 0f;
            return CapPullEvent.Gripped;
        }
        if (!Armed)
            return CapPullEvent.None;
        if (!gripHeld)
            return End();
        _elapsed += dt;
        float pulled = apart - _start;
        if (pulled > _best)
            _best = pulled;
        if (pulled >= Distance)
        {
            Armed = false;
            return CapPullEvent.Lit;
        }
        return _elapsed > Seconds ? End() : CapPullEvent.None;
    }

    public void Reset()
    {
        Armed = false;
        _wasHeld = false;
    }

    CapPullEvent End()
    {
        Armed = false;
        return _best >= Distance * ShortShare ? CapPullEvent.TooShort : CapPullEvent.None;
    }
}
