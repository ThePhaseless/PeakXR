namespace PeakXR.Core;

/// <summary>One frame of an injected button: OR these into the game's pressed/held/released input fields.</summary>
public readonly struct PressFrame
{
    public readonly bool Pressed;
    public readonly bool Held;
    public readonly bool Released;

    public PressFrame(bool pressed, bool held, bool released)
    {
        Pressed = pressed;
        Held = held;
        Released = released;
    }
}

/// <summary>
/// A gesture pressing a game button on the player's behalf: press, hold until the game reports the action done
/// (or a time limit), then release. The game's own hold timers and cast progress run unchanged, so a gesture
/// takes the same code path as the button. If the player presses the button themselves the injection steps
/// aside without a release, so their press continues seamlessly. Call <see cref="Update"/> once per frame.
/// </summary>
public sealed class InjectedPress
{
    bool _started;
    float _maxSeconds;

    /// <summary>True from <see cref="Begin"/> until the press is released or abandoned.</summary>
    public bool Active { get; private set; }

    /// <summary>Seconds held since the press frame.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Start a press next <see cref="Update"/>. Ignored while one is active.</summary>
    /// <param name="maxSeconds">Release after this long even if the action never reports done.</param>
    public void Begin(float maxSeconds)
    {
        if (Active)
            return;
        Active = true;
        _started = false;
        _maxSeconds = maxSeconds;
        Elapsed = 0f;
    }

    /// <param name="userHeld">The player already holds the button this frame (their input wins).</param>
    /// <param name="done">The action finished or its target went away: release now.</param>
    /// <param name="dt">Frame time, seconds.</param>
    public PressFrame Update(bool userHeld, bool done, float dt)
    {
        if (!Active)
            return default;
        if (userHeld)
        {
            Active = false;
            return default;
        }
        if (!_started)
        {
            if (done)
            {
                Active = false;
                return default;
            }
            _started = true;
            return new PressFrame(true, true, false);
        }
        Elapsed += dt;
        if (done || Elapsed >= _maxSeconds)
        {
            Active = false;
            return new PressFrame(false, false, true);
        }
        return new PressFrame(false, true, false);
    }

    /// <summary>Drop any press without emitting a release (e.g. the input is no longer this player's).</summary>
    public void Reset()
    {
        Active = false;
        _started = false;
        Elapsed = 0f;
    }
}
