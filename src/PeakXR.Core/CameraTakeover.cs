namespace PeakXR.Core;

/// <summary>
/// Comfort state machine for "the game takes the camera" moments (ragdoll, spectate, cutscene).
/// The view never cuts: it fades to black, switches between first person and the detached view
/// while the screen is black, holds briefly, then fades back in. Short glitches in the request
/// are debounced so a one-frame stumble does not blink the screen.
/// </summary>
public sealed class CameraTakeover
{
    enum Phase
    {
        Idle,
        FadingOut,
        Hold,
        FadingIn,
    }

    /// <summary>Seconds the request must stay on before the view detaches.</summary>
    public float EnterDelay = 0.2f;

    /// <summary>Seconds the request must stay off before the view returns to first person.</summary>
    public float ExitDelay = 0.6f;

    /// <summary>Seconds for each fade (out and in). 0 switches instantly.</summary>
    public float FadeSeconds = 0.25f;

    /// <summary>Seconds the screen stays black after a switch.</summary>
    public float HoldSeconds = 0.1f;

    Phase _phase;
    bool _committed;
    float _wantTimer;
    float _holdTimer;
    bool _blink;

    /// <summary>The view to show now: false = first person, true = detached.</summary>
    public bool Detached { get; private set; }

    /// <summary>0 = clear, 1 = black.</summary>
    public float Fade { get; private set; }

    /// <summary>True during the <see cref="Update"/> in which the view switched (screen fully black):
    /// the caller repositions the rig then.</summary>
    public bool SwitchedThisFrame { get; private set; }

    /// <summary>Fade out and back in without changing the view, e.g. when the spectated player
    /// changes. <see cref="SwitchedThisFrame"/> is raised at black.</summary>
    public void Blink() => _blink = true;

    /// <summary>Jumps to a view with no fade (use only while nothing is visible, e.g. at start).</summary>
    public void Reset(bool detached)
    {
        Detached = detached;
        _committed = detached;
        _phase = Phase.Idle;
        Fade = 0f;
        _wantTimer = 0f;
        _blink = false;
    }

    public void Update(bool wantDetached, float dt)
    {
        SwitchedThisFrame = false;

        if (wantDetached != _committed)
        {
            _wantTimer += dt;
            if (_wantTimer >= (wantDetached ? EnterDelay : ExitDelay))
            {
                _committed = wantDetached;
                _wantTimer = 0f;
            }
        }
        else
        {
            _wantTimer = 0f;
        }

        bool needSwitch = _committed != Detached || _blink;
        if (needSwitch && (_phase == Phase.Idle || _phase == Phase.FadingIn))
            _phase = Phase.FadingOut;

        switch (_phase)
        {
            case Phase.FadingOut:
                if (!needSwitch)
                {
                    _phase = Phase.FadingIn;
                    StepIn(dt);
                    break;
                }
                Fade = FadeSeconds <= 0f ? 1f : System.Math.Min(1f, Fade + dt / FadeSeconds);
                if (Fade >= 1f)
                    SwitchAtBlack();
                break;

            case Phase.Hold:
                if (_committed != Detached || _blink)
                {
                    SwitchAtBlack();
                    break;
                }
                _holdTimer += dt;
                if (_holdTimer >= HoldSeconds)
                    _phase = Phase.FadingIn;
                break;

            case Phase.FadingIn:
                StepIn(dt);
                break;
        }
    }

    void SwitchAtBlack()
    {
        Fade = 1f;
        Detached = _committed;
        _blink = false;
        SwitchedThisFrame = true;
        _holdTimer = 0f;
        _phase = Phase.Hold;
    }

    void StepIn(float dt)
    {
        Fade = FadeSeconds <= 0f ? 0f : System.Math.Max(0f, Fade - dt / FadeSeconds);
        if (Fade <= 0f)
            _phase = Phase.Idle;
    }
}
