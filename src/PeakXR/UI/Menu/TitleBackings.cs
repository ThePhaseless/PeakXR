using System.Collections.Generic;
using PeakXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.Menu;

/// <summary>
/// No backdrops on the VR title: what only works over a flat screen is hidden and the text carries its own contrast.
/// <list type="bullet">
/// <item>Backings (<see cref="TitleLayout.IsBacking"/>): every full-canvas graphic (MainPage/WhiteFade, the cream
/// intro and connecting cover; Darken; Vignette; the settings, join and first-launch page backgrounds) and large
/// sprite-less fills behind text (the credits' black strip, MainPage/Credits/BG).</item>
/// <item>Silhouettes: a near-black copy of another image's sprite (MainPage/LogoBlack, the TitleIn/TitleLoading reveal
/// over the logo, same sprite and size). Over a 3D scene it reads as a hole where the logo is; the logo under it is already right.</item>
/// <item>Text: every TMP text on the canvas gets an outline that contrasts with its own colour, through one shared
/// material copy per font material, so white labels read over sand and sky and dark sign text keeps its look.</item>
/// </list>
/// Graphics are hidden through their canvas renderer alpha, so the game's colour animation and raycast blocking stay.
/// Applied on every page scan; <see cref="Restore"/> puts everything back.
/// </summary>
internal sealed class TitleBackings
{
    private const float OutlineWidth = 0.2f;
    private const float SilhouetteMaxChannel = 0.05f;
    private static readonly Color DarkOutline = new(0.04f, 0.03f, 0.02f, 0.9f);
    private static readonly Color LightOutline = new(0.96f, 0.94f, 0.86f, 0.9f);

    private readonly List<Graphic> hidden = new();
    private readonly List<float> hiddenAlphas = new();
    private readonly List<TMP_Text> texts = new();
    private readonly List<Material> originals = new();
    private readonly Dictionary<Material, Material> darkCopies = new();
    private readonly Dictionary<Material, Material> lightCopies = new();
    private readonly HashSet<Material> copies = new();
    private readonly List<Graphic> graphics = new();
    private readonly List<TMP_Text> found = new();
    private readonly Vector3[] corners = new Vector3[4];

    public int Hidden => hidden.Count;
    public int Outlined => texts.Count;

    public void Apply(Canvas canvas)
    {
        var rect = (RectTransform)canvas.transform;
        var bounds = rect.rect;
        if (bounds.width < 1f || bounds.height < 1f)
            return;
        canvas.GetComponentsInChildren(true, graphics);
        for (int i = 0; i < graphics.Count; i++)
        {
            var g = graphics[i];
            if (
                g is not (Image or RawImage)
                || hidden.Contains(g)
                || g.TryGetComponent<Mask>(out _)
                || g.TryGetComponent<RectMask2D>(out _)
            )
                continue;
            if (IsSilhouette(i) || IsBacking(g, rect, bounds))
            {
                hidden.Add(g);
                hiddenAlphas.Add(g.canvasRenderer.GetAlpha());
            }
        }
        graphics.Clear();
        foreach (var g in hidden)
            if (g != null && g.canvasRenderer.GetAlpha() > 0f)
                g.canvasRenderer.SetAlpha(0f);

        canvas.GetComponentsInChildren(true, found);
        foreach (var t in found)
            Outline(t);
        found.Clear();
    }

    public void Restore()
    {
        for (int i = 0; i < hidden.Count; i++)
            if (hidden[i] != null)
                hidden[i].canvasRenderer.SetAlpha(hiddenAlphas[i]);
        hidden.Clear();
        hiddenAlphas.Clear();
        for (int i = 0; i < texts.Count; i++)
            if (texts[i] != null && copies.Contains(texts[i].fontSharedMaterial))
                texts[i].fontSharedMaterial = originals[i];
        texts.Clear();
        originals.Clear();
        foreach (var m in copies)
            Object.Destroy(m);
        copies.Clear();
        darkCopies.Clear();
        lightCopies.Clear();
    }

    private bool IsBacking(Graphic g, RectTransform canvas, Rect bounds)
    {
        g.rectTransform.GetWorldCorners(corners);
        Vector2 a = canvas.InverseTransformPoint(corners[0]);
        Vector2 b = canvas.InverseTransformPoint(corners[2]);
        var lo = Vector2.Max(Vector2.Min(a, b), bounds.min);
        var hi = Vector2.Min(Vector2.Max(a, b), bounds.max);
        if (hi.x <= lo.x || hi.y <= lo.y)
            return false;
        bool flat = g is Image image ? image.sprite == null : ((RawImage)g).texture == null;
        bool holdsText =
            flat
            && (
                g.GetComponentInChildren<TMP_Text>(true) != null
                || g.GetComponentInChildren<Text>(true) != null
            );
        return TitleLayout.IsBacking(
            (hi.x - lo.x) / bounds.width,
            (hi.y - lo.y) / bounds.height,
            flat,
            holdsText
        );
    }

    /// <summary>A near-black image drawn over (after, in hierarchy order) a coloured one with the same sprite. Drop
    /// shadows share their art's sprite too, but are drawn before it.</summary>
    private bool IsSilhouette(int index)
    {
        var g = graphics[index];
        if (
            g is not Image image
            || image.sprite == null
            || MaxChannel(g.color) > SilhouetteMaxChannel
        )
            return false;
        for (int i = 0; i < index; i++)
            if (
                graphics[i] is Image o
                && o.sprite == image.sprite
                && MaxChannel(o.color) > SilhouetteMaxChannel
                && SameSize(o.rectTransform, g.rectTransform)
            )
                return true;
        return false;
    }

    /// <summary>Within 5% both ways: a copy of the art, not a panel sharing a generic rounded sprite.</summary>
    private static bool SameSize(RectTransform a, RectTransform b)
    {
        var x = a.rect.size;
        var y = b.rect.size;
        return Mathf.Abs(x.x - y.x) <= 0.05f * Mathf.Max(x.x, y.x)
            && Mathf.Abs(x.y - y.y) <= 0.05f * Mathf.Max(x.y, y.y);
    }

    private static float MaxChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

    private void Outline(TMP_Text t)
    {
        var source = t.fontSharedMaterial;
        if (source == null || copies.Contains(source))
            return;
        if (
            !source.HasProperty(ShaderUtilities.ID_OutlineWidth)
            || !source.HasProperty(ShaderUtilities.ID_OutlineColor)
            || source.GetFloat(ShaderUtilities.ID_OutlineWidth) >= OutlineWidth
        )
            return;
        var c = t.color;
        bool light = TitleLayout.LightOutline(c.r, c.g, c.b);
        var cache = light ? lightCopies : darkCopies;
        if (!cache.TryGetValue(source, out var copy))
        {
            copy = new Material(source) { name = source.name + " (PeakXR outline)" };
            copy.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
            copy.SetColor(ShaderUtilities.ID_OutlineColor, light ? LightOutline : DarkOutline);
            cache[source] = copy;
            copies.Add(copy);
        }
        int i = texts.IndexOf(t);
        if (i < 0)
        {
            texts.Add(t);
            originals.Add(source);
        }
        else
            originals[i] = source;
        t.fontSharedMaterial = copy;
    }
}
