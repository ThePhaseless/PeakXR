namespace PeakXR.Core;

/// <summary>
/// Which scene the running load is heading to, learned from the game building its scene-load process. Built
/// before a load starts, it waits for the start (so a load that never starts leaves no trace); built inside a
/// running load it applies at once, unless a load request in that same frame was refused, which means the
/// process belonged to the refused request and never runs.
/// </summary>
public sealed class LoadDestination
{
    private string pending;
    private string previous;
    private int nestedFrame = -1;

    public string Current { get; private set; }

    /// <summary>A scene-load process was built. True when <see cref="Current"/> changed now.</summary>
    public bool Built(string scene, bool loading, int frame)
    {
        if (!loading)
        {
            pending = scene;
            return false;
        }
        previous = Current;
        Current = scene;
        nestedFrame = frame;
        return true;
    }

    /// <summary>A load request was refused (one is running). True when <see cref="Current"/> was put back.</summary>
    public bool Rejected(int frame)
    {
        if (frame != nestedFrame)
            return false;
        nestedFrame = -1;
        Current = previous;
        return true;
    }

    /// <summary>A load started: the process built for it (or none) names its destination.</summary>
    public void Started()
    {
        Current = pending;
        pending = null;
        nestedFrame = -1;
    }

    /// <summary>The load ended; a process already built for the next load stays pending.</summary>
    public void Ended() => Current = null;

    public void Clear()
    {
        Current = null;
        pending = null;
        nestedFrame = -1;
    }
}
