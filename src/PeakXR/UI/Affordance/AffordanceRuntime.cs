using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.UI.World;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Affordances;

/// <summary>
/// Draws what <see cref="Affordance"/> reports: per-hand denied haptic patterns; per cue a red ring at the refused
/// spot and, when the caller knows what would work, a teal ring at that spot joined to the red one by a thin teal
/// line; one two-line reason label (reason, then next step) sized like the ui.spatial labels in the game's prompt
/// font; and, while a hand points to interact, a faint ray as long as the real interaction reach. The reach matches
/// interact.physical's clamp (InteractPatches.Raycasts: <c>Interaction.distance</c> minus the hand's offset from
/// the camera), and an interactible that the ray finds past it gets a hover cue. Everything fades; nothing flashes.
/// </summary>
[DefaultExecutionOrder(31300)]
internal sealed class AffordanceRuntime : MonoBehaviour
{
    private const int RingPoints = 24;
    private const float RingDegrees = 1.8f;
    private const float LabelPixels = 30f;
    private const float HoverLabelAfter = 0.4f;
    private const float HoverFade = 0.15f;
    private const float RayWidth = 0.0025f;

    private static readonly Color RayColor = new(1f, 1f, 1f, 0.35f);
    private static readonly RaycastHit[] RayHits = new RaycastHit[16];

    /// <summary>Labels that ride on a hand stay at least this far from the eye, a comfortable focus distance.</summary>
    private const float MinLabelDistance = 0.45f;

    /// <summary>One cue in the world: refused spot, valid spot, the line between.</summary>
    private sealed class Cue
    {
        public LineRenderer Deny,
            Hint,
            Link;
        public Vector3 Position;
        public DenyReason Reason;
        public DenyHint Spot;
        public bool Notice;

        /// <summary>Label text in place of the reason's own (an item name), or null.</summary>
        public string Text;

        // Hover bookkeeping.
        public int Frame = -10;
        public float Since,
            SeenAt = -100f;
    }

    private static AffordanceRuntime instance;

    private readonly HapticPatternPlayer leftBuzz = new();
    private readonly HapticPatternPlayer rightBuzz = new();
    private readonly Cue denied = new();
    private readonly Cue[] hovers = { new(), new() };
    private LineRenderer ray;
    private GameObject labelRoot;
    private TextMeshProUGUI label;
    private Image plate;
    private GUIManager fontFrom;
    private Material fontClone;
    private bool built;

    private bool denyLeft;
    private float denyAt = -100f;

    // What the label shows now: the denial (-1) or a hover (hand index), its reason and distance step.
    private int labelOwner = -1;
    private DenyReason labelReason;
    private int labelDecimeters;
    private string labelText;

    public static void Create()
    {
        var go = new GameObject("PeakXR_Affordance");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AffordanceRuntime>();
    }

    /// <param name="notice">Information, not a refusal: no red ring, a single soft tick, the label in the hint colour.</param>
    public static void Show(
        bool left,
        Vector3 pos,
        DenyReason reason,
        DenyHint hint,
        bool notice = false,
        string text = null
    )
    {
        var self = instance;
        if (self == null)
            return;
        var now = Time.unscaledTime;
        if (notice)
            Haptics.Pulse(left, 0.25f, 0.04f);
        else
            (left ? self.leftBuzz : self.rightBuzz).Start(DenyHaptics.For(reason), now);
        self.denied.Notice = notice;
        self.denied.Text = text;
        self.denied.Position = pos;
        self.denied.Reason = reason;
        self.denied.Spot = hint;
        self.denyLeft = left;
        self.denyAt = now;
        self.SetLabel(-1, reason, hint.Meters, text);
    }

    public static void Hover(
        bool left,
        Vector3 pos,
        DenyReason reason,
        DenyHint hint,
        bool notice = false,
        string text = null
    )
    {
        var self = instance;
        if (self == null)
            return;
        var h = self.hovers[left ? 0 : 1];
        var now = Time.unscaledTime;
        if (h.Reason != reason || h.Frame < Time.frameCount - 1)
            h.Since = now;
        h.Position = pos;
        h.Reason = reason;
        h.Spot = hint;
        h.Notice = notice;
        h.Text = text;
        h.Frame = Time.frameCount;
        h.SeenAt = now;
    }

    private void OnEnable() => Application.onBeforeRender += Pose;

    private void OnDisable() => Application.onBeforeRender -= Pose;

    private void Update()
    {
        var now = Time.unscaledTime;
        Buzz(leftBuzz, true, now);
        Buzz(rightBuzz, false, now);
    }

