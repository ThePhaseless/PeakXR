using System.Collections.Generic;
using System.Text;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.UI;
using Zorro.UI.Modal;

namespace PeakXR.UI.Menu;

/// <summary>
/// Gives the converted title canvas depth, using only what the game shows at runtime:
/// <list type="bullet">
/// <item>Each direct child of the current <see cref="UIPage"/> (MainMenuPageHandler, A/MainMenuPageHandler.cs:14) that
/// holds a <see cref="Selectable"/> and is hand-sized (<see cref="TitleLayout.IsHandSized"/>) slides along its sight
/// line to arm's reach and turns to face the eye: the signpost and the studio logos come to hand, the logo, timer and
/// credits stay out in the scene, and big pages (settings) stay a reading panel.</item>
/// <item>No backdrop on any page: ui.world's BackdropStrip hides the flat-screen backings (registered through
/// <see cref="PeakXR.UI.World.CanvasRules.AddCustomMenu"/>) and outlines the text; <see cref="TitleSilhouettes"/> hides the logo's black
/// reveal copy, which the strip keeps as art.</item>
/// <item>On one screen (no group brought to hand) the canvas Animator plays the game's own intro (TitleIn slides
/// <c>MainPage/Menu</c> up from below the screen edge), cropped to the canvas as a monitor's edge crops it. With groups
/// at hand it is run to the end of each non-looping state, and the canvas fades in at its final spot instead; pages
/// fade in when they switch.</item>
/// <item>A fingertip press on a near button clicks it, next to the laser.</item>
/// </list>
/// Anything not found leaves that part as the plain floating panel. Everything is undone when VR stops.
/// </summary>
internal sealed class TitleMenuDepth
{
    private const float FadeSeconds = 0.35f;
    private const float RescanSeconds = 2f;
    private const float ModalAlpha = 0.2f;

    private sealed class Group
    {
        public RectTransform Rect;
        public Vector3 BasePosition,
            BaseScale,
            AppliedPosition,
            AppliedScale;
        public Quaternion BaseRotation,
            AppliedRotation;

        /// <summary>0 for a group brought to hand; otherwise a text group left on the panel, scaled by this
        /// about its pivot to stay readable from the eye.</summary>
        public float Grow;
    }

    // Most a text group on the panel may grow: past this it would cover the scene it sits in.
    private const float MaxTextGrowth = 3f;

    private readonly List<Group> groups = new();
    private readonly List<Selectable> buttons = new();
    private readonly TitleSilhouettes silhouettes = new();
    private readonly List<Graphic> graphics = new();
    private readonly List<Selectable> selectables = new();
    private readonly List<TMPro.TMP_Text> texts = new();
    private readonly float[] textHeights = new float[32];
    private readonly Vector3[] corners = new Vector3[4];
    private readonly PokeButton[] pokes = { new(), new() };
    private readonly StringBuilder log = new();

    private Canvas canvas;
    private RectTransform canvasRect;
    private UIPageHandler pages;
    private UIPage page;
    private Animator intro;
    private RectMask2D crop;
    private CanvasGroup fade;
    private bool ownFade;
    private float fadeStart;
    private float nextScan;
    private int introState;
    private int acquiredFrame;
    private string lastLog;
    private PointerEventData pokeData;
    private EventSystem pokeSystem;
    private Vector3 lastEye;

    /// <summary>Button groups brought to reach on the current page, for the self-test.</summary>
    public int NearGroups { get; private set; }

    /// <summary>Closest interactable near button to <paramref name="eye"/> in metres, or infinity.</summary>
    public float NearestButton(Vector3 eye)
    {
        var best = float.PositiveInfinity;
        foreach (var b in buttons)
            if (b != null && b.isActiveAndEnabled)
                best = Mathf.Min(best, Vector3.Distance(eye, b.transform.position));
        return best;
    }

    /// <summary>Called every frame from the title placement, before ui.world moves the canvas, with the stage's
    /// fixed eye point: works on the pose the canvas had last frame (it does not move once placed).</summary>
    /// <param name="oneScreen">Every group stays on the panel: the game's intro plays, cropped to the canvas.</param>
    public void Tick(Canvas target, Vector3 eye, float reach, bool oneScreen)
    {
        if (target != canvas)
        {
            Restore();
            Acquire(target);
            return;
        }
        if (
            canvas == null
            || Time.frameCount == acquiredFrame
            || Mathf.Abs(canvasRect.lossyScale.x) < 1e-6f
        )
            return;

        if (oneScreen)
            Crop();
        else
        {
            Uncrop();
            SkipIntro();
        }
        var current = pages != null ? pages.currentPage : null;
        if (current != page || Time.unscaledTime >= nextScan)
        {
            if (current != page)
                StartFade();
            page = current;
            Scan(eye);
        }
        lastEye = eye;
        ApplyGroups(eye, reach);
        UpdateFade();
        Poke();
    }

