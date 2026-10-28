namespace PeakXR.Core;

/// <summary>
/// A hand striking a target: hammering a piton, striking a flare or match. A strike counts when the hand
/// comes in from outside <see cref="RearmRadius"/> and reaches <see cref="Radius"/> at
/// <see cref="MinSpeed"/> or faster; the hand must pull back out before the next one counts.
/// </summary>
public sealed class StrikeGesture
{
    public float Radius = 0.15f;
    public float RearmRadius = 0.3f;

    /// <summary>m/s toward (or past) the target.</summary>
    public float MinSpeed = 1.5f;

    bool _armed;

    /// <summary>The hand has been out past <see cref="RearmRadius"/> since the last strike.</summary>
    public bool Armed => _armed;

    /// <summary>Strikes since the last <see cref="Reset"/>.</summary>
    public int Count { get; private set; }

    /// <param name="distance">Hand to target, metres.</param>
    /// <param name="speed">Hand speed toward the target (or plain hand speed for a swipe), m/s.</param>
    /// <returns>True on the frame a strike lands.</returns>
    public bool Update(float distance, float speed)
    {
        if (distance > RearmRadius)
        {
            _armed = true;
            return false;
        }
        if (!_armed || distance > Radius || speed < MinSpeed)
            return false;
        _armed = false;
        Count++;
        return true;
    }

    public void Reset()
    {
        _armed = false;
        Count = 0;
    }
}