    private static void Buzz(HapticPatternPlayer player, bool left, float now)
    {
        while (player.TryNext(now, out var pulse))
            Haptics.Pulse(left, pulse.Amplitude, pulse.Seconds);
    }

    private void LateUpdate()
    {
        var rig = VRRig.Instance;
        if (!Affordance.Enabled || rig == null || rig.Head == null)
        {
            HideAll();
            return;
        }
        if (!built)
            Build();
        EnsureFont();
        UpdateRay(rig);
        Pose();
    }

    /// <summary>
    /// The pointing hand's interact reach. The game's own pass (Interaction.DoInteractableRaycasts, Interaction.cs:196,
    /// run from the hand by interact.physical) has already picked a target, its sphere fallback (:239-262) included; only
    /// when it found nothing does an interactible straight ahead past the reach get the "Too far" cue. The ray skips
    /// what vanilla skips (:224): your own ragdoll, the held item and arrows stuck in you.
    /// </summary>
    private void UpdateRay(VRRig rig)
    {
        int hand = PhysicalInteraction.AimHand;
        var c = Character.localCharacter;
        var interaction = Interaction.instance;
        var cam = MainCamera.instance;
        var gui = GUIManager.instance;
        var aim = hand >= 0 ? rig.Aim(hand == 0) : null;
        if (
            aim == null
            || !AffordanceFeature.ReachRay.Value
            || LaserPointer.Showing
            || c == null
            || interaction == null
            || cam == null
            || !c.data.fullyConscious
            || (gui != null && (gui.windowBlockingInput || gui.wheelActive))
        )
        {
            ray.enabled = false;
            return;
        }
        float full = interaction.distance;
        float reach = Mathf.Max(0f, full - Vector3.Distance(cam.transform.position, aim.position));
        var origin = aim.position;
        var dir = aim.forward;
        float length = reach;
        int n = Physics.RaycastNonAlloc(
            origin,
            dir,
            RayHits,
            full,
            HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
            QueryTriggerInteraction.Collide
        );
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if ((best < 0 || RayHits[i].distance < RayHits[best].distance) && Counts(c, RayHits[i]))
                best = i;
        }
        if (best >= 0)
        {
            var hit = RayHits[best];
            if (hit.distance <= reach)
                length = hit.distance;
            else if (interaction.currentHovered == null)
            {
                var target = hit.collider.GetComponentInParent<IInteractible>();
                if (target != null && target.IsInteractible(c))
                    Affordance.Hover(
                        hand == 0,
                        hit.point,
                        DenyReason.OutOfReach,
                        DenyHint.Range(origin, hit.point, reach)
                    );
            }
        }
        for (int i = 0; i < n; i++)
            RayHits[i] = default;
        ray.SetPosition(0, origin);
        ray.SetPosition(1, origin + dir * length);
        ray.enabled = length > 0.02f;
    }

    /// <summary>The hits vanilla's interact ray considers (Interaction.cs:224).</summary>
    private static bool Counts(Character c, in RaycastHit hit)
    {
        var col = hit.collider;
        // Your own body: vanilla checks the ragdoll's collider list (internal); its parent Character is the same test.
        if (col == null || col.GetComponentInParent<Character>() == c)
            return false;
        if (
            Item.TryGetItemFromCollider(col, out var item)
            && item != null
            && item == c.data.currentItem
        )
            return false;
        var parent = col.transform.parent;
        return parent == null || !parent.TryGetComponent<global::Peak.ArrowOnMe>(out _);
    }

    /// <summary>Poses cues and the label; runs late and again right before rendering for the hand-following label.</summary>
    private void Pose()
    {
        var rig = VRRig.Instance;
        if (!built || rig == null || rig.Head == null || !Affordance.Enabled)
            return;
        var eye = rig.Head.position;
        var now = Time.unscaledTime;

        float denyAlpha = DenyFade.Alpha(now - denyAt);
        PoseCue(denied, eye, denyAlpha);

        for (int i = 0; i < 2; i++)
        {
            var h = hovers[i];
            float alpha = HoverAlpha(h, now);
            PoseCue(h, eye, alpha);
            // A steady hover earns the label, but only once the current owner's label is gone (or it is the owner):
            // one label at a time, no swapping between hands.
            bool ownerLive =
                labelOwner >= 0
                    ? labelOwner != i && HoverAlpha(hovers[labelOwner], now) > 0f
                    : denyAlpha > 0f;
            if (alpha > 0f && now - h.Since >= HoverLabelAfter && !ownerLive)
                SetLabel(i, h.Reason, h.Spot.Meters, h.Text);
        }

        PoseLabel(rig, eye, now, denyAlpha);
    }

    private static float HoverAlpha(Cue h, float now)
    {
        float age = now - h.SeenAt;
        return h.Reason != DenyReason.None && age < HoverFade ? 1f - age / HoverFade : 0f;
    }

    private void PoseLabel(VRRig rig, Vector3 eye, float now, float denyAlpha)
    {
        if (labelRoot == null)
            return;
        float alpha;
        Vector3 anchor;
        bool atHand = false;
        if (labelOwner >= 0)
        {
            var h = hovers[labelOwner];
            alpha = h.Reason == labelReason ? 0.85f * HoverAlpha(h, now) : 0f;
            anchor = h.Position;
        }
        else
        {
            alpha = denyAlpha;
            var hand = rig.Hand(denyLeft);
            // Near the hand: ride above it. Farther (a target, a wall): sit above the spot.
            atHand = hand != null && (hand.position - denied.Position).sqrMagnitude < 0.35f * 0.35f;
            anchor = atHand ? hand.position : denied.Position;
        }
        if (alpha <= 0.001f)
        {
            if (labelRoot.activeSelf)
                labelRoot.SetActive(false);
            return;
        }
        if (!labelRoot.activeSelf)
            labelRoot.SetActive(true);
        // An overhang refusal is on a ceiling: its label hangs below it, not inside the rock.
        var reason = labelOwner >= 0 ? hovers[labelOwner].Reason : denied.Reason;
        var p =
            anchor
            + Vector3.up
                * (
                    reason == DenyReason.Overhang ? -0.12f
                    : atHand ? 0.13f
                    : 0.1f
                );
        // Toward the eye, off the surface it is about (more for far spots), and never closer than a comfortable focus.
        var toEye = eye - p;
        float d = toEye.magnitude;
        if (d > 1e-3f)
        {
            p += toEye / d * Mathf.Min(d * 0.5f, atHand ? 0.04f : Mathf.Max(0.06f, d * 0.05f));
            var fromEye = p - eye;
            if (fromEye.magnitude < MinLabelDistance)
                p = eye + fromEye.normalized * MinLabelDistance;
        }
        float mpp = SpatialMath.MetersPerPixel(
            Vector3.Distance(p, eye),
            SpatialRuntime.DegreesPerPixel
        );
        labelRoot.transform.SetPositionAndRotation(
            p,
            SpatialRuntime.FaceEyeYaw(p, eye, rig.Head.forward)
        );
        labelRoot.transform.localScale = new Vector3(mpp, mpp, mpp);
        bool notice = labelOwner < 0 ? denied.Notice : hovers[labelOwner].Notice;
        var tc = notice ? Affordance.HintColor : Affordance.DenyColor;
        tc.a = alpha;
        label.color = tc;
        plate.color = new Color(0.06f, 0.05f, 0.05f, 0.7f * alpha);
    }

    private static void PoseCue(Cue cue, Vector3 eye, float alpha)
    {
        var spot = cue.Spot.SpotNow;
        bool hint = cue.Spot.HasSpot && (spot - cue.Position).sqrMagnitude > 1e-4f;
        // A notice has no refused spot: with no separate hint, its own spot is the teal one.
        PoseRing(
            cue.Deny,
            cue.Position,
            eye,
            cue.Notice && hint ? 0f : alpha,
            cue.Notice ? Affordance.HintColor : Affordance.DenyColor
        );
        PoseRing(cue.Hint, spot, eye, hint ? alpha : 0f, Affordance.HintColor);
        var link = cue.Link;
        if (!hint || alpha <= 0.001f)
        {
            if (link.enabled)
                link.enabled = false;
            return;
        }
        link.SetPosition(0, cue.Position);
        link.SetPosition(1, spot);
        link.widthMultiplier =
            SpatialMath.SpanMeters(Vector3.Distance(spot, eye), RingDegrees) * 0.08f;
        var c = Affordance.HintColor;
        c.a *= alpha * 0.7f;
        link.sharedMaterial.color = c;
        if (!link.enabled)
            link.enabled = true;
    }

    private static void PoseRing(
        LineRenderer ring,
        Vector3 pos,
        Vector3 eye,
        float alpha,
        Color color
    )
    {
        if (alpha <= 0.001f)
        {
            if (ring.enabled)
                ring.enabled = false;
            return;
        }
        float d = Vector3.Distance(pos, eye);
        float r = SpatialMath.SpanMeters(d, RingDegrees) * 0.5f;
        // Depth-tested, so a ring behind a rock stays hidden (stereo-correct); lifted toward the eye off the surface it
        // marks so the surface does not cut it.
        if (d > 1e-3f)
            pos += (eye - pos) / d * Mathf.Min(Mathf.Max(0.03f, r), d * 0.5f);
        var t = ring.transform;
        t.SetPositionAndRotation(pos, SpatialRuntime.FaceEye(pos, eye));
        t.localScale = new Vector3(r, r, r);
        ring.widthMultiplier = r * 0.18f;
        color.a *= alpha;
        ring.sharedMaterial.color = color;
        if (!ring.enabled)
            ring.enabled = true;
    }

    /// <summary>Sets the label text when its owner, reason or distance (in 10 cm steps) changes: a TMP text change
    /// re-meshes and the string allocates, so never per frame.</summary>
    private void SetLabel(int owner, DenyReason reason, float meters, string text)
    {
        int dm = Mathf.RoundToInt(Mathf.Max(0f, meters) * 10f);
        if (
            owner == labelOwner
            && reason == labelReason
            && dm == labelDecimeters
            && ReferenceEquals(text, labelText)
            && owner >= 0
        )
            return;
        labelOwner = owner;
        labelReason = reason;
        labelDecimeters = dm;
        labelText = text;
        if (label == null)
            return;
        label.text = text ?? DenyText.Compose(reason, dm * 0.1f);
        label.ForceMeshUpdate();
        var size = label.GetRenderedValues(false);
        ((RectTransform)plate.transform).sizeDelta = new Vector2(size.x + 28f, size.y + 14f);
    }

    private void HideAll()
    {
        if (!built)
            return;
        Hide(denied);
        Hide(hovers[0]);
        Hide(hovers[1]);
        ray.enabled = false;
        if (labelRoot != null && labelRoot.activeSelf)
            labelRoot.SetActive(false);
    }

    private static void Hide(Cue cue) =>
        cue.Deny.enabled = cue.Hint.enabled = cue.Link.enabled = false;

    private void Build()
    {
        built = true;
        foreach (var cue in new[] { denied, hovers[0], hovers[1] })
        {
            cue.Deny = Line("DenyRing", RingPoints, true, Affordance.DenyColor, drawOnTop: false);
            cue.Hint = Line("HintRing", RingPoints, true, Affordance.HintColor, drawOnTop: false);
            cue.Link = Line("HintLink", 2, false, Affordance.HintColor, drawOnTop: false);
            cue.Link.useWorldSpace = true;
        }
        ray = Line("PointRay", 2, false, RayColor, drawOnTop: false);
        ray.useWorldSpace = true;
        ray.widthMultiplier = RayWidth;
        BuildLabel();
    }

    private LineRenderer Line(string name, int points, bool ring, Color color, bool drawOnTop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = ring;
        line.positionCount = points;
        if (ring)
        {
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
        }
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = UIMaterials.Create(color, drawOnTop, renderQueue: 3200);
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }

    private void BuildLabel()
    {
        labelRoot = new GameObject("PeakXR_DenyLabel", typeof(RectTransform), typeof(Canvas));
        labelRoot.transform.SetParent(transform, false);
        labelRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)labelRoot.transform).sizeDelta = new Vector2(480f, 100f);

        var plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
        plateGo.transform.SetParent(labelRoot.transform, false);
        plate = plateGo.GetComponent<Image>();
        plate.raycastTarget = false;
        var plateMat = new Material(plate.material) { name = "PeakXR_DenyPlate" };
        plateMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        plate.material = plateMat;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(labelRoot.transform, false);
        ((RectTransform)textGo.transform).sizeDelta = new Vector2(480f, 100f);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = LabelPixels;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        labelRoot.SetActive(false);
    }

    /// <summary>The game's prompt font, drawn over the world so a label never sinks into the wall it is about.</summary>
    private void EnsureFont()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui == fontFrom || label == null)
            return;
        var source =
            gui.interactName != null
                ? gui.interactName.GetComponentInChildren<TMP_Text>(true)
                : null;
        // No prompt font yet: try again next frame.
        if (source == null || source.font == null)
            return;
        fontFrom = gui;
        label.font = source.font;
        var mat = label.fontSharedMaterial;
        if (fontClone != null)
            Destroy(fontClone);
        fontClone = null;
        if (mat != null)
        {
            fontClone = new Material(mat) { name = "PeakXR_DenyLabel (on top)" };
            fontClone.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            label.fontSharedMaterial = fontClone;
        }
        // Re-render the current text in the new font.
        int owner = labelOwner;
        labelOwner = -2;
        SetLabel(owner, labelReason, labelDecimeters * 0.1f, labelText);
    }
}
