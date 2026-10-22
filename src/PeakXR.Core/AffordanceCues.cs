using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The shape drawn inside a cue's ring, so a cue reads without colour or text: a slash for "no", a bar for "full",
/// a sweeping arc for "wait", a pin for a notice ("here it is"), and a picture for the common causes (a bolt for
/// stamina, two inward chevrons for both hands, arrows for aim, a person for friends, a padlock, an empty cup, a
/// weight, wavy lines for spoiled, "!" for danger). The valid spot of a hint is always a double ring with a chevron
/// travelling toward it, a shape none of these use. Appended only.
/// </summary>
public enum CueGlyph : byte
{
    Slash,
    Bar,
    Wait,
    Pin,
    Stamina,
    TwoHands,
    ArrowUp,
    ArrowDown,
    Ground,
    People,
    Padlock,
    Empty,
    Weight,
    Wavy,
    Danger,
}

/// <summary>
/// Which sound a cue plays: refused (falling two notes), would-work (rising two notes), notice (one soft bell),
/// transmit (two quick high blips: a ping or call went out), cancel (a soft, small fall: called off, not refused),
/// end stop (a dull low knock at a limit), danger (a trembling note: harm is near). Appended only.
/// </summary>
public enum CueSound : byte
{
    Refused,
    WouldWork,
    Notice,
    Transmit,
    Cancel,
    EndStop,
    Danger,
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
            DenyReason.TooSoon
            or DenyReason.KeepHolding
            or DenyReason.CantSwitch
            or DenyReason.Unanchored
            or DenyReason.NotNow => CueGlyph.Wait,
            DenyReason.Stowed or DenyReason.Dropped => CueGlyph.Pin,
            DenyReason.NoStamina => CueGlyph.Stamina,
            DenyReason.NeedsTwoHands => CueGlyph.TwoHands,
            DenyReason.AimHigher => CueGlyph.ArrowUp,
            DenyReason.AimLower => CueGlyph.ArrowDown,
            DenyReason.NeedsGround => CueGlyph.Ground,
            DenyReason.FriendsFar or DenyReason.NeedsFriend => CueGlyph.People,
            DenyReason.NotHere or DenyReason.Blocked or DenyReason.Locked or DenyReason.Petrified =>
                CueGlyph.Padlock,
            DenyReason.UsedUp or DenyReason.NotLit => CueGlyph.Empty,
            DenyReason.TooHeavy => CueGlyph.Weight,
            DenyReason.Spoiled => CueGlyph.Wavy,
            DenyReason.FuseLit or DenyReason.DangerSoon => CueGlyph.Danger,
            _ => CueGlyph.Slash,
        };
}

/// <summary>
/// The strokes of each glyph, in units of the ring's radius (x right, y up, ring centre at 0). At most two
/// polylines, so the runtime draws any glyph with two line renderers and no allocation. <see cref="CueGlyph.Wait"/>
/// has none: its arc sweeps (<see cref="CueMotion.Sweep"/>). The pin stands above the ring.
/// </summary>
public static class GlyphShape
{
    /// <summary>Most points in any stroke.</summary>
    public const int MaxPoints = 9;

    private static readonly Vector2[] None = Array.Empty<Vector2>();

