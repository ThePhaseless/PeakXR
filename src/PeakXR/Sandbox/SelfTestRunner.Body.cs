using System;
using System.Collections;
using PeakXR.Avatar;
using PeakXR.Comfort;
using PeakXR.Core;
using PeakXR.Locomotion;
using PeakXR.Look;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.UI.Status;
using PeakXR.VR;
using PeakXR.XR;
using Photon.Pun;
using UnityEngine;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakXR.Sandbox;

/// <summary>
/// WP3 steps (locomotion, stamina, body, environment): one per mechanic, each asserting the game's outcome and the
/// cue the body feels (<see cref="ExpectCue"/> for limits, <see cref="ExpectFeel"/> for take-off, landing, footing
/// and danger). States come from the sandbox (stamina, statuses, pass-out), never from waiting for them.
/// </summary>
internal sealed partial class SelfTestRunner
{
    Vector3 headAt;
    Quaternion headTurn;
    bool headScripted;

    static bool DomLeft => VRInput.LeftHanded;

    /// <summary>The move stick's hand, which is also the wrist with the stamina bar's refusal.</summary>
    static bool OffLeft => !VRInput.LeftHanded;

    IEnumerator MovementSteps()
    {
        yield return Restoring("vr.140-roomscale-blocked", 20f, RoomScaleBlockedStep());
        yield return HandStep("vr.141-forced-look-side", 15f, ForcedLookSideStep());
        yield return Restoring("vr.142-sprint-out", 20f, SprintOutStep());
        yield return Restoring("vr.154-jump-out", 15f, JumpOutStep());
        yield return Restoring("vr.143-crouch-walk", 25f, CrouchWalkStep());
        yield return HandStep("vr.144-jump", 15f, JumpStep());
        yield return Restoring("vr.155-hop-jump", 30f, HopStep());
        yield return Restoring("vr.158-arm-swing-still", 20f, SwingStillStep());
        yield return HandStep("vr.145-landing", 25f, LandingStep());
        yield return WarpBack();
        yield return HandStep("vr.146-slope", 30f, SlopeStep());
        yield return WarpBack();
        yield return HandStep("vr.148-ragdoll-getup", 15f, GetUpStep());
        yield return HandStep("vr.149-knockback-side", 10f, KnockbackSideStep());
        yield return HandStep("vr.150-pre-pass-out", 20f, PrePassOutStep());
        yield return HandStep("vr.147-morale-glow", 10f, MoraleGlowStep());
        yield return Restoring("vr.153-spectate-single", 30f, SpectateSingleStep());
    }

    // ---------------------------------------------------------------- scripted head

    /// <summary>Holds the headset at <paramref name="position"/> and <paramref name="rotation"/> (tracking space) on
    /// top of the scripted hands until <see cref="ReleaseHead"/>.</summary>
    void ScriptHead(Vector3 position, Quaternion rotation)
    {
        headAt = position;
        headTurn = rotation;
        if (headScripted)
            return;
        headScripted = true;
        var hands = applyHands;
        VRInput.TestOverride = () =>
        {
            hands();
            VRInput.HeadPosition = headAt;
            VRInput.HeadRotation = headTurn;
        };
    }

    void ReleaseHead()
    {
        if (!headScripted)
            return;
        headScripted = false;
        if (VRInput.TestOverride != IdleHands)
            VRInput.TestOverride = finished ? null : applyHands;
    }

    IEnumerator PressJump()
    {
        Script(DomLeft).Primary = true;
        yield return null;
        yield return null;
        Script(DomLeft).Primary = false;
        yield return null;
    }

    static float Ease(float from, float to, float t) => Mathf.Lerp(from, to, Mathf.Clamp01(t));

    // ---------------------------------------------------------------- steps

