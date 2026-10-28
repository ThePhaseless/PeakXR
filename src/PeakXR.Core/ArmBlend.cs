namespace PeakXR.Core;

/// <summary>
/// How strongly tracked hands drive a character's arm IK: 0 leaves the game's own arms, 1 is fully tracked. While
/// the game ragdolls the body (falls, knock-downs, passing out) its arm joints still chase the animated pose at
/// full spring, so tracked IK kept the limp body's arms held out, and with the view detached behind the body it
/// pointed them back at the camera. The weight drops while the body ragdolls, so the arms go limp with it, and
/// blends back in once the game hands control back. Both directions ease, so arms never snap.
/// </summary>
public sealed class ArmBlend
{
    /// <summary>Ragdoll control below which the body counts as ragdolled; the camera feature uses the same value
    /// to take the view.</summary>
    public const float ControlThreshold = 0.5f;

    /// <summary>Seconds to let go when the body starts to ragdoll. Shorter than the camera's enter delay plus its
    /// fade, so the arms are released before a detached view moves the hands away from the body.</summary>
    public const float ReleaseSeconds = 0.25f;

    /// <summary>Seconds to take the arms back once the body is under control again.</summary>
    public const float ReturnSeconds = 0.5f;

    float _raw;

    /// <summary>Eased blend weight, 0..1.</summary>
    public float Weight => _raw * _raw * (3f - 2f * _raw);

    /// <summary>Whether tracked hands may drive the arms: the game is not ragdolling the body.</summary>
    /// <param name="down">Passed out, fully passed out, dead or carried.</param>
    public static bool Tracked(bool down, float fallSeconds, float ragdollControl) =>
        !down && fallSeconds <= 0f && ragdollControl >= ControlThreshold;

    public void Update(bool tracked, float dt)
    {
        float seconds = tracked ? ReturnSeconds : ReleaseSeconds;
        float step = dt / seconds;
        _raw = tracked ? System.Math.Min(1f, _raw + step) : System.Math.Max(0f, _raw - step);
    }

    /// <summary>Back to the game's arms at once (the pose went stale), so the next pose blends in.</summary>
    public void Reset() => _raw = 0f;
}
