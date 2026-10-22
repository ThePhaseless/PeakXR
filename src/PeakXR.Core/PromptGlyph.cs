using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>What the game's interact or hand prompt offers, as a picture instead of its verb text (D8).</summary>
public enum PromptVerb : byte
{
    None,

    /// <summary>PICKUP (Item.cs:680).</summary>
    Grab,

    /// <summary>OPEN (Luggage.cs:158, Backpack.cs:225).</summary>
    Open,

    /// <summary>CARRY a passed-out friend (CharacterInteractible.cs:35).</summary>
    Carry,

    /// <summary>DROP the friend you carry (CharacterInteractible.cs:27).</summary>
    Drop,

    /// <summary>EAT (CharacterInteractible.cs:31).</summary>
    Eat,

    /// <summary>Any other interaction, and a held item used on a friend (secondary prompt).</summary>
    Use,

    /// <summary>The climbing lunge (GUIManager.cs:371).</summary>
    Lunge,

    /// <summary>SETPITON while climbing with spikes (GUIManager.cs:1080-1084).</summary>
    Piton,

    /// <summary>Struggle out of a web (CharacterAfflictions.cs:803).</summary>
    Struggle,

    /// <summary>Keep holding: an <c>IInteractibleConstant</c> target (IInteractibleConstant.cs:5-7), whose ring fills
    /// with <c>Interaction.constantInteractableProgress</c> (Interaction.cs:57).</summary>
    Hold,
}

/// <summary>Kind of hovered interactible, as far as the prompt verb cares.</summary>
public enum PromptTarget : byte
{
    None,
    Item,

    /// <summary>A suitcase or a backpack on the ground: it opens.</summary>
    Container,
    Character,
    Other,
}

/// <summary>
/// D8: the verb of the game's prompt (GUIManager.RefreshInteractablePrompt, GUIManager.cs:1045-1085) drawn as a
/// glyph, in units of its radius (x right, y up), at most two polylines like <see cref="GlyphShape"/>. The mapping
/// follows the game's own branches rather than its localized text, so it holds in every language.
/// </summary>
public static class PromptGlyph
{
    public const int MaxPoints = GlyphShape.MaxPoints;

    /// <summary>Radius of the hold glyph's track ring and of its fill.</summary>
    public const float HoldRadius = 0.7f;

    private static readonly Vector2[] None = Array.Empty<Vector2>();

    /// <summary>Primary verb for a target; for a character, the order of CharacterInteractible.GetInteractionText
    /// (CharacterInteractible.cs:23-37).</summary>
    public static PromptVerb Primary(
        PromptTarget target,
        bool carriedByMe,
        bool cannibal,
        bool carriable
    ) =>
        target switch
        {
            PromptTarget.Item => PromptVerb.Grab,
            PromptTarget.Container => PromptVerb.Open,
            PromptTarget.Other => PromptVerb.Use,
            PromptTarget.Character when carriedByMe => PromptVerb.Drop,
            PromptTarget.Character when cannibal => PromptVerb.Eat,
            PromptTarget.Character when carriable => PromptVerb.Carry,
            _ => PromptVerb.None,
        };

    /// <summary>Secondary verb: the piton prompt overrides it while climbing with spikes (GUIManager.cs:1080),
    /// else it is a held item used on a friend.</summary>
    public static PromptVerb Secondary(bool piton) => piton ? PromptVerb.Piton : PromptVerb.Use;

    /// <summary>Whether <paramref name="verb"/>'s glyph shows at the target. With target names off
    /// (<c>UI.TargetNames</c>) a plain pickup is shown by the game's glow alone, so the obvious grab glyph goes;
    /// every other verb still needs its glyph.</summary>
    public static bool InPlace(PromptVerb verb, bool targetNames) =>
        targetNames || verb != PromptVerb.Grab;

