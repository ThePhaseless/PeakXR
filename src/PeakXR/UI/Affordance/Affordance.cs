using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// What WOULD work, carried with a refusal: a spot where the action succeeds (the far end of the real reach, the
/// item's second grip, a friend's mouth) and how far the player still has to close. Drawn as a soft teal ring with a
/// thin line from the refused spot to it, so the next try goes there; <see cref="Meters"/> also goes into the label.
/// </summary>
internal readonly struct DenyHint
{
    public readonly bool HasSpot;
    public readonly Vector3 Spot;
    public readonly float Meters;

    /// <summary>When set, the spot moves with this transform (a grip on a held item, the wrist status bar).</summary>
    public readonly Transform Follow;

    /// <summary>More spots that would work, each a teal ring (every missing friend in sight). No line or chevron:
    /// those lead to the main spot only.</summary>
    public readonly SpotSet Marks;

    private DenyHint(
        bool hasSpot,
        Vector3 spot,
        float meters,
        Transform follow = null,
        SpotSet marks = default
    )
    {
        HasSpot = hasSpot;
        Spot = spot;
        Meters = meters;
        Follow = follow;
        Marks = marks;
    }

    /// <summary>Where the spot is this frame.</summary>
    public Vector3 SpotNow => Follow != null ? Follow.position : Spot;

    /// <summary>This hint with one more marked spot (up to <see cref="SpotSet.Capacity"/>; more are dropped).
    /// Allocation-free. On a hint with no main spot, the first mark becomes the main spot.</summary>
    public DenyHint And(Vector3 spot) =>
        HasSpot ? new(true, Spot, Meters, Follow, Marks.And(spot.N())) : At(spot);

    /// <summary>The action works at <paramref name="spot"/>.</summary>
    public static DenyHint At(Vector3 spot) => new(true, spot, 0f);

    /// <summary>The action works at <paramref name="target"/>, which moves; null gives no spot.</summary>
    public static DenyHint On(Transform target) =>
        target != null ? new(true, target.position, 0f, target) : default;

    /// <summary>The stamina bar on the wrist (ui.world <c>HudMode.Wrist</c>), for "rest first" refusals.</summary>
    public static DenyHint Wrist()
    {
        var rig = VRRig.Instance;
        var ui = PeakXR.UI.World.UIWorldFeature.Settings;
        if (rig == null || ui == null || ui.Hud.Value != PeakXR.UI.World.HudMode.Wrist)
            return default;
        return On(rig.Hand(ui.WristLeft));
    }

    /// <summary>A range ends at <paramref name="limit"/>, <paramref name="meters"/> short of the refused spot.</summary>
    public static DenyHint Closer(Vector3 limit, float meters) => new(true, limit, meters);

    /// <summary>The reach from <paramref name="origin"/> ends at <paramref name="max"/> on the way to
    /// <paramref name="target"/>.</summary>
    public static DenyHint Range(Vector3 origin, Vector3 target, float max)
    {
        var limit = DenyRange.Boundary(origin.N(), target.N(), max).U();
        return new(true, limit, Vector3.Distance(origin, target) - max);
    }
}

/// <summary>
/// The one place a VR refusal or limit is reported, so every one gets the same multi-sense cue: the red preview of
/// the refused thing, a ring with the reason's glyph, the refused note and the denied haptic (text only with
/// <c>UI.ActionHintText</c>). <see cref="Deny"/> is for an attempt that just failed (a grip, a trigger press, a
/// release); <see cref="Hover"/> is the pre-emptive "can't" state, called every frame while it holds.
/// <see cref="Progress"/>, <see cref="Contact"/>, <see cref="Cancel"/>, <see cref="EndStop"/> and <see cref="Danger"/>
/// are the non-refusal feels. All are cheap no-ops in flat play or if ui.affordance did not install, so callers never
/// need to check first. <c>UI.ActionHints</c> off hides only the teal "would work" parts; refusals still cue.
/// <c>Interaction.Haptics</c> off mutes the haptics only. Allocation-free.
/// </summary>
internal static class Affordance
{
    /// <summary>Colour of a "can't" cue (aim line end, ring, label text). Muted, never a full-saturation flash.</summary>
    public static readonly Color DenyColor = new(1f, 0.42f, 0.36f, 0.85f);

