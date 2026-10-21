using System;
using System.Numerics;

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

/// <summary>
/// Size and place of a refusal's cue. A ring of under 2 degrees at a laser tip, or one inside the hand and the held
/// item, went unseen; a refused attempt gets a ring big enough to read at a glance, lifted clear of the hand that
/// tried, near where the player is already looking.
/// </summary>
public static class CueLayout
{
    /// <summary>Angular diameter of a refused attempt's ring (about 4 cm at the hand, 22 cm at 2.5 m).</summary>
    public const float DenyDegrees = 5f;

    /// <summary>Angular diameter of a pre-emptive "can't" ring: quieter than a refusal, it shows while aiming.</summary>
    public const float HoverDegrees = 2.5f;

    /// <summary>The self-test's floor for a denial cue's angular diameter.</summary>
    public const float MinDegrees = 3.5f;

    /// <summary>The self-test's limit for how far off the action's direction the cue may sit, in degrees.</summary>
    public const float MaxOffDegrees = 25f;

    /// <summary>A refused spot this close to the hand is about the hand (a held item), so the cue rides it.</summary>
    public const float NearHand = 0.35f;

    /// <summary>Height of a hand cue above the wrist: clear of the hand model and most held items.</summary>
    public const float AboveHand = 0.1f;

    /// <summary>Nearest a cue sits to the eye: a comfortable focus distance.</summary>
    public const float MinEyeDistance = 0.35f;

    public static bool AtHand(Vector3 spot, Vector3 hand) =>
        Vector3.DistanceSquared(spot, hand) < NearHand * NearHand;

    /// <summary>Where a cue about the hand goes: above it, a little toward the eye, never closer than
    /// <see cref="MinEyeDistance"/>.</summary>
    public static Vector3 HandAnchor(Vector3 hand, Vector3 eye)
    {
        var p = hand + Vector3.UnitY * AboveHand;
        var toEye = eye - p;
        float d = toEye.Length();
        if (d > 1e-4f)
            p += toEye / d * MathF.Min(0.04f, d * 0.25f);
        var fromEye = p - eye;
        if (fromEye.Length() < MinEyeDistance)
        {
            // A hand at the face: keep the height, push out level, on the hand's side (straight ahead if none).
            float dy = fromEye.Y;
            var flat = new Vector3(hand.X - eye.X, 0f, hand.Z - eye.Z);
            float len = flat.Length();
            flat = len > 1e-3f ? flat / len : Vector3.UnitZ;
            float h = MathF.Sqrt(MathF.Max(0f, MinEyeDistance * MinEyeDistance - dy * dy));
            p = new Vector3(eye.X, p.Y, eye.Z) + flat * h;
        }
        return p;
    }

    /// <summary>Angular diameter in degrees of a ring of <paramref name="radius"/> at <paramref name="distance"/>.</summary>
    public static float AngularDegrees(float radius, float distance) =>
        distance > 1e-4f ? 2f * MathF.Atan(radius / distance) / UnityQuat.Deg2Rad : 0f;

    /// <summary>
    /// The self-test's rule for a denial cue: seen from <paramref name="eye"/> looking at <paramref name="action"/>
    /// (the refused spot or hand), a ring at <paramref name="center"/> of <paramref name="radius"/> lies within
    /// <see cref="MaxOffDegrees"/> of that view and spans at least <see cref="MinDegrees"/>.
    /// </summary>
    public static bool Readable(
        Vector3 eye,
        Vector3 action,
        Vector3 center,
        float radius,
        out string why
    )
    {
        float off = SelfTestChecks.AngleOff(eye, action - eye, center);
        float size = AngularDegrees(radius, Vector3.Distance(center, eye));
        why =
            off > MaxOffDegrees ? $"cue {off:0} deg off the action (max {MaxOffDegrees:0})"
            : size < MinDegrees ? $"cue too small: {size:0.0} deg (min {MinDegrees:0.0})"
            : $"cue {size:0.0} deg, {off:0} deg off the action";
        return off <= MaxOffDegrees && size >= MinDegrees;
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

/// <summary>
/// The red preview of a refused thing (the held item, a piton ghost): its own colour tint pulled toward red, so it
/// still reads as that item. Colours are RGBA in a <see cref="Vector4"/>.
/// </summary>
public static class DenyTintMath
{
    /// <summary>Multiplier at full strength: keeps red, drops green and blue.</summary>
    public static readonly Vector4 Red = new(1f, 0.3f, 0.26f, 1f);

    /// <summary><paramref name="baseTint"/> pulled toward red by <paramref name="strength"/> (0..1, clamped).</summary>
    public static Vector4 Tint(Vector4 baseTint, float strength) =>
        Vector4.Lerp(baseTint, baseTint * Red, Math.Clamp(strength, 0f, 1f));

    /// <summary>The colour <paramref name="now"/> is the full red tint of <paramref name="baseTint"/>.</summary>
    public static bool IsTinted(Vector4 baseTint, Vector4 now) =>
        Vector4.Distance(now, Tint(baseTint, 1f)) < 0.02f
        && Vector4.Distance(now, baseTint) > 0.02f;
}