    // Indexed by PromptVerb.
    private static readonly Vector2[][] First =
    {
        None,
        // Grab: a hand's curve closing round a thing.
        Arc(0.1f, 0f, 0.55f, 0.35f, 1.65f),
        // Open: a box.
        new Vector2[] { new(-0.5f, 0.05f), new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.05f) },
        // Carry: a lifting arrow.
        new Vector2[] { new(0f, -0.15f), new(0f, 0.6f) },
        // Drop: an arrow down.
        new Vector2[] { new(0f, 0.6f), new(0f, -0.3f) },
        // Eat: an open mouth.
        Arc(0f, 0.05f, 0.5f, 1.1f, 1.9f),
        // Use: a dot pressed into the ring.
        Circle(0f, 0f, 0.18f),
        GlyphShape.Stroke(CueGlyph.ArrowUp, 0),
        // Piton: the hammer's handle.
        new Vector2[] { new(-0.45f, -0.55f), new(0.2f, 0.25f) },
        // Struggle: "<" of "< >", shaking outward.
        new Vector2[] { new(-0.3f, 0.35f), new(-0.65f, 0f), new(-0.3f, -0.35f) },
        // Hold: the empty track ring; the fill is drawn over it (HoldFill).
        Circle(0f, 0f, HoldRadius),
    };

    private static readonly Vector2[][] Second =
    {
        None,
        // Grab: the thing held.
        Circle(0.15f, 0f, 0.16f),
        // Open: the lid, lifted.
        new Vector2[] { new(-0.55f, 0.15f), new(0.35f, 0.55f) },
        // Carry: the shoulders it lifts onto.
        new Vector2[] { new(-0.55f, -0.55f), new(0f, -0.3f), new(0.55f, -0.55f) },
        // Drop: the head of the arrow and nothing under it.
        new Vector2[] { new(-0.35f, 0f), new(0f, -0.35f), new(0.35f, 0f) },
        // Eat: the bite.
        Circle(0f, 0.25f, 0.12f),
        None,
        GlyphShape.Stroke(CueGlyph.ArrowUp, 1),
        // Piton: the hammer's head.
        new Vector2[] { new(-0.05f, 0.55f), new(0.5f, -0.05f) },
        new Vector2[] { new(0.3f, 0.35f), new(0.65f, 0f), new(0.3f, -0.35f) },
        None,
    };

    /// <summary>Stroke <paramref name="index"/> (0 or 1) of <paramref name="verb"/>; empty when it has none.
    /// Shared arrays: never modify.</summary>
    public static Vector2[] Stroke(PromptVerb verb, int index)
    {
        int v = (int)verb;
        var set = index == 0 ? First : Second;
        return v < set.Length && index is 0 or 1 ? set[v] : None;
    }

    /// <summary>
    /// The hold glyph's fill for <paramref name="progress"/> (0..1): an arc on the track ring from the top, clockwise,
    /// written into <paramref name="into"/> (allocation-free); returns its point count, 0 before any progress.
    /// </summary>
    public static int HoldFill(float progress, Vector2[] into)
    {
        float p = progress > 1f ? 1f : progress;
        if (!(p > 0f) || into.Length < 2)
            return 0;
        int n = Math.Max(2, (int)MathF.Round(1f + p * (into.Length - 1)));
        for (int i = 0; i < n; i++)
        {
            float a = MathF.PI * 0.5f - 2f * MathF.PI * p * i / (n - 1);
            into[i] = new Vector2(HoldRadius * MathF.Cos(a), HoldRadius * MathF.Sin(a));
        }
        return n;
    }

    private static Vector2[] Circle(float x, float y, float r) => Arc(x, y, r, 0f, 2f);

    /// <summary>An arc from <paramref name="from"/> to <paramref name="to"/> turns (times pi) counter-clockwise.</summary>
    private static Vector2[] Arc(float x, float y, float r, float from, float to)
    {
        var p = new Vector2[MaxPoints];
        for (int i = 0; i < p.Length; i++)
        {
            float a = MathF.PI * (from + (to - from) * i / (p.Length - 1));
            p[i] = new Vector2(x + r * MathF.Cos(a), y + r * MathF.Sin(a));
        }
        return p;
    }
}
