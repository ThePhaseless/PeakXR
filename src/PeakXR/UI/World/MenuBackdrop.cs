using PeakXR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.World;

/// <summary>
/// One soft, rounded, semi-transparent backdrop behind a world-space menu's drawn content. The game's menus
/// darken the whole screen with a full-canvas dimmer; on a world panel that dimmer becomes a hard black slab
/// floating in the scene, so it is made invisible and this backdrop keeps the text readable instead.
/// Built when a canvas converts, refitted only when the content or panel scale changes, removed on revert.
/// </summary>
internal static class MenuBackdrop
{
    // Sprite: a rounded rect whose corners and feathered rim live in the 9-slice border, so it stays round at any size.
    private const int TextureSize = 64;
    private const int Border = 28;
    private const float CornerRadius = 24f;
    private const float FeatherTexels = 16f;
    private const float SpritePixelsPerUnit = 100f;

    // World size of the slice border (corner + rim) and the gap between content and backdrop edge.
    private const float BorderMeters = 0.05f;
    private const float PaddingMeters = 0.04f;

    // Dark enough to read white text against bright sky and sand while the scene still shows through.
    private static readonly Color Tint = new(0.04f, 0.05f, 0.07f, 0.62f);

    // A full-canvas graphic this dark is a dimmer, not artwork.
    private const float DimmerMaxChannel = 0.35f;

    private static Sprite sprite;

    public static void Attach(TrackedCanvas t)
    {
        var go = new GameObject("PeakXR_MenuBackdrop", typeof(RectTransform));
        go.layer = t.Canvas.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(t.Rect, worldPositionStays: false);
        rect.SetAsFirstSibling();
        rect.anchorMin = rect.anchorMax = t.Rect.pivot;
        rect.pivot = new Vector2(0.5f, 0.5f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var image = go.AddComponent<Image>();
        image.sprite = Sprite();
        image.type = Image.Type.Sliced;
        image.color = Tint;
        image.raycastTarget = false;
        t.Backdrop = image;
        t.BackdropScale = 0f;
        t.BackdropAlpha = Tint.a;
        t.BackdropWasOpen = false;
        go.SetActive(false);
    }

    /// <summary>Puts every hidden dimmer back and removes the backdrop.</summary>
    public static void Detach(TrackedCanvas t)
    {
        for (int i = 0; i < t.Dimmers.Count; i++)
            if (t.Dimmers[i] != null)
                t.Dimmers[i].canvasRenderer.SetAlpha(t.DimmerAlphas[i]);
        t.Dimmers.Clear();
        t.DimmerAlphas.Clear();
        if (t.Backdrop != null)
            Object.Destroy(t.Backdrop.gameObject);
        t.Backdrop = null;
    }

    /// <summary>
    /// A full-canvas graphic found while measuring. Dark ones are dimmers: hidden through the canvas renderer
    /// alpha, so they still block raycasts like the game expects and the game's own colour writes are kept.
    /// </summary>
    public static void OnFullCanvasGraphic(TrackedCanvas t, Graphic g)
    {
        if (g is not (Image or RawImage))
            return;
        var c = g.color;
        if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > DimmerMaxChannel)
            return;
        // A mask graphic culled at alpha 0 stops writing its stencil and would hide everything it masks.
        if (g.TryGetComponent<Mask>(out _) || g.TryGetComponent<RectMask2D>(out _) || IsViewport(g))
            return;
        if (!t.Dimmers.Contains(g))
        {
            t.Dimmers.Add(g);
            t.DimmerAlphas.Add(g.canvasRenderer.GetAlpha());
        }
        // Re-asserted each measure in case something reset the renderer alpha.
        if (g.canvasRenderer.GetAlpha() > 0f)
            g.canvasRenderer.SetAlpha(0f);
    }

    /// <summary>Forgets dimmers destroyed with their page.</summary>
    public static void PruneDimmers(TrackedCanvas t)
    {
        for (int i = t.Dimmers.Count - 1; i >= 0; i--)
            if (t.Dimmers[i] == null)
            {
                t.Dimmers.RemoveAt(i);
                t.DimmerAlphas.RemoveAt(i);
            }
    }

    private static bool IsViewport(Graphic g)
    {
        var scroll = g.GetComponentInParent<ScrollRect>(true);
        return scroll != null && scroll.viewport == g.rectTransform;
    }

    /// <summary>Shows the backdrop with its menu once the page has settled, fades it with the page and fits
    /// it around the measured content.</summary>
    public static void Sync(TrackedCanvas t)
    {
        var image = t.Backdrop;
        // Metres per canvas pixel as placed this frame.
        var mpp = t.Rect.lossyScale.x;
        var show = t.IsOpen && t.Settled && mpp >= 1e-6f;
        if (image.gameObject.activeSelf != show)
            image.gameObject.SetActive(show);
        if (!show)
            return;

        // Follow the page's CanvasGroup fade (EndScreen, BoardingPass, Modal, ScaleOnEnable).
        var probe = t.FadeProbe;
        var fade =
            probe != null && probe.isActiveAndEnabled
                ? probe.canvasRenderer.GetInheritedAlpha()
                : 1f;
        var alpha = Tint.a * fade;
        if (alpha != t.BackdropAlpha)
        {
            t.BackdropAlpha = alpha;
            image.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
        }

        if (
            mpp == t.BackdropScale
            && t.ContentSize == t.BackdropSize
            && t.ContentCenter == t.BackdropCenter
        )
            return;
        t.BackdropScale = mpp;
        t.BackdropSize = t.ContentSize;
        t.BackdropCenter = t.ContentCenter;

        var rect = image.rectTransform;
        var padding = PaddingMeters / mpp;
        rect.anchoredPosition = t.ContentCenter;
        rect.sizeDelta = t.ContentSize + new Vector2(2f * padding, 2f * padding);
        // Slice border spans BorderMeters whatever the panel scale: canvas px = Border * refPPU / (spritePPU * multiplier).
        var borderPixels = BorderMeters / mpp;
        image.pixelsPerUnitMultiplier =
            Border * t.Canvas.referencePixelsPerUnit / (SpritePixelsPerUnit * borderPixels);
    }

    private static Sprite Sprite()
    {
        if (sprite != null)
            return sprite;
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false)
        {
            name = "PeakXR_MenuBackdrop",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color32[TextureSize * TextureSize];
        const float half = TextureSize * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            var a = RoundedRect.Alpha(
                x + 0.5f - half,
                y + 0.5f - half,
                half,
                half,
                CornerRadius,
                FeatherTexels
            );
            pixels[y * TextureSize + x] = new Color32(
                255,
                255,
                255,
                (byte)Mathf.RoundToInt(a * 255f)
            );
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        sprite = UnityEngine.Sprite.Create(
            tex,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f),
            SpritePixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            new Vector4(Border, Border, Border, Border)
        );
        sprite.name = tex.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
