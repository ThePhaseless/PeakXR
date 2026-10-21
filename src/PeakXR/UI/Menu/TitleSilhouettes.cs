using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.Menu;

/// <summary>
/// Hides black reveal copies on the VR title: a near-black image drawn over (after, in hierarchy order) a coloured
/// one with the same sprite and size. MainMenu/Canvas/MainPage/LogoBlack is one: the canvas Animator's TitleLoading
/// and TitleIn clips fade it to uncover the logo over a cream cover. Over the 3D scene it reads as a hole where the
/// logo is, and the logo under it is already right. Drop shadows share their art's sprite but are drawn before it.
/// Backdrops and text outlines are ui.world's <c>BackdropStrip</c>, which this leaves alone: it only takes graphics
/// still visible, so nothing is hidden by both.
/// </summary>
/// <remarks>Raycasts stay vanilla, as in <c>BackdropStrip</c>: a hidden graphic keeps its mesh batched
/// (<c>cullTransparentMesh</c> off), so it keeps its depth and still blocks what it blocked.</remarks>
internal sealed class TitleSilhouettes
{
    private const float MaxChannel = 0.05f;

    private readonly List<Image> hidden = new();
    private readonly List<float> alphas = new();
    private readonly List<bool> culls = new();
    private readonly List<Image> images = new();

    public int Hidden => hidden.Count;

    public void Apply(Canvas canvas)
    {
        canvas.GetComponentsInChildren(true, images);
        for (int i = 0; i < images.Count; i++)
        {
            var g = images[i];
            var cr = g.canvasRenderer;
            if (hidden.Contains(g) || cr.GetAlpha() <= 0f || !IsSilhouette(i))
                continue;
            hidden.Add(g);
            alphas.Add(cr.GetAlpha());
            culls.Add(cr.cullTransparentMesh);
            cr.cullTransparentMesh = false;
            cr.SetAlpha(0f);
        }
        images.Clear();
        // The game's tweens may reset a renderer alpha; keep ours at 0.
        foreach (var g in hidden)
            if (g != null && g.canvasRenderer.GetAlpha() > 0f)
                g.canvasRenderer.SetAlpha(0f);
    }

    /// <summary>Shows them again; an alpha someone else changed since is left as they set it.</summary>
    public void Restore()
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            var g = hidden[i];
            if (g == null)
                continue;
            if (g.canvasRenderer.GetAlpha() <= 0f)
                g.canvasRenderer.SetAlpha(alphas[i]);
            g.canvasRenderer.cullTransparentMesh = culls[i];
        }
        hidden.Clear();
        alphas.Clear();
        culls.Clear();
    }

    private bool IsSilhouette(int index)
    {
        var g = images[index];
        if (g.sprite == null || Max(g.color) > MaxChannel)
            return false;
        for (int i = 0; i < index; i++)
        {
            var o = images[i];
            if (
                o.sprite == g.sprite
                && Max(o.color) > MaxChannel
                && SameSize(o.rectTransform, g.rectTransform)
            )
                return true;
        }
        return false;
    }

    private static float Max(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

    /// <summary>Within 5% both ways: a copy of the art, not a panel sharing a generic rounded sprite.</summary>
    private static bool SameSize(RectTransform a, RectTransform b)
    {
        var x = a.rect.size;
        var y = b.rect.size;
        return Mathf.Abs(x.x - y.x) <= 0.05f * Mathf.Max(x.x, y.x)
            && Mathf.Abs(x.y - y.y) <= 0.05f * Mathf.Max(x.y, y.y);
    }
}
