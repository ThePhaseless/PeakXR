using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Classes of game events that move or flash the view without the player doing it (docs/mechanics.md rows
    /// tagged <c>new:comfort-events</c>). Each class gets one VR treatment from <see cref="ComfortPolicy"/>.
    /// </summary>
    public enum ComfortEvent
    {
        /// <summary>An impulse shoves the local body (explosions, mob hits, bonks, falling rocks).</summary>
        Knockback,

        /// <summary>A large ballistic launch (cannon, geyser, bad mushroom, scout cannon, jump pads).</summary>
        Launch,

        /// <summary>The body starts tumbling (Character.Fall). The view itself is rig.camera's takeover.</summary>
        Ragdoll,

        /// <summary>The game writes the look direction (grabs, tongues, forced turns). player.look keeps the
        /// view on the head; this is only the cue that something turned the body.</summary>
        ForcedLook,

        /// <summary>Camera shake (Perlin or spring shake, cinematic rumble). Always zeroed in VR.</summary>
        Shake,

        /// <summary>Full-view colour flashes and status pulses (damage, cold, heat, poison).</summary>
        StatusFlash,

        /// <summary>Post-processing pulses that blur, warp or darken the view (dizzy, blind, drunk).</summary>
        PostEffect,

        /// <summary>Instant position change of the local body (warps, respawns, returns to a checkpoint).</summary>
        Teleport,

        /// <summary>Continuous pushes (wind zones, currents, conveyor-like forces).</summary>
        WindPush,
    }

    /// <summary>What PeakXR does in VR when a <see cref="ComfortEvent"/> happens.</summary>
    public enum ComfortTreatment
    {
        None,

        /// <summary>Close the comfort vignette briefly, scaled by the event's strength.</summary>
        VignettePulse,

        /// <summary>Black at once, hold, fade back in: for jumps the eye should not see.</summary>
        FadeHoldFade,

        /// <summary>Rumble both controllers instead of moving the view.</summary>
        HapticPulse,

        /// <summary>Leave the body for the horizon-locked third-person view while the event lasts.</summary>
        ThirdPerson,
    }

    /// <summary>One event's resolved request. All zero means nothing to do.</summary>
    public struct ComfortResponse
    {
        /// <summary>Vignette intensity 0..1 held for <see cref="VignetteSeconds"/>, then released.</summary>
        public float Vignette;
        public float VignetteSeconds;

        /// <summary>Go black now and stay black for <see cref="FadeHoldSeconds"/>.</summary>
        public bool Fade;
        public float FadeHoldSeconds;

        /// <summary>Controller rumble amplitude 0..1 and length.</summary>
        public float Haptic;
        public float HapticSeconds;

        /// <summary>Seconds of detached third-person view (renew every frame for a lasting event).</summary>
        public float DetachSeconds;

        public bool Any => Vignette > 0f || Fade || Haptic > 0f || DetachSeconds > 0f;
    }

    /// <summary>
    /// The one table mapping comfort events to VR treatments. Defaults favour comfort: nothing the game does may
    /// move the view, so motion becomes a vignette, a fade, a rumble or a detached view.
    /// </summary>
    public static class ComfortPolicy
    {
        /// <summary>Vignette closing for a full-strength event before the master intensity.</summary>
        public const float MaxVignette = 0.8f;
        public const float VignetteSeconds = 0.6f;
        public const float FadeHoldSeconds = 0.15f;
        public const float HapticSeconds = 0.12f;
        public const float DetachSeconds = 0.5f;

        public static ComfortTreatment Default(ComfortEvent e)
        {
            switch (e)
            {
                case ComfortEvent.Knockback:
                case ComfortEvent.WindPush:
                    return ComfortTreatment.VignettePulse;
                case ComfortEvent.Launch:
                    return ComfortTreatment.ThirdPerson;
                case ComfortEvent.Ragdoll:
                case ComfortEvent.ForcedLook:
                case ComfortEvent.Shake:
                    return ComfortTreatment.HapticPulse;
                case ComfortEvent.Teleport:
                    return ComfortTreatment.FadeHoldFade;
                case ComfortEvent.StatusFlash:
                case ComfortEvent.PostEffect:
                    return ComfortTreatment.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e));
            }
        }

        /// <summary>Events the body feels: they also rumble when haptics are on, whatever the treatment.</summary>
        public static bool IsPhysical(ComfortEvent e) =>
            e == ComfortEvent.Knockback
            || e == ComfortEvent.Launch
            || e == ComfortEvent.Ragdoll
            || e == ComfortEvent.Shake;

        /// <param name="magnitude">Event strength 0..1 (NaN counts as full).</param>
        /// <param name="intensity">Master intensity 0..1; 0 turns every treatment off.</param>
        /// <param name="haptics">Physical events also rumble.</param>
        public static ComfortResponse Resolve(
            ComfortEvent e,
            ComfortTreatment treatment,
            float magnitude,
            float intensity,
            bool haptics
        )
        {
            var r = new ComfortResponse();
            float k = Clamp01(intensity);
            if (k <= 0f || treatment == ComfortTreatment.None)
                return r;
            float m = float.IsNaN(magnitude) ? 1f : Clamp01(magnitude);
            // A weak event still shows a little, so the cue never vanishes entirely.
            float strength = k * (0.3f + 0.7f * m);
            switch (treatment)
            {
                case ComfortTreatment.VignettePulse:
                    r.Vignette = MaxVignette * strength;
                    r.VignetteSeconds = VignetteSeconds;
                    break;
                case ComfortTreatment.FadeHoldFade:
                    r.Fade = true;
                    r.FadeHoldSeconds = FadeHoldSeconds;
                    break;
                case ComfortTreatment.HapticPulse:
                    r.Haptic = strength;
                    r.HapticSeconds = HapticSeconds;
                    break;
                case ComfortTreatment.ThirdPerson:
                    r.DetachSeconds = DetachSeconds;
                    break;
            }
            if (haptics && r.Haptic <= 0f && IsPhysical(e))
            {
                r.Haptic = strength;
                r.HapticSeconds = HapticSeconds;
            }
            return r;
        }

        static float Clamp01(float v) =>
            v < 0f ? 0f
            : v > 1f ? 1f
            : v;
    }

    /// <summary>
    /// Mixes the comfort requests of one frame and over time: the strongest vignette pulse wins until it runs
    /// out, fades go black at once (the jump already happened) and fade back in after the longest hold, detach
    /// lasts while renewed, and haptics are reported once. Call <see cref="Add"/> any time, then
    /// <see cref="Update"/> once per frame.
    /// </summary>
    public sealed class ComfortPulses
    {
        const int Slots = 8;

        /// <summary>Seconds for the vignette to open after a pulse ends.</summary>
        public float VignetteRelease = 0.3f;

        /// <summary>Seconds to fade back in after the hold.</summary>
        public float FadeInSeconds = 0.3f;

        readonly float[] _strength = new float[Slots];
        readonly float[] _left = new float[Slots];
        float _fadeHold;
        bool _fadeRequested;
        float _detachLeft;
        float _hapticAmp,
            _hapticSec;

        public float Vignette { get; private set; }

        /// <summary>0 = clear, 1 = black.</summary>
        public float Fade { get; private set; }

        /// <summary>The fade went black this frame: draw it without easing.</summary>
        public bool SnapBlack { get; private set; }

        public bool Detached => _detachLeft > 0f;

        /// <summary>Rumble to play this frame (0 = none).</summary>
        public float HapticAmplitude { get; private set; }
        public float HapticSeconds { get; private set; }

        public void Add(in ComfortResponse r)
        {
            if (r.Vignette > 0f && r.VignetteSeconds > 0f)
                AddVignette(r.Vignette, r.VignetteSeconds);
            if (r.Fade)
            {
                _fadeRequested = true;
                _fadeHold = Math.Max(_fadeHold, r.FadeHoldSeconds);
            }
            if (r.DetachSeconds > 0f)
                _detachLeft = Math.Max(_detachLeft, r.DetachSeconds);
            if (r.Haptic > _hapticAmp)
                _hapticAmp = r.Haptic;
            if (r.Haptic > 0f && r.HapticSeconds > _hapticSec)
                _hapticSec = r.HapticSeconds;
        }

        public void Update(float dt)
        {
            float peak = 0f;
            for (int i = 0; i < Slots; i++)
            {
                if (_left[i] <= 0f)
                    continue;
                if (_strength[i] > peak)
                    peak = _strength[i];
                _left[i] -= dt;
                if (_left[i] <= 0f)
                    _strength[i] = 0f;
            }
            if (peak >= Vignette)
                Vignette = peak;
            else
                Vignette = Math.Max(
                    peak,
                    Vignette - (VignetteRelease > 0f ? dt / VignetteRelease : 1f)
                );

            SnapBlack = false;
            if (_fadeRequested)
            {
                _fadeRequested = false;
                SnapBlack = Fade < 1f;
                Fade = 1f;
            }
            else if (_fadeHold > 0f)
            {
                _fadeHold = Math.Max(0f, _fadeHold - dt);
            }
            else if (Fade > 0f)
            {
                Fade = FadeInSeconds > 0f ? Math.Max(0f, Fade - dt / FadeInSeconds) : 0f;
            }

            if (_detachLeft > 0f)
                _detachLeft -= dt;

            HapticAmplitude = _hapticAmp;
            HapticSeconds = _hapticSec;
            _hapticAmp = _hapticSec = 0f;
        }

        public void Reset()
        {
            Array.Clear(_strength, 0, Slots);
            Array.Clear(_left, 0, Slots);
            Vignette = Fade = 0f;
            _fadeHold = _detachLeft = 0f;
            _fadeRequested = SnapBlack = false;
            _hapticAmp = _hapticSec = 0f;
            HapticAmplitude = HapticSeconds = 0f;
        }

        void AddVignette(float strength, float seconds)
        {
            // A pulse renewed every frame (a launch, a push zone) extends its own slot.
            for (int i = 0; i < Slots; i++)
            {
                if (_left[i] > 0f && Math.Abs(_strength[i] - strength) < 1e-4f)
                {
                    _left[i] = Math.Max(_left[i], seconds);
                    return;
                }
            }
            // Otherwise an empty slot, or the weakest; a full table drops the weakest pulse.
            int pick = 0;
            for (int i = 0; i < Slots; i++)
            {
                if (_left[i] <= 0f)
                {
                    pick = i;
                    break;
                }
                if (_strength[i] < _strength[pick])
                    pick = i;
            }
            if (_left[pick] > 0f && _strength[pick] > strength)
                return;
            _strength[pick] = strength;
            _left[pick] = seconds;
        }
    }
}

