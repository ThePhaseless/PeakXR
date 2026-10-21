using System.Collections.Generic;
using PeakXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.UI.Common;

/// <summary>
/// Keeps text readable once nothing sits behind it: a soft SDF underlay (a glow behind the glyphs) that contrasts
/// with the text colour, set on the TMP material, in place of a panel. Not an outline: TMP centres the outline on
/// the glyph edge, so it eats into the face and turned the game's rough, grainy font into speckles; the underlay sits
/// behind the face and leaves the glyphs as drawn. Materials are cloned once per source and shade and shared.
/// </summary>
/// <remarks>
/// Both shipped TMP SDF shaders (Distance Field, Mobile/Distance Field in globalgamemanagers.assets) carry
/// UNDERLAY_ON variants, and the game's own DarumaDropOne "SDF Shadow"/"SDF Outline" materials use it. A shader that
/// does not declare the keyword gets the outline fallback instead. TMP's ratio C scales dilate plus softness (kept at
/// or under 1) to the font's gradient scale, so the halo stays inside the atlas padding (6 to 16 for the game fonts).
/// </remarks>
internal static class TextHalo
{
    // SDF units: grown a little past the glyph edge and feathered, so it reads as a halo, not a stroke.
    private const float Dilate = 0.35f;
    private const float Softness = 0.45f;

    // Fallback: a thin feathered rim, the face grown by half of it so the rim does not eat into the glyphs.
    private const float RimWidth = 0.12f;
    private const float RimSoftness = 0.15f;

    // Already outlined at least this much by the game: left as it is.
    private const float OwnOutline = 0.1f;
    private static readonly Color Dark = new(0f, 0f, 0f, 0.85f);

    // Paper-white, so dark text keeps the look it had on the game's light panels.
    private static readonly Color Light = new(0.98f, 0.95f, 0.88f, 0.9f);
    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineSoftness = Shader.PropertyToID("_OutlineSoftness");
    private static readonly int FaceDilate = Shader.PropertyToID("_FaceDilate");
    private static readonly int UnderlayColor = Shader.PropertyToID("_UnderlayColor");
    private static readonly int UnderlayDilate = Shader.PropertyToID("_UnderlayDilate");
    private static readonly int UnderlaySoftness = Shader.PropertyToID("_UnderlaySoftness");
    private static readonly int UnderlayX = Shader.PropertyToID("_UnderlayOffsetX");
    private static readonly int UnderlayY = Shader.PropertyToID("_UnderlayOffsetY");
    private const string UnderlayOn = "UNDERLAY_ON";
    private const string OutlineOn = "OUTLINE_ON";
    private static readonly Dictionary<(int, bool), Material> clones = new();

    /// <summary>Gives <paramref name="text"/> a halo material. Returns the material it had, or null when
    /// nothing changed (no SDF underlay support, or already outlined or underlaid).</summary>
    public static Material Apply(TMP_Text text)
    {
        var source = text.fontSharedMaterial;
        if (source == null || clones.ContainsValue(source))
            return null;
        if (
            source.IsKeywordEnabled(UnderlayOn)
            || (source.HasProperty(OutlineWidth) && source.GetFloat(OutlineWidth) >= OwnOutline)
        )
            return null;
        var c = text.color;
        var light = BackdropRules.NeedsLightHalo(c.r, c.g, c.b);
        var key = (source.GetInstanceID(), light);
        if (!clones.TryGetValue(key, out var halo) || halo == null)
        {
            halo = new Material(source) { name = source.name + " (PeakXR halo)" };
            if (!Halo(halo, light))
            {
                Object.Destroy(halo);
                return null;
            }
            clones[key] = halo;
        }
        text.fontSharedMaterial = halo;
        return source;
    }

    /// <summary>Gives a material this mod owns the halo, in place: the underlay where the shader declares it, else
    /// the outline. False when the shader has neither. Set it before the material reaches a text, so TMP sizes the
    /// glyph quads for the halo's padding.</summary>
    public static bool Halo(Material m, bool light)
    {
        if (m == null || m.shader == null)
            return false;
        var color = light ? Light : Dark;
        var underlay = new LocalKeyword(m.shader, UnderlayOn);
        if (underlay.isValid && m.HasProperty(UnderlayColor))
        {
            m.SetColor(UnderlayColor, color);
            m.SetFloat(UnderlayDilate, Dilate);
            m.SetFloat(UnderlaySoftness, Softness);
            m.SetFloat(UnderlayX, 0f);
            m.SetFloat(UnderlayY, 0f);
            m.SetKeyword(underlay, true);
        }
        else if (m.HasProperty(OutlineWidth) && m.HasProperty(OutlineColor))
        {
            m.SetFloat(OutlineWidth, RimWidth);
            m.SetColor(OutlineColor, color);
            if (m.HasProperty(OutlineSoftness))
                m.SetFloat(OutlineSoftness, RimSoftness);
            if (m.HasProperty(FaceDilate))
                m.SetFloat(FaceDilate, m.GetFloat(FaceDilate) + RimWidth * 0.5f);
            // The mobile shader gates its outline behind this keyword; the full shader always draws it.
            var outline = new LocalKeyword(m.shader, OutlineOn);
            if (outline.isValid)
                m.SetKeyword(outline, true);
        }
        else
            return false;
        // Ratios scale the effect to the font's gradient scale, keeping it inside the atlas padding.
        ShaderUtilities.UpdateShaderRatios(m);
        return true;
    }
}
