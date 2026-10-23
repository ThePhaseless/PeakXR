using PeakXR.Avatar;
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
    private const float LabelPixels = 30f;
    private const float HoverLabelAfter = 0.4f;
    private const float HoverFade = 0.15f;
    private const float RayWidth = 0.0025f;

    /// <summary>A rim stroke's width over its coloured stroke's: a thin dark edge each side.</summary>
    private const float RimWidth = 1.9f;

    private static readonly Color RayColor = new(1f, 1f, 1f, 0.35f);

    /// <summary>The thin dark edge on both sides of a "can't" stroke, so the red reads on red rock, snow and sky.</summary>
    private static readonly Color RimColor = new(0.06f, 0.03f, 0.03f, 0.6f);
    private static readonly RaycastHit[] RayHits = new RaycastHit[16];

    /// <summary>Labels that ride on a hand stay at least this far from the eye, a comfortable focus distance.</summary>
    private const float MinLabelDistance = 0.45f;

    /// <summary>One cue in the world: refused spot (ring plus glyph), valid spot (double ring), the line between with a
    /// chevron travelling toward the valid spot.</summary>
    private sealed class Cue
    {
        public LineRenderer Deny,
            Hint,
            Link;

        /// <summary>Children of <see cref="Deny"/> (they share its pose): slash, bar or pin, and the wait arc.</summary>
        public LineRenderer Mark,
            Arc;

        /// <summary>Dark rims: a wider dark stroke drawn under the ring, the glyph and the wait arc.</summary>
        public LineRenderer RingRim,
            MarkRim,
            ArcRim;

        /// <summary>Child of <see cref="Hint"/>: the inner ring that makes it a double ring.</summary>
        public LineRenderer Inner;
        public LineRenderer Chevron;

        /// <summary>Rings at the hint's extra spots (<see cref="DenyHint.Marks"/>); the refusal cue only.</summary>
        public LineRenderer[] Marks;
        public CueGlyph Glyph;
        public Vector3 Position;
        public DenyReason Reason;
        public DenyHint Spot;
        public bool Notice;

        /// <summary>Angular diameter of its rings (<see cref="CueLayout"/>).</summary>
        public float Degrees;

        /// <summary>The refused spot was the hand itself (a held item): the cue rides above the hand, clear of it.</summary>
        public bool AtHand;

        /// <summary>Label text in place of the reason's own (an item name), or null.</summary>
        public string Text;

        // Hover bookkeeping.
        public int Frame = -10;
        public float Since,
            SeenAt = -100f;

        // Refusal bookkeeping (one refusal cue per hand). The refused attempt is still held (the trigger, else the
        // grip, that was down when it was refused): the cue stays.
        public bool Left;
        public float At = -100f;
        public bool Holding;
        public bool ByTrigger;
        public float HeldUntil = -100f;
        public float HoldCap;

        /// <summary>This frame's opacity of a refusal cue.</summary>
        public float Alpha;

        /// <summary>A refusal's in-view copy at the edge of the view cone while its real spot is out of sight
        /// (<see cref="CueEdge"/>), and the pointer from it toward the real spot.</summary>
        public Cue Edge;
        public LineRenderer Pointer,
            PointerRim;
        public bool EdgeOn;

        /// <summary>Its ring has been drawn at a readable opacity since it started.</summary>
        public bool Drawn = true;
    }

    private static AffordanceRuntime instance;
    private const int ArcPoints = 9;
    private const float WouldWorkDelay = 0.15f;
    private const float HoverVolume = 0.6f;

    /// <summary>Sounds for hovers, which start and stop at will: one per reason and hand per 0.9 s.</summary>
    private readonly DenyGate hoverSounds = new();

    // The would-work note follows the refused one by a moment, at the valid spot.
    private float pendingAt = -1f;
    private Vector3 pendingPos;
    private float pendingVolume;

    private readonly HapticPatternPlayer leftBuzz = new();
    private readonly HapticPatternPlayer rightBuzz = new();

    /// <summary>One refusal cue per hand (0 left, 1 right): a refusal on one hand never replaces the other's, so a
    /// cue for both hands at once (a slide, the feet) or a free hand's refusal right after keeps both drawn
    /// (VR run 33).</summary>
    private readonly Cue[] denials =
    {
        new() { Left = true },
        new(),
    };

    /// <summary>The refusal or notice shown last: the label follows it.</summary>
    private Cue denied;

    /// <summary>The refusal whose label shows because its reason fired for the first time
    /// (<c>UI.FirstTimeRefusalLabels</c>); null when none.</summary>
    private Cue firstLabel;

    /// <summary>The label shows this frame: <c>UI.ActionHintText</c>, or a first-time refusal still on show.</summary>
    private bool textOn;

    /// <summary>The refusal whose red preview <see cref="DenyTint"/> shows (one at a time).</summary>
    private Cue tintOwner;
    private readonly Cue[] hovers = { new(), new() };
    private LineRenderer ray;
    private GameObject labelRoot;
    private TextMeshProUGUI label;
    private GUIManager fontFrom;
    private Material fontClone;
    private bool built;

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
        instance.denied = instance.denials[1];
    }

    /// <summary>The red preview the caller just set (a placement ghost, a tint target) belongs to the refusal shown
    /// last, and ends with it.</summary>
    public static void ClaimTint()
    {
        if (instance != null)
            instance.tintOwner = instance.denied;
    }

    /// <param name="notice">Information, not a refusal: no red ring, a single soft tick, the label in the hint colour.</param>
    public static void Show(
        bool left,
        Vector3 pos,
        DenyReason reason,
        DenyHint hint,
        bool notice = false,
        string text = null,
        bool atHand = false
    )
    {
        var self = instance;
        if (self == null)
            return;
        var now = Time.unscaledTime;
        if (notice)
            CueLog.Capture.Pulse(
                left,
                0.25f,
                0.04f,
                Haptics.Pulse(left, 0.25f, 0.04f),
                now,
                Haptics.Muted
            );
        else
        {
            var pattern = DenyHaptics.For(reason);
            (left ? self.leftBuzz : self.rightBuzz).Start(pattern, now);
            // The whole triple holds the hand, gaps included: nothing lower cuts or slips into it.
            var last = pattern[pattern.Length - 1];
            Haptics.Hold(left, HapticPriority.Alert, last.At + last.Seconds);
        }
        var d = self.denials[left ? 0 : 1];
        // The same refusal reported again while its cue is up (even every frame) keeps one live cue: it never
        // restarts from 0, which would blink or never get drawn (DenyFade.Refresh).
        float start = DenyFade.Refresh(
            d.At,
            d.HeldUntil,
            now,
            d.Reason == reason && d.Notice == notice
        );
        // Diagnostic (rare, so the string is fine): this hand's previous cue was replaced before its ring was ever
        // drawn, which a self-test reports as "no ring drawn".
        if (!d.Drawn && d.Reason != DenyReason.None && now - d.At < 1f)
            Plugin.Log.LogInfo(
                $"[ui.affordance] {(left ? "left" : "right")} {d.Reason} replaced by {reason} "
                    + $"{now - d.At:0.000} s after it, before its ring was drawn"
            );
        // The same refusal still on show keeps its first-time label; a new firing of a seen reason shows none.
        bool continues = start == d.At && d.Reason == reason && self.firstLabel == d;
        if (start != d.At)
            d.Drawn = false;
        ulong seen = AffordanceFeature.SeenRefusals.Value;
        bool first =
            FirstRefusalLabel.Show(
                false,
                AffordanceFeature.FirstTimeRefusalLabels.Value,
                ref seen,
                reason
            ) || continues;
        // Written once per reason ever, so the config save never lands on a hot path twice.
        if (seen != AffordanceFeature.SeenRefusals.Value)
            AffordanceFeature.SeenRefusals.Value = seen;
        self.firstLabel = first ? d : null;
        d.Notice = notice;
        d.Glyph = DenyGlyph.For(reason);
        d.Text = text;
        self.PlayCue(notice, pos, hint, 1f);
        // The source decides, not distance: a refusal of the hand's own action (Affordance.DenyAtHand) rides the hand
        // and is about the item in it; a world target 30 cm away stays a world cue.
        d.AtHand = atHand;
        var held =
            Character.localCharacter != null ? Character.localCharacter.data.currentItem : null;
        bool carrying = held != null && LocalHold.ItemInLeftHand == left;
        // The red preview: the held item. A placement ghost or tint target comes from the caller (ClaimTint). This
        // hand's new refusal ends its own old preview, never the other hand's.
        if (!notice && atHand && carrying)
        {
            DenyTint.HeldItem(held);
            self.tintOwner = d;
        }
        else if (self.tintOwner == d || self.tintOwner == null || !DenyTint.Active)
        {
            DenyTint.Clear();
            self.tintOwner = null;
        }
        d.Position = pos;
        d.Reason = reason;
        d.Spot = hint;
        d.At = start;
        ref var input = ref VRInput.Hand(left);
        d.ByTrigger = input.Trigger > 0.5f;
        d.Holding = !notice && (d.ByTrigger || input.Grip > 0.5f);
        d.HoldCap = DenyFade.HoldCap(d.ByTrigger, carrying);
        d.HeldUntil = now;
        self.denied = d;
        self.SetLabel(-1, reason, hint.Meters, text);
    }

    /// <summary>The live refusal ring of the <paramref name="left"/> hand, for the self-test: false unless it is drawn
    /// and clearly visible. Per hand, so a notice on the other hand meanwhile (a free-hand hint) does not hide it.</summary>
    public static bool DeniedRing(bool left, out Vector3 center, out float radius)
    {
        center = default;
        radius = 0f;
        var self = instance;
        if (self == null || !self.built)
            return false;
        var d = self.denials[left ? 0 : 1];
        if (!d.Deny.enabled)
            return false;
        var t = d.Deny.transform;
        center = t.position;
        radius = t.localScale.x;
        return d.Deny.sharedMaterial.color.a > 0.3f;
    }

    /// <summary>A teal "would work" part (valid-spot ring, link, chevron) is drawn now, for the self-test.</summary>
    public static bool HintDrawn
    {
        get
        {
            var self = instance;
            if (self == null || !self.built)
                return false;
            foreach (var cue in self.All)
            {
                if (cue.Hint.enabled || cue.Link.enabled || cue.Chevron.enabled)
                    return true;
            }
            return MarksDrawn > 0;
        }
    }

    /// <summary>How many extra hint rings (<see cref="DenyHint.Marks"/>) are drawn now, for the self-test.</summary>
    public static int MarksDrawn
    {
        get
        {
            var self = instance;
            if (self == null || !self.built)
                return 0;
            int n = 0;
            foreach (var d in self.denials)
            {
                foreach (var ring in d.Marks)
                {
                    if (ring.enabled)
                        n++;
                }
            }
            return n;
        }
    }

    /// <summary>The reason label is up now (<c>UI.ActionHintText</c>, or a reason's first firing), for the self-test.</summary>
    public static bool LabelShown =>
        instance != null && instance.labelRoot != null && instance.labelRoot.activeSelf;

    /// <summary>The label's text now, for the self-test.</summary>
    public static string LabelText =>
        instance != null && instance.label != null ? instance.label.text : null;

    /// <summary>The reach ray is drawn now, for the self-test.</summary>
    public static bool RayShown => instance != null && instance.built && instance.ray.enabled;

    private Cue[] All => all ??= new[] { denials[0], denials[1], hovers[0], hovers[1] };
    private Cue[] all;

    /// <param name="silent">No sound on a new hover (a danger warning plays its own).</param>
    public static void Hover(
        bool left,
        Vector3 pos,
        DenyReason reason,
        DenyHint hint,
        bool notice = false,
        string text = null,
        bool silent = false
    )
    {
        var self = instance;
        if (self == null)
            return;
        var h = self.hovers[left ? 0 : 1];
        var now = Time.unscaledTime;
        if (h.Reason != reason || h.Frame < Time.frameCount - 1)
        {
            h.Since = now;
            // A new hover sounds once, softer than a refusal; a hover that flickers on and off stays quiet.
            if (!silent && self.hoverSounds.Allow(reason, left, now))
                self.PlayCue(notice, pos, hint, HoverVolume);
        }
        h.Glyph = DenyGlyph.For(reason);
        h.Position = pos;
        h.Reason = reason;
        h.Spot = hint;
        h.Notice = notice;
        h.Text = text;
        h.Frame = Time.frameCount;
        h.SeenAt = now;
    }

    private void OnEnable()
    {
        Application.onBeforeRender += Pose;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneUnloaded;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= Pose;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneUnloaded;
    }

    private static void SceneUnloaded(UnityEngine.SceneManagement.Scene _) => DenyTint.Dispose();

    private void Update()
    {
        var now = Time.unscaledTime;
        Buzz(leftBuzz, true, now);
        Buzz(rightBuzz, false, now);
        if (pendingAt >= 0f && now >= pendingAt)
        {
            pendingAt = -1f;
            CueAudio.Play(CueSound.WouldWork, pendingPos, pendingVolume);
        }
    }

    /// <summary>Refused (or notice) at the spot; if there is a valid spot, the would-work note there a moment later.</summary>
    private void PlayCue(bool notice, Vector3 pos, DenyHint hint, float volume)
    {
        // The valid spot and its rising note are hints (UI.ActionHints); without them it all sounds at the spot.
        bool spot = Affordance.Hints && hint.HasSpot && (hint.SpotNow - pos).sqrMagnitude > 1e-4f;
        if (notice)
        {
            CueAudio.Play(CueSound.Notice, spot ? hint.SpotNow : pos, volume);
            return;
        }
        CueAudio.Play(CueSound.Refused, pos, volume);
        if (!spot)
            return;
        pendingAt = Time.unscaledTime + WouldWorkDelay;
        pendingPos = hint.SpotNow;
        pendingVolume = volume;
    }

    private static void Buzz(HapticPatternPlayer player, bool left, float now)
    {
        while (player.TryNext(now, out var pulse))
            CueLog.Capture.Pulse(
                left,
                pulse.Amplitude,
                pulse.Seconds,
                Haptics.Pulse(left, pulse.Amplitude, pulse.Seconds, HapticPriority.Alert),
                now,
                Haptics.Muted
            );
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
        // Text is optional (UI.ActionHintText, or a reason's first firing): the label is built only when asked for.
        bool hintText = AffordanceFeature.ActionHintText.Value;
        textOn = hintText || (firstLabel != null && labelOwner < 0 && denied == firstLabel);
        if (
            (hintText || AffordanceFeature.FirstTimeRefusalLabels.Value || textOn)
            && labelRoot == null
        )
            BuildLabel();
        else if (!textOn && labelRoot != null && labelRoot.activeSelf)
            labelRoot.SetActive(false);
        if (labelRoot != null)
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
            || !Affordance.Hints
            || LaserPointer.Showing
            || c == null
            || interaction == null
            || cam == null
            || !c.data.fullyConscious
            || (gui != null && (gui.windowBlockingInput || gui.wheelActive))
            || UsingItemIn(c, hand == 0)
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
        // A hand at the face (a bugle at the mouth) would draw the ray across the view.
        ray.enabled =
            length > 0.02f
            && (
                rig.Head == null
                || !LaserLook.CrossesFace(
                    origin.N(),
                    (origin + dir * length).N(),
                    rig.Head.position.N()
                )
            );
    }

    /// <summary>The hand holds the current item and is using it (a toot, a cast, eating): its index is off the
    /// trigger by grip, not to point, so no reach ray.</summary>
    private static bool UsingItemIn(Character c, bool left)
    {
        var item = c.data.currentItem;
        return item != null
            && LocalHold.ItemInLeftHand == left
            && (item.isUsingPrimary || item.isUsingSecondary || item.progress > 0f);
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

        for (int i = 0; i < 2; i++)
        {
            var d = denials[i];
            if (d.Holding)
            {
                ref var input = ref VRInput.Hand(d.Left);
                if (
                    (d.ByTrigger ? input.Trigger > 0.5f : input.Grip > 0.5f)
                    && now - d.At < d.HoldCap
                )
                    d.HeldUntil = now;
                else
                    d.Holding = false;
            }
            d.Alpha = DenyFade.Alpha(now - d.At, d.HeldUntil - d.At);
            var hand = rig.Hand(d.Left);
            if (d.AtHand && hand != null)
                d.Position = CueLayout.HandAnchor(hand.position.N(), eye.N()).U();
            PoseCue(d, eye, d.Alpha);
            CaptureRing(d, eye, now);
            PoseEdge(d, rig, eye, now);
        }
        var owner = tintOwner;
        if (DenyTint.Active && owner != null)
        {
            // Shown this frame (age 0, alpha 0): applied from the next pose on, never cleared here.
            if (DenyFade.Over(now - owner.At, owner.HeldUntil - owner.At))
            {
                DenyTint.Clear();
                tintOwner = null;
            }
            else if (owner.Alpha > 0.001f)
                DenyTint.Apply(owner.Alpha);
        }
        float denyAlpha = denied.Alpha;

        for (int i = 0; i < 2; i++)
        {
            var h = hovers[i];
            float alpha = HoverAlpha(h, now);
            // The live refusal of this hand already says it, bigger: no second ring inside it.
            if (denials[i].Alpha > 0f && h.Reason == denials[i].Reason)
                alpha = 0f;
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

    /// <summary>The refusal's ring (a notice's teal one when it has no red one) as the eye sees it, while clearly
    /// visible: for <see cref="CueLog"/>.</summary>
    /// <summary>
    /// The refusal's in-view copy (<see cref="CueEdge"/>): while its real spot lies outside the comfortable cone
    /// (the head camera's field when known, at most 45 degrees half-angle), the same ring and glyph at the cone's
    /// edge toward the spot, 1 m from the eye, with a small pointer toward it. The real ring stays drawn.
    /// </summary>
    private static void PoseEdge(Cue d, VRRig rig, Vector3 eye, float now)
    {
        var e = d.Edge;
        var forward = rig.Head.forward;
        float half = CueEdge.ConeHalf(rig.HeadCamera != null ? rig.HeadCamera.fieldOfView : 0f);
        float off = Vector3.Angle(forward, d.Position - eye);
        d.EdgeOn = d.Alpha > 0.001f && CueEdge.Needed(off, half, d.EdgeOn);
        float alpha = d.EdgeOn ? d.Alpha : 0f;
        e.Glyph = d.Glyph;
        e.Notice = d.Notice;
        e.Reason = d.Reason;
        e.Degrees = d.Degrees;
        e.Spot = default;
        e.Position = CueEdge
            .Spot(
                eye.N(),
                forward.N(),
                d.Position.N(),
                CueEdge.EdgeDegrees(half, d.Degrees),
                CueEdge.Depth,
                out var outward
            )
            .U();
        PoseCue(e, eye, alpha);
        bool shown = alpha > 0.001f && e.Deny.enabled;
        if (shown)
        {
            var t = e.Deny.transform;
            var c = t.position;
            float r = t.localScale.x;
            var o = outward.U();
            var across = Vector3.Cross(o, eye - c);
            across = across.sqrMagnitude > 1e-8f ? across.normalized : Vector3.up;
            // A chevron just outside the ring, pointing toward the real spot.
            var tip = c + o * (r * 1.9f);
            var root = c + o * (r * 1.4f);
            d.Pointer.SetPosition(0, root + across * (r * 0.45f));
            d.Pointer.SetPosition(1, tip);
            d.Pointer.SetPosition(2, root - across * (r * 0.45f));
            d.Pointer.widthMultiplier = r * 0.18f;
            for (int i = 0; i < 3; i++)
                d.PointerRim.SetPosition(i, d.Pointer.GetPosition(i));
            d.PointerRim.widthMultiplier = d.Pointer.widthMultiplier * RimWidth;
            if (alpha >= 0.3f)
                CueLog.Capture.Edge(
                    d.Left,
                    c.N(),
                    CueLayout.AngularDegrees(r, Vector3.Distance(c, eye)),
                    now
                );
        }
        var color = d.Notice ? Affordance.HintColor : Affordance.DenyColor;
        SetLine(d.Pointer, shown, alpha, color);
        SetLine(d.PointerRim, shown, alpha, RimColor);
    }

    private void CaptureRing(Cue d, Vector3 eye, float now)
    {
        var ring =
            d.Deny.enabled ? d.Deny
            : d.Hint.enabled ? d.Hint
            : null;
        if (ring == null || d.Alpha < 0.3f)
            return;
        d.Drawn = true;
        var t = ring.transform;
        var center = t.position;
        CueLog.Capture.Ring(
            d.Left,
            d.Reason,
            d.Glyph,
            center.N(),
            CueLayout.AngularDegrees(t.localScale.x, Vector3.Distance(center, eye)),
            DenyTint.Active && tintOwner == d,
            now
        );
    }

    private static float HoverAlpha(Cue h, float now)
    {
        float age = now - h.SeenAt;
        return h.Reason != DenyReason.None && age < HoverFade ? 1f - age / HoverFade : 0f;
    }

    private void PoseLabel(VRRig rig, Vector3 eye, float now, float denyAlpha)
    {
        if (labelRoot == null || !textOn)
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
            var hand = rig.Hand(denied.Left);
            // A refusal of the hand's own action: ride above the hand. A world target or wall: sit above the spot.
            atHand = hand != null && denied.AtHand;
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
                    : atHand ? CueLayout.AboveHand + 0.07f
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
    }

    private static void PoseCue(Cue cue, Vector3 eye, float alpha)
    {
        float now = Time.unscaledTime;
        var spot = cue.Spot.SpotNow;
        // The teal valid spot is a hint (UI.ActionHints); without it a notice marks its own spot with the pin.
        bool hint =
            Affordance.Hints && cue.Spot.HasSpot && (spot - cue.Position).sqrMagnitude > 1e-4f;
        bool shown = alpha > 0.001f;
        // A notice has no refused spot: with no separate hint, its own spot is the teal one (with a pin).
        bool refusedRing = shown && !(cue.Notice && hint);
        var color = cue.Notice ? Affordance.HintColor : Affordance.DenyColor;
        PoseRing(cue.Deny, cue.Position, eye, refusedRing ? alpha : 0f, color, cue.Degrees);
        cue.RingRim.widthMultiplier = cue.Deny.widthMultiplier * RimWidth;
        SetLine(cue.RingRim, refusedRing && cue.Deny.enabled, alpha, RimColor);
        PoseGlyph(cue, refusedRing, alpha, color, now);
        PoseRing(
            cue.Hint,
            spot,
            eye,
            hint ? alpha : 0f,
            Affordance.HintColor,
            cue.Degrees * CueMotion.Breathe(now)
        );
        cue.Inner.widthMultiplier = cue.Hint.widthMultiplier;
        SetLine(cue.Inner, hint && shown, alpha, Affordance.HintColor);
        PoseMarks(cue, eye, hint ? alpha : 0f, now);
        var link = cue.Link;
        if (!hint || !shown)
        {
            link.enabled = false;
            cue.Chevron.enabled = false;
            return;
        }
        link.SetPosition(0, cue.Position);
        link.SetPosition(1, spot);
        link.widthMultiplier =
            SpatialMath.SpanMeters(Vector3.Distance(spot, eye), cue.Degrees) * 0.06f;
        // A cue at the hand (ClimbStow's hand-to-slot line) with the hand at the face: no bar across the view.
        SetLine(
            link,
            !LaserLook.CrossesFace(cue.Position.N(), spot.N(), eye.N()),
            alpha * 0.6f,
            Affordance.HintColor
        );
        PoseChevron(cue, spot, eye, alpha, now);
    }

    /// <summary>The hint's extra spots (<see cref="DenyHint.Marks"/>, each missing friend): a teal ring at each,
    /// breathing a little out of step with the main one so they read as a set. Refusals only; hovers have none.</summary>
    private static void PoseMarks(Cue cue, Vector3 eye, float alpha, float now)
    {
        if (cue.Marks == null)
            return;
        var marks = cue.Spot.Marks;
        for (int i = 0; i < cue.Marks.Length; i++)
        {
            bool on = i < marks.Count;
            PoseRing(
                cue.Marks[i],
                on ? marks[i].U() : Vector3.zero,
                eye,
                on ? alpha : 0f,
                Affordance.HintColor,
                cue.Degrees * CueMotion.Breathe(now + 0.25f * (i + 1))
            );
        }
    }

    /// <summary>The refused ring's inner shape (<see cref="GlyphShape"/>): a slash (no), a bar (full), a sweeping arc
    /// (wait), a pin above it (a notice: here it is), or a picture of the cause (a bolt, an arrow, a padlock...). Two
    /// strokes at most: <see cref="Cue.Mark"/> draws the first, <see cref="Cue.Arc"/> the second or the wait arc.
    /// Shape carries the meaning, so colour is never needed.</summary>
    private static void PoseGlyph(Cue cue, bool shown, float alpha, Color color, float now)
    {
        var glyph = cue.Glyph;
        bool wait = glyph == CueGlyph.Wait;
        var first = GlyphShape.Stroke(glyph, 0);
        var second = GlyphShape.Stroke(glyph, 1);
        bool mark = shown && first.Length > 1;
        bool arc = shown && (wait || second.Length > 1);
        if (mark)
        {
            SetStroke(cue.Mark, first);
            cue.Mark.widthMultiplier = cue.Deny.widthMultiplier;
        }
        if (arc)
        {
            if (wait)
            {
                // A quarter turn of arc sweeping round inside the ring.
                if (cue.Arc.positionCount != ArcPoints)
                    cue.Arc.positionCount = ArcPoints;
                float start = CueMotion.Sweep(now) * Mathf.Deg2Rad;
                for (int i = 0; i < ArcPoints; i++)
                {
                    float a = start + i * (Mathf.PI * 0.5f) / (ArcPoints - 1);
                    cue.Arc.SetPosition(
                        i,
                        new Vector3(Mathf.Cos(a) * 0.6f, Mathf.Sin(a) * 0.6f, 0f)
                    );
                }
            }
            else
                SetStroke(cue.Arc, second);
            cue.Arc.widthMultiplier = cue.Deny.widthMultiplier;
        }
        SetLine(cue.Mark, mark, alpha, color);
        SetLine(cue.Arc, arc, alpha, color);
        if (mark)
            Rim(cue.Mark, cue.MarkRim);
        if (arc)
            Rim(cue.Arc, cue.ArcRim);
        SetLine(cue.MarkRim, mark, alpha, RimColor);
        SetLine(cue.ArcRim, arc, alpha, RimColor);
    }

    private static void SetStroke(LineRenderer line, System.Numerics.Vector2[] points)
    {
        if (line.positionCount != points.Length)
            line.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++)
            line.SetPosition(i, new Vector3(points[i].X, points[i].Y, 0f));
    }

    /// <summary>The dark rim under a stroke: the same points, an open stroke a touch longer at both ends so they
    /// get an edge too.</summary>
    private static void Rim(LineRenderer stroke, LineRenderer rim)
    {
        int n = stroke.positionCount;
        if (rim.positionCount != n)
            rim.positionCount = n;
        for (int i = 0; i < n; i++)
            rim.SetPosition(i, stroke.GetPosition(i));
        var a = stroke.GetPosition(0);
        var b = stroke.GetPosition(n - 1);
        if (n >= 2 && (a - b).sqrMagnitude > 1e-4f)
        {
            rim.SetPosition(0, a + (a - stroke.GetPosition(1)).normalized * 0.06f);
            rim.SetPosition(n - 1, b + (b - stroke.GetPosition(n - 2)).normalized * 0.06f);
        }
        rim.widthMultiplier = stroke.widthMultiplier * RimWidth;
    }

    /// <summary>A small chevron moving from the refused spot to the valid one: which way, at a glance.</summary>
    private static void PoseChevron(Cue cue, Vector3 spot, Vector3 eye, float alpha, float now)
    {
        var from = cue.Position;
        var along = spot - from;
        float length = along.magnitude;
        if (length < 0.05f)
        {
            cue.Chevron.enabled = false;
            return;
        }
        var dir = along / length;
        var at = from + along * CueMotion.Travel(now);
        float size = SpatialMath.SpanMeters(Vector3.Distance(at, eye), cue.Degrees) * 0.4f;
        var side = Vector3.Cross(dir, eye - at);
        side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.up;
        cue.Chevron.SetPosition(0, at - dir * size + side * size * 0.8f);
        cue.Chevron.SetPosition(1, at);
        cue.Chevron.SetPosition(2, at - dir * size - side * size * 0.8f);
        cue.Chevron.widthMultiplier = size * 0.18f;
        SetLine(cue.Chevron, true, alpha, Affordance.HintColor);
    }

    private static void SetLine(LineRenderer line, bool on, float alpha, Color color)
    {
        if (on)
        {
            color.a *= alpha;
            line.sharedMaterial.color = color;
        }
        if (line.enabled != on)
            line.enabled = on;
    }

    private static void PoseRing(
        LineRenderer ring,
        Vector3 pos,
        Vector3 eye,
        float alpha,
        Color color,
        float degrees
    )
    {
        if (alpha <= 0.001f)
        {
            if (ring.enabled)
                ring.enabled = false;
            return;
        }
        float d = Vector3.Distance(pos, eye);
        float r = SpatialMath.SpanMeters(d, degrees) * 0.5f;
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
    }

    private void HideAll()
    {
        if (!built)
            return;
        // VR stopped or hints off: the tint goes and the ghost is destroyed, not just hidden.
        DenyTint.Dispose();
        Hide(denials[0]);
        Hide(denials[1]);
        Hide(hovers[0]);
        Hide(hovers[1]);
        ray.enabled = false;
        if (labelRoot != null && labelRoot.activeSelf)
            labelRoot.SetActive(false);
    }

    private static void Hide(Cue cue)
    {
        cue.Deny.enabled =
            cue.Hint.enabled =
            cue.Link.enabled =
            cue.Mark.enabled =
            cue.Arc.enabled =
            cue.RingRim.enabled =
            cue.MarkRim.enabled =
            cue.ArcRim.enabled =
            cue.Inner.enabled =
            cue.Chevron.enabled =
                false;
        if (cue.Marks != null)
        {
            foreach (var ring in cue.Marks)
                ring.enabled = false;
        }
        if (cue.Edge != null)
        {
            Hide(cue.Edge);
            cue.Pointer.enabled = cue.PointerRim.enabled = false;
            cue.EdgeOn = false;
        }
    }

    private void Build()
    {
        built = true;
        foreach (var cue in All)
        {
            // A refused attempt draws over the world: the hand that tried, the item it holds or a rock would hide it.
            // Hovers stay depth-tested, so a "can't" ring behind a rock stays behind it.
            bool top = cue == denials[0] || cue == denials[1];
            BuildCue(cue, top, marks: top);
            if (top)
            {
                cue.Edge = new Cue();
                BuildCue(cue.Edge, true, marks: false);
                cue.PointerRim = Line("EdgePointerRim", 3, false, RimColor, true, null, 3190);
                cue.PointerRim.useWorldSpace = true;
                cue.Pointer = Line("EdgePointer", 3, false, Affordance.DenyColor, true);
                cue.Pointer.useWorldSpace = true;
            }
        }
        ray = Line("PointRay", 2, false, RayColor, drawOnTop: false);
        ray.useWorldSpace = true;
        ray.widthMultiplier = RayWidth;
    }

    /// <summary>One cue's lines: the refused ring with its glyph and rims, the would-work double ring, link and
    /// chevron, and with <paramref name="marks"/> the extra hint rings.</summary>
    private void BuildCue(Cue cue, bool top, bool marks)
    {
        cue.Degrees = top ? CueLayout.DenyDegrees : CueLayout.HoverDegrees;
        cue.Deny = Line("DenyRing", RingPoints, true, Affordance.DenyColor, top);
        // Rims render first (lower queue), so the coloured stroke lies on a dark edge, not on a plate.
        cue.RingRim = Line("RingRim", RingPoints, true, RimColor, top, cue.Deny.transform, 3190);
        cue.Mark = Line("Glyph", 2, false, Affordance.DenyColor, top, cue.Deny.transform);
        cue.MarkRim = Line("GlyphRim", 2, false, RimColor, top, cue.Deny.transform, 3190);
        cue.ArcRim = Line("WaitArcRim", ArcPoints, false, RimColor, top, cue.Deny.transform, 3190);
        cue.Arc = Line("WaitArc", ArcPoints, false, Affordance.DenyColor, top, cue.Deny.transform);
        cue.Hint = Line("HintRing", RingPoints, true, Affordance.HintColor, top);
        cue.Inner = Line(
            "HintInner",
            RingPoints,
            true,
            Affordance.HintColor,
            top,
            cue.Hint.transform
        );
        cue.Inner.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
        if (marks)
        {
            cue.Marks = new LineRenderer[SpotSet.Capacity];
            for (int i = 0; i < cue.Marks.Length; i++)
                cue.Marks[i] = Line("HintMark", RingPoints, true, Affordance.HintColor, top);
        }
        cue.Link = Line("HintLink", 2, false, Affordance.HintColor, top);
        cue.Link.useWorldSpace = true;
        cue.Chevron = Line("Chevron", 3, false, Affordance.HintColor, top);
        cue.Chevron.useWorldSpace = true;
    }

    private LineRenderer Line(
        string name,
        int points,
        bool ring,
        Color color,
        bool drawOnTop,
        Transform parent = null,
        int renderQueue = 3200
    )
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : transform, false);
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
        line.sharedMaterial = UIMaterials.Create(color, drawOnTop, renderQueue);
        // A refusal drawn on top sorts like other hand UI: after smoke, under the comfort overlays.
        if (drawOnTop)
            line.sortingOrder = DrawOrder.HandUi;
        line.startColor = line.endColor = Color.white;
        line.enabled = false;
        return line;
    }

    private void BuildLabel()
    {
        labelRoot = new GameObject("PeakXR_DenyLabel", typeof(RectTransform), typeof(Canvas));
        labelRoot.transform.SetParent(transform, false);
        var labelCanvas = labelRoot.GetComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.WorldSpace;
        labelCanvas.sortingOrder = DrawOrder.HandUi;
        ((RectTransform)labelRoot.transform).sizeDelta = new Vector2(480f, 100f);

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
            // No plate behind the reason: a dark rim keeps the red and teal text readable on snow and sky.
            TextHalo.Halo(fontClone, light: false, tight: true);
            label.fontSharedMaterial = fontClone;
        }
        // Re-render the current text in the new font.
        int owner = labelOwner;
        labelOwner = -2;
        SetLabel(owner, labelReason, labelDecimeters * 0.1f, labelText);
    }
}