    // Indexed by CueGlyph.
    private static readonly Vector2[][] First =
    {
        new Vector2[] { new(-0.55f, -0.55f), new(0.55f, 0.55f) },
        new Vector2[] { new(-0.7f, 0f), new(0.7f, 0f) },
        None,
        new Vector2[] { new(0f, 1f), new(0f, 2.4f) },
        // Stamina: a bolt.
        new Vector2[]
        {
            new(0.15f, 0.65f),
            new(-0.25f, 0.02f),
            new(0.25f, -0.02f),
            new(-0.15f, -0.65f),
        },
        // TwoHands: ">" of "> <".
        new Vector2[] { new(-0.65f, 0.38f), new(-0.2f, 0f), new(-0.65f, -0.38f) },
        // Arrow shafts, up then down.
        new Vector2[] { new(0f, -0.6f), new(0f, 0.6f) },
        new Vector2[] { new(0f, 0.6f), new(0f, -0.6f) },
        // Ground: the line.
        new Vector2[] { new(-0.6f, -0.45f), new(0.6f, -0.45f) },
        // People: the head.
        Circle(0f, 0.3f, 0.2f),
        // Padlock: the body.
        new Vector2[]
        {
            new(-0.4f, -0.55f),
            new(0.4f, -0.55f),
            new(0.4f, 0.02f),
            new(-0.4f, 0.02f),
            new(-0.4f, -0.55f),
        },
        // Empty: an open cup.
        new Vector2[]
        {
            new(-0.42f, 0.4f),
            new(-0.42f, -0.45f),
            new(0.42f, -0.45f),
            new(0.42f, 0.4f),
        },
        // Weight: a trapezoid.
        new Vector2[]
        {
            new(-0.3f, 0.2f),
            new(0.3f, 0.2f),
            new(0.5f, -0.5f),
            new(-0.5f, -0.5f),
            new(-0.3f, 0.2f),
        },
        // Wavy: the upper wave.
        Wave(0.22f),
        // Danger: the "!" stem.
        new Vector2[] { new(0f, 0.6f), new(0f, -0.15f) },
    };

    private static readonly Vector2[][] Second =
    {
        None,
        None,
        None,
        None,
        None,
        new Vector2[] { new(0.65f, 0.38f), new(0.2f, 0f), new(0.65f, -0.38f) },
        // Arrow heads, up then down.
        new Vector2[] { new(-0.4f, 0.2f), new(0f, 0.6f), new(0.4f, 0.2f) },
        new Vector2[] { new(-0.4f, -0.2f), new(0f, -0.6f), new(0.4f, -0.2f) },
        // Ground: a chevron down onto the line.
        new Vector2[] { new(-0.3f, 0.25f), new(0f, -0.1f), new(0.3f, 0.25f) },
        // People: the shoulders.
        Arc(0f, -0.55f, 0.45f),
        // Padlock: the shackle.
        Arc(0f, 0.02f, 0.26f),
        None,
        // Weight: the handle.
        Arc(0f, 0.2f, 0.16f),
        Wave(-0.22f),
        // Danger: the "!" dot.
        new Vector2[] { new(0f, -0.42f), new(0f, -0.55f) },
    };

    /// <summary>Stroke <paramref name="index"/> (0 or 1) of <paramref name="glyph"/>; empty when it has none.
    /// Shared arrays: never modify.</summary>
    public static Vector2[] Stroke(CueGlyph glyph, int index)
    {
        int g = (int)glyph;
        var set = index == 0 ? First : Second;
        return g < set.Length && index is 0 or 1 ? set[g] : None;
    }

    private static Vector2[] Circle(float x, float y, float r)
    {
        var p = new Vector2[MaxPoints];
        for (int i = 0; i < p.Length; i++)
        {
            float a = i * 2f * MathF.PI / (p.Length - 1);
            p[i] = new Vector2(x + r * MathF.Cos(a), y + r * MathF.Sin(a));
        }
        return p;
    }

    /// <summary>The upper half circle, left to right.</summary>
    private static Vector2[] Arc(float x, float y, float r)
    {
        var p = new Vector2[7];
        for (int i = 0; i < p.Length; i++)
        {
            float a = MathF.PI * (1f - (float)i / (p.Length - 1));
            p[i] = new Vector2(x + r * MathF.Cos(a), y + r * MathF.Sin(a));
        }
        return p;
    }