    private const float EndStopRepeat = 0.3f;
    private const float DangerSoundRepeat = 0.8f;

    private static readonly DenyGate Gate = new();
    private static readonly ProgressRamp[] Ramps = { new(), new() };
    private static readonly DangerPulse[] Dangers = { new(), new() };
    private static readonly float[] EndStopAt = { float.NegativeInfinity, float.NegativeInfinity };
    private static float dangerSoundAt = float.NegativeInfinity;
    private static float progressAt = float.NegativeInfinity;

    /// <summary>Accepted denials since start, for the self-test.</summary>
    public static int Count { get; private set; }

    public static DenyReason LastReason { get; private set; }
    public static bool LastLeft { get; private set; }
    public static Vector3 LastPosition { get; private set; }

    /// <summary>ui.affordance installed (its runtime exists), whatever the setting.</summary>
    public static bool Installed => AffordanceFeature.ActionHints != null;

    /// <summary>Refusal cues run: in VR with ui.affordance installed. Not tied to <c>UI.ActionHints</c>: a failure
    /// is never silent.</summary>
    public static bool Enabled => VRSession.Active && Installed;

    /// <summary>The teal "would work" parts show (valid-spot ring, chevron, rising note, reach ray):
    /// <see cref="Enabled"/> and <c>UI.ActionHints</c> on.</summary>
    public static bool Hints => Enabled && AffordanceFeature.ActionHints.Value;

    /// <summary>
    /// An attempt by the <paramref name="left"/> hand at <paramref name="worldPos"/> did nothing because of
    /// <paramref name="reason"/>. Rate-limited per reason and hand. Returns true when it was shown.
    /// </summary>
    public static bool Deny(bool left, Vector3 worldPos, DenyReason reason) =>
        Deny(left, worldPos, reason, default);

    /// <summary>As above, with what would work instead (<see cref="DenyHint"/>).</summary>
    public static bool Deny(bool left, Vector3 worldPos, DenyReason reason, DenyHint hint) =>
        DenyCore(left, worldPos, reason, hint, false, null);

    /// <summary>As above, with the refused thing to turn red while the cue shows (<see cref="DenyTint.Target"/>):
    /// an <see cref="Item"/>, a UI <c>Graphic</c> or any component whose renderers to tint. A part of a character is
    /// never tinted (D10: ring only). Null gives no tint.</summary>
    public static bool Deny(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint,
        Component tint
    ) => DenyCore(left, worldPos, reason, hint, false, tint);

    /// <summary>A stamina refusal at the hand: NoStamina, the stamina bar turned red, the wrist raised (the status
    /// alert's pulse) and the bar as the would-work spot.</summary>
    public static bool DenyStamina(bool left) =>
        RaiseWrist(DenyAtHand(left, DenyReason.NoStamina, DenyHint.Wrist(), StaminaBar()));

    /// <summary>As <see cref="DenyStamina(bool)"/>, for an attempt at a spot in the world (a wall grab).</summary>
    public static bool DenyStamina(bool left, Vector3 worldPos) =>
        RaiseWrist(
            DenyCore(left, worldPos, DenyReason.NoStamina, DenyHint.Wrist(), false, StaminaBar())
        );

    private static bool DenyCore(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint,
        bool atHand,
        Component tint
    )
    {
        if (!Enabled)
            return false;
        if (!Gate.Allow(reason, left, Time.unscaledTime))
        {
            // A quick retry is still felt; the ring and sound wait out the longer window.
            if (Gate.AllowHaptic(reason, left, Time.unscaledTime))
                AffordanceRuntime.BuzzOnly(left, reason);
            return false;
        }
        Count++;
        CueLog.Capture.Reason(left, reason, notice: false, Time.unscaledTime);
        LastReason = reason;
        LastLeft = left;
        LastPosition = worldPos;
        Plugin.Log.LogDebug(
            $"[ui.affordance] denied {reason} ({(left ? "left" : "right")} hand) at {worldPos}"
        );
        AffordanceRuntime.Show(left, worldPos, reason, hint, atHand: atHand);
        if (tint != null)
        {
            DenyTint.Target(tint);
            AffordanceRuntime.ClaimTint();
        }
        // Both hands are needed: the free one feels where to go.
        if (reason == DenyReason.NeedsTwoHands)
            Feel(!left, CueFeel.Contact, CueHaptics.Contact);
        return true;
    }

