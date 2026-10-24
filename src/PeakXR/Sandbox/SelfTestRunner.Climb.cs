using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact.Climb;
using PeakXR.Locomotion;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using PeakXR.XR;
using Photon.Pun;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Body-gesture and climb-hand steps of the scripted-hands run. player.locomotion <see cref="BodyGestures"/>: a
/// scripted head dip must crouch the character, and alternating arm swings with the stick forward must sprint.
/// interact.physical <see cref="ClimbHands"/>: a rope shot into the air, a placed piton and (after the Tropics
/// jump) a jungle vine are gripped, pulled and let go with scripted hands. A step whose object cannot be set up is
/// SKIP with the reason.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Undoes what a body step changed (head dip, the user's ArmSwingSprint value). The step runner drops a
    /// timed-out or failed step without disposing it, so a finally block would not run; this does, after the step.</summary>
    Action bodyRestore;

    IEnumerator BodySteps()
    {
        yield return Restoring("vr.40-physical-crouch", 15f, CrouchStep());
        yield return Restoring("vr.41-arm-swing-sprint", 20f, ArmSwingStep());
        yield return Restoring("vr.45-lean", 60f, LeanStep());
        yield return HandStep("vr.42-rope-hand-over-hand", 40f, RopeStep());
        yield return WarpBack();
        yield return HandStep("vr.44-piton-hang", 30f, PitonStep());
        yield return HandStep("vr.44c-pickaxe-hang", 30f, PitonStep(pickaxe: true));
        yield return Restoring("vr.46-lunge", 40f, LungeStep());
        yield return ClimbSteps();
    }

    /// <summary>One step per climb, rope, handle and grab mechanic (interact.physical). Each asserts the motion's
    /// outcome and its cue; the rope and prop steps return to the spawn after themselves.</summary>
    IEnumerator ClimbSteps()
    {
        yield return Restoring("vr.46b-lunge-too-soon", 30f, LungeTooSoonStep());
        yield return Restoring("vr.46c-reach-no-lunge", 30f, ReachNoLungeStep());
        yield return Restoring("vr.47-climb-deny-reach", 30f, ClimbDenyReachStep());
        yield return Restoring("vr.47b-free-hand-pocket", 25f, FreeHandPocketStep());
        yield return Restoring("vr.47d-ping-at-item", 25f, PingAtItemStep());
        yield return Restoring("vr.47e-arrive-in-closed-grip", 30f, ArriveInClosedGripStep());
        yield return Restoring("vr.238-grip-toggle-item", 25f, GripToggleItemStep());
        yield return Restoring("vr.47f-grab-on-slope", 30f, GrabOnSlopeStep());
        yield return Restoring("vr.61b-two-hands-optional", 25f, TwoHandsOptionalStep());
        yield return Restoring("vr.48-climb-deny-steep", 30f, ClimbDenySteepStep());
        yield return Restoring("vr.48b-surface-texture", 30f, SurfaceTextureStep());
        yield return Restoring("vr.49-condor", 30f, CondorStep());
        yield return Restoring("vr.50-climb-hover", 30f, ClimbHoverStep());
        yield return Restoring("vr.51-air-grip", 30f, AirGripStep());
        yield return Restoring("vr.51b-falling-catch", 30f, FallingCatchStep());
        yield return Restoring("vr.52-climb-empty", 40f, ClimbEmptyStep());
        yield return Restoring("vr.53-forced-detach", 30f, ForcedDetachStep());
        yield return Restoring("vr.237-grip-toggle-climb", 40f, GripToggleClimbStep());
        yield return Restoring("vr.54-mantle", 45f, MantleStep());
        yield return Restoring("vr.44b-handle-rest", 30f, HandleRestStep());
        yield return Restoring("vr.44d-hammer-spike", 40f, HammerSpikeStep());
        yield return Restoring("vr.44e-item-hand-no-hang", 35f, ItemHandNoHangStep());
        yield return Restoring("vr.55-handle-occupied", 30f, HandleOccupiedStep());
        yield return Restoring("vr.56-fragile-piton", 40f, FragilePitonStep());
        yield return Restoring("vr.42b-rope-pump", 45f, RopePumpStep());
        yield return WarpBack();
        yield return Restoring("vr.42d-pump-not-mine", 40f, RopePumpNotMineStep());
        yield return WarpBack();
        yield return Restoring("vr.42c-rope-end-stop", 40f, RopeEndStep());
        yield return WarpBack();
        yield return Restoring("vr.57-rope-deny-stamina", 40f, RopeDenyStaminaStep());
        yield return WarpBack();
        yield return Restoring("vr.58-rope-deny-unanchored", 40f, RopeUnanchoredStep());
        yield return WarpBack();
        yield return Restoring("vr.58b-breakable-anchor", 45f, BreakableAnchorStep());
        yield return WarpBack();
    }

    /// <summary>
    /// Offline: <see cref="SandboxActions.PlaceWall"/> (7 m of face, far above <see cref="LungeHeadroom"/>) moved so
    /// its face stands 0.6 m ahead of the settled player, its face and the ground there. Removed after the step.
    /// Placed 3 m out, the warp to its front left the body 1.1 m short of the spot (run 32: warped to
    /// (24.70, 1.84, -372.05), next seen at (23.71, 2.65, -372.51)) and the grip 1.55 m from the eye, past the
    /// hand-cast grab's 1.25 m.
    /// </summary>
    bool LungeWall(Character c, out RaycastHit wall, out Vector3 stand)
    {
        wall = default;
        stand = default;
        var placed = SandboxActions.PlaceWall();
        if (placed == null)
            return false;
        OnRestore(() =>
        {
            if (placed != null)
                UnityEngine.Object.Destroy(placed);
        });
        // Half the wall's 0.5 m thickness behind the face.
        var ahead = Flat(placed.transform.forward);
        var at = c.Center + ahead * (0.6f + 0.25f);
        placed.transform.position = new Vector3(at.x, Feet(c).y + 3f, at.z);
        Physics.SyncTransforms();
        var mask = HelperFunctions.terrainMapMask;
        if (
            !Physics.Raycast(
                c.Center,
                Flat(placed.transform.position - c.Center),
                out wall,
                8f,
                mask,
                QueryTriggerInteraction.Ignore
            )
            || wall.collider.gameObject != placed
        )
            return false;
        var front = wall.point + Flat(wall.normal) * 0.6f;
        if (
            !Physics.Raycast(
                front + Vector3.up * 2f,
                Vector3.down,
                out var ground,
                6f,
                mask,
                QueryTriggerInteraction.Ignore
            )
        )
            return false;
        stand = ground.point + Vector3.up * 0.1f;
        return true;
    }

    /// <summary>A warp target for standing on <paramref name="stand"/> (ground + 0.1 m): the game's warp moves the
    /// body's centre there (Character.cs:1904-1910), so it is raised by the centre's height over the feet. A centre
    /// sunk to the ground pops back up and lands off the spot (0.3 m at spawn, 1.1 m beside the test wall in run 32).</summary>
    static Vector3 CentreOver(Character c, Vector3 stand) =>
        stand + Vector3.up * Mathf.Clamp(c.Center.y - Feet(c).y - 0.1f, 0f, 1.2f);

    /// <summary>Face above the grip vr.46 needs: the 0.4 m pull, the ~1.4 m jump and the catch above the body. Run 29's
    /// wall (1.2 m proven) ran out under the jump, the game ended the climb and there was nothing left to catch.</summary>
    const float LungeHeadroom = 2.8f;

    /// <summary>Least rise from the throw to the catch that proves the lunge moved the body (m), not only the jump
    /// clock: about half of what run 43 measured (0.66 m in 0.24 s).</summary>
    const float LungeMinRise = 0.3f;

    /// <summary>
    /// vr.46: the climb lunge (interact.physical <c>TryLunge</c>, <see cref="ClimbInput.Lunge"/>). Climbing on one hand,
    /// that hand thrown upward faster than <c>Interaction.LungeSpeed</c> as it lets go queues the sprint press the
    /// game's climb jump reads (CharacterClimbing.cs:122); <c>RPCA_ClimbJump</c> (:238-240) resets
    /// <c>sinceClimbJump</c>. The hand then catches the wall inside the lunge grace and the climb never ended. Out of
    /// stamina (god mode off; the game refuses the jump from <c>outOfStaminaFor</c> 0.5 s, :122) the same throw gets
    /// the NoStamina refusal cue and no jump.
    /// </summary>
    IEnumerator LungeStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var cfg = Interact.InteractConfig.Instance;
        if (!cfg.ArmLunge)
        {
            stepSkip = "Interaction.ArmLunge off";
            yield break;
        }
        // The sandbox's 7 m test wall offline, so the catch is always tested; online, a tall enough map wall.
        if (
            !LungeWall(c, out var wall, out var stand)
            && !FindWall(c, out wall, out stand, LungeHeadroom)
        )
        {
            stepSkip = NoWall;
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, CentreOver(c, stand), false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(wall.normal), Vector3.up));
        yield return Wait(0.6f);
        bool left = VRInput.LeftHanded;
        // A lunge starts from a hold up by the head: the arm then flings up and lets go above the shoulder
        // (LungeIntent). Run 58 held at the chest, and the 0.25 s of rising grip before the release pushed the body
        // down the wall until the climb broke.
        if (!WallInFront(c, wall, LungeHoldUp, out var hit))
        {
            Fail($"wall not in front after the warp (wall {wall.point}, body {c.Center})");
            yield return LetGoAll(c);
            yield break;
        }
        yield return MoveTo(left, () => hit.point + hit.normal * 0.04f, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield return LetGoAll(c);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        if (!c.data.isClimbing)
        {
            Fail($"climb did not start ({PeakXR.Interact.PhysicalInteraction.LastGripNote})");
            yield return LetGoAll(c);
            yield break;
        }
        // Pull up first (as vr.15), so the throw's own pull-down cannot set the feet back on the ground.
        yield return PullDown(left, 0.4f, 0.6f);
        // The game takes a climb jump at most once a second (CharacterClimbing.cs:122).
        yield return WaitFor(() => c.data.sinceClimbJump > 1.1f, 2f);
        yield return Wait(0.3f);
        // The jump carries the body about 1.4 m up the wall (playerSlide 8 decays to 0 in ~0.4 s, CharacterClimbing.cs:242,
        // :343-344) and the catch reaches 0.3 m above the centre. FindWall only proves 1.2 m of face, so on a lower wall
        // the requested position leaves the face, the game ends the climb (:266-271) and the body tops out: nothing to catch.
        bool roomy =
            Physics.Raycast(
                c.Center + Vector3.up * 2f,
                -Flat(wall.normal),
                out var top,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            ) && ClimbRules.GrabAngleOk(Vector3.Angle(top.normal, Vector3.up));

        // The throw: 0.15 s straight up, let go 70 % of the way at twice the lunge speed (at least 4 m/s).
        float speed = Mathf.Max(2f * cfg.LungeSpeed, 4f);
        var from = Script(left).Position;
        float jumpBefore = c.data.sinceClimbJump;
        float whooshMark = CueMark();
        yield return FlingRelease(left, from, speed);
        float released = Now;
        // The rise counts from the release: the gripped 0.25 s fling pushes the body down the wall first (run 57: from
        // the fling's start the same jump read 0.23 m against 0.58 m for run 56's shorter swing).
        float y0 = flingReleaseY;
        yield return WaitFor(() => c.data.sinceClimbJump < Mathf.Min(jumpBefore, 0.5f), 0.4f);
        bool jumped = c.data.sinceClimbJump < Mathf.Min(jumpBefore, 0.5f);
        float jumpAfter = Now - released;
        if (!roomy)
        {
            if (!jumped)
                Fail(
                    $"the throw did not make the game's climb jump ({speed:0.0} m/s throw, lunge from {cfg.LungeSpeed:0.0})"
                );
            else
                stepSkip =
                    $"climb jump True {jumpAfter:0.00} s after the release, catch not tested: no climbable face 2 m above the body, the lunge tops out this wall";
            yield return LetGoAll(c);
            yield break;
        }
        // Catch: the same hand back on the wall in front of the body, inside the grace.
        // The body rises with the jump: the catch point is the wall in front of it, read every frame.
        var catchAt = hit.point + hit.normal * 0.04f;
        yield return MoveTo(
            left,
            () =>
                catchAt = WallInFront(c, wall, 0.3f, out var again)
                    ? again.point + again.normal * 0.04f
                    : catchAt,
            0.15f
        );
        yield return GripTo(left, 1f, 0.05f);
        yield return WaitFor(
            () => PeakXR.Interact.PhysicalInteraction.HandAction(left) == GripPressAction.Climb,
            0.5f
        );
        float caughtAfter = Now - released;
        bool caught =
            PeakXR.Interact.PhysicalInteraction.HandAction(left) == GripPressAction.Climb
            && c.data.isClimbing;
        // A climb that ended and started again on the catch restarts the game's own clock (CharacterClimbing.cs:107);
        // that clock keeps running after a climb ends (:106), so an ended climb is read from isClimbing.
        var sinceStart = PeakXR.Interact.PhysicalInteraction.SinceLastClimbStarted;
        bool unbroken =
            c.data.isClimbing
            && (sinceStart == null || sinceStart(c.refs.climbing) > caughtAfter - 0.05f);
        string gripNote = PeakXR.Interact.PhysicalInteraction.LastGripNote;
        float rise = c.Center.y - y0;
        bool whoosh = ClimbCues.Since(ClimbCue.Whoosh, left, whooshMark, out _);
        yield return Shot("vr-46-lunge");

        // Out of stamina: the same throw is refused with the stamina cue, and the game takes no jump.
        string refusal = "refusal not checked (UI.ActionHints off)";
        if (UI.Affordances.Affordance.Enabled && caught)
        {
            // God mode off (Run turns it back on) and the bar empty; the loop holds it there against regeneration.
            SandboxActions.SetStamina(0f, 0f);
            float drainEnd = Now + 3f;
            while (
                Now < drainEnd && (c.data.outOfStaminaFor < 0.6f || c.data.sinceClimbJump < 1.1f)
            )
            {
                c.data.currentStamina = 0f;
                c.data.extraStamina = 0f;
                yield return null;
            }
            UI.Affordances.Affordance.ResetGate();
            int denials = UI.Affordances.Affordance.Count;
            float noJumpBefore = c.data.sinceClimbJump;
            bool climbingAtThrow = c.data.isClimbing;
            from = Script(left).Position;
            float mark = CueMark();
            yield return FlingRelease(left, from, speed);
            bool Denied() =>
                UI.Affordances.Affordance.Count > denials
                && UI.Affordances.Affordance.LastReason == DenyReason.NoStamina
                && UI.Affordances.Affordance.LastLeft == left;
            // Every channel of the stamina refusal on the throwing hand, the stamina bar turned red.
            yield return ExpectCue(left, DenyReason.NoStamina, mark, true, 1.5f, seen: false);
            bool denied = Denied();
            // ExpectCue has proven the ring drawn and readable. CueCheck asks whether it is still drawn now, but a
            // refusal on a release (the grip already open) is not held, so it may have faded (run 34).
            bool noJump = c.data.sinceClimbJump >= noJumpBefore;
            SandboxActions.SetGodMode(true);
            refusal =
                $"out of stamina {c.data.outOfStaminaFor:0.0} s (climbing {climbingAtThrow}): denied {denied}"
                + $" ({UI.Affordances.Affordance.LastReason}), no jump {noJump}; {cueNote}";
            if (!denied)
                Fail("no NoStamina refusal for a lunge without stamina: " + refusal);
            else if (!noJump)
                Fail("the game took the climb jump without stamina: " + refusal);
        }
        var note = F(
            $"climb jump {jumped} {jumpAfter:0.00} s after the release ({speed:0.0} m/s throw, lunge from {cfg.LungeSpeed:0.0}), whoosh {whoosh}, caught {caught} after {caughtAfter:0.00} s (grace 0.6 s), climb unbroken {unbroken}, rose {rise:0.00} m; {refusal}"
        );
        if (!jumped)
            Fail("the throw did not make the game's climb jump: " + note);
        else if (!whoosh)
            Fail("the lunge made no whoosh: " + note);
        else if (!caught)
            Fail($"the hand did not catch the wall after the lunge: {note}; last grip: {gripNote}");
        else if (!unbroken)
            Fail("the climb ended between the lunge and the catch: " + note);
        // The jump's slide lifts the body about 1.4 m (above); half of the 0.66 m run 43 rose by the catch.
        else if (rise < LungeMinRise)
            Fail("the climb jump did not carry the body up: " + note);
        Note(note);
        yield return LetGoAll(c);
    }

    IEnumerator Restoring(string id, float timeout, IEnumerator body)
    {
        try
        {
            yield return HandStep(id, timeout, body);
        }
        finally
        {
            var restore = bodyRestore;
            bodyRestore = null;
            restore?.Invoke();
        }
    }

    /// <summary>Run after the Tropics jump: the only biome with jungle vines.</summary>
    IEnumerator VineSteps()
    {
        // Restoring: each grabs the vine from the test ledge (VineLedge), which must go after the step.
        yield return Restoring("vr.43-vine", 45f, VineStep());
        yield return Restoring("vr.43b-vine-look-back", 45f, VineLookBackStep());
        yield return Restoring("vr.43c-vine-slide", 30f, VineSlideStep());
        yield return Restoring("vr.59-vine-deny-stamina", 40f, VineDenyStaminaStep());
    }

    /// <summary>vr.40: a 15 % bow (a look at the belt, a nod) must not crouch; the head held 40 % of standing height
    /// low sets <c>CharacterData.isCrouching</c> (CharacterMovement.cs:241); back up, it clears. The physical crouch
    /// enters at 20 % (<see cref="PhysicalCrouch.EnterFraction"/>).</summary>
    IEnumerator CrouchStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!BodyGestures.CrouchOn || rig.SeatedMode)
        {
            stepSkip = "Locomotion.PhysicalCrouch off or seated mode";
            yield break;
        }
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        float low = eye - standing * 0.4f;
        float bow = eye - standing * 0.15f;
        float headY = bow;
        var hands = applyHands;
        bodyRestore = () =>
        {
            if (VRInput.TestOverride != hands && VRInput.TestOverride != IdleHands)
                VRInput.TestOverride = finished ? null : IdleHands;
        };
        VRInput.TestOverride = () =>
        {
            hands();
            var p = VRInput.HeadPosition;
            p.y = headY;
            VRInput.HeadPosition = p;
        };
        yield return Wait(1f);
        bool bowCrouched = c.data.isCrouching;
        headY = low;
        yield return WaitFor(() => c.data.isCrouching, 2f);
        bool crouched = c.data.isCrouching;
        yield return Wait(0.6f);
        float lift = rig.CrouchLift;
        yield return Shot("vr-40-crouch");
        VRInput.TestOverride = hands;
        yield return WaitFor(() => !c.data.isCrouching, 2f);
        var note =
            $"bow to {bow:0.00} m crouched {bowCrouched}; head to {low:0.00} m (eye {eye:0.00}), crouched {crouched}, view lift {lift:0.00} m, "
            + $"crouching after standing {c.data.isCrouching}";
        if (bowCrouched)
            Fail("a 15 % bow crouched: " + note);
        else if (!crouched || c.data.isCrouching)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.41: with <c>Locomotion.ArmSwingSprint</c> on and the stick forward, alternating swings set
    /// <c>CharacterData.isSprinting</c> (CharacterMovement.cs:263); still hands with the same stick do not.</summary>
    IEnumerator ArmSwingStep()
    {
        var c = Character.localCharacter;
        var setting = BodyGestures.ArmSwingSetting;
        if (setting == null)
        {
            stepSkip = "player.locomotion not installed";
            yield break;
        }
        bool was = setting.Value;
        bodyRestore = () => setting.Value = was;
        setting.Value = true;
        bool moveLeft = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        var restLeft = Rest(head, forward, -right).Position;
        var restRight = Rest(head, forward, right).Position;
        Script(moveLeft).Stick = new Vector2(0f, 1f);
        yield return Wait(0.8f);
        bool sprintStill = c.data.isSprinting;
        bool sprintSwing = false;
        float start = Now;
        while (Now - start < 2f)
        {
            float z = 0.25f * Mathf.Sin(2f * Mathf.PI * 1.6f * (Now - start));
            SetHand(true, restLeft + forward * z);
            SetHand(false, restRight - forward * z);
            sprintSwing |= c.data.isSprinting;
            yield return null;
        }
        Script(moveLeft).Stick = Vector2.zero;
        SetHand(true, restLeft);
        SetHand(false, restRight);
        setting.Value = was;
        yield return Wait(0.5f);
        var note = $"sprinting with still hands {sprintStill}, while swinging {sprintSwing}";
        if (sprintStill || !sprintSwing)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.42: a rope from the rope shooter's anchor prefab (RopeShooter.cs:104), shot in place through
    /// <c>RopeAnchorProjectile.GetShot</c> and <c>RopeAnchorWithRope.SpawnRope</c>, hangs in front. A grip on a
    /// segment grabs it (<c>GrabRopeRpc</c>, CharacterRopeHandling.cs:144); pulling that hand down moves the climb
    /// point up the rope; the other hand then takes over and its pull climbs on; that hand opening and closing again
    /// inside <c>ClimbRegripGrace</c> keeps the rope; opening it for good lets go once the grace runs out.
    /// </summary>
    IEnumerator RopeStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        yield return HangRope();
        if (testRope == null)
            yield break;
        var rope = testRope;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.8f);
        var api = rope.climbingAPI;
        float y0 = api.GetPosition(c.data.ropePercent).y;
        yield return Shot("vr-42-rope");
        yield return PullDown(left, 0.4f, 0.6f);
        yield return Wait(0.3f);
        bool still = c.data.isRopeClimbing;
        float rise = still ? api.GetPosition(c.data.ropePercent).y - y0 : 0f;
        // Hand over hand: the other hand takes the rope at its own reach, the first lets go, and that pull climbs on.
        bool other = !left;
        float y1 = api.GetPosition(c.data.ropePercent).y;
        var next = RopeSegmentNear(other, out _, out _);
        yield return MoveTo(other, () => next.position, 0.5f);
        string far = OverReach(other);
        yield return GripTo(other, 1f, 0.1f);
        yield return GripTo(left, 0f, 0.1f);
        yield return PullDown(other, 0.4f, 0.6f);
        yield return Wait(0.3f);
        bool still2 = c.data.isRopeClimbing;
        float rise2 = still2 ? api.GetPosition(c.data.ropePercent).y - y1 : 0f;
        // A regrip inside Interaction.ClimbRegripGrace: the only gripping hand opens for half the grace and closes
        // again where it is, and the rope stays held.
        float grace = Interact.InteractConfig.Instance.ClimbRegripGrace;
        yield return GripTo(other, 0f, 0.02f);
        yield return Wait(grace * 0.5f);
        yield return GripTo(other, 1f, 0.02f);
        yield return Wait(grace * 2f);
        bool kept = c.data.isRopeClimbing;
        yield return GripTo(other, 0f, 0.1f);
        yield return WaitFor(() => !c.data.isRopeClimbing, 1.5f);
        var note =
            $"climb point rose {rise:0.00} m for a 0.40 m pull, on the rope after the pull {still}, "
            + $"then {rise2:0.00} m for the other hand's pull (on the rope {still2}), "
            + F($"on the rope after a {grace * 0.5f:0.00} s regrip {kept}, ")
            + $"after letting go {c.data.isRopeClimbing}";
        if (far != null)
            Fail(far + ": " + note);
        else if (rise < 0.1f)
            Fail("arm pull did not climb the rope: " + note);
        else if (rise2 < 0.1f)
            Fail("the second hand's pull did not climb on: " + note);
        else if (!kept)
            Fail("a regrip inside the grace dropped the rope: " + note);
        else if (c.data.isRopeClimbing)
            Fail("letting go did not leave the rope: " + note);
        else
            stepNote = note;
        yield return LetGoAll(c);
    }

    /// <summary>
    /// A rope from the rope shooter's anchor prefab (RopeShooter.cs:104), shot in place through
    /// <c>RopeAnchorProjectile.GetShot</c> and <c>RopeAnchorWithRope.SpawnRope</c>, hanging 0.7 m in front from 2.6 m up;
    /// <see cref="testRope"/> once it has spooled out and settled, else null with the step skipped or failed. The
    /// local player owns it, so it simulates here (Rope.cs:155). A warp to open sky sets <see cref="WarpBack"/>.
    /// <paramref name="height"/> and <paramref name="segments"/> (0.25 m each, Rope.cs:291-294; 0 for the shooter's
    /// own length) hang a shorter rope higher, clear of the ground, so it can swing.
    /// </summary>
    IEnumerator HangRope(float height = 2.6f, float segments = 0f)
    {
        testRope = null;
        testAnchor = null;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var shooter = FindItemComponent<RopeShooter>();
        if (shooter == null || shooter.ropeAnchorWithRopePref == null)
        {
            stepSkip = "no RopeShooter item with a rope anchor prefab";
            yield break;
        }
        var forward = Flat(rig.Head.forward);
        if (!RopeRoom(c.Center, forward))
        {
            // The spawn is often under a wing or a palm: hang the rope under open sky nearby (WarpBack returns).
            if (!OpenSkyNear(c.Center, forward, out var ground))
            {
                stepSkip = "no clear space for a hanging rope within 15 m of the spawn";
                yield break;
            }
            yield return WarpTo(ground);
            if (!RopeRoom(c.Center, forward))
            {
                stepSkip = $"no clear space for a hanging rope after warping to {ground}";
                yield break;
            }
        }
        var top = c.Center + forward * 0.7f + Vector3.up * height;
        var go = PhotonNetwork.Instantiate(
            shooter.ropeAnchorWithRopePref.name,
            top,
            Quaternion.identity,
            0
        );
        spawned.Add(go);
        go.GetComponent<RopeAnchorProjectile>()
            .photonView.RPC(
                "GetShot",
                RpcTarget.AllBuffered,
                top,
                0.05f,
                segments > 0f ? segments : shooter.length,
                -forward
            );
        var anchor = go.GetComponent<RopeAnchorWithRope>();
        yield return WaitFor(() => anchor.rope != null, 3f);
        if (anchor.rope == null)
        {
            Fail("the anchor spawned no rope");
            yield break;
        }
        spawned.Add(anchor.ropeInstance);
        // Spool-out (RopeAnchorWithRope.spoolOutTime) and settling.
        yield return Wait(anchor.spoolOutTime + 1.5f);
        testRope = anchor.rope;
        testAnchor = go;
    }

    /// <summary>The <paramref name="left"/> hand grips the test rope's segment nearest its reach and the game grabs it
    /// (<c>GrabRopeRpc</c>, CharacterRopeHandling.cs:144); fails the step otherwise.</summary>
    IEnumerator GrabRope(bool left)
    {
        var c = Character.localCharacter;
        var segment = RopeSegmentNear(left, out var reach, out float best);
        if (segment == null)
        {
            Fail("rope has no segments");
            yield break;
        }
        yield return MoveTo(left, () => segment.position, 0.6f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => c.data.isRopeClimbing,
            2f,
            FormattableString.Invariant(
                $"grip on the rope did not grab it (nearest segment {Mathf.Sqrt(best):0.00} m from the reach point, {segment.position.y - reach.y:0.00} m above it)"
            )
        );
    }

    /// <summary>The test rope's segment nearest the <paramref name="left"/> hand's reach point (0.2 m above it).</summary>
    Transform RopeSegmentNear(bool left, out Vector3 reach, out float best)
    {
        reach = VRRig.Instance.Origin.TransformPoint(Script(left).Position) + Vector3.up * 0.2f;
        Transform segment = null;
        best = float.MaxValue;
        foreach (var t in testRope.GetRopeSegments())
        {
            float d = t != null ? (t.position - reach).sqrMagnitude : float.MaxValue;
            if (d < best)
            {
                best = d;
                segment = t;
            }
        }
        return segment;
    }

    /// <summary>Nothing solid between the body centre and the rope's anchor point, 0.7 m ahead and 2.6 m up, nor
    /// along the line the rope hangs down from it to the body's height: run 26 hung it after vr.45 near the crashed
    /// plane, where it caught 2.7 m up and the reach found no segment within arm's length.</summary>
    static bool RopeRoom(Vector3 center, Vector3 forward)
    {
        var top = center + forward * 0.7f + Vector3.up * 2.6f;
        return !Physics.Linecast(
                center,
                top,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            && !Physics.SphereCast(
                top,
                0.2f,
                Vector3.down,
                out _,
                2.6f,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            );
    }

    /// <summary>Flat open ground within 15 m (rings of 12 points every 3 m, nearest first) with room for a hanging
    /// rope over a body standing on it; <paramref name="ground"/> is the point to warp to.</summary>
    static bool OpenSkyNear(Vector3 center, Vector3 forward, out Vector3 ground)
    {
        for (float r = 3f; r <= 15f; r += 3f)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = center + Quaternion.AngleAxis(i * 30f, Vector3.up) * forward * r;
                // From 6 m up the first hit is the ground under open sky (an overhang would be hit first).
                if (
                    !Physics.Raycast(
                        p + Vector3.up * 6f,
                        Vector3.down,
                        out var hit,
                        12f,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                    || hit.normal.y < 0.8f
                )
                    continue;
                var body = hit.point + Vector3.up;
                if (
                    Physics.CheckSphere(
                        body,
                        0.4f,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    ) || !RopeRoom(body, forward)
                )
                    continue;
                ground = hit.point;
                return true;
            }
        }
        ground = default;
        return false;
    }

    /// <summary>
    /// vr.43: the nearest jungle vine of Capsule type (the only type <c>IsInteractible</c> accepts, JungleVine.cs:96).
    /// Standing on a test ledge under it (<see cref="VineLedge"/>), the hand reaches up and grips the drawn vine, and
    /// the grip grabs it the way a player's does (the hand-picked interact, then <c>GrabVineRpc</c>,
    /// CharacterVineClimbing.cs:155); a pull along it moves <c>vinePercent</c> (CharacterVineClimbing.cs:118); opening
    /// the hand lets go.
    /// </summary>
    IEnumerator VineStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        bool left = VRInput.LeftHanded;
        yield return GrabVine(left);
        if (vineSpot == null || !stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var vine = testVine;
        float p0 = c.data.vinePercent;
        var along =
            vine.GetPosition(Mathf.Clamp01(p0 + 0.05f), 1, 1)
            - vine.GetPosition(Mathf.Clamp01(p0 - 0.05f), 1, 1);
        along = along.sqrMagnitude > 1e-6f ? along.normalized : Flat(rig.Head.forward);
        var start = vine.GetPosition(p0, 1, 1);
        yield return Shot("vr-43-vine");
        // Pulling the hand back along the vine moves the body forward along it.
        yield return MoveTrack(
            left,
            Script(left).Position + rig.Origin.InverseTransformVector(-along * 0.4f),
            0.6f
        );
        yield return Wait(0.3f);
        bool still = c.data.isVineClimbing;
        float moved = Vector3.Dot(vine.GetPosition(c.data.vinePercent, 1, 1) - start, along);
        yield return GripTo(left, 0f, 0.1f);
        yield return WaitFor(() => !c.data.isVineClimbing, 1.5f);
        var note =
            $"climb point moved {moved:0.00} m along the vine for a 0.40 m pull, on the vine after the pull "
            + $"{still}, after letting go {c.data.isVineClimbing}";
        if (moved < 0.1f)
            Fail("arm pull did not move along the vine: " + note);
        else if (c.data.isVineClimbing)
            Fail("letting go did not leave the vine: " + note);
        else
            stepNote = note;
        yield return LetGoAll(c);
    }

    /// <summary>
    /// The player's vine grab (vr.43): standing on <see cref="VineLedge"/>, the <paramref name="left"/> hand reaches up
    /// to the drawn vine and grips, the hand-picked interact grabs it (<c>GrabVineRpc</c>, CharacterVineClimbing.cs:155),
    /// and the grab's slide is waited out. Fails the step when the grip does not grab; leaves
    /// <see cref="vineSpot"/> null with the step skipped when there is no vine.
    /// </summary>
    IEnumerator GrabVine(bool left)
    {
        var c = Character.localCharacter;
        yield return VineLedge();
        if (vineSpot == null)
            yield break;
        var spot = vineSpot;
        yield return MoveTo(left, () => spot.position, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => c.data.isVineClimbing,
            2f,
            "the hand's grip on the vine did not grab it: "
                + TraversalGrips.LastNote
                + "; "
                + PeakXR.Interact.PhysicalInteraction.LastGripNote
        );
        if (!stepOk)
            yield break;
        // A grab carries the body's speed along the vine into a slide (CharacterVineClimbing.cs:166-167); input is
        // ignored until it ends.
        yield return WaitFor(() => !c.refs.vineClimbing.Sliding(), 4f);
        yield return Wait(0.5f);
    }

    /// <summary>The nearest jungle vine of Capsule type (the only type <c>IsInteractible</c> accepts, JungleVine.cs:96),
    /// with the character warped 1.2 m under its middle unless <paramref name="warp"/> is false;
    /// <see cref="testVine"/>, or null with the step skipped.</summary>
    IEnumerator ToVine(bool warp = true)
    {
        testVine = null;
        var c = Character.localCharacter;
        JungleVine vine = null;
        float best = float.MaxValue;
        // Once per step, never per frame.
        foreach (var v in FindObjectsByType<JungleVine>(FindObjectsSortMode.None))
        {
            if (
                v.colliderType != JungleVine.ColliderType.Capsule
                || v.colliderRoot == null
                || v.colliderRoot.childCount < 4
            )
                continue;
            float d = (v.GetPosition(0.5f, 1, 1) - c.Center).sqrMagnitude;
            if (d < best)
            {
                best = d;
                vine = v;
            }
        }
        if (vine == null)
        {
            stepSkip = "no climbable jungle vine in this level";
            yield break;
        }
        testVine = vine;
        if (!warp)
            yield break;
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            vine.GetPosition(0.5f, 1, 1) + Vector3.down * 1.2f,
            false
        );
        yield return Wait(0.2f);
        yield return WaitFor(() => !c.warping, 3f);
    }

    /// <summary>The point on <see cref="testVine"/>'s drawn middle a hand grips, set by <see cref="VineLedge"/>.</summary>
    Transform vineSpot;

    /// <summary>
    /// <see cref="ToVine"/>'s vine with the player standing on a Map-layer test ledge 2 m under its drawn middle, so
    /// the vine hangs 0.4 m over the eye, in an overhead reach. <see cref="vineSpot"/> marks that point; both go when
    /// the step's <see cref="Restoring"/> runs. Run 39 warped into the air under it: the body fell, and the hand,
    /// placed a frame behind the fall, closed 0.9 m off the vine. Run 43 warped the body's centre onto the ledge top
    /// (not <see cref="CentreOver"/>) and it sank under it: the grip sat 1.14 m above the eye (tracking y 2.74).
    /// </summary>
    IEnumerator VineLedge()
    {
        vineSpot = null;
        var c = Character.localCharacter;
        yield return ToVine(warp: false);
        if (testVine == null)
            yield break;
        var marker = new GameObject("PeakXR_TestVineSpot").transform;
        OnRestore(() => UnityEngine.Object.Destroy(marker.gameObject));
        marker.position = testVine.GetPosition(0.5f, 1, 1) + Vector3.down * testVine.colliderOffset;
        var feet = marker.position + Vector3.down * 2f;
        var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ledge.name = "PeakXR_TestVineLedge";
        ledge.layer = LayerMask.NameToLayer("Map");
        OnRestore(() =>
        {
            if (ledge != null)
                UnityEngine.Object.Destroy(ledge);
        });
        ledge.transform.SetPositionAndRotation(feet + Vector3.down * 0.1f, Quaternion.identity);
        ledge.transform.localScale = new Vector3(2f, 0.2f, 2f);
        Physics.SyncTransforms();
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            CentreOver(c, feet + Vector3.up * 0.1f),
            false
        );
        yield return WaitFor(() => !c.warping, 2f);
        yield return WaitFor(() => c.data.isGrounded, 2f);
        yield return Wait(0.5f);
        vineSpot = marker;
    }

    /// <summary>
    /// vr.44: a hammered piton (the climbing spike item's <c>hammeredVersionPrefab</c>, placed like
    /// CharacterItems.cs:1080) on the wall vr.15 climbs. A grip on its handle hangs (<c>RPCA_Hang</c>,
    /// ClimbHandle.cs:54-64); opening the hand lets go (<c>CancelHandle</c>, CharacterClimbing.cs:198).
    /// vr.44c does the same with a placed pickaxe (<c>ClimbHandle.isPickaxe</c>, ClimbHandle.cs:11, :34).
    /// </summary>
    IEnumerator PitonStep(bool pickaxe = false)
    {
        var c = Character.localCharacter;
        if (!ClimbHands.HandleHands)
        {
            stepSkip = "Interaction.PitonGripHang off";
            yield break;
        }
        yield return PlacePiton(pickaxe);
        if (testHandle == null)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var handle = testHandle;
        if (pickaxe && !handle.isPickaxe)
        {
            Fail("the placed handle is not a pickaxe");
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.5f);
        bool left = VRInput.LeftHanded;
        yield return MoveTo(left, () => handle.transform.position, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => c.data.currentClimbHandle == handle,
            2f,
            "grip on the piton handle did not hang"
        );
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(1f);
        bool hanging = c.data.currentClimbHandle == handle;
        yield return Shot(pickaxe ? "vr-44c-pickaxe" : "vr-44-piton");
        yield return GripTo(left, 0f, 0.1f);
        yield return WaitFor(() => c.data.currentClimbHandle == null, 1.5f);
        var note =
            $"{(handle.isPickaxe ? "pickaxe" : "piton")}: hanging after 1 s {hanging}, hanging after letting go {c.data.currentClimbHandle != null}";
        if (!hanging)
            Fail("the hang did not hold while gripping: " + note);
        else if (c.data.currentClimbHandle != null)
            Fail("letting go did not leave the handle: " + note);
        else
            stepNote = note;
        yield return LetGoAll(c);
    }

    /// <summary>
    /// A hammered piton (the climbing spike item's <c>hammeredVersionPrefab</c>, placed like CharacterItems.cs:1080) on
    /// a wall in front of the warped player: <see cref="testHandle"/>, or null with the step skipped or failed. With
    /// <paramref name="pickaxe"/>, the spike item whose placed version is a pickaxe handle.
    /// </summary>
    IEnumerator PlacePiton(bool pickaxe = false)
    {
        testHandle = null;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var spike = pickaxe ? PickaxeSpike() : FindItemComponent<ClimbingSpikeComponent>();
        if (spike == null || spike.hammeredVersionPrefab == null)
        {
            stepSkip = pickaxe
                ? "no item places a pickaxe handle (ClimbHandle.isPickaxe on a hammered prefab)"
                : "no climbing spike item with a hammered prefab";
            yield break;
        }
        if (!FindWall(c, out var wall, out var stand))
        {
            stepSkip = NoWall;
            yield break;
        }
        var normal = Flat(wall.normal);
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -normal, Vector3.up));
        yield return Wait(0.6f);
        if (!WallInFront(c, wall, 0.4f, out var hit))
        {
            Fail($"wall not in front after the warp (wall {wall.point}, body {c.Center})");
            yield break;
        }
        // A player steps up to the face before placing a piton; the stand spot can leave it up to 2 m off, and a
        // handle 1.18 m from the eye (run 47, vr.44) is out of any reach. Step to about 0.4 m from it (run 53, vr.56: 0.5 m left the handle 0.85 m out).
        // Toward the hit itself, and again if the face found after the step is still out: WallInFront can fall back
        // to a ray at the far point FindWall started from (run 55, vr.44b: the handle placed 1.37 m out after the step).
        var steps = new System.Text.StringBuilder();
        for (int step = 0; step < 2; step++)
        {
            var gap = hit.point - rig.Head.position;
            gap.y = 0f;
            // 0.65 m out is still a comfortable reach to a hold at chest height; nearer the face the ground is often
            // steeper (run 57, vr.44b: stepped 0.21 m in from 0.61 m, the body then slid 3 m down the slope).
            if (gap.magnitude <= 0.65f)
                break;
            var bodyBefore = c.Center;
            var standBefore = new Vector3(c.Center.x, TrackedFloorY() + 0.1f, c.Center.z);
            var hitBefore = hit;
            steps.Append(F($"step {step + 1}: face {gap.magnitude:0.00} m out"));
            c.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                standBefore + gap.normalized * (gap.magnitude - 0.5f),
                false
            );
            yield return Wait(0.5f);
            yield return WaitFor(() => !c.warping, 3f);
            yield return Wait(0.5f);
            // A spot the body will not stay on is no step: go back to where it stood.
            var landed = c.Center;
            yield return Wait(1f);
            if (FlatDistance(landed, c.Center) > 0.2f)
            {
                steps.Append(
                    F(
                        $", slid {FlatDistance(landed, c.Center):0.00} m off the stepped-to spot, stepped back; "
                    )
                );
                c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standBefore, false);
                yield return Wait(0.5f);
                yield return WaitFor(() => !c.warping, 3f);
                yield return Wait(1f);
                hit = hitBefore;
                break;
            }
            if (!WallInFront(c, wall, 0.4f, out hit))
            {
                Fail(
                    $"wall not in front after stepping up to it (wall {wall.point}, body {c.Center})"
                );
                yield break;
            }
            rig.RotateYaw(
                Vector3.SignedAngle(
                    Flat(rig.Head.forward),
                    Flat(hit.point - rig.Head.position),
                    Vector3.up
                )
            );
            steps.Append(
                F(
                    $", body moved {FlatDistance(bodyBefore, c.Center):0.00} m of {gap.magnitude - 0.5f:0.00} asked; "
                )
            );
        }
        // Still out after the steps (run 56, vr.56: 1.00 m out, the step's effect unlogged): place on the face where
        // a player standing here would, straight ahead of the eyes at chest height, if there is one within reach.
        var outNow = hit.point - rig.Head.position;
        outNow.y = 0f;
        if (
            outNow.magnitude > 0.75f
            && Physics.Raycast(
                rig.Head.position + Vector3.down * 0.3f,
                Flat(rig.Head.forward),
                out var near,
                0.75f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            steps.Append(
                F(
                    $"placed on the face ahead at {near.distance:0.00} m instead of {outNow.magnitude:0.00} m"
                )
            );
            hit = near;
        }
        if (steps.Length > 0)
            Note("piton step-in: " + steps);
        var go = PhotonNetwork.InstantiateItemRoom(
            spike.hammeredVersionPrefab.name,
            hit.point,
            Quaternion.LookRotation(-hit.normal, Vector3.up)
        );
        if (go == null)
        {
            Fail("placing the hammered piton failed");
            yield break;
        }
        spawned.Add(go);
        var handle = go.GetComponentInChildren<ClimbHandle>();
        if (handle == null)
        {
            Fail("the hammered piton has no ClimbHandle");
            yield break;
        }
        testHandle = handle;
    }

    /// <summary>The climbing spike item whose hammered version is a pickaxe handle, or null.</summary>
    static ClimbingSpikeComponent PickaxeSpike()
    {
        foreach (var item in SandboxActions.Items)
        {
            var spike = item != null ? item.GetComponent<ClimbingSpikeComponent>() : null;
            var placed = spike != null ? spike.hammeredVersionPrefab : null;
            var handle = placed != null ? placed.GetComponentInChildren<ClimbHandle>(true) : null;
            if (handle != null && handle.isPickaxe)
                return spike;
        }
        return null;
    }

    /// <summary>The first item prefab in the database that carries <typeparamref name="T"/>.</summary>
    static T FindItemComponent<T>()
        where T : Component
    {
        foreach (var item in SandboxActions.Items)
        {
            var found = item != null ? item.GetComponent<T>() : null;
            if (found != null)
                return found;
        }
        return null;
    }

    // ---------------------------------------------------------------- WP2: climb, rope, vine, handle and grab steps

    /// <summary>The face in front of the body after <see cref="ToWall"/> (valid while <see cref="wallReady"/>), the
    /// wall FindWall found and the ground spot the player was warped to.</summary>
    RaycastHit wallFace;
    RaycastHit wallBase;
    Vector3 wallStand;
    bool wallReady;
    Rope testRope;
    GameObject testAnchor;
    JungleVine testVine;
    ClimbHandle testHandle;

    /// <summary>A stamina drain started by <see cref="StartEmpty"/> runs.</summary>
    bool keepEmpty;

    /// <summary>Adds an undo to what the step's <see cref="Restoring"/> runs after it, keeping earlier ones.</summary>
    void OnRestore(Action undo)
    {
        var before = bodyRestore;
        bodyRestore = () =>
        {
            before?.Invoke();
            undo();
        };
    }

    /// <summary>Where the game's aim rays start: the camera (CharacterClimbing.cs:541), or the head before it exists.</summary>
    static Vector3 EyeNow() =>
        MainCamera.instance != null
            ? MainCamera.instance.transform.position
            : VRRig.Instance.Head.position;

    /// <summary>
    /// Offline the sandbox's 7 m test wall, online a map wall (<see cref="FindWall"/>); the player is warped 0.6 m
    /// in front of it, turned to face it, and <see cref="wallFace"/> is the face in front. Sets the skip or failure
    /// when it cannot.
    /// </summary>
    IEnumerator ToWall(float headroom = 1.2f)
    {
        wallReady = false;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (
            !LungeWall(c, out wallBase, out wallStand)
            && !FindWall(c, out wallBase, out wallStand, headroom)
        )
        {
            stepSkip = NoWall;
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, CentreOver(c, wallStand), false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(
            Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(wallBase.normal), Vector3.up)
        );
        yield return Wait(0.6f);
        if (!WallInFront(c, wallBase, 0.1f, out wallFace))
        {
            Fail($"wall not in front after the warp (wall {wallBase.point}, body {c.Center})");
            yield break;
        }
        wallReady = true;
    }

    Vector3 WallHold => wallFace.point + wallFace.normal * 0.04f;

    /// <summary>The hand closes on <see cref="wallFace"/> and the grip climb starts; fails the step otherwise.</summary>
    IEnumerator GripWall(bool left, float up = 0f)
    {
        var c = Character.localCharacter;
        var hold = WallHold;
        // A higher hold on the same face, <paramref name="up"/> above the body centre (a lunge's, by the head).
        if (up > 0f && WallInFront(c, wallBase, up, out var higher))
            hold = higher.point + higher.normal * 0.04f;
        yield return MoveTo(left, () => hold, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        if (!c.data.isClimbing)
            Fail($"climb did not start ({PeakXR.Interact.PhysicalInteraction.LastGripNote})");
    }

    /// <summary>God mode off and both stamina bars held at zero for the rest of the step (the game regenerates
    /// them every frame); god mode returns after it. False offline-only refusals and the like.</summary>
    bool StartEmpty()
    {
        if (!SandboxActions.SetStamina(0f, 0f))
            return false;
        keepEmpty = true;
        OnRestore(() =>
        {
            keepEmpty = false;
            SandboxActions.SetGodMode(true);
        });
        StartCoroutine(KeepEmpty());
        return true;
    }

    IEnumerator KeepEmpty()
    {
        while (keepEmpty)
        {
            var c = Character.localCharacter;
            if (c != null)
            {
                c.data.currentStamina = 0f;
                c.data.extraStamina = 0f;
                // Regeneration waits 2 s after a use when empty (Character.cs:1285-1286); without this it refills
                // in FixedUpdate before the next input sample, and a grip sees stamina (run 33's vr.57 and vr.59).
                c.data.sinceUseStamina = 0f;
            }
            yield return null;
        }
    }

    /// <summary><see cref="ExpectFeel"/> with <see cref="cueNote"/> cleared first, so a failure's note never
    /// carries the previous step's cue (run 33's vr.50 quoted vr.49's Carried).</summary>
    /// <summary>
    /// Puts <paramref name="prefab"/> straight into a free slot and equips it (<c>Player.AddItem</c>, Player.cs:91;
    /// <c>CharacterItems.EquipSlot</c>, CharacterItems.cs:482), master only. <c>GameUtils.InstantiateAndGrab</c>
    /// spawns the item at the hip first (GameUtils.cs:263-271): run 35's cactus stuck to the body there ("Stuck to
    /// Character", StickyItemComponent.cs:69-80) and never reached the hand.
    /// </summary>
    IEnumerator EquipDirect(Item prefab)
    {
        var c = Character.localCharacter;
        if (prefab == null || c == null || c.player == null)
        {
            Fail("no item or player");
            yield break;
        }
        if (!PhotonNetwork.IsMasterClient)
        {
            stepSkip = "equipping straight into a slot needs the master client";
            yield break;
        }
        if (!c.player.AddItem(prefab.itemID, null, out var slot) || slot == null)
        {
            Fail("no free slot for " + prefab.name);
            yield break;
        }
        c.refs.items.EquipSlot(Zorro.Core.Optionable<byte>.Some(slot.itemSlotID));
        yield return Until(() => Holding(prefab), 5f, "equipped item not in hand");
        // One Update so PhysicalInteraction.TrackHeldItem has picked the holding hand.
        yield return null;
        if (!stepOk)
            yield break;
        var held = HeldItem();
        spawned.Add(held.gameObject);
        // Run 36's cactus was gone from the hand by the drop tap ("drop release: nothing") and a "Stuck to Character"
        // was logged: watch it stay in hand, and name its state if it does not.
        float start = Now;
        while (Now - start < 0.5f && c.data.currentItem == held)
            yield return null;
        if (c.data.currentItem != held)
        {
            string stuck =
                held != null
                && held.TryGetComponent<StickyItemComponent>(out var s)
                && s.stuckToCharacter != null
                    ? s.stuckToCharacter.name
                    : "nobody";
            Fail(
                F(
                    $"{prefab.name} left the hand {Now - start:0.00} s after equipping: state {(held != null ? held.itemState.ToString() : "destroyed")}, stuck to {stuck}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
                )
            );
        }
    }

    /// <summary>
    /// vr.47d: the finger gun aimed at an Apple on the ground for 1 s (well past <c>FingerGun.DefaultWindow</c>), then
    /// squeezed. A pointing hand is aiming a ping, so interact.physical's pointed interact keeps waiting
    /// (<see cref="GripIntent.DeferPointedInteract"/>): the squeeze pings (<c>PointPinger._timeLastPinged</c>,
    /// PointPinger.cs:68-71) and the Apple stays on the ground.
    /// </summary>
    IEnumerator PingAtItemStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var pinger = LocalPinger();
        if (pinger == null || Interact.Ping.PingInput.LastPinged == null)
        {
            stepSkip = "no PointPinger or interact.ping not installed";
            yield break;
        }
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        if (apple == null)
        {
            Fail("no Apple item");
            yield break;
        }
        // Past the grab radius, inside the game's 2 m interact reach: only a pointed interact could take it.
        var spot = Flat(rig.Head.forward) * 1.2f + rig.Head.position;
        spot.y = c.Center.y;
        if (!SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var item = go != null ? go.GetComponent<Item>() : null;
        yield return Wait(0.6f);
        if (item == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        bool InWorld() => item != null && item.gameObject.activeInHierarchy;
        bool left = false;
        // Aim at the Apple's visible top, as a player points at what they see. Its centre can sit below the uneven
        // ground it settled into (the run-50 ground pickups): the game's interact line then meets the ground first,
        // and its sphere-cast fallback stops there (Interaction.cs:214-241), so run 58 hovered nothing.
        var aim = TopOf(item);
        yield return PingCooldownOver(pinger);
        yield return FingerGunAim(left, aim, 1f);
        // The precondition that makes the step mean something: a pointed interact could reach the Apple. The hand's
        // ray is the game's distance less the hand's offset from the head (InteractPatches.Raycasts), and the sphere
        // cast behind it starts half its area out and reaches its area past the end (Interaction.cs:241).
        var pointFrom = rig.Aim(left);
        var interaction = Interaction.instance;
        float rayReach =
            pointFrom != null && interaction != null
                ? interaction.distance
                    - Vector3.Distance(EyeNow(), pointFrom.position)
                    + 1.5f * interaction.area
                : 0f;
        float toApple =
            pointFrom != null ? Vector3.Distance(pointFrom.position, aim) : float.MaxValue;
        if (toApple >= rayReach)
        {
            Fail(
                F(
                    $"the Apple lies {toApple:0.00} m from the pointing hand, past the pointed interact's {rayReach:0.00} m: nothing could pick it up, the step would prove nothing"
                )
            );
            yield break;
        }
        bool keptWhileAiming = InWorld() && HeldItem() == null;
        float gameMark = Time.time;
        yield return FingerGunSqueeze(left, aim);
        yield return Until(
            () => Interact.Ping.PingInput.LastPinged(pinger) >= gameMark,
            2f,
            "the squeeze did not ping"
        );
        yield return GripTo(left, 0f, 0.1f);
        yield return Wait(0.5f);
        bool kept = InWorld() && HeldItem() == null;
        var note = F(
            $"Apple {toApple:0.00} m from the pointing hand (ray {rayReach:0.00} m), on the ground after 1 s of aiming {keptWhileAiming}, after the ping {kept}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (!keptWhileAiming || !kept)
        {
            Fail("aiming a ping at an item picked it up: " + note);
            yield break;
        }
        // External review #13: the same pointing grip with the thumb resting on the stick is a pointed interact, not
        // a ping aim. It takes the Apple and pings nothing.
        float pingBefore = Interact.Ping.PingInput.LastPinged(pinger);
        // The ping's marker stands on the Apple for 2 s (PointPinger.cs:97-104); let it go first.
        yield return Wait(Mathf.Max(0f, pingBefore + 2.1f - Time.time));
        Script(left).StickTouch = true;
        OnRestore(() => Script(left).StickTouch = false);
        yield return FingerGunAim(left, aim, 0.1f);
        // Still pointing at it while the interact lands, as a player does.
        for (float until = Now + 2f; Now < until && !Holding(apple); )
        {
            AimHand(rig, left, aim);
            yield return null;
        }
        bool taken = Holding(apple);
        bool pinged = Interact.Ping.PingInput.LastPinged(pinger) != pingBefore;
        var hovered = Interaction.instance != null ? Interaction.instance.currentHovered : null;
        // What the hand's own aim line meets first, the way the game's interact line runs from it.
        var aimPose = rig.Aim(left);
        string first =
            aimPose != null
            && Physics.Raycast(
                aimPose.position,
                aimPose.forward,
                out var firstHit,
                4f,
                HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                QueryTriggerInteraction.Collide
            )
                ? F($"{firstHit.collider.name} at {firstHit.distance:0.00} m")
                : "nothing";
        Script(left).StickTouch = false;
        note += F(
            $"; thumb on the stick: pointed interact took it {taken}, pinged {pinged}, hovered {(hovered is UnityEngine.Object o && o != null ? o.name : "nothing")}, aim line meets {first}, aim hand {PeakXR.Interact.PhysicalInteraction.AimHand}, {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (!taken || pinged)
            Fail("a pointed grip with the thumb resting did not interact cleanly: " + note);
        else
            Note(note);
    }

    /// <summary>A point near the top of an item's solid colliders, where the eye sees it on the ground.</summary>
    static Vector3 TopOf(Item item)
    {
        foreach (var col in item.colliders)
        {
            if (col != null && col.enabled && !col.isTrigger)
            {
                var b = col.bounds;
                return b.center + Vector3.up * (0.7f * b.extents.y);
            }
        }
        return item.Center();
    }

    /// <summary>
    /// vr.47e: an item that arrives in a hand whose grip is already closed is not the grip's to throw
    /// (<see cref="ReleaseArming"/>). The right hand holds an Apple with its grip closed; a second item is given (the
    /// game equips it, CharacterItems.cs:457) and a fast release follows: the new item stays in hand. Closing the grip
    /// on it again and the same release throws it.
    /// </summary>
    IEnumerator ArriveInClosedGripStep()
    {
        var items = new List<Item>(SandboxActions.Items);
        var apple = FindItem(items, AppleFragment);
        var other = FindItem(items, "Airplane Food") ?? FindItem(items, "Granola");
        if (apple == null || other == null)
        {
            stepSkip = "no Apple or second one-handed food item";
            yield break;
        }
        const bool left = false;
        if (!SandboxActions.GiveItem(apple, left))
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => Holding(apple), 5f, "given Apple not in hand");
        if (!stepOk)
            yield break;
        spawned.Add(HeldItem().gameObject);
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
        // Arrives while the grip stays closed.
        SandboxActions.GiveItem(other, left);
        yield return Until(() => Holding(other), 5f, "second item not equipped");
        if (!stepOk)
            yield break;
        var arrived = HeldItem();
        // Read now: the thrown item is gone by the note (run 53 threw reading its name).
        string arrivedName = arrived.name;
        spawned.Add(arrived.gameObject);
        yield return null;
        // vr.13's in-reach swing; the old arc ended about 1 m from the shoulder.
        ThrowArc(left, out var windup, out var release);
        yield return MoveTrack(left, windup, 0.4f);
        yield return Wait(0.3f);
        yield return SwingRelease(left, windup, release);
        yield return Wait(0.6f);
        bool kept = HeldItem() == arrived;
        // A fresh close on it arms the release again.
        yield return MoveTrack(left, windup, 0.3f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
        yield return SwingRelease(left, windup, release);
        yield return WaitFor(() => HeldItem() == null, 1.5f);
        bool thrown = HeldItem() == null;
        var note =
            $"{arrivedName} arrived in the closed grip: kept on the release {kept}; thrown after a fresh close {thrown}";
        if (!kept)
            Fail("a release dropped an item that arrived in the closed grip: " + note);
        else if (!thrown)
            Fail("a fresh close and fast release did not throw: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.47f: an item resting on sloped ground, partly sunk into it as items settle on uneven terrain. A 25 degree
    /// Map-layer slab rises away 0.7 m ahead of the feet, clear of the body; an Apple is held still with its centre 2 cm below the slab's
    /// surface. A hand on it grabs it: the grab's line of sight takes the item's top when its centre and nearest point
    /// are under the surface (<see cref="GrabSelector.SightPoints"/>).
    /// </summary>
    IEnumerator GrabOnSlopeStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        if (apple == null)
        {
            Fail("no Apple item");
            yield break;
        }
        var forward = Flat(rig.Head.forward);
        var slope =
            Vector3.up * Mathf.Cos(25f * Mathf.Deg2Rad) - forward * Mathf.Sin(25f * Mathf.Deg2Rad);
        var onSlope = Feet(c) + forward * 0.7f + Vector3.up * 0.15f;
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "PeakXR_TestSlope";
        slab.layer = LayerMask.NameToLayer("Map");
        OnRestore(() =>
        {
            if (slab != null)
                UnityEngine.Object.Destroy(slab);
        });
        PlaceSlab(slab, onSlope, slope);
        if (!SandboxActions.SpawnItemAt(apple, onSlope + slope * 0.2f))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var item = go != null ? go.GetComponent<Item>() : null;
        yield return null;
        if (item == null || item.rig == null)
        {
            Fail("spawned Apple has no Item or body");
            yield break;
        }
        // Held still, sunk 2 cm, so it neither rolls down the slope nor sits cleanly on top.
        item.rig.isKinematic = true;
        item.transform.position = onSlope - slope * 0.02f;
        Physics.SyncTransforms();
        yield return Wait(0.3f);
        bool left = false;
        var at = item.Center();
        // A player squats for an item on the ground; a standing reach to it was about 1.5 m from the eye.
        LookAtHand(left);
        yield return ReachDown(left, () => at, 0.6f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => Holding(apple), 3f);
        bool held = Holding(apple);
        var note = F(
            $"Apple sunk 0.02 m into a 25 deg slope, hand {Vector3.Distance(rig.Hand(left).position, at):0.00} m from it: held {held}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (!held)
            Fail("a hand on an item resting on a slope did not grab it: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.44d (external review #7): hammering a climbing spike is faster than holding. Holding the use button with the
    /// spike aimed at the test wall starts the game's cast; the free hand strikes the preview <c>HammerStrikes</c>
    /// times. The spike goes in sooner than the item's hold time, but no sooner than the cap
    /// (<see cref="HammerCast.MaxSpeedup"/>).
    /// </summary>
    IEnumerator HammerSpikeStep()
    {
        var c = Character.localCharacter;
        var tools = Interact.Tools.ToolsConfig.Instance;
        if (!tools.HammerSpikes)
        {
            stepSkip = "Tools.HammerSpikes off";
            yield break;
        }
        var spike = FindItemComponent<ClimbingSpikeComponent>();
        if (spike == null)
        {
            stepSkip = "no climbing spike item";
            yield break;
        }
        yield return ToWall();
        if (!wallReady)
            yield break;
        yield return GiveAndHold(spike.GetComponent<Item>());
        if (!stepOk)
            yield break;
        float castTime = spike.GetComponent<Item>().usingTimePrimary;
        bool holdLeft = LocalHold.ItemInLeftHand;
        bool free = !holdLeft;
        var rig = VRRig.Instance;
        // The spike hand aims at the face in front.
        var face = wallFace.point;
        var spikeAim = rig.Origin.InverseTransformPoint(
            rig.Head.position + Flat(face - rig.Head.position) * 0.3f + Vector3.down * 0.15f
        );
        SetHand(holdLeft, spikeAim);
        // Aim the spike hand's aim pose (not the grip) at the face: interact.aim casts the spike from it. Run 58 turned
        // the grip toward the wall, the aim pose pointed at the floor, and the preview landed by the feet.
        for (float until = Now + 0.3f; Now < until; )
        {
            AimHand(rig, holdLeft, face);
            yield return null;
        }
        var handlesBefore = new HashSet<int>();
        foreach (var h in FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None))
            handlesBefore.Add(h.GetInstanceID());
        int before = handlesBefore.Count;
        Script(holdLeft).Trigger = 1f;
        yield return WaitFor(() => Interact.Tools.SpikeHammer.Casting, 2f);
        if (!Interact.Tools.SpikeHammer.Casting)
        {
            Script(holdLeft).Trigger = 0f;
            Fail("holding the use button at the wall started no spike cast");
            yield return LetGoAll(c);
            yield break;
        }
        float started = Now;
        var preview = Interact.Tools.SpikeHammer.Preview;
        float offFace = Mathf.Abs(Vector3.Dot(preview - wallFace.point, wallFace.normal));
        if (offFace > 0.2f)
        {
            Script(holdLeft).Trigger = 0f;
            Fail(
                F(
                    $"the spike's preview is {offFace:0.00} m off the wall face, at {preview}: it aimed elsewhere"
                )
            );
            yield return LetGoAll(c);
            yield break;
        }
        int needed = tools.HammerStrikes;
        for (int i = 0; i < needed && Interact.Tools.SpikeHammer.Casting; i++)
        {
            var point = Interact.Tools.SpikeHammer.Preview;
            // Back past the strike's re-arm radius (SpikeHammer: 0.35 m) so each blow counts; run 57's 0.3 m never
            // re-armed it and landed 0 of 3. Along the preview's own normal, the direction the strike is measured on.
            var back = point + Interact.Tools.SpikeHammer.PreviewNormal * 0.45f;
            yield return MoveTo(free, () => back, 0.12f);
            yield return Wait(0.05f);
            // 0.45 m in 0.12 s: a strike at about 3.7 m/s into the wall.
            yield return MoveTo(free, () => point, 0.12f);
            yield return null;
        }
        int strikes = Interact.Tools.SpikeHammer.Strikes;
        string swing = Interact.Tools.SpikeHammer.LastSwing;
        yield return WaitFor(
            () => FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None).Length > before,
            castTime + 1f
        );
        float took = Now - started;
        // The game places the spike itself (InstantiateItemRoom, CharacterItems.cs:1080), so it is not in spawned:
        // left on the wall, every later step's grip at that stand took it as a handle (run 57: vr.21 on).
        bool placed = false;
        foreach (var h in FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None))
        {
            if (handlesBefore.Contains(h.GetInstanceID()))
                continue;
            placed = true;
            var view = h.GetComponentInParent<PhotonView>();
            spawned.Add(view != null ? view.gameObject : h.gameObject);
        }
        Script(holdLeft).Trigger = 0f;
        float earliest = castTime / HammerCast.MaxSpeedup;
        var note = F(
            $"{strikes} of {needed} strikes: placed {placed} after {took:0.00} s (hold {castTime:0.00} s, cap {earliest:0.00} s); last {swing}"
        );
        if (!placed)
            Fail("the hammered spike was not placed: " + note);
        else if (took >= castTime - 0.05f)
            Fail("hammering was not faster than holding: " + note);
        else if (took < earliest - 0.15f)
            Fail("hammering beat the cap: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>The body's centre height when <see cref="FlingRelease"/> let go.</summary>
    float flingReleaseY;

    /// <summary>
    /// A climb lunge's fling (<see cref="LungeIntent"/>): the hand goes straight up at <paramref name="speed"/>, lets go
    /// at or above the head, and keeps rising past the confirm delay, as a thrown arm does.
    /// </summary>
    IEnumerator FlingRelease(bool left, Vector3 from, float speed)
    {
        // A real fling lets go early: the grip opens as the hand passes the shoulder (or after 0.12 s at the latest),
        // so the rising grip barely pushes the body down, and the arm carries on up past the confirm delay.
        float start = Now;
        float released = float.MaxValue;
        while (true)
        {
            float t = Now - start;
            SetHand(left, from + Vector3.up * (speed * t));
            bool atShoulder =
                VRInput.HeadPosition.y - Script(left).Position.y
                <= LungeIntent.ShoulderDrop - 0.05f;
            // At least 0.06 s of swing first, as a real fling has: a release one frame into the motion (run 59 started
            // the no-stamina fling at the shoulder) gives the hand tracker no speed to fit, so no lunge was seen.
            if (released == float.MaxValue && ((atShoulder && t >= 0.06f) || t >= 0.12f))
            {
                Script(left).Grip = 0f;
                flingReleaseY = Character.localCharacter.Center.y;
                released = t;
            }
            if (t >= released + 0.25f)
                yield break;
            yield return null;
        }
    }

    /// <summary>How far above the body centre a lunge's hold sits: about head height.</summary>
    const float LungeHoldUp = 0.5f;

    /// <summary>
    /// vr.46c (external review #16): a brisk reach never lunges. Climbing on one hand, that hand lets go at chest height
    /// moving up at the lunge speed and closes on the wall again 0.12 s later, as a hand-over-hand reach does: no climb
    /// jump, no refusal.
    /// </summary>
    IEnumerator ReachNoLungeStep()
    {
        var c = Character.localCharacter;
        var cfg = Interact.InteractConfig.Instance;
        if (!cfg.ArmLunge)
        {
            stepSkip = "Interaction.ArmLunge off";
            yield break;
        }
        yield return ToWall(headroom: LungeHeadroom);
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.3f, 0.5f);
        yield return WaitFor(() => c.data.sinceClimbJump > 1.1f, 2f);
        UI.Affordances.Affordance.ResetGate();
        float jumpBefore = c.data.sinceClimbJump;
        float mark = CueMark();
        var from = Script(left).Position;
        float speed = Mathf.Max(1.2f * cfg.LungeSpeed, 3f);
        // Up through chest height, open, then close on the face again.
        float start = Now;
        while (Now - start < 0.2f)
        {
            float t = Now - start;
            SetHand(left, from + Vector3.up * (speed * t));
            if (t >= 0.08f)
                Script(left).Grip = 0f;
            yield return null;
        }
        Script(left).Grip = 1f;
        yield return Wait(0.5f);
        bool jumped = c.data.sinceClimbJump < jumpBefore;
        bool climbing = c.data.isClimbing;
        yield return ExpectNoCue(left, mark, 0f);
        var note = F(
            $"reach at {speed:0.0} m/s from chest height: climb jump {jumped}, still climbing {climbing}"
        );
        if (jumped)
            Fail("a brisk reach set off a climb lunge: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.61b (external review #19): with <c>Interaction.RequireTwoHands</c> off, a two-handed tool held in one hand
    /// works as in the flat game: the use press reaches the game and no NeedsTwoHands refusal fires (vr.61 proves the
    /// refusal with the setting on).
    /// </summary>
    IEnumerator TwoHandsOptionalStep()
    {
        var c = Character.localCharacter;
        var entry = Interact.InteractConfig.Instance.RequireTwoHandsEntry;
        var hook = FindItem(new List<Item>(SandboxActions.Items), "RescueHook");
        if (entry == null || hook == null)
        {
            stepSkip = "interact.physical not bound or no RescueHook";
            yield break;
        }
        bool was = entry.Value;
        OnRestore(() => entry.Value = was);
        entry.Value = false;
        yield return GiveAndHold(hook);
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        Affordance.ResetGate();
        float mark = CueMark();
        bool reached = false;
        Script(left).Trigger = 1f;
        for (float end = Now + 0.4f; Now < end; )
        {
            reached |= c.input.usePrimaryIsPressed;
            yield return null;
        }
        Script(left).Trigger = 0f;
        yield return Wait(0.2f);
        entry.Value = was;
        // The game's own refusal may follow (a hook with nothing in range is OutOfReach, RescueHook.cs:108-116, run
        // 57): that is the press reaching the game. Only the two-hand refusal is wrong here.
        var h = CueLog.Capture.Hand(left);
        bool twoHandRefusal = h.Reason == DenyReason.NeedsTwoHands && h.ReasonAt >= mark;
        var note =
            $"{HeldItem()?.name} one-handed: use reached the game {reached}, last cue {h.Reason}";
        if (!reached)
            Fail("the use press of a two-handed tool held one-handed was blocked: " + note);
        else if (twoHandRefusal)
            Fail("NeedsTwoHands with Interaction.RequireTwoHands off: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.44e: the hand holding an item does not hang from a piton; the free hand does. Vanilla keeps the item while
    /// hanging (ClimbHandle.cs:54-67, CharacterClimbing.cs:640-647, no unequip). With an Apple in the right hand, that
    /// hand closing on the piton keeps the Apple, does not hang and gets HandsFull; the empty left hand then hangs,
    /// and the Apple stays in the right hand.
    /// </summary>
    IEnumerator ItemHandNoHangStep()
    {
        var c = Character.localCharacter;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        if (apple == null || !ClimbHands.HandleHands)
        {
            stepSkip = apple == null ? "no Apple item" : "Interaction.PitonGripHang off";
            yield break;
        }
        yield return PlacePiton();
        if (testHandle == null)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var handle = testHandle;
        const bool itemHand = false;
        if (!SandboxActions.GiveItem(apple, itemHand))
        {
            stepSkip = SandboxActions.LastMessage;
            yield return LetGoAll(c);
            yield break;
        }
        yield return Until(() => Holding(apple), 5f, "given Apple not in hand");
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        spawned.Add(HeldItem().gameObject);
        yield return null;
        Affordance.ResetGate();
        float mark = CueMark();
        yield return MoveTo(itemHand, () => handle.transform.position, 0.5f);
        yield return GripTo(itemHand, 1f, 0.1f);
        string gripNote = PeakXR.Interact.PhysicalInteraction.LastGripNote;
        yield return ExpectCue(itemHand, DenyReason.HandsFull, mark, true, 1f);
        string cue = $"{cueNote}; {gripNote}";
        if (!stepOk)
            Note("item hand grip: " + gripNote);
        yield return Wait(0.4f);
        bool hungByItemHand = c.data.currentClimbHandle != null;
        bool keptAfterItemHand = Holding(apple);
        yield return GripTo(itemHand, 0f, 0.1f);
        yield return MoveTo(!itemHand, () => handle.transform.position, 0.5f);
        yield return GripTo(!itemHand, 1f, 0.1f);
        yield return WaitFor(() => c.data.currentClimbHandle == handle, 2f);
        bool hungByFreeHand = c.data.currentClimbHandle == handle;
        bool keptWhileHanging = Holding(apple);
        var note =
            $"item hand on the piton: hung {hungByItemHand}, kept the Apple {keptAfterItemHand} ({cue}); "
            + $"free hand: hung {hungByFreeHand}, Apple still held {keptWhileHanging}";
        if (hungByItemHand || !keptAfterItemHand)
            Fail("the hand holding an item hung from the piton: " + note);
        else if (!hungByFreeHand)
            Fail("the free hand did not hang: " + note);
        else if (!keptWhileHanging)
            Fail("hanging with the free hand lost the held item: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>Both hands' last cue records relative to <paramref name="since"/> (seconds after it; negative is
    /// before): which reason or notice fired and which ring was last captured, to tell a replaced cue from an undrawn
    /// one.</summary>
    static string CueState(float since)
    {
        string One(bool isLeft)
        {
            var h = UI.Affordances.CueLog.Capture.Hand(isLeft);
            return F(
                $"{(isLeft ? "L" : "R")} {(h.Notice ? "notice " : "")}{h.Reason} at {h.ReasonAt - since:+0.00;-0.00}, ring {h.RingReason} {h.RingDegrees:0.0} deg at {h.RingAt - since:+0.00;-0.00}"
            );
        }
        return One(true) + "; " + One(false);
    }

    IEnumerator Feel(bool left, CueFeel feel, float since, float within)
    {
        cueNote = null;
        yield return ExpectFeel(left, feel, since, within);
    }

    /// <summary>
    /// vr.46b: a lunge thrown while the game's climb jump cools down (<c>sinceClimbJump</c> at most 1 s,
    /// CharacterClimbing.cs:122) gets the TooSoon refusal at the throwing hand, and no jump happens
    /// (<c>RPCA_ClimbJump</c> would reset the clock, :237-242).
    /// </summary>
    IEnumerator LungeTooSoonStep()
    {
        var c = Character.localCharacter;
        var cfg = Interact.InteractConfig.Instance;
        if (!cfg.ArmLunge)
        {
            stepSkip = "Interaction.ArmLunge off";
            yield break;
        }
        yield return ToWall(headroom: LungeHeadroom);
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GripWall(left, LungeHoldUp);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return PullDown(left, 0.3f, 0.5f);
        UI.Affordances.Affordance.ResetGate();
        // As if a jump had just happened: the game's clock counts on from here.
        c.data.sinceClimbJump = 0.2f;
        float poked = Time.time;
        float speed = Mathf.Max(2f * cfg.LungeSpeed, 4f);
        var from = Script(left).Position;
        float mark = CueMark();
        yield return FlingRelease(left, from, speed);
        yield return ExpectCue(left, DenyReason.TooSoon, mark, false, 1f, seen: false);
        bool noJump = c.data.sinceClimbJump >= 0.2f + (Time.time - poked) - 0.1f;
        var note = F(
            $"fling 0.2 s after a jump: {cueNote}; no jump {noJump} (since jump {c.data.sinceClimbJump:0.00} s)"
        );
        if (!noJump)
            Fail("the game took a climb jump inside its cooldown: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.47: the OutOfReach refusal on a wall grip (<c>PhysicalInteraction.DenyClimb</c>) needs a hold more than
    /// <see cref="TraversalRefusal.GameWallReach"/> (1.35 m) from the camera (CharacterClimbing.cs:534, :552-561). The
    /// hold is where the hand's cast hits (<c>CastHold</c>): at most 0.15 m plus <c>ClimbGrabRadius</c> past the hand,
    /// which sits at most <see cref="MaxHandFromEye"/> from the eye the camera rides. So only a radius above 0.2 m (the
    /// setting allows 0.25) in a full-stretch reach along a slanted face can be refused for reach; at the default no
    /// player meets the cue. The step skips with the numbers rather than fake a 1.6 m arm (mod design issue,
    /// reported).
    /// </summary>
    IEnumerator ClimbDenyReachStep()
    {
        var cfg = Interact.InteractConfig.Instance;
        if (!cfg.PhysicalClimb)
        {
            stepSkip = "Interaction.GrabToClimb off";
            yield break;
        }
        float farthest = MaxHandFromEye + 0.15f + cfg.ClimbRadius;
        stepSkip =
            F(
                $"mod issue: a player's grip reaches holds at most {farthest:0.00} m from the eye ({MaxHandFromEye:0.00} m arm, 0.15 m cast, ClimbGrabRadius {cfg.ClimbRadius:0.00} m); OutOfReach needs more than {TraversalRefusal.GameWallReach:0.00} m"
            )
            + (
                farthest > TraversalRefusal.GameWallReach
                    ? ", reachable only at full stretch along a slanted face, not scripted"
                    : ", so no player meets it"
            );
    }

    /// <summary>
    /// vr.47b: with an Apple in the right hand, the free left hand grips a second Apple on the ground. The game pockets
    /// an item picked up while another is held (Item.Interact, Item.cs:578-591): the second one leaves the world with
    /// the Stowed notice at the left hand (pin, bell, light tick), and the first stays in the right hand.
    /// </summary>
    IEnumerator FreeHandPocketStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        if (apple == null)
        {
            Fail("no Apple item");
            yield break;
        }
        if (!SandboxActions.GiveItem(apple, false))
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => Holding(apple), 5f, "given Apple not in hand");
        if (!stepOk)
            yield break;
        yield return null;
        var held = HeldItem();
        spawned.Add(held.gameObject);
        var spot = GroundAhead(PropSetOut);
        if (!SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var second = go != null ? go.GetComponent<Item>() : null;
        yield return Wait(0.4f);
        if (second == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        // Run 48: the dropped Apple rolled 0.98 m out, past a stooped reach.
        yield return RestWithinReach(second, spot);
        bool InWorld() => second != null && second.gameObject.activeInHierarchy;
        var last = second.Center();
        UI.Affordances.Affordance.ResetGate();
        LookAtHand(true);
        float mark = CueMark();
        // The player stoops for it: a standing head put this hand 1.36 m from the eye in run 43.
        yield return ReachDown(
            true,
            () =>
            {
                if (InWorld())
                    last = second.Center();
                return last;
            },
            0.6f
        );
        if (OverReach(true) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(true, 1f, 0.1f);
        yield return WaitFor(() => !InWorld(), 2f);
        bool pocketed = !InWorld();
        // No pickup, no pocket notice: say which failed (run 50 read the missing pickup as a missing Stowed cue).
        if (!pocketed)
        {
            Fail(
                F(
                    $"the free hand did not pick up the second Apple (hand {Vector3.Distance(rig.Hand(true).position, last):0.00} m from it, stooped {headDrop:0.00} m), so no Stowed notice was due; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
                )
            );
            yield break;
        }
        yield return ExpectCue(true, DenyReason.Stowed, mark, false, 1f);
        bool kept = HeldItem() == held && !LocalHold.ItemInLeftHand;
        var note =
            $"second Apple pocketed {pocketed} ({cueNote}); first still in the right hand {kept}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}";
        if (!pocketed)
            Fail("the free hand did not take the second Apple: " + note);
        else if (!kept)
            Fail("the held Apple changed hands: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.48: a 40 degree slab at chest height (steeper than ground, flatter than the game's 50 degree minimum,
    /// <c>AcceptableGrabAngle</c>, CharacterClimbing.cs:511-532) and then a ceiling tilted 5 degrees (past its 170
    /// degree maximum). An open hand on the slope draws no chalk ring; a grip on each gets the TooFlat and then the
    /// Overhang cue, and no climb.
    /// </summary>
    IEnumerator ClimbDenySteepStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!Interact.InteractConfig.Instance.PhysicalClimb)
        {
            stepSkip = "Interaction.GrabToClimb off";
            yield break;
        }
        bool left = VRInput.LeftHanded;
        var forward = Flat(rig.Head.forward);
        var eye = rig.Head.position;
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "PeakXR_TestSlab";
        slab.layer = LayerMask.NameToLayer("Map");
        OnRestore(() =>
        {
            if (slab != null)
                UnityEngine.Object.Destroy(slab);
        });

        var slope =
            Vector3.up * Mathf.Cos(40f * Mathf.Deg2Rad) - forward * Mathf.Sin(40f * Mathf.Deg2Rad);
        var onSlope = eye + forward * 0.6f + Vector3.down * 0.4f;
        PlaceSlab(slab, onSlope, slope);
        UI.Affordances.Affordance.ResetGate();
        yield return MoveTo(left, () => onSlope + slope * 0.03f, 0.5f);
        yield return Wait(0.3f);
        bool ringOnSlope = ClimbHover.Drawn(left);
        // The clock starts at the grip: run 35's refusal came 0.96 s after a mark taken before the 0.8 s approach,
        // too late for its ring to show inside the 1 s window.
        LookAtHand(left);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.TooFlat, mark, false, 1f);
        // Run 33 had the reason but no ring: name the refusal that held the ring last, in case another replaced it.
        string slopeCue =
            cueNote
            ?? F(
                $"last refusal {UI.Affordances.Affordance.LastReason} ({(UI.Affordances.Affordance.LastLeft ? "left" : "right")}), {UI.Affordances.Affordance.Count} accepted in the whole run; {CueState(mark)}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
            );
        bool climbedSlope = c.data.isClimbing;
        yield return GripTo(left, 0f, 0.1f);
        yield return Wait(0.5f);

        var ceiling =
            -Vector3.up * Mathf.Cos(5f * Mathf.Deg2Rad) - forward * Mathf.Sin(5f * Mathf.Deg2Rad);
        var under = eye + forward * 0.35f + Vector3.up * 0.25f;
        PlaceSlab(slab, under, ceiling);
        UI.Affordances.Affordance.ResetGate();
        mark = CueMark();
        yield return MoveTo(left, () => under + ceiling * 0.03f, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Overhang, mark, false, 1f);
        bool climbedCeiling = c.data.isClimbing;
        var note =
            $"slope: ring {ringOnSlope}, {slopeCue}, climbing {climbedSlope}; ceiling: {cueNote}, climbing {climbedCeiling}";
        if (ringOnSlope)
            Fail("the chalk ring marked a slope the game refuses: " + note);
        else if (climbedSlope || climbedCeiling)
            Fail("a refused surface was climbed: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>A 0.8 m square, 0.2 m thick slab on the Map layer whose face at <paramref name="point"/> looks along
    /// <paramref name="normal"/>.</summary>
    static void PlaceSlab(GameObject slab, Vector3 point, Vector3 normal)
    {
        slab.transform.SetPositionAndRotation(
            point - normal * 0.1f,
            Quaternion.FromToRotation(Vector3.up, normal)
        );
        slab.transform.localScale = new Vector3(0.8f, 0.2f, 0.8f);
        Physics.SyncTransforms();
    }

    /// <summary>
    /// vr.49: carried by a condor (<c>CharacterData.carrierCondor</c>, set by <c>Condor.Carry</c>, Condor.cs:292-300) the
    /// game refuses every wall grab (CharacterClimbing.cs:562). A stand-in condor (inactive, so none of its own code
    /// runs) is poked in; a grip on the wall gets the Carried cue and no climb.
    /// </summary>
    IEnumerator CondorStep()
    {
        var c = Character.localCharacter;
        yield return ToWall();
        if (!wallReady)
            yield break;
        var fake = new GameObject("PeakXR_TestCondor");
        fake.SetActive(false);
        var condor = fake.AddComponent<Condor>();
        c.data.carrierCondor = condor;
        OnRestore(() =>
        {
            if (c != null && c.data.carrierCondor == condor)
                c.data.carrierCondor = null;
            UnityEngine.Object.Destroy(fake);
        });
        bool left = VRInput.LeftHanded;
        UI.Affordances.Affordance.ResetGate();
        float mark = CueMark();
        var hold = WallHold;
        yield return MoveTo(left, () => hold, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Carried, mark, false, 1f, seen: false);
        bool climbed = c.data.isClimbing;
        c.data.carrierCondor = null;
        var note =
            $"carried: {cueNote}; climbing {climbed}; shortcut: a stand-in Condor in carrierCondor (a real carry, Condor.cs:292-300, needs a scene condor with its nest)";
        if (climbed)
            Fail("climbed while carried: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.50: an open hand at the wall draws the chalk ring (<see cref="ClimbHover"/>) and one contact tick, with no
    /// refusal; closing it climbs, and the ring goes.
    /// </summary>
    IEnumerator ClimbHoverStep()
    {
        var c = Character.localCharacter;
        if (!Interact.InteractConfig.Instance.ClimbHoverRing)
        {
            stepSkip = "Interaction.ClimbHoverRing off";
            yield break;
        }
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        var hold = WallHold;
        // The rest pose can already touch a wall this close: start 0.35 m off it, past the retouch time, so the
        // first touch is the one measured.
        var off = hold + wallFace.normal * 0.35f;
        yield return MoveTo(left, () => off, 0.3f);
        yield return Wait(0.5f);
        bool drawnBefore = ClimbHover.Drawn(left);
        float mark = CueMark();
        yield return MoveTo(left, () => hold, 0.5f);
        yield return WaitFor(() => ClimbHover.Drawn(left), 1f);
        bool drawn = ClimbHover.Drawn(left);
        // Height of the ring over the wall's plane: lifted clear of rough rock (ClimbHover.Lift).
        float lift = Vector3.Dot(ClimbHover.Position(left) - wallFace.point, wallFace.normal);
        yield return Feel(left, CueFeel.Contact, mark, 0.5f);
        string contact =
            $"{cueNote} (ring off the wall {drawnBefore}, lifted {lift * 100f:0.0} cm)";
        yield return ExpectNoCue(left, mark, 0f);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        bool climbed = c.data.isClimbing;
        bool gone = !ClimbHover.Drawn(left);
        var note = $"ring drawn {drawn}, {contact}; climbing on grip {climbed}, ring gone {gone}";
        if (!drawn)
            Fail("no chalk ring under the open hand: " + note);
        else if (lift < ClimbHover.Lift - 0.005f)
            Fail("the chalk ring sits too close to the rock: " + note);
        else if (!climbed)
            Fail($"grip did not climb: {note}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}");
        else if (!gone)
            Fail("the ring stayed on the gripping hand: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.51: jump and grab. The player is dropped from 2.5 m above the ground in front of the wall; the hand catches
    /// the face while airborne, and the grip climb starts mid-fall (forced attempt, CharacterClimbing.cs:567).
    /// </summary>
    IEnumerator AirGripStep()
    {
        var c = Character.localCharacter;
        yield return ToWall(headroom: LungeHeadroom);
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, wallStand + Vector3.up * 2.5f, false);
        yield return WaitFor(() => !c.warping, 2f);
        var hold = WallHold;
        yield return MoveTo(
            left,
            () =>
                hold = WallInFront(c, wallBase, 0.3f, out var face)
                    ? face.point + face.normal * 0.04f
                    : hold,
            0.12f
        );
        bool airborne = !c.data.isGrounded;
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.05f);
        yield return WaitFor(() => c.data.isClimbing, 1.5f);
        bool climbed = c.data.isClimbing;
        yield return ExpectNoCue(left, mark, 0f);
        var note =
            $"airborne at the grip {airborne}, climbing {climbed}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}";
        if (!airborne)
            stepSkip = "landed before the grip: " + note;
        else if (!climbed)
            Fail("the air grip did not catch the wall: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.52: climbing with both stamina bars empty, an upward arm pull is dropped by the game
    /// (CharacterClimbing.cs:297-310): the body does not rise, and the pulling hand gets the NoStamina cue with the
    /// stamina bar turned red.
    /// </summary>
    IEnumerator ClimbEmptyStep()
    {
        var c = Character.localCharacter;
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        if (!StartEmpty())
        {
            stepSkip = SandboxActions.LastMessage;
            yield return LetGoAll(c);
            yield break;
        }
        yield return WaitFor(() => c.data.outOfStaminaFor > 0.2f, 3f);
        UI.Affordances.Affordance.ResetGate();
        float mark = CueMark();
        float y0 = c.Center.y;
        yield return PullDown(left, 0.4f, 0.6f);
        yield return ExpectCue(left, DenyReason.NoStamina, mark, true, 1f, seen: false);
        float rise = c.Center.y - y0;
        var note = F(
            $"out of stamina {c.data.outOfStaminaFor:0.0} s: rose {rise:0.00} m for a 0.40 m pull; {cueNote}"
        );
        if (rise >= 0.1f)
            Fail("climbed without stamina: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.53: the game ends a climb the hand still grips (<c>StopAnyClimbing</c>, CharacterClimbing.cs:626-638, as an
    /// overhang, a frog or a ragdoll would): the hand gets the hard "torn off" beat and the cancel feel.
    /// </summary>
    IEnumerator ForcedDetachStep()
    {
        var c = Character.localCharacter;
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.3f);
        float mark = CueMark();
        c.refs.climbing.StopAnyClimbing();
        yield return Feel(left, CueFeel.Cancel, mark, 1f);
        Note($"torn off while gripping: {cueNote}");
        yield return LetGoAll(c);
    }

    /// <summary>Hangs the <paramref name="left"/> hand on <see cref="testHandle"/>; fails the step otherwise.</summary>
    IEnumerator HangOnHandle(bool left)
    {
        var c = Character.localCharacter;
        var handle = testHandle;
        yield return Wait(0.5f);
        yield return MoveTo(left, () => handle.transform.position, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => c.data.currentClimbHandle == handle,
            2f,
            "grip on the piton handle did not hang"
        );
    }

    /// <summary>
    /// vr.44b: hanging from a piton rests the climber (stamina regenerates, CharacterClimbing.cs:160-227); with the bar
    /// low, the gripping hand feels the slow progress fill.
    /// </summary>
    IEnumerator HandleRestStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.HandleHands)
        {
            stepSkip = "Interaction.PitonGripHang off";
            yield break;
        }
        yield return PlacePiton();
        if (testHandle == null)
        {
            yield return LetGoAll(c);
            yield break;
        }
        bool left = VRInput.LeftHanded;
        yield return HangOnHandle(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        if (!SandboxActions.SetStamina(0.3f, 0f))
        {
            stepSkip = SandboxActions.LastMessage;
            yield return LetGoAll(c);
            yield break;
        }
        OnRestore(() => SandboxActions.SetGodMode(true));
        var piton = testHandle.GetComponent<ShittyPiton>();
        float mark = CueMark();
        yield return Feel(left, CueFeel.Progress, mark, 1.5f);
        if (!stepOk && piton != null && piton.crack != null && piton.crack.activeSelf)
            stepSkip =
                "the piton began to crack before the rest was felt (vr.56 covers the warning)";
        else
            Note($"resting at {c.GetTotalStamina():0.00} stamina: {cueNote}");
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.55: a piton someone else hangs from (<c>ClimbHandle.hanger</c>, ClimbHandle.cs:7, :99-102; poked with an
    /// inactive stand-in character) refuses the grab: the grip gets the Occupied cue with the piton turned red, and
    /// neither hangs nor climbs the wall behind it.
    /// </summary>
    IEnumerator HandleOccupiedStep()
    {
        var c = Character.localCharacter;
        yield return PlacePiton();
        if (testHandle == null)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var handle = testHandle;
        var fake = new GameObject("PeakXR_TestHanger");
        fake.SetActive(false);
        var other = fake.AddComponent<Character>();
        handle.hanger = other;
        OnRestore(() =>
        {
            if (handle != null && handle.hanger == other)
                handle.hanger = null;
            UnityEngine.Object.Destroy(fake);
        });
        bool left = VRInput.LeftHanded;
        yield return Wait(0.5f);
        UI.Affordances.Affordance.ResetGate();
        float mark = CueMark();
        yield return MoveTo(left, () => handle.transform.position, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Occupied, mark, true, 1f, seen: false);
        yield return Wait(0.3f);
        bool hung = c.data.currentClimbHandle != null;
        bool climbed = c.data.isClimbing;
        handle.hanger = null;
        var note = $"taken handle: {cueNote}; hanging {hung}, climbing {climbed}";
        if (hung || climbed)
            Fail("a taken handle was used: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.56: a hammered piton gives way (<c>ShittyPiton</c>, ShittyPiton.cs:42-124: cracks every 1.5 s after 1-5 s of
    /// hanging, 4 cracks break it). The gripping hand feels the danger warning once it cracks, and the hard "torn off"
    /// beat and cancel when it breaks.
    /// </summary>
    IEnumerator FragilePitonStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.HandleHands)
        {
            stepSkip = "Interaction.PitonGripHang off";
            yield break;
        }
        yield return PlacePiton();
        if (testHandle == null)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var piton = testHandle.GetComponent<ShittyPiton>();
        string source = "from the prefab";
        if (piton == null || piton.crack == null)
        {
            // The hammered piton is sturdy, and the fragile ones are scenery props with no prefab to spawn
            // (PSM_PitonNormal.cs). Fit the game's own ShittyPiton to it: its timer, cracks and break run unchanged;
            // only the crack and burst visuals are empty stand-ins. It must sit beside the handle's PhotonView.
            var host = testHandle.gameObject;
            if (!host.TryGetComponent<PhotonView>(out _))
            {
                Fail("the hammered piton's handle has no PhotonView beside it for a ShittyPiton");
                yield return LetGoAll(c);
                yield break;
            }
            var crack = new GameObject("PeakXR_TestCrack");
            var burst = new GameObject("PeakXR_TestBreak");
            crack.SetActive(false);
            burst.SetActive(false);
            crack.transform.SetParent(host.transform, false);
            burst.transform.SetParent(host.transform, false);
            OnRestore(() =>
            {
                UnityEngine.Object.Destroy(crack);
                UnityEngine.Object.Destroy(burst);
            });
            piton = host.AddComponent<ShittyPiton>();
            piton.crack = crack;
            piton.vfx = burst;
            piton.cracKSFX = Array.Empty<SFX_Instance>();
            piton.breakSFX = Array.Empty<SFX_Instance>();
            source = "fitted";
            // Start subscribes to the handle's hang events (ShittyPiton.cs:37-43) before the hang.
            yield return null;
        }
        bool left = VRInput.LeftHanded;
        yield return HangOnHandle(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        // The crack starts 1-5 s into the hang (ShittyPiton.cs:42, :72-75).
        yield return WaitFor(() => piton == null || piton.crack.activeSelf, GameTiming.Budget(5f));
        // The window runs from the mark to 3 s after the crack showed (run 40 gave the warning only 1 s from the mark,
        // long gone by the first crack).
        yield return Feel(left, CueFeel.Danger, mark, CueMark() - mark + 3f);
        string warning = cueNote;
        float broke = CueMark();
        // Four cracks 1.5 s apart, the first at once, break it (ShittyPiton.cs:20, :22, :63-66, :89-91): 4.5 s.
        yield return WaitFor(() => c.data.currentClimbHandle == null, GameTiming.Budget(4.5f));
        bool fell = c.data.currentClimbHandle == null;
        if (fell)
            // The window runs to 1.5 s past the break (run 41 gave the Cancel only 1 s from the mark).
            yield return Feel(left, CueFeel.Cancel, broke, CueMark() - broke + 1.5f);
        else
            Fail("the cracking piton never broke under the hang");
        Note(
            $"fragile piton {source}; cracking: {warning}; broke {fell}{(fell ? ": " + cueNote : "")}"
        );
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.42b (D7): on a rope the player put out, the head swaying along the swing at its own rate pumps it: the climb
    /// point swings farther than with a still head, and nothing refuses.
    /// </summary>
    IEnumerator RopePumpStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ClimbHands.RopeHands || !ClimbHands.RopePumping)
        {
            stepSkip = "Interaction.RopeHandOverHand or Interaction.RopeSwingPump off";
            yield break;
        }
        // A 2.5 m rope from 4.2 m up hangs clear of the ground: run 34's full-length rope lay on it and could not swing.
        yield return HangRope(4.2f, 10f);
        if (testRope == null)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(1f);
        var api = testRope.climbingAPI;
        // The swing is the climb point's travel along the sway axis (peak to peak). Run 33 measured its distance from
        // GetPosition(0), which is not always the anchor (segment order varies), and read a constant 2.24 m.
        var along = Flat(VRInput.HeadRotation * Vector3.forward);
        var axis = Flat(rig.Origin.TransformDirection(along));
        float lo = float.MaxValue,
            hi = float.MinValue;
        void Track()
        {
            float x = Vector3.Dot(api.GetPosition(c.data.ropePercent), axis);
            lo = Mathf.Min(lo, x);
            hi = Mathf.Max(hi, x);
        }
        float end = Now + 3f;
        while (Now < end)
        {
            Track();
            yield return null;
        }
        float still = hi - lo;
        lo = float.MaxValue;
        hi = float.MinValue;
        // Pump as a person does: lean the head the way the swing is moving, up to 0.25 m each way at 1 m/s. Run 35's
        // fixed 0.35 Hz sway ran out of phase with this rope's own swing and damped it (0.23 m still, 0.07 m pumped).
        var hands = applyHands;
        OnRestore(() =>
        {
            if (VRInput.TestOverride != hands && VRInput.TestOverride != IdleHands)
                VRInput.TestOverride = finished ? null : IdleHands;
        });
        var baseHead = VRInput.HeadPosition;
        float offset = 0f;
        float target = 0.25f;
        VRInput.TestOverride = () =>
        {
            hands();
            VRInput.HeadPosition = baseHead + along * offset;
        };
        float mark = CueMark();
        float push = 0f;
        float lastX = Vector3.Dot(api.GetPosition(c.data.ropePercent), axis);
        float measureFrom = Now + 6f;
        bool measuring = false;
        end = Now + 9f;
        while (Now < end && c.data.isRopeClimbing)
        {
            float dt = Time.deltaTime;
            float x = Vector3.Dot(api.GetPosition(c.data.ropePercent), axis);
            float v = dt > 0f ? (x - lastX) / dt : 0f;
            lastX = x;
            if (Mathf.Abs(v) > 0.05f)
                target = 0.25f * Mathf.Sign(v);
            offset = Mathf.MoveTowards(offset, target, dt);
            // The swing built up by pumping: its last 3 s.
            if (!measuring && Now >= measureFrom)
            {
                measuring = true;
                lo = float.MaxValue;
                hi = float.MinValue;
            }
            Track();
            push = Mathf.Max(push, ClimbHands.LastPump);
            yield return null;
        }
        float pumped = hi > lo ? hi - lo : 0f;
        VRInput.TestOverride = hands;
        yield return ExpectNoCue(left, mark, 0f);
        bool owned = testRope.photonView != null && testRope.photonView.IsMine;
        var note = F(
            $"swing peak to peak still {still:0.00} m, pumped {pumped:0.00} m (push up to {push:0.0} m/s²), rope owned {owned}"
        );
        if (push <= 0f)
            Fail("the head sway never pushed the rope: " + note);
        else if (pumped < still + 0.1f)
            Fail("pumping did not swing the rope farther: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.42d: on a rope someone else owns (forced: offline every rope is the player's), only the owner simulates it
    /// (Rope.cs:155), so nothing is pushed. Looking around (head turns with a little drift) and one lean to grab get no
    /// refusal at all; a clear pump (a 0.25 m lean back and forth at 0.5 Hz, <see cref="PumpIntent"/>) gets exactly one
    /// NotHere for the hold.
    /// </summary>
    IEnumerator RopePumpNotMineStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands || !ClimbHands.RopePumping || !Affordance.Enabled)
        {
            stepSkip = !Affordance.Enabled
                ? "ui.affordance not installed"
                : "Interaction.RopeHandOverHand or Interaction.RopeSwingPump off";
            yield break;
        }
        yield return HangRope(4.2f, 10f);
        if (testRope == null)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(1f);
        var along = Flat(VRInput.HeadRotation * Vector3.forward);
        var hands = applyHands;
        OnRestore(() =>
        {
            ClimbHands.SelfTestNotMine = false;
            if (VRInput.TestOverride != hands && VRInput.TestOverride != IdleHands)
                VRInput.TestOverride = finished ? null : IdleHands;
        });
        ClimbHands.SelfTestNotMine = true;
        Affordance.ResetGate();
        var baseHead = VRInput.HeadPosition;
        var baseTurn = VRInput.HeadRotation;
        var side = Vector3.Cross(Vector3.up, along);
        float push = 0f;
        // Head pose by phase: 0 look around, 1 one lean to grab, 2 a clear pump.
        int phase = 0;
        float start = Now;
        VRInput.TestOverride = () =>
        {
            hands();
            float t = Now - start;
            switch (phase)
            {
                case 0:
                    // Turn to look 60 degrees either way; the tracked eye drifts a few cm about the neck.
                    VRInput.HeadRotation =
                        Quaternion.AngleAxis(60f * Mathf.Sin(1.2f * t), Vector3.up) * baseTurn;
                    VRInput.HeadPosition =
                        baseHead
                        + side * (0.04f * Mathf.Sin(1.2f * t))
                        + along * (0.03f * Mathf.Cos(1.7f * t));
                    break;
                case 1:
                    VRInput.HeadRotation = baseTurn;
                    VRInput.HeadPosition =
                        baseHead + along * (0.3f * Mathf.Sin(Mathf.PI * Mathf.Min(t, 2f) / 2f));
                    break;
                default:
                    VRInput.HeadRotation = baseTurn;
                    VRInput.HeadPosition =
                        baseHead + along * (0.25f * Mathf.Sin(2f * Mathf.PI * 0.5f * t));
                    break;
            }
        };
        IEnumerator Hold(float seconds)
        {
            for (float end = Now + seconds; Now < end && c.data.isRopeClimbing; )
            {
                push = Mathf.Max(push, ClimbHands.LastPump);
                yield return null;
            }
        }
        int denies = Affordance.Count;
        float quietMark = CueMark();
        yield return Hold(4f);
        phase = 1;
        start = Now;
        yield return Hold(3f);
        int quietDenies = Affordance.Count - denies;
        yield return ExpectNoCue(left, quietMark, 0f);
        phase = 2;
        start = Now;
        denies = Affordance.Count;
        float mark = CueMark();
        yield return Hold(6f);
        VRInput.TestOverride = hands;
        ClimbHands.SelfTestNotMine = false;
        yield return ExpectCue(
            left,
            DenyReason.NotHere,
            mark,
            tint: false,
            within: 0f,
            seen: false
        );
        var cue = cueNote;
        int count = Affordance.Count - denies;
        yield return LetGoAll(c);
        if (!stepOk)
            yield break;
        if (push > 0f)
            Fail($"pushed a rope someone else owns ({push:0.0} m/s²)");
        else if (quietDenies > 0)
            Fail($"looking around or one lean refused {quietDenies} times");
        else if (count > 1)
            Fail($"refused {count} times in one hold: {cue}");
        else
            Note(
                $"looking around and one lean: no refusal; clear pump: {cue}; shortcut: ownership forced by ClimbHands.SelfTestNotMine (offline a rope taken off the player reads RopeSyncer's remote segments, which never arrive, Rope.cs:186-193)"
            );
    }

    /// <summary>
    /// vr.42c: the climb point near the rope's top (<c>ropePercent</c> poked to 0.02), one more upward pull reaches the
    /// anchor, where the game clamps it with no event (CharacterRopeHandling.cs:64-65): the hand feels the end-stop knock.
    /// </summary>
    IEnumerator RopeEndStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        yield return HangRope();
        if (testRope == null)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        // Near the anchor: the higher end. Percent follows segment order, which varies (RopeClimbingAPI.cs:20-25), and
        // run 33 poked 0.02 at the end lying on the ground, where the steep segment dropped the climber (:57-61).
        var api = testRope.climbingAPI;
        bool zeroIsTop = api.GetPosition(0f).y > api.GetPosition(1f).y;
        float topEnd = zeroIsTop ? 0f : 1f;
        c.data.ropePercent = zeroIsTop ? 0.02f : 0.98f;
        yield return Wait(0.6f);
        float mark = CueMark();
        yield return PullDown(left, 0.4f, 0.6f);
        yield return Feel(left, CueFeel.EndStop, mark, 1f);
        Note(
            F(
                $"rope percent {c.data.ropePercent:0.000} after the pull (top end {topEnd:0}), on the rope {c.data.isRopeClimbing}: {cueNote}"
            )
        );
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.57: out of stamina, a grip on the rope is refused (<c>RopeSegment.IsInteractible</c>, RopeSegment.cs:49-58):
    /// the NoStamina cue with the stamina bar red, no rope climb, and the grip does not fall through to anything else.
    /// </summary>
    IEnumerator RopeDenyStaminaStep()
    {
        var c = Character.localCharacter;
        yield return HangRope();
        if (testRope == null)
            yield break;
        if (!StartEmpty())
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return WaitFor(() => PeakXR.Interact.PhysicalInteraction.OutOfStamina(c), 2f);
        yield return GripRefusedLine(
            DenyReason.NoStamina,
            () => c.data.isRopeClimbing,
            RopeSegmentNear(VRInput.LeftHanded, out _, out _)
        );
    }

    /// <summary>
    /// vr.58: a rope that is not anchored (<c>Rope.attachmenState</c> poked to unattached, so <c>isClimbable</c> is
    /// false, Rope.cs:115-121) refuses the grip with the Unanchored cue and the rope turned red.
    /// </summary>
    IEnumerator RopeUnanchoredStep()
    {
        var c = Character.localCharacter;
        yield return HangRope();
        if (testRope == null)
            yield break;
        var rope = testRope;
        bool left = VRInput.LeftHanded;
        var segment = RopeSegmentNear(left, out _, out _);
        if (segment == null)
        {
            Fail("rope has no segments");
            yield break;
        }
        // The hand waits beside the rope first: unanchored, the owner's rope may start to sag.
        yield return MoveTo(
            left,
            () => segment.position + Flat(c.Center - segment.position) * 0.3f,
            0.5f
        );
        rope.attachmenState = Rope.ATTACHMENT.unattached;
        OnRestore(() =>
        {
            if (rope != null)
                rope.attachmenState = Rope.ATTACHMENT.anchored;
        });
        yield return null;
        yield return null;
        yield return GripRefusedLine(DenyReason.Unanchored, () => c.data.isRopeClimbing, segment);
        rope.attachmenState = Rope.ATTACHMENT.anchored;
    }

    /// <summary>The main hand grips <paramref name="at"/> on a line the game refuses: the cue for
    /// <paramref name="reason"/> with the tint, then neither <paramref name="onLine"/> nor a wall climb.</summary>
    IEnumerator GripRefusedLine(DenyReason reason, Func<bool> onLine, Transform at)
    {
        var c = Character.localCharacter;
        bool left = VRInput.LeftHanded;
        if (at == null)
        {
            Fail("nothing to grip");
            yield break;
        }
        UI.Affordances.Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        yield return MoveTo(left, () => at.position, 0.4f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        // Close the grip while the hand keeps tracking the line: under a vine the body is falling, and a hand left
        // still in tracking space falls with it (run 36's vr.59: the vine was found but the hand had dropped off it).
        Script(left).Grip = 1f;
        yield return MoveTo(left, () => at.position, 0.15f);
        yield return ExpectCue(left, reason, mark, true, 1f);
        yield return Wait(0.3f);
        bool on = onLine();
        bool climbing = c.data.isClimbing;
        var note =
            $"{reason}: {cueNote}; on the line {on}, wall climbing {climbing}; {TraversalGrips.LastNote}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}";
        if (on || climbing)
            Fail("the refused grip still took hold: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.43b: the game moves along a vine by stick Y times a sign from the look direction
    /// (CharacterVineClimbing.cs:67-68, <c>JungleVine.GetSign</c>, JungleVine.cs:426-435). Hanging from the hand's grab
    /// (<see cref="GrabVine"/>), the player turns 180 degrees with the hand kept on the vine; the same world-space hand
    /// pull still moves the body the way the hand pulls (ClimbHands re-signs it).
    /// </summary>
    IEnumerator VineLookBackStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        bool left = VRInput.LeftHanded;
        yield return GrabVine(left);
        if (vineSpot == null || !stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var vine = testVine;
        float p0 = c.data.vinePercent;
        var along =
            vine.GetPosition(Mathf.Clamp01(p0 + 0.05f), 1, 1)
            - vine.GetPosition(Mathf.Clamp01(p0 - 0.05f), 1, 1);
        along = along.sqrMagnitude > 1e-6f ? along.normalized : Flat(rig.Head.forward);
        float signBefore = vine.GetSign(c.data.lookDirection_Flat, p0);
        // The turn swings the tracking space round the eye; the gripping hand stays where it holds the vine.
        var held = rig.Origin.TransformPoint(Script(left).Position);
        rig.RotateYaw(180f);
        SetHand(left, rig.Origin.InverseTransformPoint(held));
        yield return Wait(0.6f);
        float signAfter = vine.GetSign(c.data.lookDirection_Flat, c.data.vinePercent);
        bool still = c.data.isVineClimbing;
        if (!still)
        {
            Fail("turning round let go of the vine");
            yield return LetGoAll(c);
            yield break;
        }
        var start = vine.GetPosition(c.data.vinePercent, 1, 1);
        float mark = CueMark();
        // Pulling the hand back along the vine moves the body forward along it, whichever way the head looks.
        yield return MoveTrack(
            left,
            Script(left).Position + rig.Origin.InverseTransformVector(-along * 0.4f),
            0.6f
        );
        yield return Wait(0.3f);
        float moved = Vector3.Dot(vine.GetPosition(c.data.vinePercent, 1, 1) - start, along);
        yield return ExpectNoCue(left, mark, 0f);
        var note = F(
            $"look sign {signBefore} then {signAfter}; moved {moved:0.00} m along the pull for a 0.40 m pull"
        );
        if (moved < 0.1f)
            Fail("travel did not follow the hands after looking back: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.59: out of stamina, a grip on a vine is refused (<c>JungleVine.IsInteractible</c>, JungleVine.cs:96-108): the
    /// NoStamina cue with the stamina bar red, and no vine climb.
    /// </summary>
    IEnumerator VineDenyStaminaStep()
    {
        var c = Character.localCharacter;
        // Empty first, then warp and grip at once: the warp leaves the body in the air under the vine, and after
        // run 37's 2 s drain it had fallen 3 m and was falling too fast for the hand to stay on the vine.
        if (!StartEmpty())
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return WaitFor(() => PeakXR.Interact.PhysicalInteraction.OutOfStamina(c), 2f);
        // Standing under the vine (VineLedge), as vr.43 grabs it.
        yield return VineLedge();
        if (vineSpot == null)
            yield break;
        yield return GripRefusedLine(DenyReason.NoStamina, () => c.data.isVineClimbing, vineSpot);
    }

    /// <summary>
    /// vr.48b: an easy "ladder" hold (a <c>ClimbModifierSurface</c> with stamina use 0.3, the game's ladder reticle
    /// threshold, CharacterClimbing.cs:140-143) added to the wall: while the hand holds it, the soft texture ticks
    /// reach that hand (<see cref="SurfaceTexture"/>).
    /// </summary>
    IEnumerator SurfaceTextureStep()
    {
        var c = Character.localCharacter;
        if (!Interact.InteractConfig.Instance.ClimbSurfaceFeel)
        {
            stepSkip = "Interaction.ClimbSurfaceFeel off";
            yield break;
        }
        yield return ToWall();
        if (!wallReady)
            yield break;
        var surface = wallFace.collider.gameObject.AddComponent<ClimbModifierSurface>();
        surface.staminaUsageMultiplier = 0.3f;
        OnRestore(() =>
        {
            if (surface != null)
                UnityEngine.Object.Destroy(surface);
        });
        bool left = VRInput.LeftHanded;
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        yield return WaitFor(() => ClimbCues.Since(ClimbCue.Texture, left, mark, out _), 2f);
        bool felt = ClimbCues.Since(ClimbCue.Texture, left, mark, out float amplitude);
        bool easy = c.data.climbMod == surface;
        yield return ExpectNoCue(left, mark, 0f);
        var note = F($"on the easy hold {easy}: texture felt {felt} at {amplitude:0.00}");
        if (!easy)
            Fail("the climb did not read the easy hold, so the texture is not its: " + note);
        else if (!felt)
            Fail("no surface texture under the climbing hand: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.51b: a falling catch. Dropped from 2.5 m in front of the wall, the hand grabs once the body falls faster than
    /// 3 m/s; the game turns the fall into a slide (CharacterClimbing.cs:371-374) and the hand feels the hard ratchet
    /// (<see cref="ClimbFeel.Catch"/>), stronger than a clean grab's.
    /// </summary>
    IEnumerator FallingCatchStep()
    {
        var c = Character.localCharacter;
        if (!Interact.InteractConfig.Instance.ClimbSurfaceFeel)
        {
            stepSkip = "Interaction.ClimbSurfaceFeel off";
            yield break;
        }
        yield return ToWall(headroom: LungeHeadroom);
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, wallStand + Vector3.up * 2.5f, false);
        yield return WaitFor(() => !c.warping, 2f);
        yield return WaitFor(() => c.data.avarageVelocity.y < -3f, 1f);
        float fall = -c.data.avarageVelocity.y;
        var hold = WallHold;
        yield return MoveTo(
            left,
            () =>
                hold = WallInFront(c, wallBase, 0.3f, out var face)
                    ? face.point + face.normal * 0.04f
                    : hold,
            0.08f
        );
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.03f);
        yield return WaitFor(() => c.data.isClimbing, 1.5f);
        bool climbed = c.data.isClimbing;
        bool felt = ClimbCues.Since(ClimbCue.Catch, left, mark, out float amplitude);
        var note = F(
            $"falling {fall:0.0} m/s at the grip: climbing {climbed}, catch felt {felt} at {amplitude:0.00}, the grab read a {PeakXR.Interact.PhysicalInteraction.LastCatchFall:0.0} m/s fall; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (!climbed)
            Fail("the falling grab did not catch: " + note);
        else if (!felt || amplitude < ClimbFeel.Catch(ClimbFeel.CleanCatch).Amplitude)
            Fail("the catch did not ratchet: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.54: a 1.7 m ledge (a Map-layer block, its top walkable) climbed hand over hand. The pull that tops out ends
    /// the game's climb on the flat top (CharacterClimbing.cs:265-271) and starts the mantle (<c>TryStartMantle</c>):
    /// the body walks onto the ledge and the gripping hand feels the settle tick.
    /// </summary>
    IEnumerator MantleStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!Interact.InteractConfig.Instance.PhysicalClimb)
        {
            stepSkip = "Interaction.GrabToClimb off";
            yield break;
        }
        var forward = Flat(rig.Head.forward);
        var feet = Feet(c);
        float top = feet.y + 1.7f;
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "PeakXR_TestLedge";
        block.layer = LayerMask.NameToLayer("Map");
        OnRestore(() =>
        {
            if (block != null)
                UnityEngine.Object.Destroy(block);
        });
        var center = feet + forward * (0.55f + 0.75f);
        center.y = top - 1.5f;
        block.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward));
        block.transform.localScale = new Vector3(2f, 3f, 1.5f);
        Physics.SyncTransforms();
        yield return Wait(0.3f);

        float mark = CueMark();
        bool hand = VRInput.LeftHanded;
        bool started = false;
        string far = null;
        for (int i = 0; i < 6; i++)
        {
            if (
                !Physics.Raycast(
                    c.Center + Vector3.up * 0.5f,
                    forward,
                    out var face,
                    1.5f,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                || face.collider.gameObject != block
            )
                break;
            var at = face.point + face.normal * 0.04f;
            yield return MoveTo(hand, () => at, 0.25f);
            far ??= OverReach(hand);
            yield return GripTo(hand, 1f, 0.08f);
            if (!started)
            {
                yield return WaitFor(() => c.data.isClimbing, 1.5f);
                started = c.data.isClimbing;
                if (!started)
                    break;
            }
            yield return GripTo(!hand, 0f, 0.05f);
            yield return PullDown(hand, 0.45f, 0.45f);
            if (!c.data.isClimbing)
                break;
            hand = !hand;
        }
        // The highest the feet got in that second: a body that went up and slid back off the lip reads differently
        // from one the mantle never lifted (run 54: feet 1.67 m under the top after a mantle that fired).
        float peakFeet = Feet(c).y;
        for (float end = Now + 1f; Now < end; )
        {
            peakFeet = Mathf.Max(peakFeet, Feet(c).y);
            yield return null;
        }
        float mantleAt = PeakXR.Interact.PhysicalInteraction.LastMantleAt;
        bool mantled = mantleAt >= mark;
        bool onTop = Feet(c).y >= top - 0.3f;
        Note(F($"feet peaked {peakFeet - top:0.00} m from the top after the pulls"));
        string settle = "no settle tick checked";
        if (mantled)
        {
            // The tick goes to whichever hand still gripped the lip when the mantle began; the loop may already have
            // switched `hand` to the open one (run 35).
            bool gripping = UI.Affordances.CueLog.Capture.Felt(
                !hand,
                CueFeel.Contact,
                mantleAt - 0.01f,
                out _
            )
                ? !hand
                : hand;
            yield return Feel(gripping, CueFeel.Contact, mantleAt - 0.01f, 0.1f);
            settle = cueNote;
        }
        var note = F(
            $"climb started {started}, mantle {mantled}, feet {Feet(c).y - top:0.00} m from the top; {settle}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (far != null)
            Fail(far + ": " + note);
        else if (!started)
            Fail("the ledge face did not climb: " + note);
        else if (!mantled)
            Fail("topping out did not start the mantle: " + note);
        else if (!onTop)
            Fail("the mantle did not carry the body onto the ledge: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.58b: a breakable rope anchor (<c>BreakableRopeAnchor</c>, BreakableRopeAnchor.cs:34-70, added to the test
    /// rope's anchor when its prefab has none) gives way 3-8 s into the climb. In its last seconds the gripping hand
    /// feels the danger warning at the anchor.
    /// </summary>
    IEnumerator BreakableAnchorStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        yield return HangRope();
        if (testRope == null || testAnchor == null)
            yield break;
        bool added = !testAnchor.TryGetComponent<BreakableRopeAnchor>(out var breakable);
        if (added)
            breakable = testAnchor.AddComponent<BreakableRopeAnchor>();
        yield return null;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        // Its timer runs only while someone climbs the rope, up to breakableTimeMinMax.y (BreakableRopeAnchor.cs:11,
        // :31, :49-52).
        yield return Feel(
            left,
            CueFeel.Danger,
            mark,
            GameTiming.Budget(breakable.breakableTimeMinMax.y)
        );
        Note($"anchor {(added ? "added" : "from the prefab")}: {cueNote}");
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.43c: a fast grab slides along the vine with input ignored (<c>Sliding</c>, CharacterVineClimbing.cs:102-127).
    /// The hand grabs the vine as in vr.43 (<see cref="GrabVine"/>), then the slide speed <c>attachVel</c> is poked to
    /// 10. The gripping hand feels and hears the whirr.
    /// </summary>
    IEnumerator VineSlideStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands || !Interact.InteractConfig.Instance.ClimbSurfaceFeel)
        {
            stepSkip = "Interaction.RopeHandOverHand or Interaction.ClimbSurfaceFeel off";
            yield break;
        }
        bool left = VRInput.LeftHanded;
        yield return GrabVine(left);
        if (vineSpot == null || !stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        // CharacterVineClimbing.attachVel (CharacterVineClimbing.cs:32, private): Sliding() is |attachVel| > 3.
        Framework.GameMember.Field<CharacterVineClimbing, float>("attachVel")(c.refs.vineClimbing) =
            10f;
        yield return WaitFor(() => ClimbCues.Since(ClimbCue.Whirr, left, mark, out _), 1f);
        bool felt = ClimbCues.Since(ClimbCue.Whirr, left, mark, out float amplitude);
        var note = F(
            $"sliding: whirr felt {felt} at {amplitude:0.00}; shortcut: attachVel poked (a grab moving fast along the vine sets it, CharacterVineClimbing.cs:166)"
        );
        if (!felt)
            Fail("a sliding vine made no whirr: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>Opens both hands, ends any rope, vine, handle or wall climb, and goes back to the spawn.</summary>
    IEnumerator LetGoAll(Character c)
    {
        Script(true).Grip = 0f;
        Script(false).Grip = 0f;
        yield return Wait(0.5f);
        var d = c.data;
        if (d.isRopeClimbing || d.isVineClimbing || d.currentClimbHandle != null || d.isClimbing)
            c.refs.climbing.StopAnyClimbing();
        SandboxActions.WarpToSpawn();
        yield return Wait(2f);
        // A body still falling from a vine or rope warp fails the next step's settle (run 33's vr.43b).
        yield return WaitFor(() => c.data.isGrounded, 3f);
        // And the view done settling after the warp, or the next step's settle starts against it (run 40's vr.59).
        yield return WaitFor(() => !Rig.RigDriver.ViewSettling, 3f);
    }
}
