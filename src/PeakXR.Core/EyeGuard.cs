using System;

namespace PeakXR.Core;

/// <summary>
/// Keeps the inside of rock off the screen after the view jumps (a warp, a respawn or revive, a segment jump, a
/// camera takeover, a spawn). <see cref="Arm"/> opens a watch window; while it is open the caller reports each frame
/// whether the eye is inside geometry, and the guard asks for black from the first inside frame until the eye has
/// been clear for <see cref="ClearSeconds"/>. The view itself is never moved here: the body settles (or the caller
/// re-places a detached view) behind the black. A body stuck in rock never keeps the headset black for longer than
/// <see cref="MaxHoldSeconds"/>.
/// </summary>
public sealed class EyeGuard
{
    /// <summary>Seconds after a jump the eye is watched.</summary>
    public float WatchSeconds = 3f;

    /// <summary>Seconds the eye must stay clear before the view comes back.</summary>
    public float ClearSeconds = 0.2f;

    /// <summary>Longest black hold; after it the guard gives up until the next jump.</summary>
    public float MaxHoldSeconds = 5f;

    private float watch;
    private float clearFor;
    private float held;

    /// <summary>True while the guard wants the view black.</summary>
    public bool Black { get; private set; }

    /// <summary>True while the eye must be checked (the window is open or the view is black).</summary>
    public bool Watching => watch > 0f || Black;

    /// <summary>The view just jumped: watch the eye for <see cref="WatchSeconds"/>.</summary>
    public void Arm() => watch = WatchSeconds;

    public void Reset()
    {
        watch = clearFor = held = 0f;
        Black = false;
    }

    /// <summary>Once per frame; returns whether the view should be black.</summary>
    public bool Update(float dt, bool inside)
    {
        if (!Watching)
            return false;
        watch = MathF.Max(0f, watch - dt);
        if (inside)
        {
            if (!Black)
                held = 0f;
            Black = true;
            clearFor = 0f;
        }
        else if (Black)
        {
            clearFor += dt;
            if (clearFor >= ClearSeconds)
                Black = false;
        }
        if (Black)
        {
            held += dt;
            if (held >= MaxHoldSeconds)
            {
                Black = false;
                watch = 0f;
            }
        }
        return Black;
    }
}