    /// <summary>Puts every moved group, hidden cover and the fade back as the game had them.</summary>
    public void Restore()
    {
        foreach (var g in groups)
            RestoreGroup(g);
        groups.Clear();
        NearGroups = 0;
        buttons.Clear();
        silhouettes.Restore();
        Uncrop();
        if (ownFade && fade != null)
            Object.Destroy(fade);
        fade = null;
        ownFade = false;
        pokes[0].Reset();
        pokes[1].Reset();
        canvas = null;
        canvasRect = null;
        pages = null;
        page = null;
        intro = null;
        lastLog = null;
    }

    private void Acquire(Canvas target)
    {
        canvas = target;
        if (canvas == null)
            return;
        canvasRect = (RectTransform)canvas.transform;
        acquiredFrame = Time.frameCount;
        pages = canvas.GetComponentInParent<UIPageHandler>(true);
        if (pages == null)
            pages = canvas.GetComponentInChildren<UIPageHandler>(true);
        // MainMenuPageHandler.IntroAnimation (A/MainMenuPageHandler.cs:20) is the Animator on the canvas itself.
        intro = canvas.GetComponent<Animator>();
        introState = 0;
        // A root CanvasGroup the game owns is left alone: no fades then.
        ownFade = !canvas.TryGetComponent(out fade);
        if (ownFade)
            fade = canvas.gameObject.AddComponent<CanvasGroup>();
        page = null;
        nextScan = 0f;
        StartFade();
    }

    /// <summary>A world panel has no screen edge: the canvas rect stands in for it, so the intro's slide-in from
    /// below shows only inside the screen. A canvas that already masks itself is left alone.</summary>
    private void Crop()
    {
        if (crop == null && !canvas.TryGetComponent(out RectMask2D _))
            crop = canvas.gameObject.AddComponent<RectMask2D>();
    }

    private void Uncrop()
    {
        if (crop != null)
            Object.Destroy(crop);
        crop = null;
    }

    private void StartFade()
    {
        fadeStart = Time.unscaledTime;
        if (ownFade && fade != null)
            fade.alpha = 0f;
    }

    /// <summary>Runs the canvas Animator through any intro state it is in, so nothing slides in from beyond a
    /// screen edge that a world panel does not have; the fade plays at the final spot instead.</summary>
    private void SkipIntro()
    {
        if (intro == null || !intro.isActiveAndEnabled || intro.runtimeAnimatorController == null)
            return;
        for (int i = 0; i < 4; i++)
        {
            var info = intro.GetCurrentAnimatorStateInfo(0);
            if (!intro.IsInTransition(0) && (info.loop || info.normalizedTime >= 1f))
                return;
            if (info.fullPathHash != introState)
            {
                introState = info.fullPathHash;
                StartFade();
                Plugin.Log.LogInfo(
                    $"[ui.menu] title intro: ran '{intro.name}' Animator state {info.fullPathHash} ({info.length:0.00} s) to its end"
                );
            }
            intro.Update(info.length + 1f);
        }
    }

    private void UpdateFade()
    {
        if (!ownFade || fade == null)
            return;
        var modal = RetrievableResourceSingleton<Modal>.GetInstanceWithoutCreating();
        bool covered = modal != null && modal.Visible;
        // A modal (failed to connect, version) opens in front of the head: dim the title behind it and keep the
        // near buttons from catching the laser through it.
        var alpha = covered
            ? ModalAlpha
            : TitleLayout.Entrance(Time.unscaledTime - fadeStart, FadeSeconds);
        if (fade.alpha != alpha)
            fade.alpha = alpha;
        if (fade.blocksRaycasts == covered)
        {
            fade.blocksRaycasts = !covered;
            fade.interactable = !covered;
        }
    }

    // ---- what is on the page ----

