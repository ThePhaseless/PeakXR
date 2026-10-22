using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Zorro.Core;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U1: the interact prompt at the thing it is about. <c>GUIManager.RefreshInteractablePrompt</c> (A/GUIManager.cs:1045)
/// fills <c>interactName</c> and <c>interactPromptPrimary/Secondary/Hold</c> (A/GUIManager.cs:84-92) for
/// <c>Interaction.currentHovered</c> (A/Interaction.cs:15); those groups move into a box posed just above the
/// silhouette of <c>IInteractible.Center()</c> (A/IInteractible.cs:13) as you see it, at the depth of its front
/// (<see cref="SpatialMath.TargetLabel"/>), so the text never crosses the object or a hand reaching into it; level,
/// facing you, at a constant angular size, its small action prompts scaled up and their monitor backings hidden. Prompts
/// that have no target sit above the acting hand instead: the piton prompt while climbing (SETPITON,
/// A/GUIManager.cs:1080-1084), the lunge prompt (<c>interactPromptLunge</c>, :94, set at :371) and the web
/// struggle prompt (<c>strugglePrompt</c>, :100, shown from A/CharacterAfflictions.cs:803). The hold ring of a held
/// interaction (ui.hand) goes just below the label through <see cref="TryRing"/>.
/// D8: in VR the verb text (GRAB, CARRY, DROP, EAT, SETPITON, lunge, struggle) gives way to a glyph drawn where the
/// text was (<see cref="PromptGlyph"/>), the struggle glyph on both wrists, with a tick (and for lunge and struggle a
/// notice) when a hand prompt appears. <c>UI.ActionHintText</c> brings the verb text back. The item or object name
/// shows only with <c>UI.TargetNames</c>; without it a plain pickup has no grab glyph either, only the game's glow
/// on what the hand would grab (<see cref="GripGlow"/>).
/// </summary>
internal sealed class TargetPrompt
{
    private const float Spacing = 6f;
    private const float RingDegrees = 3.5f;
    private const int Parts = 6;

    /// <summary>Smallest text, in HUD pixels (1.35 degrees at the floor <see cref="SpatialRuntime.DegreesPerPixel"/>):
    /// the game's action prompts are sized for a monitor and read as specks at a target.</summary>
    private const float MinFontPixels = 30f;

    /// <summary>Frames to keep measuring after a target change: the game writes the new name a frame later.</summary>
    private const int RestackFrames = 3;

    /// <summary>Angular size of a verb glyph: a touch above the text line it replaces.</summary>
    private const float GlyphDegrees = 1.8f;
    private const float WristGlyphDegrees = 2.4f;

    /// <summary>Glyph slots: primary, secondary, lunge (in place of their text), struggle on the left and right wrist,
    /// and the hold ring beside the primary glyph.</summary>
    private const int Glyphs = 6;

    private const int HoldSlot = 5;

    /// <summary>Points of the hold ring's fill (<see cref="PromptGlyph.HoldFill"/>).</summary>
    private const int HoldPoints = 17;

    /// <summary>The part whose text each in-place glyph slot replaces.</summary>
    private static readonly int[] GlyphPart = { 1, 2, 4 };
    private static readonly Color GlyphColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color GlyphRim = new(0.06f, 0.05f, 0.05f, 0.6f);

    private static readonly List<Renderer> renderers = new();
    private static readonly List<TMP_Text> texts = new();
    private static TargetPrompt current;

    private readonly SpatialBox box = new("PeakXR_TargetPrompt");
    private readonly GameObject[] parts = new GameObject[Parts];
    private readonly bool[] shown = new bool[Parts];
    private IInteractible target;
    private Item heldItem;
    private float radius = 0.1f,
        heldRadius,
        height;
    private bool anyShown,
        warned;
    private int resolvedFrame = -1,
        restack;

    private sealed class GlyphView
    {
        public Transform Root;
        public LineRenderer First,
            Second,
            FirstRim,
            SecondRim;
        public PromptVerb Verb;
    }

    private readonly List<CanvasRenderer> verbTexts = new();

