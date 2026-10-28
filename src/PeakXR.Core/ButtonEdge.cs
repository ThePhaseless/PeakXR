namespace PeakXR.Core;

/// <summary>Per-frame edge detection for a digital button. Call <see cref="Update"/> once per frame.</summary>
public sealed class ButtonEdge
{
    /// <summary>True on the first frame the button is down.</summary>
    public bool Pressed { get; private set; }

    /// <summary>True on the first frame the button is up again.</summary>
    public bool Released { get; private set; }

    /// <summary>True while the button is down.</summary>
    public bool Held { get; private set; }

    /// <summary>
    /// Seconds since the press frame while held (0 on the press frame). On the release frame it keeps
    /// the duration of the press that just ended; afterwards it is 0.
    /// </summary>
    public float HeldTime { get; private set; }

    public void Update(bool down, float dt)
    {
        Pressed = down && !Held;
        Released = !down && Held;
        if (down)
            HeldTime = Pressed ? 0f : HeldTime + dt;
        else if (!Released)
            HeldTime = 0f;
        Held = down;
    }

    public void Reset()
    {
        Pressed = Released = Held = false;
        HeldTime = 0f;
    }
}

/// <summary>Turns an analog value (trigger, grip) into a button with press/release hysteresis.</summary>
public sealed class AnalogButton
{
    readonly ButtonEdge _edge = new ButtonEdge();

    public float PressThreshold;
    public float ReleaseThreshold;

    public AnalogButton(float pressThreshold = 0.75f, float releaseThreshold = 0.65f)
    {
        PressThreshold = pressThreshold;
        ReleaseThreshold = releaseThreshold;
    }

    public float Value { get; private set; }
    public bool Pressed => _edge.Pressed;
    public bool Released => _edge.Released;
    public bool Held => _edge.Held;
    public float HeldTime => _edge.HeldTime;

    public void Update(float value, float dt)
    {
        Value = value;
        bool down = _edge.Held ? value > ReleaseThreshold : value >= PressThreshold;
        _edge.Update(down, dt);
    }

    public void Reset()
    {
        Value = 0f;
        _edge.Reset();
    }
}
