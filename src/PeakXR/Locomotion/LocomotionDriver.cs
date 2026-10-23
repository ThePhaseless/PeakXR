using PeakXR.Avatar;
using PeakXR.Comfort;
using PeakXR.Core;
using PeakXR.Look;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// Per-frame comfort work that must run even when the game skips input sampling: the vignette, the seated flag on
/// the rig and the room-scale watchdog. Lives on its own DontDestroyOnLoad object. The vignette also runs while
/// dead, passed out or carried: the detached view then moves without the player, and that motion is read from the
/// rig origin itself.
/// </summary>
internal sealed class LocomotionDriver : MonoBehaviour
{
    private readonly ComfortVignette vignette = new();
    private readonly VignetteMesh mesh = new();
    private Vector3 lastHead;
    private Vector3 headVelocity;
    private bool hadHead;
    private Vector3 lastOrigin;
    private bool hadOrigin;

    /// <summary>The vignette's closing this frame, 0..1 (self-test).</summary>
    internal static float Intensity { get; private set; }

    /// <summary>The artificial view speed it read this frame, m/s (self-test).</summary>
    internal static float ViewSpeed { get; private set; }

    private void Update()
    {
        LocomotionInput.Watchdog();
        var rig = VRRig.Instance;
        if (rig != null)
        {
            rig.SeatedMode = ComfortConfig.Seated;
            rig.SeatedReachGain = ComfortConfig.SeatedReachGain;
        }

        float dt = Time.deltaTime;
        var c = Character.localCharacter;
        if (VRSession.Active)
        {
            BodyFeedback.Tick(c);
            EnvironmentCues.Tick(c);
        }
        bool on =
            VRSession.Active
            && ComfortConfig.Vignette
            && rig != null
            && rig.Origin != null
            && c != null;
        vignette.Enabled = on;
        vignette.Strength = ComfortConfig.VignetteStrength;
        vignette.LinearMin = ComfortConfig.VignetteStartSpeed;
        vignette.LinearMax = Mathf.Max(ComfortConfig.VignetteFullSpeed, vignette.LinearMin + 0.1f);
        // Off the own body (spectating, passed out, carried, the chase view of a tumble or launch) the view's motion
        // is the rig origin's: the watched body's climbs and falls, not this body's velocity.
        bool detached =
            on
            && (
                !Rig.RigDriver.ViewOnBody
                || Rig.RigDriver.ViewDetached
                || c.data.dead
                || c.data.fullyPassedOut
            );
        float speed =
            !on ? 0f
            : detached ? OriginSpeed(rig, dt)
            : ArtificialSpeed(c, rig, dt);
        if (!detached)
            hadOrigin = false;
        // Detached frames don't sample the head: the first frame back must not read the whole detached time as one.
        if (!on || detached)
            hadHead = false;
        ViewSpeed = speed;
        float intensity = vignette.Update(speed, LookPatches.SmoothTurnRate, dt);
        // comfort.events pulses (knockbacks, wind, launches) close the same ring, whatever the speed.
        if (VRSession.Active && rig != null)
            intensity = Mathf.Max(intensity, ComfortEventsRuntime.VignetteBoost);
        Intensity = intensity;
        mesh.Show(rig != null ? rig.Head : null, intensity);
    }

    /// <summary>The rig origin's speed: the view's motion not made by the head. A snap or a re-place behind a fade
    /// reads 0 (<see cref="ViewMotionLimit.ArtificialSpeed"/>).</summary>
    private float OriginSpeed(VRRig rig, float dt)
    {
        var origin = rig.Origin.position;
        float speed = hadOrigin
            ? ViewMotionLimit.ArtificialSpeed((origin - lastOrigin).N(), dt)
            : 0f;
        lastOrigin = origin;
        hadOrigin = true;
        return speed;
    }

    /// <summary>
    /// Speed of the view that the player did not make: the character's real velocity (walking, sprinting, falling,
    /// sliding, being thrown) minus the real head's, so room-scale steps, where the body follows the head, do not
    /// close the vignette.
    /// </summary>
    private float ArtificialSpeed(Character c, VRRig rig, float dt)
    {
        var head = VRInput.HeadPosition;
        if (hadHead && dt > 0f)
            headVelocity = Vector3.Lerp(
                headVelocity,
                (head - lastHead) / dt,
                1f - Mathf.Exp(-10f * dt)
            );
        else
            headVelocity = Vector3.zero;
        lastHead = head;
        hadHead = VRInput.HeadTracked;
        return (c.data.avarageVelocity - rig.Origin.TransformVector(headVelocity)).magnitude;
    }

    private void OnDestroy() => mesh.Destroy();
}