    /// <summary>vr.140: with the body held still (RoomScaleWalking off), the head drifts 0.9 m back: before the 1 m
    /// recenter fade, both hands get NotNow with a ring at the body's feet (LocomotionInput.WarnFar).</summary>
    IEnumerator RoomScaleBlockedStep()
    {
        var setting = LocomotionConfig.RoomScaleSetting;
        if (setting == null)
        {
            stepSkip = "player.locomotion not installed";
            yield break;
        }
        bool was = setting.Value;
        bodyRestore = () =>
        {
            setting.Value = was;
            ReleaseHead();
        };
        setting.Value = false;
        HeadFrame(out var head, out var forward, out _);
        var rot = VRInput.HeadRotation;
        float since = CueMark();
        float start = Now;
        while (Now - start < 1f)
        {
            ScriptHead(head - forward * Ease(0f, 0.9f, Now - start), rot);
            yield return null;
        }
        ScriptHead(head - forward * 0.9f, rot);
        yield return ExpectCue(true, DenyReason.NotNow, since, false, 1.5f);
        if (stepOk)
            yield return ExpectCue(false, DenyReason.NotNow, since, false, 0.5f);
        string note = cueNote;
        start = Now;
        while (Now - start < 0.6f)
        {
            ScriptHead(head - forward * Ease(0.9f, 0f, (Now - start) / 0.6f), rot);
            yield return null;
        }
        ReleaseHead();
        setting.Value = was;
        if (stepOk)
            stepNote = "head 0.9 m from a body that cannot follow: " + note;
    }

    /// <summary>vr.141: a scripted look 40 degrees to the right (what a frog tongue or the scoutmaster writes into
    /// lookValues) takes the heading; the ForcedLook rumble is stronger on the right hand.</summary>
    IEnumerator ForcedLookSideStep()
    {
        var c = Character.localCharacter;
        if (!ComfortEventsConfig.Haptics)
        {
            stepSkip = "comfort haptics off";
            yield break;
        }
        float at = ComfortEventsRuntime.LastAt;
        float baseYaw = c.data.lookValues.x;
        float weight = 0f;
        float start = Now;
        int i = 0;
        while (Now - start < 0.4f)
        {
            // Changing each frame, so player.look keeps reading it as another writer.
            c.data.lookValues = new Vector2(baseYaw + 40f + (i++ % 2) * 0.5f, c.data.lookValues.y);
            weight = Mathf.Max(weight, LookPatches.ScriptedLookWeight);
            yield return null;
        }
        yield return WaitFor(() => ComfortEventsRuntime.LastAt > at, 1f);
        float l = ComfortEventsRuntime.LastLeftAmplitude;
        float r = ComfortEventsRuntime.LastRightAmplitude;
        var note = $"look weight {weight:0.00}, rumble left {l:0.00} right {r:0.00}";
        if (ComfortEventsRuntime.LastAt <= at || !(r > l))
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.142: stick sprint runs; the bar emptied mid-sprint stops it (CharacterMovement.cs:263) and the
    /// wrist hand gets NoStamina with the bar turned red.</summary>
    IEnumerator SprintOutStep()
    {
        var c = Character.localCharacter;
        bool move = OffLeft;
        bodyRestore = () =>
        {
            Script(move).Stick = Vector2.zero;
            Script(move).StickClick = false;
        };
        if (!SandboxActions.SetStamina(1f, 0f))
        {
            stepSkip = "sandbox stamina control unavailable (online?)";
            yield break;
        }
        float started = CueMark();
        Script(move).Stick = new Vector2(0f, 1f);
        Script(move).StickClick = true;
        if (LocomotionConfig.SprintToggle)
        {
            yield return Wait(0.15f);
            Script(move).StickClick = false;
        }
        yield return WaitFor(() => c.data.isSprinting, 2f);
        bool sprinted = c.data.isSprinting;
        var from = c.Center;
        yield return Wait(0.5f);
        float speed = FlatDistance(c.Center, from) / 0.5f;
        // A sprint with stamina left is a success: nothing on the wrist hand.
        yield return ExpectNoCue(move, started, 0f);
        if (!stepOk)
            yield break;
        float since = CueMark();
        SandboxActions.SetStamina(0f, 0f);
        yield return WaitFor(() => !c.data.isSprinting, 1f);
        bool stopped = !c.data.isSprinting;
        if (!sprinted || !stopped)
        {
            Fail($"sprinted {sprinted} at {speed:0.0} m/s, stopped when empty {stopped}");
            yield break;
        }
        yield return ExpectCue(move, DenyReason.NoStamina, since, true, 1.5f);
        Script(move).Stick = Vector2.zero;
        Script(move).StickClick = false;
        if (stepOk)
            stepNote = $"sprint {speed:0.0} m/s, stopped empty: {cueNote}";
    }

    /// <summary>vr.154: a jump that spends the last of the stamina (JumpRpc's UseStamina, CharacterMovement.cs:673)
    /// gives the same NoStamina cue at the wrist hand.</summary>
    IEnumerator JumpOutStep()
    {
        var c = Character.localCharacter;
        if (!SandboxActions.SetStamina(0.006f, 0f))
        {
            stepSkip = "sandbox stamina control unavailable (online?)";
            yield break;
        }
        int before = BodyFeedback.TakeOffs;
        float since = CueMark();
        // Resting regenerates (Character.cs:1289): a fresh spend keeps the bar at the set value until the jump.
        c.data.sinceUseStamina = 0f;
        yield return PressJump();
        float start = Now;
        while (Now - start < 0.5f && c.data.currentStamina >= 0.005f)
        {
            c.data.sinceUseStamina = 0f;
            yield return null;
        }
        if (BodyFeedback.TakeOffs == before)
        {
            Fail("the jump press did not take off");
            yield break;
        }
        yield return ExpectCue(OffLeft, DenyReason.NoStamina, since, true, 1.5f);
        if (stepOk)
            stepNote = $"stamina {c.data.currentStamina:0.000} after the jump: {cueNote}";
    }

    /// <summary>vr.143: a real crouch latches with a soft tick on both hands, the crouched walk covers about half
    /// the standing walk (CharacterMovement.cs:408), and standing up ticks again.</summary>
    IEnumerator CrouchWalkStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!BodyGestures.CrouchOn || rig.SeatedMode)
        {
            stepSkip = "Locomotion.PhysicalCrouch off or seated mode";
            yield break;
        }
        bool move = OffLeft;
        bodyRestore = () =>
        {
            Script(move).Stick = Vector2.zero;
            ReleaseHead();
        };
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        var head = VRInput.HeadPosition;
        var rot = VRInput.HeadRotation;