    private static Vector3 HandPosition(bool left)
    {
        var hand = VRRig.Instance != null ? VRRig.Instance.Hand(left) : null;
        return hand != null ? hand.position : Vector3.zero;
    }

    /// <summary>The game's stamina bar backing (StaminaBar.cs:10), which vanilla itself flashes when out of
    /// stamina (OutOfStaminaPulse, :188).</summary>
    private static Component StaminaBar()
    {
        var gui = GUIManager.instance;
        return gui != null && gui.bar != null ? gui.bar.backing : null;
    }

    private static bool RaiseWrist(bool shown)
    {
        if (shown)
            PeakXR.UI.Spatial.StatusAlerts.Raise();
        return shown;
    }

    /// <summary>Per frame while an action fills (a hold, a pull, a charge): a light tick at its own pace, stronger
    /// toward done (<paramref name="fraction"/> 0..1). Not a refusal. <paramref name="hold"/> false for a ramp that
    /// is not a hold's fill (stamina refilling on a grip, a countdown, a heat search), so it never silences the
    /// progress ring's quarter ticks (<see cref="ProgressActive"/>).</summary>
    public static void Progress(bool left, float fraction, bool hold = true)
    {
        if (!Enabled)
            return;
        if (hold)
            progressAt = Time.unscaledTime;
        if (Ramps[left ? 0 : 1].TryTick(Time.unscaledTime, fraction, out var p))
            Feel(left, CueFeel.Progress, p);
    }

    /// <summary>A hold's <see cref="Progress"/> ramp ran on either hand in the last 0.2 s: it owns this hold's progress
    /// feel, so the progress ring keeps only its completion tick (one hold, one progress haptic).</summary>
    public static bool ProgressActive => Time.unscaledTime - progressAt < 0.2f;

    /// <summary>One tick as the hand meets something (a hold, a handle, a friend's hand). Not a refusal.</summary>
    public static void Contact(bool left)
    {
        if (Enabled)
            Feel(left, CueFeel.Contact, CueHaptics.Contact);
    }

    /// <summary>An attempt was called off (released early, pulled away): a soft tick and the soft falling note at
    /// the hand. Not a refusal; pair it with a <see cref="Deny"/> when the game gave a reason.</summary>
    public static void Cancel(bool left)
    {
        if (!Enabled)
            return;
        Feel(left, CueFeel.Cancel, CueHaptics.Cancel);
        CueAudio.Play(CueSound.Cancel, HandPosition(left), 1f);
    }

    /// <summary>A gesture hit its limit (spool length, zoom, cannon angle, a slider's end): one hard thunk and a
    /// dull knock at the hand. Call it on reaching the limit; repeats within 0.3 s are dropped.</summary>
    public static void EndStop(bool left)
    {
        if (!Enabled)
            return;
        float now = Time.unscaledTime;
        ref float at = ref EndStopAt[left ? 0 : 1];
        if (now - at < EndStopRepeat)
            return;
        at = now;
        Feel(left, CueFeel.EndStop, CueHaptics.EndStop);
        CueAudio.Play(CueSound.EndStop, HandPosition(left), 1f);
    }

    /// <summary>
    /// Per frame while harm nears (food about to burn, a lit fuse, a lethal item): pulses that speed up and
    /// strengthen with <paramref name="urgency"/> (0..1), a trembling note now and then, and a red ring with the
    /// "!" glyph at <paramref name="worldPos"/>. A warning, not a refusal.
    /// </summary>
    public static void Danger(bool left, Vector3 worldPos, float urgency)
    {
        if (!Enabled)
            return;
        float now = Time.unscaledTime;
        if (Dangers[left ? 0 : 1].TryTick(now, urgency, out var p))
        {
            Feel(left, CueFeel.Danger, p);
            if (now - dangerSoundAt >= DangerSoundRepeat)
            {
                dangerSoundAt = now;
                CueAudio.Play(CueSound.Danger, worldPos, 1f);
            }
        }
        AffordanceRuntime.Hover(left, worldPos, DenyReason.DangerSoon, default, silent: true);
    }

