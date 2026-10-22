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
    private readonly Cue denied = new();
    private readonly Cue[] hovers = { new(), new() };
    private LineRenderer ray;
    private GameObject labelRoot;
    private TextMeshProUGUI label;
    private GUIManager fontFrom;
    private Material fontClone;
    private bool built;

    private bool denyLeft;
    private float denyAt = -100f;

    // The refused attempt is still held (the trigger, else the grip, that was down when it was refused): the cue stays.
    private bool denyHolding;
    private bool denyByTrigger;
    private float denyHeldUntil = -100f;
    private float denyHoldCap;

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
        string text = null,
        bool atHand = false
    )
    {
        var self = instance;
        if (self == null)
            return;
        var now = Time.unscaledTime;
        if (notice)
            CueLog.Capture.Pulse(left, 0.25f, 0.04f, Haptics.Pulse(left, 0.25f, 0.04f), now);
        else
            (left ? self.leftBuzz : self.rightBuzz).Start(DenyHaptics.For(reason), now);
        self.denied.Notice = notice;
        self.denied.Glyph = DenyGlyph.For(reason);
        self.denied.Text = text;
        self.PlayCue(notice, pos, hint, 1f);
        // The same refusal repeated while its cue is up (a check every frame, rate-limited) keeps it lit: no ramp
        // from zero, which would blink.
        bool live =
            self.denied.Reason == reason
            && self.denyLeft == left
            && DenyFade.Alpha(now - self.denyAt, self.denyHeldUntil - self.denyAt) > 0f;
        // The source decides, not distance: a refusal of the hand's own action (Affordance.DenyAtHand) rides the hand
        // and is about the item in it; a world target 30 cm away stays a world cue.
        self.denied.AtHand = atHand;
        var held =
            Character.localCharacter != null ? Character.localCharacter.data.currentItem : null;
        bool carrying = held != null && LocalHold.ItemInLeftHand == left;
        // The red preview: the held item. A placement ghost comes from the caller (Affordance.Preview).
        if (!notice && atHand && carrying)
            DenyTint.HeldItem(held);
        else
            DenyTint.Clear();
        self.denied.Position = pos;
        self.denied.Reason = reason;
        self.denied.Spot = hint;
        self.denyLeft = left;
        self.denyAt = live ? now - DenyFade.In : now;
        ref var input = ref VRInput.Hand(left);
        self.denyByTrigger = input.Trigger > 0.5f;
        self.denyHolding = !notice && (self.denyByTrigger || input.Grip > 0.5f);
        self.denyHoldCap = DenyFade.HoldCap(self.denyByTrigger, carrying);
        self.denyHeldUntil = now;
        self.SetLabel(-1, reason, hint.Meters, text);
    }

    /// <summary>The live refusal's ring, for the self-test: false unless it is drawn and clearly visible.</summary>
    public static bool DeniedRing(out Vector3 center, out float radius)
    {
        center = default;
        radius = 0f;
        var self = instance;
        if (self == null || !self.built || !self.denied.Deny.enabled)
            return false;
        var t = self.denied.Deny.transform;
        center = t.position;
        radius = t.localScale.x;
        return self.denied.Deny.sharedMaterial.color.a > 0.3f;
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
        {
            h.Since = now;
            // A new hover sounds once, softer than a refusal; a hover that flickers on and off stays quiet.
            if (self.hoverSounds.Allow(reason, left, now))
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
        bool spot = hint.HasSpot && (hint.SpotNow - pos).sqrMagnitude > 1e-4f;
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
                Haptics.Pulse(left, pulse.Amplitude, pulse.Seconds),
                now
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
        // Text is optional (UI.ActionHintText): the label is built only when asked for.
        bool text = AffordanceFeature.ActionHintText.Value;
        if (text && labelRoot == null)
            BuildLabel();
        else if (!text && labelRoot != null && labelRoot.activeSelf)
            labelRoot.SetActive(false);
        if (text)
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

        if (denyHolding)
        {
            ref var input = ref VRInput.Hand(denyLeft);
            if (
                (denyByTrigger ? input.Trigger > 0.5f : input.Grip > 0.5f)
                && now - denyAt < denyHoldCap
            )
                denyHeldUntil = now;
            else
                denyHolding = false;
        }
        float denyAlpha = DenyFade.Alpha(now - denyAt, denyHeldUntil - denyAt);
        var denyHand = rig.Hand(denyLeft);
        if (denied.AtHand && denyHand != null)
            denied.Position = CueLayout.HandAnchor(denyHand.position.N(), eye.N()).U();
        PoseCue(denied, eye, denyAlpha);
        CaptureRing(eye, denyAlpha, now);
        if (DenyTint.Active)
        {
            // Shown this frame (age 0, alpha 0): applied from the next pose on, never cleared here.
            if (DenyFade.Over(now - denyAt, denyHeldUntil - denyAt))
                DenyTint.Clear();
            else if (denyAlpha > 0.001f)
                DenyTint.Apply(denyAlpha);
        }

        for (int i = 0; i < 2; i++)
        {
            var h = hovers[i];
            float alpha = HoverAlpha(h, now);
            // The live refusal of this hand already says it, bigger: no second ring inside it.
            if (denyAlpha > 0f && denyLeft == (i == 0) && h.Reason == denied.Reason)
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
    private void CaptureRing(Vector3 eye, float alpha, float now)
    {
        var ring =
            denied.Deny.enabled ? denied.Deny
            : denied.Hint.enabled ? denied.Hint
            : null;
        if (ring == null || alpha < 0.3f)
            return;
        var t = ring.transform;
        var center = t.position;
        CueLog.Capture.Ring(
            denyLeft,
            denied.Reason,
            denied.Glyph,
            center.N(),
            CueLayout.AngularDegrees(t.localScale.x, Vector3.Distance(center, eye)),
            DenyTint.Active,
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
        if (labelRoot == null || !AffordanceFeature.ActionHintText.Value)
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
        bool hint = cue.Spot.HasSpot && (spot - cue.Position).sqrMagnitude > 1e-4f;
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

    /// <summary>The refused ring's inner shape: a slash (no), a bar (full), a sweeping arc (wait) or a pin above it
    /// (a notice: here it is). Shape carries the meaning, so colour is never needed.</summary>
    private static void PoseGlyph(Cue cue, bool shown, float alpha, Color color, float now)
    {
        var glyph = cue.Glyph;
        bool mark = shown && glyph != CueGlyph.Wait;
        bool arc = shown && glyph == CueGlyph.Wait;
        if (mark)
        {
            switch (glyph)
            {
                case CueGlyph.Bar:
                    cue.Mark.SetPosition(0, new Vector3(-0.7f, 0f, 0f));
                    cue.Mark.SetPosition(1, new Vector3(0.7f, 0f, 0f));
                    break;
                case CueGlyph.Pin:
                    cue.Mark.SetPosition(0, new Vector3(0f, 1f, 0f));
                    cue.Mark.SetPosition(1, new Vector3(0f, 2.4f, 0f));
                    break;
                default:
                    cue.Mark.SetPosition(0, new Vector3(-0.7f, -0.7f, 0f));
                    cue.Mark.SetPosition(1, new Vector3(0.7f, 0.7f, 0f));
                    break;
            }
            cue.Mark.widthMultiplier = cue.Deny.widthMultiplier;
        }
        if (arc)
        {
            // A quarter turn of arc sweeping round inside the ring.
            float start = CueMotion.Sweep(now) * Mathf.Deg2Rad;
            for (int i = 0; i < ArcPoints; i++)
            {
                float a = start + i * (Mathf.PI * 0.5f) / (ArcPoints - 1);
                cue.Arc.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.6f, Mathf.Sin(a) * 0.6f, 0f));
            }
            cue.Arc.widthMultiplier = cue.Deny.widthMultiplier;
        }
        SetLine(cue.Mark, mark, alpha, color);
        SetLine(cue.Arc, arc, alpha, color);
        if (mark)
        {
            // A touch longer than the stroke, so its ends get an edge too.
            cue.MarkRim.SetPosition(0, cue.Mark.GetPosition(0) * 1.08f);
            cue.MarkRim.SetPosition(1, cue.Mark.GetPosition(1) * 1.08f);
            cue.MarkRim.widthMultiplier = cue.Mark.widthMultiplier * RimWidth;
        }
        if (arc)
        {
            for (int i = 0; i < ArcPoints; i++)
                cue.ArcRim.SetPosition(i, cue.Arc.GetPosition(i));
            cue.ArcRim.widthMultiplier = cue.Arc.widthMultiplier * RimWidth;
        }
        SetLine(cue.MarkRim, mark, alpha, RimColor);
        SetLine(cue.ArcRim, arc, alpha, RimColor);
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
        Hide(denied);
        Hide(hovers[0]);
        Hide(hovers[1]);
        ray.enabled = false;
        if (labelRoot != null && labelRoot.activeSelf)
            labelRoot.SetActive(false);
    }

    private static void Hide(Cue cue) =>
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

    private void Build()
    {
        built = true;
        foreach (var cue in new[] { denied, hovers[0], hovers[1] })
        {
            // A refused attempt draws over the world: the hand that tried, the item it holds or a rock would hide it.
            // Hovers stay depth-tested, so a "can't" ring behind a rock stays behind it.
            bool top = cue == denied;
            cue.Degrees = top ? CueLayout.DenyDegrees : CueLayout.HoverDegrees;
            cue.Deny = Line("DenyRing", RingPoints, true, Affordance.DenyColor, top);
            // Rims render first (lower queue), so the coloured stroke lies on a dark edge, not on a plate.
            cue.RingRim = Line(
                "RingRim",
                RingPoints,
                true,
                RimColor,
                top,
                cue.Deny.transform,
                3190
            );
            cue.Mark = Line("Glyph", 2, false, Affordance.DenyColor, top, cue.Deny.transform);
            cue.MarkRim = Line("GlyphRim", 2, false, RimColor, top, cue.Deny.transform, 3190);
            cue.ArcRim = Line(
                "WaitArcRim",
                ArcPoints,
                false,
                RimColor,
                top,
                cue.Deny.transform,
                3190
            );
            cue.Arc = Line(
                "WaitArc",
                ArcPoints,
                false,
                Affordance.DenyColor,
                top,
                cue.Deny.transform
            );
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
            cue.Link = Line("HintLink", 2, false, Affordance.HintColor, top);
            cue.Link.useWorldSpace = true;
            cue.Chevron = Line("Chevron", 3, false, Affordance.HintColor, top);
            cue.Chevron.useWorldSpace = true;
        }
        ray = Line("PointRay", 2, false, RayColor, drawOnTop: false);
        ray.useWorldSpace = true;
        ray.widthMultiplier = RayWidth;
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