        float walked = 0f;
        yield return Walk(c, move, 1f, d => walked = d);
        yield return Wait(0.6f);

        float since = CueMark();
        ScriptHead(new Vector3(head.x, eye - standing * 0.4f, head.z), rot);
        yield return WaitFor(() => c.data.isCrouching, 2f);
        if (!c.data.isCrouching)
        {
            Fail("the head held low did not crouch");
            yield break;
        }
        yield return ExpectFeel(true, CueFeel.Contact, since, 1f);
        if (!stepOk)
            yield break;
        float crouched = 0f;
        yield return Walk(c, move, 1f, d => crouched = d);
        since = CueMark();
        ScriptHead(head, rot);
        yield return WaitFor(() => !c.data.isCrouching, 2f);
        yield return ExpectFeel(false, CueFeel.Contact, since, 1f);
        ReleaseHead();
        float ratio = walked > 0.01f ? crouched / walked : 0f;
        var note = $"walked {walked:0.00} m standing, {crouched:0.00} m crouched (x{ratio:0.00})";
        if (!stepOk)
            yield break;
        if (ratio < 0.3f || ratio > 0.75f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>The move stick forward for <paramref name="seconds"/>; reports the flat distance walked.</summary>
    IEnumerator Walk(Character c, bool move, float seconds, Action<float> walked)
    {
        var from = c.Center;
        Script(move).Stick = new Vector2(0f, 1f);
        yield return Wait(seconds);
        Script(move).Stick = Vector2.zero;
        walked(FlatDistance(c.Center, from));
        yield return Wait(0.4f);
    }

    /// <summary>vr.144: the jump button takes off within 0.3 s with a take-off tick; pressed again before the game
    /// allows another (sinceJump under 0.3 s, CharacterMovement.cs:418), it refuses with TooSoon at the feet.</summary>
    IEnumerator JumpStep()
    {
        var c = Character.localCharacter;
        int before = BodyFeedback.TakeOffs;
        float since = CueMark();
        float pressed = Now;
        yield return PressJump();
        yield return WaitFor(() => BodyFeedback.TakeOffs > before, 0.5f);
        float took = Now - pressed;
        if (BodyFeedback.TakeOffs == before || took > 0.3f)
        {
            Fail($"took off {BodyFeedback.TakeOffs > before} after {took:0.00} s");
            yield break;
        }
        yield return ExpectFeel(DomLeft, CueFeel.Contact, since, 0.5f);
        if (!stepOk)
            yield break;
        string takeOff = cueNote;
        yield return WaitFor(() => !c.data.chargingJump, 0.5f);
        int airborne = BodyFeedback.TakeOffs;
        since = CueMark();
        yield return PressJump();
        yield return ExpectCue(DomLeft, DenyReason.TooSoon, since, false, 1f);
        if (stepOk && BodyFeedback.TakeOffs != airborne)
            Fail("the second press jumped again");
        else if (stepOk)
            stepNote = $"took off in {took:0.00} s ({takeOff}); second press: {cueNote}";
    }

    /// <summary>
    /// vr.155: with <c>Locomotion.HopToJump</c> on, standing up fast out of a crouch and looking down then back up
    /// (both with the arms lifting) do not jump; arms swung up while the head rises above standing do, with the
    /// take-off tick.
    /// </summary>
    IEnumerator HopStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var setting = BodyGestures.HopSetting;
        if (setting == null || rig.SeatedMode)
        {
            stepSkip = "player.locomotion not installed or seated mode";
            yield break;
        }
        bool was = setting.Value;
        bodyRestore = () =>
        {
            setting.Value = was;
            ReleaseHead();
        };
        setting.Value = true;
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        HeadFrame(out var head0, out _, out var right);
        var head = new Vector3(head0.x, eye, head0.z);
        var rot = VRInput.HeadRotation;
        HeadFrame(out var h, out var forward, out _);
        var restLeft = Rest(h, forward, -right).Position;
        var restRight = Rest(h, forward, right).Position;
        int before = BodyFeedback.TakeOffs;

        // Up out of a deep crouch, arms swinging up, ending 2 cm above standing.
        ScriptHead(head + Vector3.down * standing * 0.4f, rot);
        yield return WaitFor(() => c.data.isCrouching, 2f);
        yield return Wait(0.5f);
        float start = Now;
        while (Now - start < 0.6f)
        {
            float t = (Now - start) / 0.35f;
            ScriptHead(head + Vector3.up * Ease(-standing * 0.4f, 0.02f, t), rot);
            SetHand(true, restLeft + Vector3.up * Ease(0f, 0.5f, t));
            SetHand(false, restRight + Vector3.up * Ease(0f, 0.5f, t));
            yield return null;
        }
        yield return RestHands(head, rot, restLeft, restRight);
        bool standUpJumped = BodyFeedback.TakeOffs != before;

        // Look 60 degrees down, then back up quickly, overshooting 5 cm, hands lifting.
        start = Now;
        while (Now - start < 1.3f)
        {
            float s = Now - start;
            float down = Ease(0f, 1f, s / 0.3f) - Ease(0f, 1f, (s - 0.8f) / 0.15f);
            ScriptHead(
                head + Vector3.up * (-0.06f * down + 0.05f * Ease(0f, 1f, (s - 0.9f) / 0.1f)),
                Quaternion.AngleAxis(60f * down, right) * rot
            );
            float lift = Ease(0f, 0.4f, (s - 0.8f) / 0.15f);
            SetHand(true, restLeft + Vector3.up * lift);
            SetHand(false, restRight + Vector3.up * lift);
            yield return null;
        }
        yield return RestHands(head, rot, restLeft, restRight);
        bool lookJumped = BodyFeedback.TakeOffs != before;

        // The hop: arms up 0.45 m in 0.2 s, the head 0.1 m above standing 0.05 s later.
        int hopBefore = BodyFeedback.TakeOffs;
        float since = CueMark();
        start = Now;
        while (Now - start < 0.6f)
        {
            float s = Now - start;
            ScriptHead(head + Vector3.up * Ease(0f, 0.1f, (s - 0.05f) / 0.2f), rot);
            SetHand(true, restLeft + Vector3.up * Ease(0f, 0.45f, s / 0.2f));
            SetHand(false, restRight + Vector3.up * Ease(0f, 0.45f, s / 0.2f));
            yield return null;
        }
        yield return WaitFor(() => BodyFeedback.TakeOffs > hopBefore, 0.5f);
        bool hopped = BodyFeedback.TakeOffs > hopBefore;
        yield return RestHands(head, rot, restLeft, restRight);
        ReleaseHead();
        setting.Value = was;
        var note =
            $"stand-up jumped {standUpJumped}, look-down jumped {lookJumped}, hop jumped {hopped}";
        if (standUpJumped || lookJumped || !hopped)
        {
            Fail(note);
            yield break;
        }
        yield return ExpectFeel(DomLeft, CueFeel.Contact, since, 1f);
        if (stepOk)
            stepNote = note;
    }

