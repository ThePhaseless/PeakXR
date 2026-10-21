using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>A UI colour (gamma space, 0..1), kept free of Unity so palette maths can be unit tested.</summary>
public readonly struct Rgba : IEquatable<Rgba>
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public Rgba(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public float Max => MathF.Max(R, MathF.Max(G, B));

    /// <summary>Max minus min channel: 0 for greys, high for strong colours.</summary>
    public float Chroma => Max - MathF.Min(R, MathF.Min(G, B));

    public Rgba Scaled(float k) => new(R * k, G * k, B * k, A);

    public static Rgba Lerp(Rgba a, Rgba b, float t) =>
        new(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            a.A + (b.A - a.A) * t
        );

    public bool Equals(Rgba other) =>
        R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

    public override bool Equals(object obj) => obj is Rgba other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(Rgba a, Rgba b) => a.Equals(b);

    public static bool operator !=(Rgba a, Rgba b) => !a.Equals(b);

    public override string ToString() => $"({R:0.00}, {G:0.00}, {B:0.00}, {A:0.00})";
}

/// <summary>
/// Colours of the VR loading room, derived from the loading screen being shown so each load gets a
/// fitting room without a per-screen table. The room keeps the screen's hue but a fixed, dark
/// brightness, so a cream or white screen never flashes bright in the headset.
/// </summary>
public readonly struct LoadingPalette : IEquatable<LoadingPalette>
{
    /// <summary>Below this chroma a fill counts as grey (black, white): no hue to take.</summary>
    public const float MinKeyChroma = 0.08f;

    /// <summary>Fills fainter than this were never really seen.</summary>
    public const float MinKeyAlpha = 0.05f;

    // Brightest channel of each room colour, for any key: the room's light level never depends on the screen.
    private const float HorizonValue = 0.14f;
    private const float VoidOfHorizon = 0.2f;
    private const float GridLineValue = 0.6f;
    private const float AccentValue = 0.95f;
    private const float InkValue = 0.9f;

    // Near-black art (luma) vanishes against the dark room.
    private const float InkLuma = 0.06f;

    /// <summary>The cool blue-grey room used when the screen gives no colour (black or white screens).</summary>
    public static readonly LoadingPalette Default = new(
        new Rgba(0.02f, 0.025f, 0.035f),
        new Rgba(0.11f, 0.12f, 0.17f),
        new Rgba(0.55f, 0.63f, 0.82f),
        new Rgba(0.95f, 0.78f, 0.36f),
        new Rgba(0.87f, 0.86f, 0.76f)
    );

    /// <summary>Camera clear colour and the top of the walls.</summary>
    public readonly Rgba Void;

    /// <summary>Bottom of the walls and the floor's rim.</summary>
    public readonly Rgba Horizon;

    public readonly Rgba GridLine;

    /// <summary>Progress bar fill.</summary>
    public readonly Rgba Accent;

    /// <summary>Replaces near-black art that was drawn for a light backing.</summary>
    public readonly Rgba Ink;

    public LoadingPalette(Rgba @void, Rgba horizon, Rgba gridLine, Rgba accent, Rgba ink)
    {
        Void = @void;
        Horizon = horizon;
        GridLine = gridLine;
        Accent = accent;
        Ink = ink;
    }

    /// <summary>The room for a screen whose hidden fills (and camera clear colour, if any) are <paramref name="fills"/>.</summary>
    public static LoadingPalette For(IReadOnlyList<Rgba> fills) =>
        TryPickKey(fills, out var key) ? FromKey(key) : Default;

    /// <summary>The most colourful visible fill: the screen's own tone (the plane screen's cream wash
    /// over its black base). False when every fill is grey or invisible.</summary>
    public static bool TryPickKey(IReadOnlyList<Rgba> fills, out Rgba key)
    {
        key = default;
        var best = MinKeyChroma;
        var found = false;
        for (int i = 0; i < fills.Count; i++)
        {
            var c = fills[i];
            if (c.A < MinKeyAlpha || c.Chroma < best)
                continue;
            best = c.Chroma;
            key = c;
            found = true;
        }
        return found;
    }

    /// <summary>Hue of <paramref name="key"/> at fixed room brightness levels.</summary>
    public static LoadingPalette FromKey(Rgba key)
    {
        var hue = key.Max > 1e-4f ? key.Scaled(1f / key.Max) : new Rgba(1f, 1f, 1f);
        hue = new Rgba(hue.R, hue.G, hue.B, 1f);
        var horizon = hue.Scaled(HorizonValue);
        return new LoadingPalette(
            horizon.Scaled(VoidOfHorizon),
            horizon,
            hue.Scaled(GridLineValue),
            hue.Scaled(AccentValue),
            hue.Scaled(InkValue)
        );
    }

    public static LoadingPalette Lerp(LoadingPalette a, LoadingPalette b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new LoadingPalette(
            Rgba.Lerp(a.Void, b.Void, t),
            Rgba.Lerp(a.Horizon, b.Horizon, t),
            Rgba.Lerp(a.GridLine, b.GridLine, t),
            Rgba.Lerp(a.Accent, b.Accent, t),
            Rgba.Lerp(a.Ink, b.Ink, t)
        );
    }

    /// <summary>Art this dark was drawn for a light backing and disappears in the room.</summary>
    public static bool NeedsInk(Rgba c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B < InkLuma;

    /// <summary>
    /// A loading screen's flat backing: a plain (untextured) fill covering at least 90% of the canvas both
    /// ways, and not a mask (hiding a mask graphic would hide what it clips). Artwork keeps its sprite.
    /// </summary>
    public static bool IsBacking(float coverX, float coverY, bool plain, bool mask) =>
        plain && !mask && coverX >= 0.9f && coverY >= 0.9f;

    public bool Equals(LoadingPalette other) =>
        Void == other.Void
        && Horizon == other.Horizon
        && GridLine == other.GridLine
        && Accent == other.Accent
        && Ink == other.Ink;

    public override bool Equals(object obj) => obj is LoadingPalette other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Void, Horizon, GridLine, Accent, Ink);

    public static bool operator ==(LoadingPalette a, LoadingPalette b) => a.Equals(b);

    public static bool operator !=(LoadingPalette a, LoadingPalette b) => !a.Equals(b);
}
