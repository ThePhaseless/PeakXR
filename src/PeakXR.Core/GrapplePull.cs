namespace PeakXR.Core;

/// <summary>
/// The local player's own rescue-hook pull (RescueHook.RPCA_RescueWall, RescueHook.cs:398): the game drops the body
/// into a short fall for it (<c>Fall(selfFallSeconds)</c>, :407), so its ragdoll control reads 0 until the pull lets
/// go (:362-395) and climbs back at 0.5/s after (Character.cs:1100). The pull is the player's own aimed move, so the
/// view and the hands stay on the body through it and its recovery: a detached tumble view there left the hands
/// metres from the wall the hook pulled them to.
/// </summary>
public sealed class GrapplePull
{
    /// <summary>Longest pull covered when no let-go arrives (the hook dropped mid-pull): the hook's own
    /// <c>maxWallHookTime</c> of 1 s (RescueHook.cs:61) and a margin.</summary>
    public const float MaxPullSeconds = 1.5f;

    /// <summary>Seconds after the let-go: ragdoll control climbs from 0 to 0.5 at the game's 0.5/s in 1 s
    /// (Character.cs:1100), plus a margin.</summary>
    public const float RecoverSeconds = 1.5f;

    bool _pulling;
    bool _released;
    float _start;
    float _letGo;

    public void Shot(float now)
    {
        _pulling = true;
        _released = false;
        _start = now;
    }

    public void LetGo(float now)
    {
        if (!_pulling)
            return;
        _pulling = false;
        _released = true;
        _letGo = now;
    }

    public void Reset() => _pulling = _released = false;

    /// <param name="falling">The body's fall timer runs (<c>fallSeconds &gt; 0</c>): after the let-go that is a new
    /// tumble, not the hook's.</param>
    public bool Covers(float now, bool falling) =>
        _pulling
            ? now - _start <= MaxPullSeconds
            : _released && !falling && now - _letGo <= RecoverSeconds;
}
