using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The avatar's eyes relative to the body root the view rides (forward, up), learned from the eye renderers. A
/// sample is taken only while avatar.local's lean is fully off: the lean puts the avatar's eyes on the headset, so
/// even a 2 cm held lean, sampled, would glide the offset (and the view) after the head. The first sample replaces
/// the starting guess at once (the caller takes it only on a still stand); later samples glide at 1/s.
/// <see cref="Converged"/> latches once the offset is within 1 cm of a sample; the lean waits for it, so the learning
/// finishes on unleaned eyes.
/// </summary>
public sealed class EyeCalibration
{
    const float ConvergedWithin = 0.01f;

    readonly Vector2 _initial;

    public EyeCalibration(Vector2 initial)
    {
        _initial = initial;
        Offset = initial;
    }

    public Vector2 Offset { get; private set; }

    /// <summary>At least one sample was taken.</summary>
    public bool Have { get; private set; }

    public bool Converged { get; private set; }

    /// <summary>Most the first sample may move the offset (metres) and still land in view: about the tracking jitter of a
    /// still head, too small to see. A bigger first change is applied behind a blink.</summary>
    public const float BlinkAbove = 0.02f;

    /// <summary>Taking <paramref name="sample"/> as the first one would move the view visibly: the caller applies it
    /// behind a blink (fade to black, apply, fade back), never as a jump in view.</summary>
    public bool FirstSampleNeedsBlink(Vector2 sample) =>
        !Have && Vector2.Distance(Offset, sample) > BlinkAbove;

    /// <returns>Whether the sample was used.</returns>
    public bool Sample(Vector2 sample, bool leaning, float dt)
    {
        if (leaning)
            return false;
        Offset = Have ? Vector2.Lerp(Offset, sample, ExpSmooth.Factor(1f, dt)) : sample;
        Have = true;
        if (Vector2.Distance(Offset, sample) < ConvergedWithin)
            Converged = true;
        return true;
    }

    public void Reset()
    {
        Offset = _initial;
        Have = false;
        Converged = false;
    }
}
