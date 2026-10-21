namespace PeakXR.Core;

/// <summary>
/// The shape drawn inside a cue's ring, so a cue reads without colour or text: a slash for "no", a bar for "full",
/// a sweeping arc for "wait", a pin for a notice ("here it is"). The valid spot of a hint is always a double ring
/// with a chevron travelling toward it, a shape none of these use.
/// </summary>
public enum CueGlyph : byte
{
    Slash,
    Bar,
    Wait,
    Pin,
}

/// <summary>Which sound a cue plays: refused (falling two notes), would-work (rising two notes), notice (one soft bell).</summary>
public enum CueSound : byte
{
    Refused,
    WouldWork,
    Notice,
}

public static class DenyGlyph
{
    public static CueGlyph For(DenyReason reason) =>
        reason switch
        {
            DenyReason.PocketsFull
            or DenyReason.PackFull
            or DenyReason.HandsFull
            or DenyReason.CantHolster => CueGlyph.Bar,
            DenyReason.TooSoon or DenyReason.KeepHolding or DenyReason.CantSwitch => CueGlyph.Wait,
            DenyReason.Stowed or DenyReason.Dropped => CueGlyph.Pin,
            _ => CueGlyph.Slash,
        };
}

/// <summary>
/// The cue sounds, synthesised once: short, quiet sine tones with soft edges (no click), far from the game's foley.
/// Refused falls a fourth, would-work rises a fifth, the notice is one bell-like note with a quick decay, so the three
/// are told apart by ear.
/// </summary>
public static class CueTones
{
    public const float Peak = 0.35f;

    public static float[] Make(CueSound sound, int sampleRate) =>
        sound switch
        {
            CueSound.Refused => TwoNotes(sampleRate, 520f, 390f, 0.07f, 0.03f),
            CueSound.WouldWork => TwoNotes(sampleRate, 523f, 784f, 0.06f, 0.025f),
            _ => Bell(sampleRate, 880f, 0.22f),
        };

    private static float[] TwoNotes(int rate, float first, float second, float note, float gap)
    {
        int n = (int)(rate * note);
        int g = (int)(rate * gap);
        var samples = new float[n * 2 + g];
        Tone(samples, 0, n, first, rate);
        Tone(samples, n + g, n, second, rate);
        return samples;
    }

    /// <summary>A sine note with a raised-cosine envelope: silent at both ends.</summary>
    private static void Tone(float[] into, int start, int count, float hz, int rate)
    {
        for (int i = 0; i < count; i++)
        {
            float env = 0.5f - 0.5f * System.MathF.Cos(2f * System.MathF.PI * i / (count - 1));
            into[start + i] = Peak * env * System.MathF.Sin(2f * System.MathF.PI * hz * i / rate);
        }
    }

    private static float[] Bell(int rate, float hz, float seconds)
    {
        int n = (int)(rate * seconds);
        int attack = rate / 200;
        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env =
                (i < attack ? (float)i / attack : 1f)
                * System.MathF.Exp(-t * 18f)
                * (1f - (float)i / n);
            float s =
                System.MathF.Sin(2f * System.MathF.PI * hz * t)
                + 0.3f * System.MathF.Sin(2f * System.MathF.PI * hz * 2f * t);
            samples[i] = Peak * env * s / 1.3f;
        }
        return samples;
    }
}

/// <summary>Comfort-safe motion of the "would work" part: slow, small, never a flash.</summary>
public static class CueMotion
{
    /// <summary>Seconds for the chevron to travel from the refused spot to the valid one.</summary>
    public const float TravelSeconds = 1.2f;

    /// <summary>Position 0..1 of the chevron along the link at <paramref name="time"/>.</summary>
    public static float Travel(float time)
    {
        float t = time / TravelSeconds;
        return t - System.MathF.Floor(t);
    }

    /// <summary>Scale of the valid ring: a gentle 1 Hz breathe of plus or minus 8 %.</summary>
    public static float Breathe(float time) =>
        1f + 0.08f * System.MathF.Sin(2f * System.MathF.PI * time);

    /// <summary>Start angle in degrees of the "wait" glyph's sweeping arc (one turn per 1.5 s).</summary>
    public static float Sweep(float time) => Travel(time * TravelSeconds / 1.5f) * 360f;
}