namespace PeakXR.Core
{
    /// <summary>
    /// Tracks one launch of the local body: active from the launch until it has landed and settled (or a cap),
    /// so the detached view lasts the whole flight. Also detects launches no hook reports: a sudden jump in
    /// upward speed (bounce pads, geysers built in prefabs) counts as a launch; a jump or a slow rise does not.
    /// </summary>
    public sealed class LaunchTracker
    {
        /// <summary>Shortest time a launch is shown, even if the body lands at once.</summary>
        public float MinSeconds = 0.4f;

        /// <summary>Longest flight before the view returns anyway.</summary>
        public float MaxSeconds = 8f;

        /// <summary>Seconds on the ground before the launch ends.</summary>
        public float SettleSeconds = 0.25f;

        /// <summary>Upward speed gain within one update (m/s) that counts as a launch.</summary>
        public float DetectSpeed = 8f;

        float _elapsed;
        float _grounded;
        float _lastUp;
        bool _haveLast;

        public bool Active { get; private set; }

        public void Start()
        {
            Active = true;
            _elapsed = 0f;
            _grounded = 0f;
        }

        public void Cancel()
        {
            Active = false;
            _haveLast = false;
        }

        /// <summary>Returns <see cref="Active"/> after this frame.</summary>
        public bool Update(bool grounded, float upSpeed, float dt)
        {
            if (_haveLast && !Active && upSpeed - _lastUp >= DetectSpeed)
                Start();
            _lastUp = upSpeed;
            _haveLast = true;
            if (!Active)
                return false;
            _elapsed += dt;
            _grounded = grounded ? _grounded + dt : 0f;
            if (_elapsed >= MaxSeconds || (_elapsed >= MinSeconds && _grounded >= SettleSeconds))
                Active = false;
            return Active;
        }
    }
}