    /// <summary>Plays a cue sound at <paramref name="worldPos"/> (<see cref="CueSound.Transmit"/> for a ping or
    /// call that went out). False in flat play or when nothing could play it.</summary>
    public static bool Play(CueSound sound, Vector3 worldPos, float volume = 1f) =>
        Enabled && CueAudio.Play(sound, worldPos, volume);

    private static void Feel(bool left, CueFeel feel, HapticPulse p)
    {
        // Danger warns like a refusal; the other feels answer what the hand did.
        bool delivered = Haptics.Pulse(
            left,
            p.Amplitude,
            p.Seconds,
            feel == CueFeel.Danger ? HapticPriority.Alert : HapticPriority.Cue
        );
        CueLog.Capture.Feel(left, feel, p.Amplitude, delivered, Haptics.Muted, Time.unscaledTime);
    }

    /// <summary>
    /// Not a refusal but something the game did on its own that the hands did not show (the held item stowed when a
    /// climb started): a single soft tick, a teal ring at <paramref name="hint"/>'s spot and the label. Same rate limit.
    /// </summary>
    public static bool Inform(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint,
        string text = null
    )
    {
        if (!Enabled || !Gate.Allow(reason, left, Time.unscaledTime))
            return false;
        CueLog.Capture.Reason(left, reason, notice: true, Time.unscaledTime);
        AffordanceRuntime.Show(left, worldPos, reason, hint, notice: true, text);
        return true;
    }

    /// <summary>
    /// The red preview of a refused placement: right after a <see cref="Deny"/> that returned true, a red ghost of
    /// <paramref name="prefab"/> (the game's placement preview) at the refused spot, for as long as the refusal shows.
    /// </summary>
    public static void Preview(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (Enabled)
        {
            DenyTint.Ghost(prefab, position, rotation);
            AffordanceRuntime.ClaimTint();
        }
    }

    /// <summary>Deny at the hand itself, for refusals with no spot in the world (two-handed use, a throw).</summary>
    public static bool DenyAtHand(bool left, DenyReason reason, DenyHint hint = default) =>
        DenyAtHand(left, reason, hint, null);

    /// <summary>As above, with the refused thing to turn red (see the tint <see cref="Deny"/> overload); without one,
    /// the item in that hand turns red.</summary>
    public static bool DenyAtHand(bool left, DenyReason reason, DenyHint hint, Component tint)
    {
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        return hand != null && DenyCore(left, hand.position, reason, hint, true, tint);
    }

    /// <summary>
    /// This frame the <paramref name="left"/> hand points or aims at <paramref name="worldPos"/>, where the game would
    /// refuse. Shows a red ring there while called; after a steady moment, the reason too. No haptic.
    /// </summary>
    public static void Hover(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint = default,
        string text = null
    )
    {
        if (Enabled)
            AffordanceRuntime.Hover(left, worldPos, reason, hint, notice: false, text);
    }

    /// <summary>A lasting notice while called every frame (where a dropped item lies): teal ring at the hint's spot,
    /// no red ring, no haptic; <paramref name="text"/> replaces the reason's label when set.</summary>
    public static void Mark(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint,
        string text = null
    )
    {
        if (Enabled)
            AffordanceRuntime.Hover(left, worldPos, reason, hint, notice: true, text);
    }

    /// <summary>Colour of the "this would work" part of a cue (the valid spot, the end of the real range).</summary>
    public static readonly Color HintColor = new(0.45f, 0.9f, 0.82f, 0.75f);

    /// <summary>The self-test starts from a clean rate limit so its denials are not swallowed.</summary>
    public static void ResetGate() => Gate.Reset();
}
