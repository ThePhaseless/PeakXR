namespace PeakXR.Core;

/// <summary>
/// Rock between the chase view and the watched body. A body tumbling past boulders or a climber on a ledge passes
/// behind rock for a moment all the time, so a block counts only after <see cref="BlockSeconds"/>; the caller then
/// fades to black, and re-places a view that trails its goal behind a blink, at most once per
/// <see cref="BlinkInterval"/> so the view never strobes.
/// </summary>
public sealed class ChaseBlock
{
    /// <summary>Seconds the line must stay blocked before it counts.</summary>
    public float BlockSeconds = 0.2f;

    /// <summary>Shortest time between two re-placing blinks.</summary>
    public float BlinkInterval = 2f;

    private float blockedFor;
    private float sinceBlink = float.PositiveInfinity;

    /// <summary>The block has lasted <see cref="BlockSeconds"/>: hide the view.</summary>
    public bool Blocked { get; private set; }

    public void Reset()
    {
        blockedFor = 0f;
        sinceBlink = float.PositiveInfinity;
        Blocked = false;
    }

    /// <summary>Once per frame. <paramref name="trailing"/>: the view lags its goal, so a re-place would clear
    /// it. Returns true when the caller should blink and re-place the view now.</summary>
    public bool Update(float dt, bool blockedNow, bool trailing)
    {
        sinceBlink += dt;
        blockedFor = blockedNow ? blockedFor + dt : 0f;
        Blocked = blockedFor >= BlockSeconds;
        if (!Blocked || !trailing || sinceBlink < BlinkInterval)
            return false;
        sinceBlink = 0f;
        return true;
    }
}