    private void Scan(Vector3 eye)
    {
        nextScan = Time.unscaledTime + RescanSeconds;
        foreach (var g in groups)
            RestoreGroup(g);
        groups.Clear();
        NearGroups = 0;
        buttons.Clear();
        silhouettes.Apply(canvas);
        if (page == null || page.transform.parent == null)
        {
            Report("no UIPage on the title canvas: plain panel");
            return;
        }

        log.Clear();
        log.Append("[ui.menu] title page '").Append(page.name).Append("': near");
        var kept = new StringBuilder(" | on panel");
        var t = page.transform;
        var layout = t.GetComponent<LayoutGroup>() != null;
        for (int i = 0; i < t.childCount; i++)
        {
            if (t.GetChild(i) is not RectTransform child || !child.gameObject.activeInHierarchy)
                continue;
            child.GetComponentsInChildren(false, selectables);
            float w = 0f,
                h = 0f;
            bool near =
                selectables.Count > 0
                && !layout
                && Measure(child, out w, out h, out var center)
                && TitleLayout.IsHandSized(w, h, Vector3.Distance(eye, center));
            if (!near)
            {
                kept.Append(' ').Append(child.name);
                var grow = TextGrowth(child, eye);
                if (grow > 1.01f)
                {
                    groups.Add(NewGroup(child, grow));
                    kept.Append($" (text x{grow:0.0})");
                }
                continue;
            }
            groups.Add(NewGroup(child, 0f));
            NearGroups++;
            buttons.AddRange(selectables);
            log.Append(' ').Append(child.name).Append($" ({w:0.00}x{h:0.00} m)");
        }
        selectables.Clear();
        log.Append(kept)
            .Append(" | silhouettes hidden ")
            .Append(silhouettes.Hidden)
            .Append(", fade ")
            .Append(ownFade ? "on" : "off (game CanvasGroup)");
        Report(log.ToString());
    }

    private static Group NewGroup(RectTransform child, float grow) =>
        new()
        {
            Rect = child,
            BasePosition = child.localPosition,
            BaseScale = child.localScale,
            BaseRotation = child.localRotation,
            AppliedPosition = child.localPosition,
            AppliedScale = child.localScale,
            AppliedRotation = child.localRotation,
            Grow = grow,
        };

    /// <summary>Scale that brings a small text group left out in the scene (the NEW ITEMS timer, the merch speech
    /// bubble) to <see cref="ReadableText.MinDegrees"/> from the eye, judged by its median font size; 1 for a
    /// reading panel (settings), the logo or a group without text.</summary>
    private float TextGrowth(RectTransform child, Vector3 eye)
    {
        child.GetComponentsInChildren(false, texts);
        int count = 0;
        var center = Vector3.zero;
        foreach (var text in texts)
        {
            if (!text.isActiveAndEnabled || count == textHeights.Length)
                continue;
            textHeights[count++] = text.fontSize * Mathf.Abs(text.transform.lossyScale.y);
            center += text.transform.position;
        }
        texts.Clear();
        if (count == 0 || !Measure(child, out var w, out var h, out _))
            return 1f;
        var distance = Vector3.Distance(eye, center / count);
        if (!TitleLayout.IsHandSized(w, h, distance))
            return 1f;
        return ReadableText.Grow(ReadableText.Median(textHeights, count), distance, MaxTextGrowth);
    }

    private void Report(string line)
    {
        if (line == lastLog)
            return;
        lastLog = line;
        Plugin.Log.LogInfo(line);
    }