    private static Vector2[] Wave(float y)
    {
        var p = new Vector2[7];
        for (int i = 0; i < p.Length; i++)
        {
            float t = (float)i / (p.Length - 1);
            p[i] = new Vector2(-0.55f + 1.1f * t, y + 0.1f * MathF.Sin(t * 2f * MathF.PI));
        }
        return p;
    }
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
            CueSound.Transmit => TwoNotes(sampleRate, 1175f, 1175f, 0.035f, 0.02f),
            CueSound.Cancel => Soft(TwoNotes(sampleRate, 440f, 392f, 0.05f, 0.015f), 0.6f),
            CueSound.EndStop => Knock(sampleRate, 170f, 0.05f),
            CueSound.Danger => Tremble(sampleRate, 660f, 0.2f),
            _ => Bell(sampleRate, 880f, 0.22f),
        };

    private static float[] Soft(float[] samples, float gain)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
        return samples;
    }

    /// <summary>A low note with a fast decay and a touch of its octave: a dull knock, not a tone.</summary>
    private static float[] Knock(int rate, float hz, float seconds)
    {
        int n = (int)(rate * seconds);
        int attack = rate / 500;
        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env =
                (i < attack ? (float)i / attack : 1f) * MathF.Exp(-t * 60f) * (1f - (float)i / n);
            float s =
                MathF.Sin(2f * MathF.PI * hz * t) + 0.5f * MathF.Sin(2f * MathF.PI * hz * 2f * t);
            samples[i] = Peak * env * s / 1.5f;
        }
        return samples;
    }

    /// <summary>One note trembling at 18 Hz under a raised-cosine envelope: unsettled, never a click.</summary>
    private static float[] Tremble(int rate, float hz, float seconds)
    {
        int n = (int)(rate * seconds);
        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
            float trem = 0.6f + 0.4f * MathF.Sin(2f * MathF.PI * 18f * t);
            samples[i] = Peak * env * trem * MathF.Sin(2f * MathF.PI * hz * t);
        }
        return samples;
    }

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

    /// <summary>Height of a hand cue above the wrist: clear of the hand model and most held items.</summary>
    public const float AboveHand = 0.1f;

    /// <summary>Nearest a cue sits to the eye: a comfortable focus distance.</summary>
    public const float MinEyeDistance = 0.35f;

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
        Gap(now, Tint(baseTint, 1f)) < 0.02f && Gap(now, baseTint) > 0.02f;

    /// <summary>Largest per-channel difference. Plain floats: the in-game check disagreed with the same maths run
    /// under .NET (VR run 28), so nothing here leans on <see cref="Vector4.Distance"/>.</summary>
    public static float Gap(Vector4 a, Vector4 b) =>
        MathF.Max(
            MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y)),
            MathF.Max(MathF.Abs(a.Z - b.Z), MathF.Abs(a.W - b.W))
        );

    /// <summary>
    /// A placement ghost's colour (<c>_Color0</c> of the game's <c>Scouts/ItemPreview</c>, an HDR blue) turned red at
    /// <paramref name="strength"/>: same glow strength and alpha, so it reads as the game's preview, refused.
    /// </summary>
    public static Vector4 Preview(Vector4 baseColor, float strength)
    {
        float glow = MathF.Max(1f, new Vector3(baseColor.X, baseColor.Y, baseColor.Z).Length());
        var red = new Vector4(glow * 0.99f, glow * 0.1f, glow * 0.08f, baseColor.W);
        return Vector4.Lerp(baseColor, red, Math.Clamp(strength, 0f, 1f));
    }

    /// <summary>Full-strength red of a flat UI colour (a bar, a button): a multiply would leave a dark bar dark.</summary>
    public static readonly Vector4 FlatRed = new(0.92f, 0.24f, 0.2f, 0.9f);

    /// <summary>A UI colour (a <c>Graphic</c>) pulled toward <see cref="FlatRed"/> by <paramref name="strength"/>;
    /// alpha never drops below the original.</summary>
    public static Vector4 Flat(Vector4 baseColor, float strength)
    {
        var red = new Vector4(FlatRed.X, FlatRed.Y, FlatRed.Z, MathF.Max(baseColor.W, FlatRed.W));
        return Vector4.Lerp(baseColor, red, Math.Clamp(strength, 0f, 1f));
    }

    /// <summary>The UI colour <paramref name="now"/> is the full red of <paramref name="baseColor"/>.</summary>
    public static bool IsFlat(Vector4 baseColor, Vector4 now) =>
        Gap(now, Flat(baseColor, 1f)) < 0.02f && Gap(now, baseColor) > 0.02f;

    /// <summary>The ghost colour <paramref name="now"/> is the full red of <paramref name="baseColor"/>.</summary>
    public static bool IsPreview(Vector4 baseColor, Vector4 now) =>
        Gap(now, Preview(baseColor, 1f)) < 0.05f && Gap(now, baseColor) > 0.05f;
}