    /// <summary>The target's name (<c>interactName</c>), hidden in VR unless <c>UI.TargetNames</c>.</summary>
    private readonly List<CanvasRenderer> nameTexts = new();
    private readonly GripGlow glow = new();
    private readonly Vector3[] partCentre = new Vector3[Parts];
    private readonly GlyphView[] views = new GlyphView[Glyphs];
    private readonly PromptVerb[] verbs = new PromptVerb[Glyphs];
    private readonly System.Numerics.Vector2[] holdPoints = new System.Numerics.Vector2[HoldPoints];
    private float holdProgress,
        holdDrawn = -1f;
    private GameObject glyphRoot;
    private bool textHidden,
        nameHidden,
        lungeShown,
        pitonShown;

    /// <summary>For the self-test: the prompt's verb text is hidden now (VR, <c>UI.ActionHintText</c> off).</summary>
    public static bool TextHidden => current != null && current.textHidden;

    /// <summary>For the self-test: the target's name is hidden now (VR, <c>UI.TargetNames</c> off).</summary>
    public static bool NameHidden => current != null && current.nameHidden;

    /// <summary>For the self-test: a glyph for <paramref name="verb"/> is drawn now.</summary>
    public static bool Drawn(PromptVerb verb)
    {
        var prompt = current;
        if (prompt == null || verb == PromptVerb.None)
            return false;
        foreach (var v in prompt.views)
        {
            if (v != null && v.Verb == verb && (v.First.enabled || v.FirstRim.enabled))
                return true;
        }
        return false;
    }

    /// <summary>For the self-test: how full the drawn hold ring is (0..1), or -1 when none is drawn.</summary>
    public static float HoldFillShown =>
        current != null && Drawn(PromptVerb.Hold) ? current.holdDrawn : -1f;

    /// <summary>
    /// Where ui.hand puts the progress ring of a held interaction: under the target's label. Computed on demand from
    /// this frame's hover, so the ring never trails a frame behind a target change.
    /// </summary>
    public static bool TryRing(VRRig rig, out Vector3 position, out float width)
    {
        position = default;
        width = 0f;
        var prompt = current;
        if (prompt == null || !prompt.box.Holding || rig == null || rig.Head == null)
            return false;
        prompt.Resolve();
        if (prompt.target == null || !prompt.TargetPose(rig, out var p, out var mpp, out var d))
            return false;
        width = SpatialMath.SpanMeters(d, RingDegrees);
        position = p + Vector3.down * (prompt.height * 0.5f * mpp + width * 0.7f);
        return true;
    }

    public void Release()
    {
        SetTextAlpha(1f);
        SetAlpha(nameTexts, 1f);
        glow.Release();
        verbTexts.Clear();
        nameTexts.Clear();
        ClearGlyphs();
        box.Restore();
        target = null;
        if (current == this)
            current = null;
    }

    public void Forget()
    {
        glow.Release();
        verbTexts.Clear();
        nameTexts.Clear();
        ClearGlyphs();
        box.Forget();
        target = null;
        if (current == this)
            current = null;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!box.Holding)
        {
            Grab(gui, hudRoot);
            if (!box.Holding)
                return;
        }
        current = this;
        Resolve();

        bool changed = false;
        anyShown = false;
        for (int i = 0; i < Parts; i++)
        {
            bool on = parts[i] != null && parts[i].activeSelf;
            anyShown |= on;
            if (on != shown[i])
            {
                shown[i] = on;
                changed = true;
            }
        }
        if (changed || restack > 0)
        {
            height = box.Stack(Spacing);
            restack = changed ? RestackFrames : restack - 1;
            foreach (int p in GlyphPart)
            {
                if (parts[p] != null && parts[p].transform is RectTransform rect)
                    partCentre[p] = box.ContentBounds(rect).center;
            }
        }

