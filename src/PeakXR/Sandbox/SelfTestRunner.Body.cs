using System;
using System.Collections;
using System.Collections.Generic;
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
    Action headOverride;

    static bool DomLeft => VRInput.LeftHanded;

    /// <summary>The move stick's hand, which is also the wrist with the stamina bar's refusal.</summary>
    static bool OffLeft => !VRInput.LeftHanded;

    /// <summary>A step's timeout covers the shared settle before it (<see cref="SettleSeconds"/>: revive, warp home,
    /// recentre, stand still, once more on a poor spot) as well as the body: run 38 timed out vr.149 inside the
    /// settle and vr.211 in its own 8 s wait. <paramref name="body"/> is the step body's worst case, every wait in
    /// it bounded.</summary>
    static float Budget(float body) => SettleSeconds + body;

    /// <summary>
    /// WP3 steps that need the Tropics segment (breakable bridges hang in the jungle). Run once the run has jumped
    /// there, next to the vine steps (SelfTestRunner.cs, the Tropics branch).
    /// </summary>
    IEnumerator TropicsBodySteps()
    {
        yield return Restoring("vr.218-bridge", Budget(16f), BridgeStep());
        yield return WarpBack();
    }

    IEnumerator MovementSteps()
    {
        yield return Restoring("vr.140-roomscale-blocked", Budget(16f), RoomScaleBlockedStep());
        yield return HandStep("vr.141-forced-look-side", Budget(3f), ForcedLookSideStep());
        yield return Restoring("vr.142-sprint-out", Budget(7f), SprintOutStep());
        yield return Restoring("vr.154-jump-out", Budget(4f), JumpOutStep());
        yield return Restoring("vr.143-crouch-walk", Budget(12f), CrouchWalkStep());
        yield return Restoring("vr.241-long-crouch-hint", Budget(12f), LongCrouchHintStep());
        yield return HandStep("vr.144-jump", Budget(4f), JumpStep());
        yield return Restoring("vr.155-hop-jump", Budget(14f), HopStep());
        yield return Restoring("vr.158-arm-swing-still", Budget(6f), SwingStillStep());
        yield return HandStep("vr.145-landing", Budget(18f), LandingStep());
        yield return WarpBack();
        yield return Restoring("vr.146-slope", Budget(10f), SlopeStep());
        yield return WarpBack();
        yield return HandStep("vr.148-ragdoll-getup", Budget(10f), GetUpStep());
        yield return HandStep("vr.149-knockback-side", Budget(12f), KnockbackSideStep());
        yield return HandStep("vr.150-pre-pass-out", Budget(16f), PrePassOutStep());
        yield return HandStep("vr.147-morale-glow", Budget(3f), MoraleGlowStep());
        yield return Restoring("vr.153-spectate-single", Budget(15f), SpectateSingleStep());
        yield return Restoring("vr.157-spectate-point", Budget(15f), SpectatePointStep());
        yield return Restoring("vr.230-stoop-reach", Budget(10f), StoopReachStep());
        yield return Restoring("vr.239-seated-floor-reach", Budget(10f), SeatedReachStep());
        yield return Restoring("vr.219-spectate-comfort", Budget(18f), SpectateComfortStep());
        yield return Restoring("vr.156-head-near-wall", Budget(5f), HeadNearWallStep());
        yield return Restoring("vr.159-amulet-pips", Budget(7f), AmuletStep());
        yield return Restoring("vr.210-heartbeat", Budget(11f), HeartbeatStep());
        yield return Restoring("vr.211-shiver", Budget(10f), ShiverStep());
        yield return Restoring("vr.151-wind-pressure", Budget(3f), WindPressureStep());
        yield return Restoring("vr.212-tornado-brace", Budget(5f), TornadoBraceStep());
        yield return Restoring("vr.152-bounce", Budget(14f), BounceStep());
        yield return WarpBack();
        yield return Restoring("vr.213-lava-sting", Budget(7f), LavaStingStep());
        yield return HandStep("vr.214-balloon", Budget(22f), BalloonTieStep());
        yield return HandStep("vr.215-parachute", Budget(36f), ParachuteStep());
        yield return HandStep("vr.216-sugar-crash", Budget(6f), SugarCrashStep());
        yield return HandStep("vr.217-float-end", Budget(10f), FloatEndStep());
    }

    // ---------------------------------------------------------------- scripted head

    /// <summary>Holds the headset at <paramref name="position"/> and <paramref name="rotation"/> (tracking space) on
    /// top of the scripted hands until <see cref="ReleaseHead"/>.</summary>
    void ScriptHead(Vector3 position, Quaternion rotation)
    {
        headAt = position;
        headTurn = rotation;
        // Re-installed whenever something else took the override (a failed step's teardown): never stale.
        if (headScripted && VRInput.TestOverride == headOverride)
            return;
        headScripted = true;
        var hands = applyHands;
        headOverride = () =>
        {
            hands();
            VRInput.HeadPosition = headAt;
            VRInput.HeadRotation = headTurn;
        };
        VRInput.TestOverride = headOverride;
    }

    void ReleaseHead()
    {
        if (!headScripted)
            return;
        headScripted = false;
        if (VRInput.TestOverride == headOverride)
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

    /// <summary><see cref="ExpectFeel"/> on each hand, for the cues that go to both (BodyFeedback.FeelBoth,
    /// Affordance.Danger on each): checking one hand passed a cue lost on the other.</summary>
    IEnumerator ExpectFeelBoth(CueFeel feel, float since, float within)
    {
        yield return ExpectFeel(true, feel, since, within);
        if (!stepOk)
            yield break;
        string left = cueNote;
        yield return ExpectFeel(false, feel, since, within);
        if (stepOk)
            cueNote = $"L {left}, R {cueNote}";
    }

    // ---------------------------------------------------------------- steps

    /// <summary>vr.140: with the body held still (RoomScaleWalking off), the head drifts 0.9 m back: before the 1 m
    /// recenter fade, the dominant hand (BodyFeedback.BodyHand) gets NotNow with a ring at the body's feet
    /// (LocomotionInput.WarnFar).</summary>
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
        yield return ExpectCue(DomLeft, DenyReason.NotNow, since, false, 1.5f, seen: false);
        string note = cueNote;
        start = Now;
        while (Now - start < 0.6f)
        {
            ScriptHead(head - forward * Ease(0.9f, 0f, (Now - start) / 0.6f), rot);
            yield return null;
        }
        ReleaseHead();
        setting.Value = was;
        if (!stepOk)
            yield break;
        if (!was)
        {
            stepNote =
                "head 0.9 m from a body that cannot follow: "
                + note
                + "; stalled follow skipped, RoomScaleWalking off";
            yield break;
        }
        yield return Wait(1f);
        yield return StalledFollow(wall => bodyRestore += () => UnityEngine.Object.Destroy(wall));
        if (stepOk)
            stepNote = $"head 0.9 m from a body that cannot follow: {note}; {stalledNote}";
    }

    string stalledNote;

    /// <summary>vr.140, the path a player meets: room-scale on, the head steps 0.7 m back (past the lean radius,
    /// short of 1 m) toward a hip-high wall just behind the body. The follow walk stalls against it
    /// (RoomScaleFollow.Stalled, after StallSeconds of no progress), which gives the same NotNow at the feet and the
    /// one recenter fade (LocomotionInput.WarnFar, RecenterFade); the body stays on its side of the wall. The head
    /// passes over the wall, so the eye guard stays out of it. Releasing the head afterwards walks the body forward
    /// onto open ground, which the next settle warps home from.</summary>
    IEnumerator StalledFollow(Action<GameObject> placed)
    {
        var c = Character.localCharacter;
        HeadFrame(out var head, out var forward, out _);
        var rot = VRInput.HeadRotation;
        var back = -Flat(VRRig.Instance.Head.forward);
        var start = c.Center;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        placed(wall);
        wall.name = "PeakXR_TestWallBehind";
        wall.layer = LayerMask.NameToLayer("Map");
        wall.transform.SetPositionAndRotation(
            // 0.45 m: run 49 put it at 0.6 and the body walked 0.36 m toward it before stalling, leaving the
            // head inside the 0.35 m lean radius, where a stall clears with no fade or NotNow (a lean, by design).
            c.data.groundPos
                + back * 0.45f
                + Vector3.up * 0.45f,
            Quaternion.LookRotation(back)
        );
        wall.transform.localScale = new Vector3(3f, 0.9f, 0.3f);
        Physics.SyncTransforms();
        yield return Wait(0.3f);
        float since = CueMark();
        float t0 = Now;
        float faded = 0f;
        float maxGap = 0f;
        bool everStalled = false;
        bool crouched = false;
        int drops = LocomotionInput.FollowDrops;
        while (Now - t0 < 1f)
        {
            ScriptHead(head - forward * Ease(0f, 0.7f, Now - t0), rot);
            faded = Mathf.Max(faded, VRFade.Target);
            maxGap = Mathf.Max(maxGap, LocomotionInput.FollowGap);
            everStalled |= LocomotionInput.FollowStalled;
            crouched |= c.data.isCrouching;
            yield return null;
        }
        ScriptHead(head - forward * 0.7f, rot);
        float stallBy = Now + 4f;
        while (
            Now < stallBy
            && !(
                faded > 0.5f
                && CueLog.Capture.Fired(DomLeft, DenyReason.NotNow, since, false, out _)
            )
        )
        {
            faded = Mathf.Max(faded, VRFade.Target);
            maxGap = Mathf.Max(maxGap, LocomotionInput.FollowGap);
            everStalled |= LocomotionInput.FollowStalled;
            crouched |= c.data.isCrouching;
            yield return null;
        }
        float back0 = Vector3.Dot(c.Center - start, back);
        yield return ExpectCue(DomLeft, DenyReason.NotNow, since, false, 0.1f, seen: false);
        string cue = cueNote;
        // What the follow did, so a miss names its cause (run 52 failed with no numbers).
        string trail = F(
            $"head-body gap up to {maxGap:0.00} m, stalled {everStalled}, follow dropouts {LocomotionInput.FollowDrops - drops}, crouched {crouched}, NotNow on the right {CueLog.Capture.Fired(false, DenyReason.NotNow, since, false, out _)}, on the left {CueLog.Capture.Fired(true, DenyReason.NotNow, since, false, out _)}, fade {faded:0.00}"
        );
        if (!stepOk)
            Note(trail);
        // The fade clears again once the recenter is done.
        yield return WaitFor(() => VRFade.Target <= 0f, 2f);
        bool cleared = VRFade.Target <= 0f;
        ReleaseHead();
        yield return Wait(2f);
        stalledNote = F(
            $"stalled follow: body {back0:0.00} m back toward the wall 0.45 m behind, cleared {cleared}, {trail}: {cue}"
        );
        if (!stepOk)
            yield break;
        if (!(faded > 0.5f) || !cleared || back0 > 0.6f)
            Fail(stalledNote);
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
        // The weight is player.look's verdict that the game's look took the heading: a rumble alone passed without it.
        if (ComfortEventsRuntime.LastAt <= at || !(r > l) || !(weight > 0.5f))
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
        yield return ExpectCue(move, DenyReason.NoStamina, since, true, 1.5f, seen: false);
        Script(move).Stick = Vector2.zero;
        Script(move).StickClick = false;
        if (stepOk)
            stepNote = $"sprint {speed:0.0} m/s, stopped empty: {cueNote}";
    }

    /// <summary>vr.154: a jump that spends the last of the stamina (JumpRpc's UseStamina, CharacterMovement.cs:667)
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
        // Resting regenerates (Character.cs:1286): a fresh spend keeps the bar at the set value until the jump.
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
        yield return ExpectCue(OffLeft, DenyReason.NoStamina, since, true, 1.5f, seen: false);
        if (stepOk)
            stepNote = $"stamina {c.data.currentStamina:0.000} after the jump: {cueNote}";
    }

    /// <summary>vr.143: a real crouch latches with a soft tick on both hands, the crouched walk covers about half
    /// the standing walk (CharacterMovement.cs:402), and standing up ticks again.</summary>
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
        if (!FaceOpen(c, rig, 6f, out string blocked))
        {
            stepSkip = "no heading with 6 m of open ground: " + blocked;
            yield break;
        }
        yield return Wait(0.5f);
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
        yield return ExpectFeelBoth(CueFeel.Contact, since, 1f);
        if (!stepOk)
            yield break;
        float crouched = 0f;
        yield return Walk(c, move, 1f, d => crouched = d);
        since = CueMark();
        ScriptHead(head, rot);
        yield return WaitFor(() => !c.data.isCrouching, 2f);
        yield return ExpectFeelBoth(CueFeel.Contact, since, 1f);
        ReleaseHead();
        float ratio = walked > 0.01f ? crouched / walked : 0f;
        var note =
            $"walked {walked:0.00} m standing, {crouched:0.00} m crouched (x{ratio:0.00}), ahead: {Ahead(c, rig)}";
        if (!stepOk)
            yield break;
        if (ratio < 0.3f || ratio > 0.75f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.241: crouch-walking past the long-crouch wait (shortened to 1 s here, 10 s in play) shows one
    /// HappenedHere notice with the recenter hint on the off hand; walking on in the same crouch shows no second.</summary>
    IEnumerator LongCrouchHintStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!BodyGestures.CrouchOn || rig.SeatedMode)
        {
            stepSkip = "Locomotion.PhysicalCrouch off or seated mode";
            yield break;
        }
        bool move = OffLeft;
        var rule = BodyGestures.LongCrouchRule;
        float wasSeconds = rule.Seconds;
        bodyRestore = () =>
        {
            rule.Seconds = wasSeconds;
            Script(move).Stick = Vector2.zero;
            ReleaseHead();
        };
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        if (!FaceOpen(c, rig, 6f, out string blocked))
        {
            stepSkip = "no heading with 6 m of open ground: " + blocked;
            yield break;
        }
        yield return Wait(0.5f);
        var head = VRInput.HeadPosition;
        var rot = VRInput.HeadRotation;
        rule.Seconds = 1f;
        ScriptHead(new Vector3(head.x, eye - standing * 0.4f, head.z), rot);
        yield return WaitFor(() => c.data.isCrouching, 2f);
        if (!c.data.isCrouching)
        {
            Fail("the head held low did not crouch");
            yield break;
        }
        int before = BodyGestures.LongCrouchHints;
        float since = CueMark();
        float walked = 0f;
        yield return Walk(c, move, 2f, d => walked = d);
        if (BodyGestures.LongCrouchHints != before + 1)
        {
            Fail(
                $"{BodyGestures.LongCrouchHints - before} hints after a 2 s crouched walk ({walked:0.00} m)"
            );
            yield break;
        }
        yield return ExpectCue(
            VRInput.OffHandLeft,
            DenyReason.HappenedHere,
            since,
            3f,
            seen: false
        );
        if (!stepOk)
            yield break;
        var note = cueNote;
        yield return Walk(c, move, 1.5f, _ => { });
        if (BodyGestures.LongCrouchHints != before + 1)
            Fail("a second hint in the same crouch");
        else
            stepNote = $"one hint after {walked:0.00} m crouched: {note}";
    }

    /// <summary>Turns the rig toward the nearest heading with <paramref name="meters"/> of open ground at hip and
    /// knee height (the move stick walks where the head looks).</summary>
    static bool FaceOpen(Character c, VRRig rig, float meters, out string blocked)
    {
        var facing = Flat(rig.Head.forward);
        blocked = null;
        for (int i = 0; i < 16; i++)
        {
            float turn = (i + 1) / 2 * 22.5f * (i % 2 == 0 ? 1f : -1f);
            var dir = Quaternion.Euler(0f, turn, 0f) * facing;
            if (
                Physics.SphereCast(
                    c.Center,
                    0.3f,
                    dir,
                    out var hit,
                    meters,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                || Physics.SphereCast(
                    c.Center + Vector3.down * 0.6f,
                    0.25f,
                    dir,
                    out hit,
                    meters,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                blocked ??= hit.collider.name;
                continue;
            }
            rig.RotateYaw(turn);
            return true;
        }
        return false;
    }

    /// <summary>What stands within 0.8 m ahead of the body, for a step's note.</summary>
    static string Ahead(Character c, VRRig rig) =>
        Physics.SphereCast(
            c.Center + Vector3.down * 0.3f,
            0.3f,
            Flat(rig.Head.forward),
            out var hit,
            0.8f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? $"{hit.collider.name} at {hit.distance:0.00} m"
            : "open";

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
    /// allows another (sinceJump under 0.3 s, CharacterMovement.cs:412), it refuses with TooSoon at the feet.</summary>
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
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
        if (!stepOk)
            yield break;
        string takeOff = cueNote;
        yield return WaitFor(() => !c.data.chargingJump, 0.5f);
        int airborne = BodyFeedback.TakeOffs;
        since = CueMark();
        yield return PressJump();
        yield return ExpectCue(DomLeft, DenyReason.TooSoon, since, false, 1f, seen: false);
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
        yield return ExpectFeelBoth(CueFeel.Contact, since, 1f);
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
        // The negatives read the gesture's own verdict: with the stick idle the game never sprints anyway
        // (CharacterMovement.cs:263 needs movementInput.y), so isSprinting passed a detector that fired standing still.
        yield return SwingFor(restLeft, restRight, forward, 1.5f, (_, s) => still |= s);
        Script(move).Stick = new Vector2(0f, 1f);
        yield return SwingFor(restLeft, restRight, right, 1.5f, (_, s) => clap |= s);
        // A player starts a sprint with stamina to spend: vr.154 just before leaves the bar at 0, and god mode's
        // refill is not this step's to rely on (run 50 walked at 1.9 m/s with no sprint at all).
        float staminaBefore = c.data.currentStamina;
        if (staminaBefore < 0.5f)
            SandboxActions.SetStamina(1f, 0f);
        bool swungRun = false,
            sprinted = false;
        yield return SwingFor(
            restLeft,
            restRight,
            forward,
            1.5f,
            (p, s) =>
            {
                run |= p && s;
                swungRun |= s;
                sprinted |= p;
            }
        );
        float speed = c.data.avarageVelocity.magnitude;
        Script(move).Stick = Vector2.zero;
        SetHand(true, restLeft);
        SetHand(false, restRight);
        setting.Value = was;
        yield return Wait(0.5f);
        var note =
            $"swing gesture standing still {still}, swinging sideways {clap}, sprinting on a running swing {run} (speed {speed:0.0} m/s; gesture seen {swungRun}, sprint seen {sprinted}, stamina {staminaBefore:0.00} before)";
        if (still || clap || !run)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>Alternating swings of 0.25 m at 1.6 Hz along <paramref name="axis"/>; reports sprinting and
    /// <see cref="BodyGestures.Swinging"/> every frame.</summary>
    IEnumerator SwingFor(
        Vector3 restLeft,
        Vector3 restRight,
        Vector3 axis,
        float seconds,
        Action<bool, bool> sprint
    )
    {
        var c = Character.localCharacter;
        float start = Now;
        while (Now - start < seconds)
        {
            float z = 0.25f * Mathf.Sin(2f * Mathf.PI * 1.6f * (Now - start));
            SetHand(true, restLeft + axis * z);
            SetHand(false, restRight - axis * z);
            sprint(c.data.isSprinting, BodyGestures.Swinging);
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
        yield return ExpectFeelBoth(CueFeel.Danger, since, 0.1f);
        if (!stepOk)
            yield break;
        string danger = cueNote;
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
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

    /// <summary>
    /// vr.146: a body on ground steeper than the game's max angle (CharacterMovement.maxAngle, :130) slides: the
    /// game resets sinceFallSlide from its hip ray (:900-919), the body leaves the ground and moves down, and the
    /// dominant hand gets TooSteep once (BodyFeedback, <see cref="SlideCue"/>). The slope is a placed slab on the Map
    /// layer 6 degrees past the limit, so the slide is the game's own and nothing lodges the body (run 35: the plane
    /// hull's 52 degree face registered the flag but held the body still). The note says what the body did.
    /// </summary>
    IEnumerator SlopeStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        float limit = Framework.GameMember.Field<CharacterMovement, float>("maxAngle")(
            c.refs.movement
        );
        float angle = limit + 6f;
        var forward = Flat(rig.Head.forward);
        var right = Vector3.Cross(Vector3.up, forward);
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyRestore = () =>
        {
            if (slab != null)
                UnityEngine.Object.Destroy(slab);
        };
        slab.name = "PeakXR_TestSlope";
        slab.layer = LayerMask.NameToLayer("Map");
        // 8 m long, falling away from the player: its high end 4 m up, clear of the beach.
        var centre = c.Center + forward * 6f + Vector3.up * 2.5f;
        slab.transform.SetPositionAndRotation(
            centre,
            Quaternion.AngleAxis(angle, right) * Quaternion.LookRotation(forward)
        );
        slab.transform.localScale = new Vector3(3f, 0.4f, 8f);
        Physics.SyncTransforms();
        if (
            !Physics.Raycast(
                centre + Vector3.up * 6f,
                Vector3.down,
                out var top,
                8f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            || top.collider.gameObject != slab
        )
        {
            Fail("the test slope is not the first thing under its centre");
            yield break;
        }
        float tilt = Vector3.Angle(Vector3.up, top.normal);
        float since = CueMark();
        warpedForProp = true;
        // The warp moves Character.Center (about the hip) there (Character.cs:1904-1957): lifted by the standing hip
        // height so the feet, not the hip, meet the slope and the hip ray finds it (run 34).
        float stand = Mathf.Clamp(c.Center.y - c.data.groundPos.y, 0.7f, 1.5f);
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, top.point + Vector3.up * stand, false);
        float minSlide = float.PositiveInfinity;
        float maxDown = 0f;
        int frames = 0,
            airborne = 0;
        float start = Now;
        while (
            Now - start < 4f
            && !CueLog.Capture.Fired(DomLeft, DenyReason.TooSteep, since, false, out _)
        )
        {
            // Warping frames count too: the warp's settling wait (Character.cs:1957) lets physics run, and run 36's
            // slide and cue both came then, while a gate on c.warping saw "0 slide frames" and failed a cue that fired.
            minSlide = Mathf.Min(minSlide, c.data.sinceFallSlide);
            if (c.data.sinceFallSlide < 0.1f)
            {
                frames++;
                if (!c.data.isGrounded)
                    airborne++;
                maxDown = Mathf.Max(maxDown, -c.data.avarageVelocity.y);
            }
            yield return null;
        }
        string where =
            $"slope {tilt:0} degrees (limit {limit:0}), slide started {minSlide < 0.1f}, "
            + $"off the ground {airborne} of {frames} slide frames, down up to {maxDown:0.0} m/s";
        yield return ExpectCue(DomLeft, DenyReason.TooSteep, since, false, 0.5f, seen: false);
        if (stepOk)
            stepNote = $"{where}: {cueNote}";
        else
            Note(minSlide < 0.1f ? where : where + ": the game never registered a slide");
        UnityEngine.Object.Destroy(slab);
        yield return WaitFor(() => c.data.isGrounded, 4f);
    }

    /// <summary>vr.148: a 2 s ragdoll (RPCA_Fall, Character.cs:1749): when control returns past 0.9 both hands get
    /// the footing tick, and the stick walks again.</summary>
    IEnumerator GetUpStep()
    {
        var c = Character.localCharacter;
        float since = CueMark();
        c.photonView.RPC("RPCA_Fall", RpcTarget.All, 2f, 0f);
        yield return WaitFor(() => c.data.currentRagdollControll < 0.5f, 1f);
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 6f);
        yield return ExpectFeelBoth(CueFeel.Contact, since, 1f);
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

    /// <summary>vr.149: knockbacks from the right (RPCA_AddForceAtPosition, Character.cs:458, applied locally) rumble
    /// the right hand harder than the left, and a harder push rumbles harder: a light 150 then a heavy 450 (vanilla
    /// hits run 200 to 500+). shortcut: the game's knockback RPC is called as a mob or bonk sends it; no attacker
    /// offline.</summary>
    IEnumerator KnockbackSideStep()
    {
        var c = Character.localCharacter;
        if (!ComfortEventsConfig.Haptics)
        {
            stepSkip = "comfort haptics off";
            yield break;
        }
        float lightL = 0f,
            lightR = 0f,
            heavyL = 0f,
            heavyR = 0f;
        bool felt = true;
        yield return Knock(c, 150f, (ok, l, r) => (felt, lightL, lightR) = (felt && ok, l, r));
        yield return Knock(c, 450f, (ok, l, r) => (felt, heavyL, heavyR) = (felt && ok, l, r));
        var note = F(
            $"push 150: rumble left {lightL:0.00} right {lightR:0.00}; push 450: left {heavyL:0.00} right {heavyR:0.00}"
        );
        if (!felt || !(lightR > lightL) || !(heavyR > heavyL) || !(heavyR > lightR))
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>One knockback of <paramref name="force"/> from the head's right; reports the first rumble after it
    /// (false when none came within 1 s), then waits for the body to get back up and the rumble to end.</summary>
    static IEnumerator Knock(Character c, float force, Action<bool, float, float> result)
    {
        float at = ComfortEventsRuntime.LastAt;
        var right = Flat(VRRig.Instance.Head.right);
        c.RPCA_AddForceAtPosition(-right * force, c.Center + right, 2f);
        yield return WaitFor(() => ComfortEventsRuntime.LastAt > at, 1f);
        result(
            ComfortEventsRuntime.LastAt > at,
            ComfortEventsRuntime.LastLeftAmplitude,
            ComfortEventsRuntime.LastRightAmplitude
        );
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 4f);
        yield return Wait(0.5f);
    }

    /// <summary>vr.150: statuses over the top (Injury 1) start passOutValue rising (Character.cs:1040): danger
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
        yield return ExpectFeelBoth(CueFeel.Danger, since, 3f);
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

    /// <summary>vr.147: the campfire morale boost (the game's MoraleBoost.SpawnMoraleBoost, MoraleBoost.cs:7, which
    /// Campfire.cs:191 and the Scoutmaster's bugle call; it reaches StaminaBar.PlayMoraleBoost, StaminaBar.cs:223,
    /// through Character.MoraleBoost, Character.cs:1592) shows no MORALEBOOST word in VR; the rising chord plays at
    /// the wrist and the bar gains the boost's extra stamina. shortcut: the boost is spawned at the body instead of
    /// lighting a campfire (the run reaches one only at a segment's end); upgrade once a step lights one.</summary>
    IEnumerator MoraleGlowStep()
    {
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        var bar = gui != null ? gui.bar : null;
        if (bar == null)
        {
            stepSkip = "no stamina bar";
            yield break;
        }
        int shown = MoraleBoostTextPatch.Shown;
        float extra = c.data.extraStamina;
        float since = CueMark();
        bool boosted = MoraleBoost.SpawnMoraleBoost(c.Center, 5f, 0.1f, 0f);
        yield return Wait(0.5f);
        var sound = CueLog.Capture.LastSound(CueSound.WouldWork);
        bool word = bar.moraleBoostText != null && bar.moraleBoostText.enabled;
        // At the cap (1, Character.cs:1632) there is nothing left to add.
        bool gained = c.data.extraStamina > extra + 0.05f || extra > 0.9f;
        var note =
            $"boost spawned {boosted}, handled {MoraleBoostTextPatch.Shown > shown}, word shown {word}, chord played {sound.Played && sound.At >= since}, extra stamina {extra:0.00} to {c.data.extraStamina:0.00}";
        if (
            !boosted
            || MoraleBoostTextPatch.Shown == shown
            || word
            || !(sound.Played && sound.At >= since)
            || !gained
        )
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
        yield return ExpectCue(handLeft, DenyReason.CantSwitch, since, false, 1.5f, seen: false);
        string note = cueNote;
        SandboxActions.Revive();
        yield return WaitFor(() => !c.data.passedOut && !c.data.fullyPassedOut, 6f);
        if (stepOk)
            stepNote = note;
    }

    // ---------------------------------------------------------------- WP3 gaps

    /// <summary>vr.157: passed out solo, pointing at nobody and pulling the trigger refuses with CantSwitch at the
    /// pointing hand (LocomotionInput.PointToWatch). Picking a teammate needs company: the MP run covers it.</summary>
    IEnumerator SpectatePointStep()
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
            Script(dom).Trigger = 0f;
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
        HeadFrame(out var head, out _, out var right);
        // Straight up: no teammate there.
        SetHand(dom, head + (dom ? -right : right) * 0.2f + Vector3.up * 0.3f);
        Script(dom).Rotation = HandLook(Vector3.up, Vector3.forward);
        yield return Wait(0.2f);
        float since = CueMark();
        Script(dom).Trigger = 1f;
        yield return Wait(0.2f);
        Script(dom).Trigger = 0f;
        yield return ExpectCue(dom, DenyReason.CantSwitch, since, false, 1.5f, seen: false);
        string note = cueNote;
        SandboxActions.Revive();
        yield return WaitFor(() => !c.data.passedOut && !c.data.fullyPassedOut, 6f);
        if (stepOk)
            stepNote = "pointing at nobody: " + note;
    }

    /// <summary>
    /// vr.156: the head leaning toward rock (body held, RoomScaleWalking off) gets a contact tick on the hands within
    /// 0.3 m of it, before any fade. The wall is placed 0.95 m ahead of the head itself (run 37: walking up to the
    /// sandbox wall timed out and the lean ended out of range), so the lean alone closes the gap.
    /// </summary>
    IEnumerator HeadNearWallStep()
    {
        var rig = VRRig.Instance;
        var setting = LocomotionConfig.RoomScaleSetting;
        if (setting == null)
        {
            stepSkip = "player.locomotion not installed";
            yield break;
        }
        bool was = setting.Value;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyRestore = () =>
        {
            setting.Value = was;
            ReleaseHead();
            if (wall != null)
                UnityEngine.Object.Destroy(wall);
        };
        setting.Value = false;
        var eye = rig.Head.position;
        var ahead = Flat(rig.Head.forward);
        wall.name = "PeakXR_TestWallNear";
        wall.layer = LayerMask.NameToLayer("Map");
        wall.transform.SetPositionAndRotation(
            eye + ahead * (0.95f + 0.15f),
            Quaternion.LookRotation(ahead)
        );
        wall.transform.localScale = new Vector3(3f, 3f, 0.3f);
        Physics.SyncTransforms();
        float WallAhead() =>
            Physics.Raycast(
                rig.Head.position,
                Flat(rig.Head.forward),
                out var hit,
                3f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
                ? hit.distance
                : float.PositiveInfinity;
        yield return Wait(0.3f);
        float gap = WallAhead();
        if (gap > 1.5f)
        {
            Fail($"the test wall is not ahead of the head ({gap:0.00} m)");
            yield break;
        }
        HeadFrame(out var head, out var forward, out _);
        var rot = VRInput.HeadRotation;
        // Under 0.8 m of drift: room-scale's NotNow warning stays out of it.
        float lean = Mathf.Min(gap - 0.15f, 0.78f);
        float since = CueMark();
        float start = Now;
        bool black = false;
        float near = gap;
        while (Now - start < 1.5f)
        {
            ScriptHead(head + forward * Ease(0f, lean, (Now - start) / 1.2f), rot);
            black |= RigDriver.EyeGuardBlack;
            near = Mathf.Min(near, WallAhead());
            yield return null;
        }
        // Straight ahead: BodyFeedback.NearWall ticks both hands.
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
        string note = $"head {near:0.00} m from the wall (from {gap:0.00}), black {black}";
        ScriptHead(head, rot);
        yield return Wait(0.5f);
        ReleaseHead();
        setting.Value = was;
        if (stepOk)
            stepNote = note + ": " + cueNote;
        else
            Note(note);
    }

    /// <summary>vr.159: two amulet charges show two lit beads on the wrist; an air jump spends one (a double tick)
    /// and its bead dims.</summary>
    IEnumerator AmuletStep()
    {
        var c = Character.localCharacter;
        var a = c.refs.afflictions;
        int total = a.totalExtraJumps;
        bodyRestore = () =>
        {
            a.totalExtraJumps = total;
            c.data.extraJumps = Mathf.Min(c.data.extraJumps, total);
        };
        a.totalExtraJumps = 2;
        c.data.extraJumps = 2;
        yield return Wait(0.3f);
        int shown = AmuletPips.Shown;
        int lit = AmuletPips.LitCount;
        int air = BodyFeedback.AirJumps;
        int before = BodyFeedback.TakeOffs;
        yield return PressJump();
        yield return WaitFor(() => BodyFeedback.TakeOffs > before && !c.data.chargingJump, 1f);
        yield return Wait(0.35f);
        float since = CueMark();
        yield return PressJump();
        yield return WaitFor(() => BodyFeedback.AirJumps > air, 1f);
        yield return Wait(0.1f);
        int litAfter = AmuletPips.LitCount;
        var note =
            $"beads {shown}, lit {lit}, after the air jump lit {litAfter}, air jumps {BodyFeedback.AirJumps - air}";
        if (shown != 2 || lit != 2 || BodyFeedback.AirJumps == air || litAfter != 1)
        {
            Fail(note);
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
        a.totalExtraJumps = total;
        c.data.extraJumps = Mathf.Min(c.data.extraJumps, total);
        yield return WaitFor(() => c.data.isGrounded, 3f);
        if (stepOk)
            stepNote = note;
    }

    /// <summary>vr.210: sprinting with the move stick low on the bar (the game's sprint drain, CharacterMovement.cs:266)
    /// beats a heartbeat; under the Weight status the beat is slower (and heavier).</summary>
    IEnumerator HeartbeatStep()
    {
        var c = Character.localCharacter;
        bool move = OffLeft;
        bodyRestore = () =>
        {
            Script(move).Stick = Vector2.zero;
            Script(move).StickClick = false;
        };
        if (!SandboxActions.SetStamina(0.25f, 0f))
        {
            stepSkip = "sandbox stamina control unavailable (online?)";
            yield break;
        }
        float light = 0f,
            heavy = 0f;
        string lightRun = null,
            heavyRun = null;
        bool ran = true;
        yield return Sprint(
            c,
            move,
            3f,
            (p, ok, run) => (light, ran, lightRun) = (p, ran && ok, run)
        );
        SandboxActions.AddStatus(STATUS.Weight, 0.5f);
        // Weight lowers the max (1 minus the status sum, Character.cs:1613), and the beat slows as the bar's fraction
        // rises (BodyCues.Heartbeat): 0.25 of a 0.5 bar beat slower with no weight term at all. Same fraction, so
        // only the weight (period x1.5 at 0.5) can slow it.
        float max = c.GetMaxStamina();
        SandboxActions.SetStamina(0.25f * max, 0f);
        yield return Sprint(
            c,
            move,
            4f,
            (p, ok, run) => (heavy, ran, heavyRun) = (p, ran && ok, run)
        );
        SandboxActions.ClearStatus(STATUS.Weight);
        var note =
            $"beat every {light:0.00} s at 0.25 of the bar ({lightRun}), with Weight 0.5 (max {max:0.00}) every {heavy:0.00} s ({heavyRun})";
        if (!ran || !(light > 0f) || !(heavy > light * 1.2f))
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>Sprints with the move stick and its click (as vr.142) until the second beat or
    /// <paramref name="seconds"/>, then stops and rests 1 s. Reports the first beat gap, which the first beat sets
    /// from the bar as the sprint starts (later gaps shorten as the sprint drains it, faster on a Weight-shrunk bar),
    /// whether the body sprinted and spent stamina, and a note on the sprint.</summary>
    IEnumerator Sprint(Character c, bool move, float seconds, Action<float, bool, string> result)
    {
        int beats = BodyFeedback.Beats;
        float before = c.data.currentStamina;
        bool sprinted = false;
        Script(move).Stick = new Vector2(0f, 1f);
        Script(move).StickClick = true;
        float start = Now;
        while (Now - start < seconds && BodyFeedback.Beats - beats < 2)
        {
            if (LocomotionConfig.SprintToggle && Now - start > 0.15f)
                Script(move).StickClick = false;
            sprinted |= c.data.isSprinting;
            yield return null;
        }
        Script(move).Stick = Vector2.zero;
        Script(move).StickClick = false;
        float spent = before - c.data.currentStamina;
        result(
            BodyFeedback.Beats - beats >= 2 ? BodyFeedback.LastBeatPeriod : 0f,
            sprinted && spent > 0f,
            F($"sprinting {sprinted} for {Now - start:0.0} s, spent {spent:0.000}")
        );
        yield return Wait(1f);
    }

    /// <summary>vr.211: Cold rising gives the cold rumble; once Cold holds steady, the rumble fades out.</summary>
    IEnumerator ShiverStep()
    {
        var c = Character.localCharacter;
        bodyRestore = () => SandboxActions.ClearStatus(STATUS.Cold);
        float rising = 0f;
        for (int i = 0; i < 4; i++)
        {
            if (!SandboxActions.AddStatus(STATUS.Cold, 0.05f))
            {
                stepSkip = "the game refused Cold (airport?)";
                yield break;
            }
            yield return Wait(0.3f);
            rising = Mathf.Max(
                rising,
                Interact.EnvironmentRumble.Level(Interact.EnvSource.Cold, true)
            );
        }
        // Steady means the game's own Cold has not risen for the meter to empty: night cold
        // (CharacterAfflictions.cs:509-511) keeps adding, and each rise is real cold that rumbles. Cold falling
        // (warming) reads as none. Bounded: 6 s, then the step skips.
        var a = c.refs.afflictions;
        float cold = a.GetCurrentStatus(STATUS.Cold);
        float still = Now;
        float steady = -1f;
        float until = Now + 6f;
        while (Now < until && steady < 0f)
        {
            float now = a.GetCurrentStatus(STATUS.Cold);
            if (now > cold)
                still = Now;
            cold = now;
            if (Now - still >= 3f)
                steady = Interact.EnvironmentRumble.Level(Interact.EnvSource.Cold, true);
            yield return null;
        }
        SandboxActions.ClearStatus(STATUS.Cold);
        var note = F(
            $"cold rumble while rising {rising:0.000}, after 3 s steady {steady:0.000} (cold {cold:0.00})"
        );
        if (steady < 0f)
            stepSkip = "Cold kept rising for 6 s (night cold): " + note;
        else if (rising < 0.1f || steady > 0.03f)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.151: the wind presses faintly on the windward hand. Deterministic on any level: the game's own wind push,
    /// <c>WindChillZone.AddWindForceToCharacter</c> (WindChillZone.cs:215), run on a zone object that is never
    /// activated (so its Awake, timers and RPCs never run), blowing from the player's right at full strength and
    /// unsheltered for 1 s. The force goes through the game's RPCA_AddForceAtPosition, where comfort.events feels it.
    /// </summary>
    IEnumerator WindPressureStep()
    {
        if (!ComfortEventsConfig.Pressure || !ComfortEventsConfig.Haptics)
        {
            stepSkip = "ComfortEvents.WindPressure or Haptics off";
            yield break;
        }
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var host = new GameObject("PeakXR_TestWind");
        host.SetActive(false);
        bodyRestore = () =>
        {
            if (host != null)
                UnityEngine.Object.Destroy(host);
        };
        try
        {
            var zone = host.AddComponent<WindChillZone>();
            var right = Flat(rig.Head.right);
            // Blowing from the right: the wind travels toward the left.
            Framework.GameMember.Field<WindChillZone, Vector3>("currentWindDirection")(zone) =
                -right;
            zone.windForce = 15f;
            zone.windPlayerFactor = 1f;
            zone.useIntensityCurve = false;
            zone.useRaycast = false;
            var push = Framework.GameMember.Method(
                typeof(WindChillZone),
                "AddWindForceToCharacter",
                new[] { typeof(Character), typeof(float) }
            );
            var args = new object[] { c, 1f };
            float at = ComfortEventsRuntime.PressureAt;
            float start = Now;
            while (Now - start < 1f)
            {
                push.Invoke(zone, args);
                yield return null;
            }
            float l = ComfortEventsRuntime.PressureLeft;
            float r = ComfortEventsRuntime.PressureRight;
            var note = $"pressure left {l:0.000} right {r:0.000}";
            if (ComfortEventsRuntime.PressureAt <= at)
                Fail("no pressure tap: " + note);
            else if (!(r > l))
                Fail("not stronger on the windward (right) hand: " + note);
            else if (Mathf.Max(l, r) > BodyCues.PressureMax + 1e-3f)
                Fail("too strong: " + note);
            else
                stepNote = note;
        }
        finally
        {
            UnityEngine.Object.Destroy(host);
        }
    }

    /// <summary>
    /// vr.212: a tornado's pull presses on the hand facing it, and crouching (the game pulls a quarter as hard,
    /// Tornado.cs:296-300) eases it. Deterministic: the game's own <c>Tornado.AttractCharacters</c> (:279) run on a
    /// tornado object that is never activated (no Start, lifetime, movement or RPCs), 10 m to the player's right,
    /// with a gentle force and a capture distance too small to grab.
    /// </summary>
    IEnumerator TornadoBraceStep()
    {
        if (!ComfortEventsConfig.Pressure || !ComfortEventsConfig.Haptics)
        {
            stepSkip = "ComfortEvents.WindPressure or Haptics off";
            yield break;
        }
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var host = new GameObject("PeakXR_TestTornado");
        host.SetActive(false);
        bodyRestore = () =>
        {
            ReleaseHead();
            if (host != null)
                UnityEngine.Object.Destroy(host);
        };
        try
        {
            var right = Flat(rig.Head.right);
            host.transform.position = c.Center + right * 10f;
            host.transform.localScale = Vector3.one;
            var tornado = host.AddComponent<Tornado>();
            tornado.force = 1f;
            tornado.range = 25f;
            tornado.captureDistance = 0.1f;
            tornado.inStrC = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            tornado.upStrC = AnimationCurve.Constant(0f, 1f, 0f);
            var attract = Framework.GameMember.Method(typeof(Tornado), "AttractCharacters");
            float standing = 0f,
                crouched = 0f,
                standLeft = 0f;
            yield return Pull(attract, tornado, 1f, (l, r) => (standLeft, standing) = (l, r));
            float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
            var head = VRInput.HeadPosition;
            ScriptHead(new Vector3(head.x, eye - 0.7f, head.z), VRInput.HeadRotation);
            yield return WaitFor(() => c.data.isCrouching, 1.5f);
            bool crouching = c.data.isCrouching;
            yield return Pull(attract, tornado, 1f, (_, r) => crouched = r);
            ReleaseHead();
            var note =
                $"pull standing left {standLeft:0.000} right {standing:0.000}, crouched right {crouched:0.000} (crouching {crouching})";
            if (!(standing > standLeft))
                Fail("not stronger on the tornado's (right) side: " + note);
            else if (!crouching || !(crouched < standing * 0.5f))
                Fail("crouching did not ease the pull: " + note);
            else
                stepNote = note;
        }
        finally
        {
            ReleaseHead();
            UnityEngine.Object.Destroy(host);
        }
    }

    /// <summary>Runs the tornado's pull every frame for <paramref name="seconds"/>; reports the last pressure tap
    /// per hand, or zeros when none went out.</summary>
    static IEnumerator Pull(
        System.Reflection.MethodInfo attract,
        Tornado tornado,
        float seconds,
        Action<float, float> taps
    )
    {
        float at = ComfortEventsRuntime.PressureAt;
        float start = Now;
        while (Now - start < seconds)
        {
            attract.Invoke(tornado, null);
            yield return null;
        }
        bool tapped = ComfortEventsRuntime.PressureAt > at;
        taps(
            tapped ? ComfortEventsRuntime.PressureLeft : 0f,
            tapped ? ComfortEventsRuntime.PressureRight : 0f
        );
    }

    /// <summary>
    /// vr.152: dropping onto a bounce pad springs: a contact spring on both hands. Deterministic: a 2 m pad on the Map
    /// layer carrying the game's own <c>CollisionModifier</c> (bounce knockback 20, as the shrooms ship), 2.5 m
    /// under the warped body. The game's collision path (CharacterMovement.OnCollision, :944) runs the bounce
    /// (CollisionModifier.cs:92-135). Removed after.
    /// </summary>
    /// <summary>
    /// The pad's spot: on the ground 3 m out, under a fall column clear of everything but that ground. Run 43 faced
    /// uphill and the pad sat inside the slope; run 45's column had something 1.5 m above the pad that caught the
    /// falling body. Headings from the view's forward round in 45 degree steps; none clear: straight ahead.
    /// </summary>
    static Vector3 BounceSpot(Character c, Vector3 forward, float stand)
    {
        Vector3 first = default;
        for (int i = 0; i < 8; i++)
        {
            float turn = (i + 1) / 2 * 45f * (i % 2 == 0 ? 1f : -1f);
            var spot = c.data.groundPos + Quaternion.Euler(0f, turn, 0f) * forward * 3f;
            float ground = GroundY(spot + Vector3.up * 2f);
            if (float.IsNaN(ground))
                continue;
            spot.y = ground;
            if (first == default)
                first = spot;
            float top = 0.4f + stand + 3f;
            if (
                !Physics.SphereCast(
                    spot + Vector3.up * top,
                    0.5f,
                    Vector3.down,
                    out var hit,
                    top,
                    HelperFunctions.AllPhysicalExceptCharacter,
                    QueryTriggerInteraction.Ignore
                )
                || hit.point.y < ground + 0.3f
            )
                return spot;
        }
        return first != default ? first : c.data.groundPos + forward * 3f;
    }

    /// <summary>The nearest collider under the body that is not the body itself, for a failure note (run 45: the
    /// body hung 2.4 m over the pad on something outside the terrain mask).</summary>
    static string Beneath(Character c)
    {
        string best = "nothing within 4 m";
        float near = float.MaxValue;
        foreach (
            var hit in Physics.RaycastAll(
                c.Center,
                Vector3.down,
                4f,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            if (hit.distance >= near || hit.collider.transform.root == c.transform.root)
                continue;
            near = hit.distance;
            best =
                $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) {hit.distance:0.00} m down";
        }
        return best;
    }

    IEnumerator BounceStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        float stand = Mathf.Clamp(c.Center.y - c.data.groundPos.y, 0.7f, 1.5f);
        var spot = BounceSpot(c, Flat(rig.Head.forward), stand);
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyRestore = () =>
        {
            if (pad != null)
                UnityEngine.Object.Destroy(pad);
        };
        pad.name = "PeakXR_TestBouncePad";
        pad.layer = LayerMask.NameToLayer("Map");
        pad.transform.SetPositionAndRotation(spot + Vector3.up * 0.2f, Quaternion.identity);
        pad.transform.localScale = new Vector3(2f, 0.4f, 2f);
        var mod = pad.AddComponent<CollisionModifier>();
        mod.knockback = 20f;
        int touches = 0;
        mod.onCollide += (who, _, _, _) =>
        {
            if (who == c)
                touches++;
        };
        Physics.SyncTransforms();
        int bounces = EnvironmentCues.Bounces;
        float since = CueMark();
        warpedForProp = true;
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            spot + Vector3.up * (0.4f + stand + 2.5f),
            false
        );
        yield return WaitFor(() => EnvironmentCues.Bounces > bounces, 6f);
        if (EnvironmentCues.Bounces == bounces)
        {
            Fail(
                F(
                    $"no bounce off the test pad: {touches} pad contacts, pad top {spot.y + 0.4f:0.00}, body at {c.Center.y:0.00}, ground under it {GroundY(c.Center):0.00}, under the body: {Beneath(c)}"
                )
            );
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
        if (stepOk)
            stepNote = $"spring {EnvironmentCues.LastBounce:0.00}: {cueNote}";
        UnityEngine.Object.Destroy(pad);
        yield return WaitFor(() => c.data.isGrounded, 4f);
    }

    /// <summary>
    /// vr.213: touching lava stings both hands. Deterministic: a thin slab carrying the game's own <c>Lava</c>
    /// component (no collider; Lava's touch is its renderer's bounds, Lava.cs:29-33, :68-92) at the body's height,
    /// so its FixedUpdate runs the real touch: the lift, then HitPlayer once a second (:89-110). Removed at the
    /// first sting. God mode keeps the statuses off.
    /// </summary>
    IEnumerator LavaStingStep()
    {
        var c = Character.localCharacter;
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyRestore = () =>
        {
            if (slab != null)
                UnityEngine.Object.Destroy(slab);
        };
        UnityEngine.Object.Destroy(slab.GetComponent<Collider>());
        slab.name = "PeakXR_TestLava";
        slab.transform.SetPositionAndRotation(c.Center, Quaternion.identity);
        slab.transform.localScale = new Vector3(3f, 0.02f, 3f);
        int stings = EnvironmentCues.Stings;
        float since = CueMark();
        slab.AddComponent<Lava>();
        yield return WaitFor(() => EnvironmentCues.Stings > stings, 2f);
        UnityEngine.Object.Destroy(slab);
        if (EnvironmentCues.Stings == stings)
        {
            Fail("the lava's touch never burned");
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Danger, since, 0.5f);
        if (stepOk)
            stepNote = cueNote;
        yield return WaitFor(() => c.data.isGrounded, 4f);
    }

    /// <summary>vr.214: a held Balloon used with the trigger (its Action_Balloon ties it, CharacterBalloons.TieNewBalloon,
    /// CharacterBalloons.cs:99) gives a lift tick on the hand holding it and none on the other; the tied balloon then
    /// pops on its own timer (TiedBalloon.popTime, TiedBalloon.cs:83), a snap on both hands.</summary>
    /// <summary>Longest vr.214 waits for the tied balloon's own pop timer (s).</summary>
    const float PopWait = 12f;

    IEnumerator BalloonTieStep()
    {
        var c = Character.localCharacter;
        var balloon = FindItem(new List<Item>(SandboxActions.Items), "Balloon");
        yield return GiveAndHold(balloon);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool hand = LocalHold.ItemInLeftHand;
        yield return GripTo(hand, 1f, 0.1f);
        float since = CueMark();
        Script(hand).TriggerTouch = true;
        Script(hand).Trigger = 1f;
        TiedBalloon tied = null;
        float end = Now + GameTiming.Budget(GameTiming.UseSeconds(item), 0.5f);
        while (
            Now < end && (tied = UnityEngine.Object.FindFirstObjectByType<TiedBalloon>()) == null
        )
            yield return null;
        Script(hand).Trigger = 0f;
        if (tied == null)
        {
            Fail($"using the held balloon tied nothing (in the {(hand ? "left" : "right")} hand)");
            yield break;
        }
        yield return ExpectFeel(hand, CueFeel.Contact, since, 1f);
        if (!stepOk)
            yield break;
        string tie = cueNote;
        // The tick belongs to the holding hand only (with the default right hand, a tick that ignores the hold
        // still lands here; the note names the hand).
        if (CueLog.Capture.Felt(!hand, CueFeel.Contact, since, out _))
        {
            Fail($"the tie ticked the empty hand too: {tie}");
            yield break;
        }
        int pops = EnvironmentCues.Pops;
        since = CueMark();
        // Its own timer when that is short; the prefab's can outlast the step (run 48 timed out at 65 s), so past
        // PopWait the game's own Pop() (what the timer calls, TiedBalloon.cs:83-85) stands in for it.
        float wait = Mathf.Min(GameTiming.Budget(tied.popTime), PopWait);
        yield return WaitFor(() => tied == null || EnvironmentCues.Pops > pops, wait);
        string popWay = "on its own timer";
        if (EnvironmentCues.Pops == pops && tied != null)
        {
            popWay = F($"shortcut: Pop() after {wait:0} s, its timer is {tied.popTime:0} s");
            since = CueMark();
            tied.Pop();
            yield return WaitFor(() => EnvironmentCues.Pops > pops, 1f);
        }
        if (EnvironmentCues.Pops == pops)
        {
            Fail($"the tied balloon popped with no pop cue ({popWay})");
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Contact, since, 0.5f);
        if (stepOk)
            stepNote =
                $"tie on the {(hand ? "left" : "right")} hand: {tie}; pop {popWay}: {cueNote}";
        yield return WaitFor(() => c.data.isGrounded, 3f);
    }

    /// <summary>vr.215: holding the AUTOPARACHUTE item (tag Parachute, CharacterData.UpdateHasParachute, :736), a drop
    /// from 40 m opens it the game's way after 1.5 s of falling (Character.cs:1122-1128, SetParachute): the opening
    /// tugs both hands, and the body lands under it (the note gives the fall speed; the game closes it on landing,
    /// :1112). shortcut: the game's warp lifts the
    /// body instead of a climb to a ledge (as vr.145).</summary>
    IEnumerator ParachuteStep()
    {
        var c = Character.localCharacter;
        if (Physics.Raycast(c.Center, Vector3.up, 41f, HelperFunctions.terrainMapMask))
        {
            stepSkip = "no open sky above the spawn";
            yield break;
        }
        var chute = FindItem(new List<Item>(SandboxActions.Items), "Parachute");
        yield return GiveAndHold(chute);
        if (!stepOk)
            yield break;
        yield return WaitFor(() => c.data.hasParachute, 1f);
        if (!c.data.hasParachute)
        {
            Fail($"holding {HeldItem()?.name} does not count as a parachute");
            yield break;
        }
        int tugs = EnvironmentCues.Tugs;
        float since = CueMark();
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, c.Center + Vector3.up * 40f, false);
        yield return WaitFor(() => !c.warping && !c.data.isGrounded, 2f);
        float dropAt = Now;
        yield return WaitFor(() => EnvironmentCues.Tugs > tugs || c.data.isGrounded, 4f);
        float openedAfter = Now - dropAt;
        if (EnvironmentCues.Tugs == tugs)
        {
            Fail(
                F(
                    $"the parachute never opened (grounded {c.data.isGrounded}, fell {openedAfter:0.0} s, sinceGrounded {c.data.sinceGrounded:0.0})"
                )
            );
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Contact, since, CueMark() - since + 1f);
        if (!stepOk)
            yield break;
        string felt = cueNote;
        float fallSpeed = 0f;
        float end = Now + 20f;
        while (Now < end && !c.data.isGrounded)
        {
            fallSpeed = Mathf.Max(fallSpeed, -c.data.avarageVelocity.y);
            yield return null;
        }
        var note = F(
            $"opened {openedAfter:0.0} s into the fall, then fell at most {fallSpeed:0.0} m/s, landed {c.data.isGrounded}: {felt}"
        );
        if (!c.data.isGrounded)
            Fail(note);
        else
            stepNote = note;
        yield return WaitFor(() => c.data.currentRagdollControll > 0.9f, 5f);
    }

    /// <summary>vr.216: a sugar rush that runs out is a crash: Affliction_InfiniteStamina ends on its own timer and its
    /// OnRemoved calls GUIManager.EndSugarRush (Affliction_InfiniteStamina.cs:55-59), the soft falling cancel cue on
    /// both hands, and none while the rush lasts. shortcut: the affliction is added as Action_ApplyInfiniteStamina
    /// (:11) adds it when a Follypop is eaten, not by eating one (vr.12 covers the eat), and for 1.5 s instead of
    /// the item's 16 s.</summary>
    IEnumerator SugarCrashStep()
    {
        var c = Character.localCharacter;
        if (GUIManager.instance == null)
        {
            stepSkip = "no GUIManager";
            yield break;
        }
        // God mode's tick clears every affliction and holds infinite stamina (SandboxActions.GodTick): off for the
        // step, the runner restores it.
        SandboxActions.SetGodMode(false);
        yield return null;
        float since = CueMark();
        c.refs.afflictions.AddAffliction(new Peak.Afflictions.Affliction_InfiniteStamina(1.5f));
        yield return WaitFor(() => c.infiniteStam, 0.5f);
        bool rush = c.infiniteStam;
        yield return Wait(0.8f);
        bool early =
            CueLog.Capture.Felt(true, CueFeel.Cancel, since, out _)
            || CueLog.Capture.Felt(false, CueFeel.Cancel, since, out _);
        float before = CueMark();
        yield return WaitFor(() => !c.infiniteStam, GameTiming.Budget(1.5f));
        bool ended = !c.infiniteStam;
        var state = $"rush started {rush}, cancel during it {early}, rush ended {ended}";
        if (!rush || early || !ended)
        {
            Fail(state);
            yield break;
        }
        yield return ExpectFeelBoth(CueFeel.Cancel, before, CueMark() - before + 0.5f);
        if (stepOk)
            stepNote = $"{state}: {cueNote}";
    }

    /// <summary>vr.217: low gravity (Affliction_LowGravity, as a super jump adds it) warns 1.5 s before it ends;
    /// progress ticks fill toward the moment gravity returns.</summary>
    IEnumerator FloatEndStep()
    {
        var c = Character.localCharacter;
        // God mode's tick clears every affliction (SandboxActions.GodTick): off for the step, the runner restores it.
        SandboxActions.SetGodMode(false);
        float since = CueMark();
        c.refs.afflictions.AddAffliction(new Peak.Afflictions.Affliction_LowGravity(1, 2.5f));
        yield return ExpectFeelBoth(CueFeel.Progress, since, 4f);
        if (stepOk)
            stepNote = cueNote;
        yield return Wait(1.5f);
        yield return WaitFor(() => c.data.isGrounded, 4f);
    }

    /// <summary>
    /// vr.218: standing on a breakable bridge whose countdown started (BreakableBridge.isBreaking, as its owner sets
    /// it when too many stand on it, :186-189), danger pulses build at the feet. Offline the local player owns it.
    /// A bridge of the level when one is active; else (run 40: none in this Tropics) one built the game's way: a
    /// vine from the vine shooter's prefab (VineShooter.cs:61, ForceBuildVine_RPC) carrying BreakableBridge, whose
    /// AddCollisionModifiers (:160) and Awake (:80, hooks its planks) run once the vine has colliders. The body lands
    /// on it; the countdown stops again before the bridge shakes. Removed after.
    /// </summary>
    IEnumerator BridgeStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        BreakableBridge bridge = null;
        GameObject built = null;
        bodyRestore = () =>
        {
            if (built != null)
                PhotonNetwork.Destroy(built);
        };
        float best = float.MaxValue;
        foreach (
            var b in UnityEngine.Object.FindObjectsByType<BreakableBridge>(FindObjectsSortMode.None)
        )
        {
            float d = Vector3.Distance(b.transform.position, c.Center);
            if (b.isActiveAndEnabled && !b.isShaking && b.photonView.IsMine && d < best)
            {
                best = d;
                bridge = b;
            }
        }
        string what = bridge != null ? $"{bridge.name} {best:0} m away" : null;
        if (bridge == null)
        {
            var shooter = FindItemComponent<VineShooter>();
            if (shooter == null || shooter.vinePrefab == null)
            {
                Fail("no breakable bridge here and no vine shooter prefab to build one");
                yield break;
            }
            var forward = Flat(rig.Head.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            var centre = c.data.groundPos + forward * 3f + Vector3.up * 1f;
            var from = centre - right * 4f;
            var to = centre + right * 4f;
            const float hang = 0.3f;
            JungleVine.CheckVinePath(from, to, hang, out var mid);
            built = PhotonNetwork.Instantiate(
                shooter.vinePrefab.name,
                from,
                Quaternion.identity,
                0
            );
            var vine = built.GetComponent<JungleVine>();
            // The shooter's vine is a climbing vine: trigger capsules (JungleVine.cs:284-290). A bridge is the same
            // vine built with solid boxes (:293, sized boxShape.x x length/meshLength wide, boxShape.y deep),
            // which run 41's search found missing ("no plank"). About 1.2 m by 1.2 m here, walkable.
            vine.colliderType = JungleVine.ColliderType.Box;
            vine.boxShape = new Vector2(1.2f * vine.meshLength / Vector3.Distance(from, to), 1.2f);
            vine.photonView.RPC("ForceBuildVine_RPC", RpcTarget.All, from, to, hang, mid);
            yield return Wait(0.2f);
            bridge = built.AddComponent<BreakableBridge>();
            bridge.AddCollisionModifiers();
            Framework.GameMember.Method(typeof(BreakableBridge), "Awake").Invoke(bridge, null);
            Physics.SyncTransforms();
            what = "a vine bridge built for the step";
        }
        if (!Plank(bridge, out var plank))
        {
            Fail($"no plank to stand on found on {what}");
            yield break;
        }
        float stand = Mathf.Clamp(c.Center.y - c.data.groundPos.y, 0.7f, 1.5f);
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, plank + Vector3.up * stand, false);
        yield return WaitFor(() => EnvironmentCues.Bridge == bridge, 5f);
        if (EnvironmentCues.Bridge != bridge)
        {
            Fail($"the body never touched {what} after landing at {plank}");
            yield break;
        }
        float since = CueMark();
        EnvironmentCues.BridgeTimer(bridge) = 1000f;
        EnvironmentCues.BridgeBreaking(bridge) = true;
        yield return ExpectFeelBoth(CueFeel.Danger, since, 1.5f);
        EnvironmentCues.BridgeBreaking(bridge) = false;
        if (stepOk)
            stepNote = $"{what}: {cueNote}";
        if (built != null)
            PhotonNetwork.Destroy(built);
        built = null;
        yield return WaitFor(() => c.data.isGrounded, 4f);
    }

    /// <summary>The top of the bridge collider nearest its middle: a raycast down onto that collider.</summary>
    static bool Plank(BreakableBridge bridge, out Vector3 top)
    {
        top = default;
        float best = float.MaxValue;
        var middle = Vector3.zero;
        int n = 0;
        var colliders = bridge.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col.isTrigger)
                continue;
            middle += col.bounds.center;
            n++;
        }
        if (n == 0)
            return false;
        middle /= n;
        foreach (var col in colliders)
        {
            if (col.isTrigger)
                continue;
            var b = col.bounds;
            var ray = new Ray(b.center + Vector3.up * (b.extents.y + 2f), Vector3.down);
            if (!col.Raycast(ray, out var hit, b.size.y + 4f))
                continue;
            float d = Vector3.Distance(b.center, middle);
            if (d < best)
            {
                best = d;
                top = hit.point;
            }
        }
        return best < float.MaxValue;
    }

    /// <summary>
    /// vr.219: passed out, the detached view follows the body without the player. The body is moved 3 m (the game's
    /// warp, as a fall or a throw would move it) and the view must ease after it: the comfort vignette closes while
    /// the view moves (LocomotionDriver reads the rig origin while detached), its acceleration stays under the
    /// chase limit (<see cref="ViewMotionLimit"/>) and its speed under the cap; jumps over 1.5 m in a frame (a
    /// re-place behind a fade) are left out. With Camera.ThirdPersonFollow 0 the view stays still, which passes.
    /// </summary>
    IEnumerator SpectateComfortStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ComfortConfig.Vignette || ComfortConfig.VignetteStrength <= 0.01f)
        {
            stepSkip = "Comfort.Vignette off or at strength 0";
            yield break;
        }
        bodyRestore = () =>
        {
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
        yield return Wait(1.5f);
        bool chase = RigDriver.ViewDetached;
        var limit = new ViewMotionLimit();
        warpedForProp = true;
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            c.Center + Vector3.up * 3f + Flat(rig.Head.right) * 2f,
            false
        );
        var last = rig.Origin.position;
        var first = last;
        var path = new System.Collections.Generic.List<Vector3>();
        var lastV = Vector3.zero;
        bool haveV = false;
        float maxAccel = 0f,
            maxSpeed = 0f,
            moving = 0f,
            vignetted = 0f,
            peak = 0f;
        float start = Now;
        int seen = Time.frameCount;
        int traced = 0;
        while (Now - start < 4f)
        {
            yield return null;
            // The view step RigDriver made last LateUpdate, with its own frame time (sampling the origin here would
            // pair a move with the next frame's dt: run 49's 12.5 m/s^2 with the limiter right).
            var step = RigDriver.LastViewStep;
            if (step.Frame == seen)
                continue;
            seen = step.Frame;
            float dt = step.Dt;
            var d = step.Origin;
            last += d;
            path.Add(last);
            if (step.Snap || dt <= 0f || d.magnitude > ViewMotionLimit.JumpDistance)
            {
                haveV = false;
                continue;
            }
            var v = d / dt;
            if (haveV)
            {
                float a = (v - lastV).magnitude / dt;
                maxAccel = Mathf.Max(maxAccel, a);
                // Trace the frames that jolt: the follow's own move against the origin's, so another writer shows.
                if (a > limit.MaxAcceleration * 1.5f && traced++ < 8)
                    Plugin.Log.LogInfo(
                        F(
                            $"[selftest] vr.219 jolt frame {step.Frame} dt {dt * 1000f:0.0} ms accel {a:0.0} m/s^2 origin v {v.magnitude:0.00} chase v {step.ChaseVelocity.magnitude:0.00} anchor step {step.Anchor.magnitude:0.000} origin step {d.magnitude:0.000} other writers {(d - step.Anchor).magnitude:0.000} m chasing {step.Chasing}"
                        )
                    );
            }
            lastV = v;
            haveV = true;
            maxSpeed = Mathf.Max(maxSpeed, v.magnitude);
            peak = Mathf.Max(peak, LocomotionDriver.Intensity);
            if (v.magnitude > 0.5f)
            {
                moving += dt;
                // Half what the speed curve asks at this speed (vr.17's rule): a weak strength closes little.
                float want =
                    0.5f
                    * ComfortConfig.VignetteStrength
                    * ComfortVignette.Drive(
                        LocomotionDriver.ViewSpeed,
                        ComfortConfig.VignetteStartSpeed,
                        ComfortConfig.VignetteFullSpeed
                    );
                if (LocomotionDriver.Intensity > want)
                    vignetted += dt;
            }
        }
        // Overshoot: how far the view went past where it ended, along its travel (the chase brakes for its goal).
        var travel = last - first;
        float overshoot = 0f;
        if (travel.magnitude > 0.1f)
        {
            var dir = travel.normalized;
            foreach (var at in path)
                overshoot = Mathf.Max(overshoot, Vector3.Dot(at - first, dir) - travel.magnitude);
        }
        SandboxActions.Revive();
        yield return WaitFor(() => !c.data.passedOut && !c.data.fullyPassedOut, 6f);
        var note =
            $"chase view {chase}, moving {moving:0.00} s (vignette on {vignetted:0.00} s, peak {peak:0.00}), "
            + $"top speed {maxSpeed:0.0} m/s, top acceleration {maxAccel:0.0} m/s^2, overshoot {overshoot:0.00} m";
        // Frame-time jitter roughens a finite difference: half again over the limit is the bar.
        if (moving > 0.2f && vignetted < moving * 0.8f)
            Fail("the view moved without the vignette: " + note);
        else if (chase && maxAccel > limit.MaxAcceleration * 1.5f)
            Fail("the view accelerated past the chase limit: " + note);
        else if (chase && maxSpeed > limit.MaxSpeed + 0.5f)
            Fail("the view moved faster than the chase cap: " + note);
        else if (chase && overshoot > 0.15f)
            Fail("the view overshot the body it follows: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.230: stooping 0.47 m with an apple in the right hand, its controller 0.35 m above the floor ahead (a fuse,
    /// a fire, a pick-up): the avatar crouches (<see cref="PhysicalCrouch"/> at a fifth of standing height), its ragdoll
    /// hand stays within 0.15 m of the controller and the apple stays welded to it (vr.87/vr.88 had a torch 1.2 m off
    /// and the weld let go). The view stays at the real head (CrouchLift).
    /// </summary>
    IEnumerator StoopReachStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var driver = AvatarPoseDriver.Of(c);
        if (!BodyGestures.CrouchOn || rig.SeatedMode || driver == null)
        {
            stepSkip = "Locomotion.PhysicalCrouch off, seated mode or no pose driver";
            yield break;
        }
        Item apple = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it != null && it.name.Contains(AppleFragment))
                apple = it;
        }
        if (apple == null || !SandboxActions.GiveItem(apple, false))
        {
            Fail("no apple to hold");
            yield break;
        }
        bodyRestore = () => ReleaseHead();
        yield return WaitFor(() => c.data.currentItem != null, 3f);
        if (c.data.currentItem == null)
        {
            Fail("the apple never reached the hand");
            yield break;
        }
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float floor = XRRuntimeInfo.FloorTracking
            ? 0f
            : eye - HeightCalibration.DefaultStandingEyeHeight;
        HeadFrame(out var head, out var forward, out var right);
        var rot = VRInput.HeadRotation;
        var low = new Vector3(head.x, eye - 0.47f, head.z) + forward * 0.15f;
        var handFrom = Script(false).Position;
        var handTo = new Vector3(head.x, floor + 0.35f, head.z) + forward * 0.45f + right * 0.2f;
        float start = Now;
        while (Now - start < 0.8f)
        {
            float t = (Now - start) / 0.8f;
            ScriptHead(Vector3.Lerp(head, low, t), rot);
            SetHand(false, Vector3.Lerp(handFrom, handTo, t));
            yield return null;
        }
        yield return Wait(1.2f);
        float worst = 0f;
        bool released = false,
            crouched = true;
        start = Now;
        var hand = c.refs.ragdoll.partDict[BodypartType.Hand_R].Rig;
        while (Now - start < 0.6f)
        {
            if (driver.HandTarget(false, out var target))
                worst = Mathf.Max(worst, Vector3.Distance(hand.position, target));
            released |= driver.HeldReleased;
            crouched &= c.data.isCrouching;
            yield return null;
        }
        start = Now;
        while (Now - start < 0.6f)
        {
            float t = (Now - start) / 0.6f;
            ScriptHead(Vector3.Lerp(low, head, t), rot);
            SetHand(false, Vector3.Lerp(handTo, handFrom, t));
            yield return null;
        }
        ReleaseHead();
        var note = F(
            $"stooped 0.47 m, crouched {crouched}, hand {worst:0.00} m from the controller, weld released {released}"
        );
        if (!crouched)
            Fail("the avatar did not crouch for the stoop: " + note);
        else if (worst > 0.15f)
            Fail("the avatar hand did not reach the low controller: " + note);
        else if (released)
            Fail("the apple left its hand weld: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.239: seated mode with the head 0.45 m below standing (the seated height recalibrates), the right controller
    /// 0.1 m above the real floor, 0.4 m ahead: <see cref="SeatedReach"/> puts the rig hand within
    /// <c>Interaction.GrabRadius</c> of an Apple on the ground under it, and never below the virtual floor.
    /// </summary>
    IEnumerator SeatedReachStep()
    {
        var rig = VRRig.Instance;
        var setting = ComfortConfig.SeatedSetting;
        if (setting == null || rig.SeatedMode || rig.SeatedReachGain <= 1f)
        {
            stepSkip =
                "player.locomotion not installed, run already seated, or Comfort.SeatedReachGain 1";
            yield break;
        }
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float realFloor = XRRuntimeInfo.FloorTracking
            ? 0f
            : eye - HeightCalibration.DefaultStandingEyeHeight;
        HeadFrame(out var head, out var forward, out _);
        var rot = VRInput.HeadRotation;
        bodyRestore = () =>
        {
            setting.Value = false;
            ReleaseHead();
            Despawn();
        };
        ScriptHead(new Vector3(head.x, eye - 0.45f, head.z), rot);
        yield return null;
        float before = rig.HeightOffset;
        setting.Value = true;
        yield return WaitFor(() => rig.HeightOffset > before + 0.3f, 3f);
        float seatedEye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float virtualFloor = seatedEye - HeightCalibration.DefaultStandingEyeHeight;
        var controller = new Vector3(head.x, realFloor + 0.1f, head.z) + forward * 0.4f;
        SetHand(false, controller);
        yield return Wait(0.3f);
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        var top = rig.Origin.TransformPoint(new Vector3(controller.x, seatedEye, controller.z));
        if (
            apple == null
            || !Physics.Raycast(
                top,
                Vector3.down,
                out var ground,
                6f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            Fail("no apple or no ground under the hand");
            yield break;
        }
        yield return SpawnItem(apple, ground.point + Vector3.up * 0.05f);
        if (!stepOk)
            yield break;
        var item = spawned[spawned.Count - 1].GetComponent<Item>();
        if (item.rig != null)
            item.rig.isKinematic = true;
        yield return Wait(0.3f);
        var hand = rig.RightHand.position;
        float reach = float.MaxValue;
        foreach (var col in item.GetComponentsInChildren<Collider>())
        {
            if (col.enabled && !col.isTrigger)
                reach = Mathf.Min(reach, Vector3.Distance(hand, col.ClosestPoint(hand)));
        }
        float local = rig.RightHand.localPosition.y;
        float grab = Interact.InteractConfig.Instance.GrabRadius;
        var note = F(
            $"seated eye {seatedEye:0.00} m, controller {controller.y - realFloor:0.00} m over the real floor, hand {local - virtualFloor:0.00} m over the virtual floor, {reach:0.00} m from the apple (grab radius {grab:0.00})"
        );
        if (local < virtualFloor - 0.01f)
            Fail("the hand went below the virtual floor: " + note);
        else if (reach > grab)
            Fail("a hand at the real floor cannot reach a ground item: " + note);
        else
            stepNote = note;
    }
}
