using System.Collections.Generic;
using PeakXR.Core;
using TMPro;
using UnityEngine;

namespace PeakXR.UI.Common;

/// <summary>
/// Keeps text readable once nothing sits behind it: a thin outline that contrasts with the text colour, set on
/// the TMP material, in place of a panel. Materials are cloned once per source and shade and shared.
/// </summary>
internal static class TextHalo
{
    // SDF units: a thin rim, not a stroke.
    private const float Width = 0.2f;

    // Already outlined at least this much by the game: left as it is.
    private const float OwnOutline = 0.1f;
    private static readonly Color Dark = new(0f, 0f, 0f, 0.85f);

    // Paper-white, so dark text keeps the look it had on the game's light panels.
    private static readonly Color Light = new(0.98f, 0.95f, 0.88f, 0.9f);
    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly Dictionary<(int, bool), Material> clones = new();

    /// <summary>Gives <paramref name="text"/> a halo material. Returns the material it had, or null when
    /// nothing changed (no SDF outline support, or already outlined).</summary>
    public static Material Apply(TMP_Text text)
    {
        var source = text.fontSharedMaterial;
        if (source == null || !source.HasProperty(OutlineWidth) || clones.ContainsValue(source))
            return null;
        if (source.GetFloat(OutlineWidth) >= OwnOutline)
            return null;
        var c = text.color;
        var light = BackdropRules.NeedsLightHalo(c.r, c.g, c.b);
        var key = (source.GetInstanceID(), light);
        if (!clones.TryGetValue(key, out var halo) || halo == null)
        {
            halo = new Material(source) { name = source.name + " (PeakXR halo)" };
            Outline(halo, light);
            clones[key] = halo;
        }
        text.fontSharedMaterial = halo;
        return source;
    }

    /// <summary>Outlines a material this mod owns, in place.</summary>
    public static void Outline(Material m, bool light)
    {
        if (m == null || !m.HasProperty(OutlineWidth))
            return;
        m.SetFloat(OutlineWidth, Width);
        m.SetColor(OutlineColor, light ? Light : Dark);
        // TMP's mobile SDF shaders gate the outline behind this keyword; the full shader always draws it.
        m.EnableKeyword("OUTLINE_ON");
    }
}
