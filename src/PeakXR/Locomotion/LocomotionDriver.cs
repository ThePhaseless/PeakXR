using PeakXR.Comfort;
using PeakXR.Core;
using PeakXR.Look;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Locomotion;

/// <summary>
/// Per-frame comfort work that must run even when the game skips input sampling: the vignette, the seated flag on
/// the rig and the room-scale watchdog. Lives on its own DontDestroyOnLoad object.
/// </summary>
internal sealed class LocomotionDriver : MonoBehaviour
{
    private readonly ComfortVignette vignette = new();
    private readonly VignetteMesh mesh = new();
    private Vector3 lastHead;
    private Vector3 headVelocity;
    private bool hadHead;

    private void Update()
    {
        LocomotionInput.Watchdog();
        var rig = VRRig.Instance;
        if (rig != null)
            rig.SeatedMode = ComfortConfig.Seated;

        float dt = Time.deltaTime;
        var c = Character.localCharacter;
        if (VRSession.Active)
            BodyFeedback.Tick(c);
        bool on =
            VRSession.Active
            && ComfortConfig.Vignette
            && rig != null
            && rig.Origin != null
            && c != null
            && !c.data.dead
            && !c.data.fullyPassedOut;
        vignette.Enabled = on;
        vignette.Strength = ComfortConfig.VignetteStrength;
        float speed = on ? ArtificialSpeed(c, rig, dt) : 0f;
        if (!on)
            hadHead = false;
        float intensity = vignette.Update(speed, LookPatches.SmoothTurnRate, dt);
        // comfort.events pulses (knockbacks, wind, launches) close the same ring, whatever the speed.
        if (VRSession.Active && rig != null)
            intensity = Mathf.Max(intensity, ComfortEventsRuntime.VignetteBoost);
        mesh.Show(rig != null ? rig.Head : null, intensity);
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