    /// <summary>Drawn size of a group on the panel in metres and its world centre.</summary>
    private bool Measure(RectTransform group, out float width, out float height, out Vector3 center)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        group.GetComponentsInChildren(false, graphics);
        foreach (var g in graphics)
        {
            if (!g.isActiveAndEnabled || g.color.a < 0.01f)
                continue;
            g.rectTransform.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                Vector2 p = canvasRect.InverseTransformPoint(c);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }
        graphics.Clear();
        var scale = Mathf.Abs(canvasRect.lossyScale.x);
        width = (max.x - min.x) * scale;
        height = (max.y - min.y) * scale;
        center = canvasRect.TransformPoint((min + max) * 0.5f);
        return max.x > min.x && max.y > min.y;
    }

    // ---- near groups ----

    /// <summary>Each near group: its pivot slides along its sight line to <paramref name="reach"/>, it scales by the
    /// same factor (so it looks the same size and in the same spot) and turns to face the eye. A value the game or its
    /// Animator wrote since last frame (TitleIn writes <c>Menu</c>'s y) becomes the new base for that axis.</summary>
    private void ApplyGroups(Vector3 eye, float reach)
    {
        foreach (var g in groups)
        {
            var rect = g.Rect;
            if (rect == null || rect.parent == null)
                continue;
            g.BasePosition = Rebase(g.BasePosition, rect.localPosition, g.AppliedPosition);
            g.BaseScale = Rebase(g.BaseScale, rect.localScale, g.AppliedScale);
            if (rect.localRotation != g.AppliedRotation)
                g.BaseRotation = rect.localRotation;

            if (g.Grow > 0f)
            {
                var grown = g.BaseScale * g.Grow;
                if (rect.localScale != grown)
                    rect.localScale = grown;
                g.AppliedPosition = rect.localPosition;
                g.AppliedRotation = rect.localRotation;
                g.AppliedScale = rect.localScale;
                continue;
            }

            var parent = rect.parent;
            var k = TitleLayout.PullFactor(
                Vector3.Distance(eye, parent.TransformPoint(g.BasePosition)),
                reach
            );
            var eyeLocal = parent.InverseTransformPoint(eye);
            var position = TitleLayout
                .AlongSightLine(eyeLocal.ToNumerics(), g.BasePosition.ToNumerics(), k)
                .ToUnity();
            var look = position - eyeLocal;
            var rotation =
                k < 1f && look.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(look, Vector3.up) * g.BaseRotation
                    : g.BaseRotation;
            var scale = g.BaseScale * k;

            if (rect.localPosition != position)
                rect.localPosition = position;
            if (rect.localRotation != rotation)
                rect.localRotation = rotation;
            if (rect.localScale != scale)
                rect.localScale = scale;
            g.AppliedPosition = rect.localPosition;
            g.AppliedRotation = rect.localRotation;
            g.AppliedScale = rect.localScale;
        }
    }

    private static Vector3 Rebase(Vector3 baseValue, Vector3 current, Vector3 applied) =>
        new(
            current.x != applied.x ? current.x : baseValue.x,
            current.y != applied.y ? current.y : baseValue.y,
            current.z != applied.z ? current.z : baseValue.z
        );

    private static void RestoreGroup(Group g)
    {
        var rect = g.Rect;
        if (rect == null)
            return;
        rect.localPosition = Rebase(g.BasePosition, rect.localPosition, g.AppliedPosition);
        rect.localScale = Rebase(g.BaseScale, rect.localScale, g.AppliedScale);
        rect.localRotation =
            rect.localRotation != g.AppliedRotation ? rect.localRotation : g.BaseRotation;
    }

    // ---- fingertip ----

    private void Poke()
    {
        var rig = VRRig.Instance;
        if (rig == null || buttons.Count == 0 || (fade != null && !fade.interactable))
            return;
        Poke(rig, true);
        Poke(rig, false);
    }

    /// <summary>A fingertip pushed into a near button clicks it (VRKeyboard's poke: arm in front, press through).
    /// A disabled button, or one an invisible blocker covers as it would the laser's click (the hidden intro cover
    /// while connecting), answers with a soft, longer buzz instead of doing nothing.</summary>
    private void Poke(VRRig rig, bool left)
    {
        var poke = pokes[left ? 0 : 1];
        var aim = rig.Aim(left);
        if (aim == null || !VRInput.Hand(left).Tracked)
        {
            poke.Reset();
            return;
        }
        var tip = Avatar.DrawnHands.PokeTip(rig, left);
        Selectable hit = null;
        var front = float.PositiveInfinity;
        foreach (var b in buttons)
        {
            if (b == null || !b.isActiveAndEnabled)
                continue;
            var rect = (RectTransform)b.transform;
            var local = rect.InverseTransformPoint(tip);
            if (!rect.rect.Contains((Vector2)local))
                continue;
            // The canvas faces away from the player, so "in front" is toward -Z.
            var d = -local.z * Mathf.Abs(rect.lossyScale.z);
            if (Mathf.Abs(d) < Mathf.Abs(front))
            {
                front = d;
                hit = b;
            }
        }
        if (hit == null || !poke.Update(front, inside: true))
            return;
        if (!hit.IsInteractable() || Blocked(hit, tip))
        {
            UIHaptics.Tick(left, 0.25f, 0.12f);
            return;
        }
        var system = EventSystem.current;
        if (system == null)
            return;
        if (pokeData == null || pokeSystem != system)
        {
            pokeData = new PointerEventData(system);
            pokeSystem = system;
        }
        pokeData.Reset();
        pokeData.button = PointerEventData.InputButton.Left;
        ExecuteEvents.Execute(hit.gameObject, pokeData, ExecuteEvents.pointerClickHandler);
        UIHaptics.Tick(left, 0.6f, 0.02f);
    }

    /// <summary>
    /// Vanilla blocking for a poke: a raycast target drawn after the button (higher <see cref="Graphic.depth"/>, not
    /// part of the button) on the sight line from the eye through the fingertip takes the click, as GraphicRaycaster
    /// would give it the laser's. Runs only on a press.
    /// </summary>
    private bool Blocked(Selectable button, Vector3 tip)
    {
        var target = button.targetGraphic;
        if (target == null || canvas == null)
            return false;
        var ray = new Ray(lastEye, tip - lastEye);
        var list = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            if (
                g.depth <= target.depth
                || !g.raycastTarget
                || g.canvasRenderer.cull
                || !g.isActiveAndEnabled
                || g.transform.IsChildOf(button.transform)
                // Another control drawn over it is a neighbour, not an invisible blocker.
                || g.GetComponentInParent<Selectable>() != null
            )
                continue;
            var rect = g.rectTransform;
            var plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out var enter))
                continue;
            if (rect.rect.Contains((Vector2)rect.InverseTransformPoint(ray.GetPoint(enter))))
                return true;
        }
        return false;
    }
}