    IEnumerator RestHands(Vector3 head, Quaternion rot, Vector3 restLeft, Vector3 restRight)
    {
        ScriptHead(head, rot);
        SetHand(true, restLeft);
        SetHand(false, restRight);
        yield return Wait(1f);
    }

    /// <summary>
    /// vr.158 (D1): arm-swing sprint ignores gestures. Running swings with the stick idle do not sprint, and
    /// clapping-like sideways swings with the stick forward do not either; a running swing while walking does.
    /// </summary>
    IEnumerator SwingStillStep()
    {
        var c = Character.localCharacter;
        var setting = BodyGestures.ArmSwingSetting;
        if (setting == null)
        {
            stepSkip = "player.locomotion not installed";
            yield break;
        }
        bool was = setting.Value;
        bool move = OffLeft;
        bodyRestore = () =>
        {
            setting.Value = was;
            Script(move).Stick = Vector2.zero;
        };
        setting.Value = true;
        HeadFrame(out var head, out var forward, out var right);
        var restLeft = Rest(head, forward, -right).Position;
        var restRight = Rest(head, forward, right).Position;
        bool still = false,
            clap = false,
            run = false;
        yield return SwingFor(restLeft, restRight, forward, 1.5f, v => still |= v);
        Script(move).Stick = new Vector2(0f, 1f);
        yield return SwingFor(restLeft, restRight, right, 1.5f, v => clap |= v);
        yield return SwingFor(restLeft, restRight, forward, 1.5f, v => run |= v);
        Script(move).Stick = Vector2.zero;
        SetHand(true, restLeft);
        SetHand(false, restRight);
        setting.Value = was;
        yield return Wait(0.5f);
        var note =
            $"sprint standing still {still}, swinging sideways {clap}, running swing {run} (speed {c.data.avarageVelocity.magnitude:0.0})";
        if (still || clap || !run)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>Alternating swings of 0.25 m at 1.6 Hz along <paramref name="axis"/>; reports
    /// <see cref="BodyGestures.Swinging"/> with sprinting every frame.</summary>
    IEnumerator SwingFor(
        Vector3 restLeft,
        Vector3 restRight,
        Vector3 axis,
        float seconds,
        Action<bool> sprint
    )
    {
        var c = Character.localCharacter;
        float start = Now;
        while (Now - start < seconds)
        {
            float z = 0.25f * Mathf.Sin(2f * Mathf.PI * 1.6f * (Now - start));
            SetHand(true, restLeft + axis * z);
            SetHand(false, restRight - axis * z);
            sprint(c.data.isSprinting && BodyGestures.Swinging);
            yield return null;
        }
    }

    /// <summary>vr.145: a 12 m drop (god mode off): fall danger pulses build before the game's fall damage time,
    /// and the landing thump is strong (amplitude 0.6 or more).</summary>
    IEnumerator LandingStep()
    {
        var c = Character.localCharacter;
        if (Physics.Raycast(c.Center, Vector3.up, 13f, HelperFunctions.terrainMapMask))
        {
            stepSkip = "no open sky above the spawn";
            yield break;
        }
        SandboxActions.SetGodMode(false);
        int landings = BodyFeedback.Landings;
        float since = CueMark();
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, c.Center + Vector3.up * 12f, false);
        yield return WaitFor(() => !c.warping && !c.data.isGrounded, 2f);
        yield return WaitFor(() => BodyFeedback.Landings > landings, 8f);
        if (BodyFeedback.Landings == landings)
        {
            Fail("never landed");
            yield break;
        }
        float thump = BodyFeedback.LastLanding;
        yield return ExpectFeel(true, CueFeel.Danger, since, 0.1f);
        if (!stepOk)
            yield break;
        string danger = cueNote;
        yield return ExpectFeel(true, CueFeel.Contact, since, 0.5f);
        if (!stepOk)
            yield break;
        float injury = c.refs.afflictions.GetCurrentStatus(STATUS.Injury);
        SandboxActions.ClearStatus(STATUS.Injury);
        var note = $"thump {thump:0.00}, injury {injury:0.00}, danger: {danger}";
        if (thump < 0.6f)
            Fail(note);
        else
            stepNote = note;
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 5f);
    }

    /// <summary>vr.146: standing on ground steeper than the game's max angle (CharacterMovement.cs:906) slides:
    /// TooSteep on both hands, once.</summary>
    IEnumerator SlopeStep()
    {
        var c = Character.localCharacter;
        if (!FindSlope(c.Center, out var spot, out float angle))
        {
            stepSkip = "no 58-75 degree slope within 30 m";
            yield break;
        }
        float since = CueMark();
        yield return WarpTo(spot);
        yield return ExpectCue(true, DenyReason.TooSteep, since, false, 3f);
        if (stepOk)
            stepNote = $"slope {angle:0} degrees: {cueNote}";
    }

    static bool FindSlope(Vector3 around, out Vector3 spot, out float angle)
    {
        for (float r = 3f; r <= 30f; r += 3f)
        {
            for (float a = 0f; a < 360f; a += 30f)
            {
                var p = around + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (
                    !Physics.Raycast(
                        p + Vector3.up * 20f,
                        Vector3.down,
                        out var hit,
                        40f,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                    continue;
                angle = Vector3.Angle(Vector3.up, hit.normal);
                if (
                    angle > 58f
                    && angle < 75f
                    && hit.collider.GetComponent<CollisionModifier>() == null
                )
                {
                    spot = hit.point + hit.normal * 0.3f;
                    return true;
                }
            }
        }
        spot = default;
        angle = 0f;
        return false;
    }

    /// <summary>vr.148: a 2 s ragdoll (RPCA_Fall, Character.cs:1752): when control returns past 0.9 both hands get
    /// the footing tick, and the stick walks again.</summary>
    IEnumerator GetUpStep()
    {
        var c = Character.localCharacter;
        float since = CueMark();
        c.photonView.RPC("RPCA_Fall", RpcTarget.All, 2f, 0f);
        yield return WaitFor(() => c.data.currentRagdollControll < 0.5f, 1f);
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 6f);
        yield return ExpectFeel(true, CueFeel.Contact, since, 1f);
        if (!stepOk)
            yield break;
        string tick = cueNote;
        float walked = 0f;
        yield return Walk(c, OffLeft, 0.8f, d => walked = d);
        var note = $"walked {walked:0.00} m after getting up; {tick}";
        if (walked < 0.3f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.149: a knockback from the right (RPCA_AddForceAtPosition, Character.cs:461, applied locally)
    /// rumbles the right hand harder than the left.</summary>
    IEnumerator KnockbackSideStep()
    {
        var c = Character.localCharacter;
        var head = VRRig.Instance.Head;
        if (!ComfortEventsConfig.Haptics)
        {
            stepSkip = "comfort haptics off";
            yield break;
        }
        float at = ComfortEventsRuntime.LastAt;
        var right = Flat(head.right);
        c.RPCA_AddForceAtPosition(-right * 300f, c.Center + right, 2f);
        yield return WaitFor(() => ComfortEventsRuntime.LastAt > at, 1f);
        float l = ComfortEventsRuntime.LastLeftAmplitude;
        float r = ComfortEventsRuntime.LastRightAmplitude;
        var note = $"rumble left {l:0.00} right {r:0.00}";
        if (ComfortEventsRuntime.LastAt <= at || !(r > l))
            Fail(note);
        else
            stepNote = note;
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 4f);
    }

    /// <summary>vr.150: statuses over the top (Injury 1) start passOutValue rising (Character.cs:1043): danger
    /// pulses on the hands before the pass-out. Cleared again before it completes.</summary>
    IEnumerator PrePassOutStep()
    {
        var c = Character.localCharacter;
        float since = CueMark();
        if (!SandboxActions.AddStatus(STATUS.Injury, 1f))
        {
            stepSkip = "the game refused the status (airport?)";
            yield break;
        }
        yield return ExpectFeel(true, CueFeel.Danger, since, 3f);
        float value = c.data.passOutValue;
        SandboxActions.ClearStatus(STATUS.Injury);
        yield return WaitFor(() => c.data.passOutValue < 0.01f || c.data.passedOut, 6f);
        if (c.data.passedOut)
        {
            SandboxActions.Revive();
            yield return WaitFor(() => !c.data.passedOut, 6f);
        }
        if (stepOk)
            stepNote = $"passOutValue {value:0.00}: {cueNote}";
    }

    /// <summary>vr.147: the campfire morale boost (StaminaBar.PlayMoraleBoost, StaminaBar.cs:223) shows no
    /// MORALEBOOST word in VR; the rising chord plays at the wrist.</summary>
    IEnumerator MoraleGlowStep()
    {
        var gui = GUIManager.instance;
        var bar = gui != null ? gui.bar : null;
        if (bar == null)
        {
            stepSkip = "no stamina bar";
            yield break;
        }
        int shown = MoraleBoostTextPatch.Shown;
        float since = CueMark();
        bar.PlayMoraleBoost(2);
        yield return Wait(0.5f);
        var sound = CueLog.Capture.LastSound(CueSound.WouldWork);
        bool word = bar.moraleBoostText != null && bar.moraleBoostText.enabled;
        var note =
            $"boost handled {MoraleBoostTextPatch.Shown > shown}, word shown {word}, chord played {sound.Played && sound.At >= since}";
        if (MoraleBoostTextPatch.Shown == shown || word || !(sound.Played && sound.At >= since))
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.153: passed out solo, the only body to watch is your own: a spectate flick refuses with
    /// CantSwitch at the flicking hand (MainCameraMovement.HandleSpecSelection, :189).</summary>
    IEnumerator SpectateSingleStep()
    {
        var c = Character.localCharacter;
        if (PhotonNetwork.PlayerList.Length > 1)
        {
            stepSkip = "not solo";
            yield break;
        }
        bool dom = DomLeft;
        bodyRestore = () =>
        {
            Script(dom).Stick = Vector2.zero;
            if (c != null && c.data.fullyPassedOut)
                SandboxActions.Revive();
        };
        if (!SandboxActions.PassOut())
        {
            stepSkip = "sandbox pass-out unavailable";
            yield break;
        }
        yield return WaitFor(() => c.data.fullyPassedOut, 6f);
        if (!c.data.fullyPassedOut)
        {
            Fail("did not pass out");
            yield break;
        }
        yield return Wait(0.5f);
        bool handLeft = dom;
        if (
            ControllerLabels.ForAction(
                VRAction.SpectateNext,
                dom,
                LocalHold.ItemInLeftHand,
                out var actionLeft,
                out _
            )
        )
            handLeft = actionLeft;
        float since = CueMark();
        Script(dom).Stick = new Vector2(0f, 1f);
        yield return Wait(0.3f);
        Script(dom).Stick = Vector2.zero;
        yield return ExpectCue(handLeft, DenyReason.CantSwitch, since, false, 1.5f);
        string note = cueNote;
        SandboxActions.Revive();
        yield return WaitFor(() => !c.data.passedOut && !c.data.fullyPassedOut, 6f);
        if (stepOk)
            stepNote = note;
    }
}