        box.SetVisible(target != null || anyShown);
        textHidden = VRSession.Active && !(AffordanceFeature.ActionHintText?.Value ?? false);
        SetTextAlpha(textHidden ? 0f : 1f);
        nameHidden = VRSession.Active && !SpatialFeature.Settings.TargetNames.Value;
        SetAlpha(nameTexts, nameHidden ? 0f : 1f);
        glow.Tick();
        PickVerbs(rig);
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        PoseBox(rig);
        PoseGlyphs(rig);
    }

    private void PoseBox(VRRig rig)
    {
        if (!box.Holding)
            return;
        var eye = rig.Head.position;
        if (target != null && TargetPose(rig, out var p, out var mpp, out _))
        {
            box.Pose(p, SpatialRuntime.FaceEyeYaw(p, eye, rig.Head.forward), mpp);
            return;
        }
        if (!anyShown)
            return;
        // No target: above the hand that acts (the pointing hand, else the dominant one).
        var hand = rig.Hand(ActingLeft());
        if (hand == null)
            return;
        var at = hand.position + Vector3.up * 0.14f;
        at += (eye - at).normalized * 0.04f;
        box.Pose(
            at,
            SpatialRuntime.FaceEyeYaw(at, eye, rig.Head.forward),
            SpatialMath.MetersPerPixel(Vector3.Distance(at, eye), SpatialRuntime.DegreesPerPixel)
        );
    }

    /// <summary>Follows this frame's hovered target; a new one gets measured and one soft tick.</summary>
    private void Resolve()
    {
        if (resolvedFrame == Time.frameCount)
            return;
        resolvedFrame = Time.frameCount;
        var c = Character.localCharacter;
        var held = c != null ? c.data.currentItem : null;
        if (held != heldItem)
        {
            heldItem = held;
            heldRadius = held != null ? Radius(held) : 0f;
        }
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        if (!hovered.UnityObjectExists())
            hovered = null;
        if (hovered == target)
            return;
        target = hovered;
        if (target == null)
            return;
        radius = target is Component comp ? Radius(comp) : 0.1f;
        restack = RestackFrames;
        var aim = PhysicalInteraction.AimHand;
        if (aim >= 0)
            UIHaptics.Tick(aim == 0, 0.12f, 0.015f);
    }

    private bool TargetPose(VRRig rig, out Vector3 position, out float mpp, out float distance)
    {
        position = default;
        mpp = distance = 0f;
        if (!target.UnityObjectExists())
            return false;
        var eye = rig.Head.position;
        // Clear the label's lower edge, or the ring under it while a held interaction runs (TryRing).
        var interaction = Interaction.instance;
        var below = height * 0.5f * SpatialRuntime.DegreesPerPixel;
        if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, target))
            below += RingDegrees * 1.2f;
        // Clear of the item in your hand too (food held over a fire), unless that item is the target.
        bool avoid = heldItem != null && !ReferenceEquals(heldItem, target);
        position = SpatialMath
            .TargetLabel(
                target.Center().ToNumerics(),
                eye.ToNumerics(),
                radius,
                below,
                avoid ? heldItem.Center().ToNumerics() : default,
                avoid ? heldRadius : 0f
            )
            .ToUnity();
        distance = Vector3.Distance(position, eye);
        mpp = SpatialMath.MetersPerPixel(distance, SpatialRuntime.DegreesPerPixel);
        return true;
    }

    /// <summary>The hand a prompt without a target belongs to: the pointing hand, else the dominant one.</summary>
    private static bool ActingLeft()
    {
        var aim = PhysicalInteraction.AimHand;
        return aim >= 0 ? aim == 0 : VRInput.LeftHanded;
    }

    /// <summary>The game's prompt texts (GUIManager.cs:1049, :1065, :1083, the lunge and struggle prompts) through
    /// the renderer alpha, so the game's own writes still land and <see cref="Release"/> shows them again.</summary>
    private void SetTextAlpha(float alpha) => SetAlpha(verbTexts, alpha);

    private static void SetAlpha(List<CanvasRenderer> list, float alpha)
    {
        foreach (var r in list)
        {
            if (r != null && r.GetAlpha() != alpha)
                r.SetAlpha(alpha);
        }
    }

    /// <summary>This frame's glyph per slot, from what the game shows. A hand prompt that just appeared ticks its
    /// hand; lunge and struggle also play the notice.</summary>
    private void PickVerbs(VRRig rig)
    {
        var c = Character.localCharacter;
        bool vr = VRSession.Active && c != null;
        bool inPlace = vr && textHidden;
        bool piton = vr && shown[2] && c.data.isClimbing && c.data.climbingSpikeCount > 0;
        bool lunge = vr && shown[4];
        var struggle = vr && shown[5] ? PromptVerb.Struggle : PromptVerb.None;

        bool acting = ActingLeft();
        if (piton && !pitonShown)
            Affordance.Contact(acting);
        if (lunge && !lungeShown)
            Appeared(acting, rig);
        if (struggle != PromptVerb.None && verbs[3] == PromptVerb.None)
        {
            Appeared(true, rig);
            Affordance.Contact(false);
        }
        pitonShown = piton;
        lungeShown = lunge;
        var primary = inPlace && shown[1] ? PrimaryVerb() : PromptVerb.None;
        // Names off: a plain pickup is shown by its glow alone (GripGlow), not a grab glyph.
        verbs[0] = PromptGlyph.InPlace(primary, !nameHidden) ? primary : PromptVerb.None;
        verbs[1] = inPlace && shown[2] ? PromptGlyph.Secondary(piton) : PromptVerb.None;
        verbs[2] = inPlace && lunge ? PromptVerb.Lunge : PromptVerb.None;
        verbs[3] = verbs[4] = struggle;
        verbs[HoldSlot] =
            inPlace && shown[1] && HoldTarget(c, out holdProgress)
                ? PromptVerb.Hold
                : PromptVerb.None;
    }

    /// <summary>
    /// The hovered target takes a held interaction (<c>IInteractibleConstant.IsConstantlyInteractable</c>,
    /// IInteractibleConstant.cs:5; Interaction.cs:120-123 starts the hold), and how far the running hold is
    /// (<c>constantInteractableProgress</c>, Interaction.cs:57; it fills only for the held target, :130-136).
    /// </summary>
    private bool HoldTarget(Character c, out float progress)
    {
        progress = 0f;
        var interaction = Interaction.instance;
        if (target is not IInteractibleConstant constant || interaction == null)
            return false;
        bool held = ReferenceEquals(interaction.currentHeldInteractible, constant);
        if (held)
        {
            float p = interaction.constantInteractableProgress;
            // A zero hold time divides to infinity or NaN.
            progress = float.IsNaN(p) ? 0f : Mathf.Clamp01(p);
        }
        return held || constant.IsConstantlyInteractable(c);
    }

    private static void Appeared(bool left, VRRig rig)
    {
        Affordance.Contact(left);
        var hand = rig.Hand(left);
        if (hand != null)
            Affordance.Play(CueSound.Notice, hand.position, 0.6f);
    }

    /// <summary>The game's branches in RefreshInteractablePrompt (GUIManager.cs:1054-1071) and
    /// CharacterInteractible.GetInteractionText (CharacterInteractible.cs:23-37), not its localized text.</summary>
    private PromptVerb PrimaryVerb()
    {
        if (target is CharacterInteractible ci)
        {
            var ch = ci.character;
            if (ch == null)
                return PromptVerb.None;
            bool carried = ch.data.carrier != null;
            return PromptGlyph.Primary(
                PromptTarget.Character,
                carried && ch.data.carrier == Character.localCharacter,
                !ch.isBot && ch.refs.customization.isCannibalizable,
                !ch.isBot && ch.data.fullyPassedOut && !ch.data.dead && !carried
            );
        }
        var kind = target switch
        {
            null => PromptTarget.None,
            Backpack => PromptTarget.Container,
            Item => PromptTarget.Item,
            Luggage => PromptTarget.Container,
            _ => PromptTarget.Other,
        };
        return PromptGlyph.Primary(kind, false, false, false);
    }

    private void PoseGlyphs(VRRig rig)
    {
        bool any = false;
        foreach (var v in verbs)
            any |= v != PromptVerb.None;
        if (!any || rig == null || rig.Head == null || !box.Holding)
        {
            HideGlyphs();
            return;
        }
        BuildGlyphs();
        var eye = rig.Head.position;
        for (int i = 0; i < Glyphs; i++)
        {
            var verb = verbs[i];
            bool wrist = i is 3 or 4;
            Transform hand = wrist ? rig.Hand(i == 3) : null;
            if (verb == PromptVerb.None || (wrist && hand == null))
            {
                Show(views[i], PromptVerb.None, default, default, 0f);
                continue;
            }
            if (i == HoldSlot)
            {
                // Beside the primary glyph, on the side away from the name: a ring that fills while you hold.
                var primary = box.Rect.TransformPoint(partCentre[GlyphPart[0]]);
                float hr =
                    SpatialMath.SpanMeters(Vector3.Distance(primary, eye), GlyphDegrees) * 0.5f;
                var facing = SpatialRuntime.FaceEye(primary, eye);
                ShowHold(views[i], primary + facing * Vector3.right * (hr * 2.6f), facing, hr);
                continue;
            }
            // In place of the text it replaces, or (struggle) just above each wrist.
            var at =
                hand != null
                    ? hand.position + Vector3.up * 0.09f
                    : box.Rect.TransformPoint(partCentre[GlyphPart[i]]);
            float degrees = hand != null ? WristGlyphDegrees : GlyphDegrees;
            float r = SpatialMath.SpanMeters(Vector3.Distance(at, eye), degrees) * 0.5f;
            Show(views[i], verb, at, SpatialRuntime.FaceEye(at, eye), r);
        }
    }

    private static void Show(
        GlyphView view,
        PromptVerb verb,
        Vector3 at,
        Quaternion facing,
        float r
    )
    {
        bool on = verb != PromptVerb.None;
        if (on && view.Verb != verb)
        {
            Stroke(view.First, view.FirstRim, PromptGlyph.Stroke(verb, 0));
            Stroke(view.Second, view.SecondRim, PromptGlyph.Stroke(verb, 1));
        }
        view.Verb = verb;
        if (on)
        {
            view.Root.SetPositionAndRotation(at, facing);
            view.Root.localScale = new Vector3(r, r, r);
            view.First.widthMultiplier = view.Second.widthMultiplier = r * 0.16f;
            view.FirstRim.widthMultiplier = view.SecondRim.widthMultiplier = r * 0.3f;
        }
        bool first = on && view.First.positionCount > 1;
        bool second = on && view.Second.positionCount > 1;
        Enable(view.First, first);
        Enable(view.FirstRim, first);
        Enable(view.Second, second);
        Enable(view.SecondRim, second);
    }

    /// <summary>The hold glyph: a thin white track ring on its dark rim, and a bold white fill over it from the top,
    /// clockwise, as far as the hold has gone. The fill is re-stroked only when it moves.</summary>
    private void ShowHold(GlyphView view, Vector3 at, Quaternion facing, float r)
    {
        if (view.Verb != PromptVerb.Hold)
        {
            var ring = PromptGlyph.Stroke(PromptVerb.Hold, 0);
            Stroke(view.Second, view.FirstRim, ring);
            view.Verb = PromptVerb.Hold;
            holdDrawn = -1f;
        }
        if (holdProgress != holdDrawn)
        {
            int n = PromptGlyph.HoldFill(holdProgress, holdPoints);
            view.First.positionCount = n;
            for (int i = 0; i < n; i++)
                view.First.SetPosition(i, new Vector3(holdPoints[i].X, holdPoints[i].Y, 0f));
            holdDrawn = holdProgress;
        }
        view.Root.SetPositionAndRotation(at, facing);
        view.Root.localScale = new Vector3(r, r, r);
        view.First.widthMultiplier = r * 0.16f;
        view.Second.widthMultiplier = r * 0.05f;
        view.FirstRim.widthMultiplier = r * 0.3f;
        Enable(view.FirstRim, true);
        Enable(view.Second, true);
        Enable(view.First, view.First.positionCount > 1);
        Enable(view.SecondRim, false);
    }

    private static void Enable(LineRenderer line, bool on)
    {
        if (line.enabled != on)
            line.enabled = on;
    }

    private static void Stroke(
        LineRenderer line,
        LineRenderer rim,
        System.Numerics.Vector2[] points
    )
    {
        line.positionCount = rim.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            var p = new Vector3(points[i].X, points[i].Y, 0f);
            line.SetPosition(i, p);
            rim.SetPosition(i, p);
        }
    }

    private void HideGlyphs()
    {
        foreach (var v in views)
        {
            if (v != null && v.Verb != PromptVerb.None)
                Show(v, PromptVerb.None, default, default, 0f);
        }
    }

    /// <summary>Lets go of the prompt state too, so the next hand prompt ticks again.</summary>
    private void ClearGlyphs()
    {
        lungeShown = pitonShown = false;
        System.Array.Clear(verbs, 0, verbs.Length);
        HideGlyphs();
    }

    private void BuildGlyphs()
    {
        if (glyphRoot != null)
            return;
        glyphRoot = new GameObject("PeakXR_PromptGlyphs");
        Object.DontDestroyOnLoad(glyphRoot);
        for (int i = 0; i < Glyphs; i++)
        {
            var root = new GameObject("Glyph" + i).transform;
            root.SetParent(glyphRoot.transform, false);
            // Rims earlier in the queue, so the white stroke draws over its dark edge.
            views[i] = new GlyphView
            {
                Root = root,
                FirstRim = Line(root, GlyphRim, 3299),
                SecondRim = Line(root, GlyphRim, 3299),
                First = Line(root, GlyphColor, 3300),
                Second = Line(root, GlyphColor, 3300),
            };
        }
    }

    private static LineRenderer Line(Transform parent, Color color, int queue)
    {
        var go = new GameObject("Stroke");
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 0;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = UIMaterials.Create(color, true, queue);
        line.sortingOrder = DrawOrder.HandUi;
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }

    private void Grab(GUIManager gui, RectTransform hudRoot)
    {
        verbTexts.Clear();
        nameTexts.Clear();
        // Stack order, top to bottom.
        parts[0] = gui.interactName;
        parts[1] = gui.interactPromptPrimary;
        parts[2] = gui.interactPromptSecondary;
        parts[3] = gui.interactPromptHold;
        parts[4] = gui.interactPromptLunge;
        parts[5] = gui.strugglePrompt != null ? gui.strugglePrompt.gameObject : null;
        for (int i = 0; i < Parts; i++)
        {
            shown[i] = parts[i] != null && parts[i].activeSelf;
            if (parts[i] != null)
            {
                box.Move(hudRoot, parts[i].transform, Vector2.zero);
                SpatialBox.Enlarge(parts[i].transform, MinFontPixels);
                // Name and action prompts: the dark smudge behind the text is a monitor backing (and key art); the
                // hold progress shows as ui.hand's ring under the label.
                if (i <= 3)
                    box.HideBackings(parts[i].transform);
                // Every part but the name carries a verb (D8).
                parts[i].GetComponentsInChildren(true, texts);
                foreach (var t in texts)
                    (i > 0 ? verbTexts : nameTexts).Add(t.canvasRenderer);
                texts.Clear();
            }
        }
        if (
            !warned
            && gui.interactPromptText != null
            && gui.interactPromptPrimary != null
            && !gui.interactPromptText.transform.IsChildOf(gui.interactPromptPrimary.transform)
        )
        {
            warned = true;
            Plugin.Log.LogWarning(
                "[ui.spatial] interactPromptText is not under interactPromptPrimary; the action text stays on the HUD"
            );
        }
        height = box.Stack(Spacing);
    }

    /// <summary>Rough size of a target or held item from its renderers, measured once per change: half its largest
    /// dimension. The box's half diagonal overstated a round item by up to 1.7 times, which lifted the label of an
    /// Apple 0.8 m away 32 degrees, out of view.</summary>
    private static float Radius(Component comp)
    {
        comp.GetComponentsInChildren(false, renderers);
        bool any = false;
        var bounds = default(Bounds);
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || !r.enabled)
                continue;
            if (any)
                bounds.Encapsulate(r.bounds);
            else
                bounds = r.bounds;
            any = true;
        }
        renderers.Clear();
        var e = bounds.extents;
        return any ? Mathf.Max(e.x, Mathf.Max(e.y, e.z)) : 0.1f;
    }
}
