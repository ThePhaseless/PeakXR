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
        yield return Restoring("vr.46-lunge", 40f, LungeStep());
        yield return ClimbSteps();
    }

    /// <summary>One step per climb, rope, handle and grab mechanic (interact.physical). Each asserts the motion's
    /// outcome and its cue; the rope and prop steps return to the spawn after themselves.</summary>
    IEnumerator ClimbSteps()
    {
        yield return Restoring("vr.46b-lunge-too-soon", 30f, LungeTooSoonStep());
        yield return Restoring("vr.47-climb-deny-reach", 30f, ClimbDenyReachStep());
        yield return Restoring("vr.47b-free-hand-pocket", 25f, FreeHandPocketStep());
        yield return Restoring("vr.47d-ping-at-item", 25f, PingAtItemStep());
        yield return Restoring("vr.48-climb-deny-steep", 30f, ClimbDenySteepStep());
        yield return Restoring("vr.48b-surface-texture", 30f, SurfaceTextureStep());
        yield return Restoring("vr.49-condor", 30f, CondorStep());
        yield return Restoring("vr.50-climb-hover", 30f, ClimbHoverStep());
        yield return Restoring("vr.51-air-grip", 30f, AirGripStep());
        yield return Restoring("vr.51b-falling-catch", 30f, FallingCatchStep());
        yield return Restoring("vr.52-climb-empty", 40f, ClimbEmptyStep());
        yield return Restoring("vr.53-forced-detach", 30f, ForcedDetachStep());
        yield return Restoring("vr.54-mantle", 45f, MantleStep());
        yield return Restoring("vr.44b-handle-rest", 30f, HandleRestStep());
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
        if (!WallInFront(c, wall, 0.1f, out var hit))
        {
            Fail($"wall not in front after the warp (wall {wall.point}, body {c.Center})");
            yield return LetGoAll(c);
            yield break;
        }
        yield return MoveTo(left, () => hit.point + hit.normal * 0.04f, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        if (!c.data.isClimbing)
        {
            Fail($"climb did not start ({PeakXR.Interact.PhysicalInteraction.LastGripNote})");
            yield return LetGoAll(c);
            yield break;
        }
        // Pull up first (as vr.15), so the throw's own pull-down cannot set the feet back on the ground.
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.4f, 0.6f);
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
        float y0 = c.Center.y;
        float whooshMark = CueMark();
        yield return SwingRelease(left, from, from + Vector3.up * speed * 0.15f);
        float released = Now;
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
            yield return SwingRelease(left, from, from + Vector3.up * speed * 0.15f);
            bool Denied() =>
                UI.Affordances.Affordance.Count > denials
                && UI.Affordances.Affordance.LastReason == DenyReason.NoStamina
                && UI.Affordances.Affordance.LastLeft == left;
            // Every channel of the stamina refusal on the throwing hand, the stamina bar turned red.
            yield return ExpectCue(left, DenyReason.NoStamina, mark, true, 1.5f);
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
        yield return HandStep("vr.43-vine", 45f, VineStep());
        yield return HandStep("vr.43b-vine-look-back", 45f, VineLookBackStep());
        yield return HandStep("vr.43c-vine-slide", 30f, VineSlideStep());
        yield return Restoring("vr.59-vine-deny-stamina", 40f, VineDenyStaminaStep());
    }

    /// <summary>vr.40: the head held 40 % of standing height low sets <c>CharacterData.isCrouching</c>
    /// (CharacterMovement.cs:241); back up, it clears.</summary>
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
            p.y = low;
            VRInput.HeadPosition = p;
        };
        yield return WaitFor(() => c.data.isCrouching, 2f);
        bool crouched = c.data.isCrouching;
        yield return Wait(0.6f);
        float lift = rig.CrouchLift;
        yield return Shot("vr-40-crouch");
        VRInput.TestOverride = hands;
        yield return WaitFor(() => !c.data.isCrouching, 2f);
        var note =
            $"head to {low:0.00} m (eye {eye:0.00}), crouched {crouched}, view lift {lift:0.00} m, "
            + $"crouching after standing {c.data.isCrouching}";
        if (!crouched || c.data.isCrouching)
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
    /// point up the rope; opening the hand lets go once the regrip grace runs out.
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
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.4f, 0.6f);
        yield return Wait(0.3f);
        bool still = c.data.isRopeClimbing;
        float rise = still ? api.GetPosition(c.data.ropePercent).y - y0 : 0f;
        yield return GripTo(left, 0f, 0.1f);
        yield return WaitFor(() => !c.data.isRopeClimbing, 1.5f);
        var note =
            $"climb point rose {rise:0.00} m for a 0.40 m pull, on the rope after the pull {still}, "
            + $"after letting go {c.data.isRopeClimbing}";
        if (rise < 0.1f)
            Fail("arm pull did not climb the rope: " + note);
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
    /// The character is warped under it and grabs through the game's own <c>Interact</c> (the button path); then a
    /// grip on the vine anchors and a pull along it moves <c>vinePercent</c> (CharacterVineClimbing.cs:118); opening
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
        yield return ToVine();
        if (testVine == null)
            yield break;
        var vine = testVine;
        vine.Interact(c);
        yield return Until(() => c.data.isVineClimbing, 2f, "the game's vine grab did not attach");
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        // A grab carries the fall speed into a slide (CharacterVineClimbing.cs:167); input is ignored until it ends.
        yield return WaitFor(() => !c.refs.vineClimbing.Sliding(), 4f);
        yield return Wait(0.5f);

        bool left = VRInput.LeftHanded;
        var onVine =
            vine.GetPosition(c.data.vinePercent, 1, 1) + Vector3.down * vine.colliderOffset;
        yield return MoveTo(left, () => onVine, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
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

    /// <summary>The nearest jungle vine of Capsule type (the only type <c>IsInteractible</c> accepts, JungleVine.cs:96),
    /// with the character warped 1.2 m under its middle; <see cref="testVine"/>, or null with the step skipped.</summary>
    IEnumerator ToVine()
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
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            vine.GetPosition(0.5f, 1, 1) + Vector3.down * 1.2f,
            false
        );
        yield return Wait(0.2f);
        yield return WaitFor(() => !c.warping, 3f);
    }

    /// <summary>
    /// vr.44: a hammered piton (the climbing spike item's <c>hammeredVersionPrefab</c>, placed like
    /// CharacterItems.cs:1080) on the wall vr.15 climbs. A grip on its handle hangs (<c>RPCA_Hang</c>,
    /// ClimbHandle.cs:54-64); opening the hand lets go (<c>CancelHandle</c>, CharacterClimbing.cs:198).
    /// </summary>
    IEnumerator PitonStep()
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
        var handle = testHandle;
        yield return Wait(0.5f);
        bool left = VRInput.LeftHanded;
        yield return MoveTo(left, () => handle.transform.position, 0.5f);
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
        yield return Shot("vr-44-piton");
        yield return GripTo(left, 0f, 0.1f);
        yield return WaitFor(() => c.data.currentClimbHandle == null, 1.5f);
        var note =
            $"hanging after 1 s {hanging}, hanging after letting go {c.data.currentClimbHandle != null}";
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
    /// a wall in front of the warped player: <see cref="testHandle"/>, or null with the step skipped or failed.
    /// </summary>
    IEnumerator PlacePiton()
    {
        testHandle = null;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var spike = FindItemComponent<ClimbingSpikeComponent>();
        if (spike == null || spike.hammeredVersionPrefab == null)
        {
            stepSkip = "no climbing spike item with a hammered prefab";
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
    /// Offline the sandbox's 7 m test wall, online a map wall (<see cref="FindWall"/>); the player is warped
    /// <paramref name="back"/> metres farther from it than the usual 0.6 m, turned to face it, and
    /// <see cref="wallFace"/> is the face in front. Sets the skip or failure when it cannot.
    /// </summary>
    IEnumerator ToWall(float back = 0f, float headroom = 1.2f)
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
        if (
            back > 0f
            && Physics.Raycast(
                wallStand + Flat(wallBase.normal) * back + Vector3.up * 2f,
                Vector3.down,
                out var ground,
                6f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            wallStand = ground.point + Vector3.up * 0.1f;
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
    IEnumerator GripWall(bool left)
    {
        var c = Character.localCharacter;
        var hold = WallHold;
        yield return MoveTo(left, () => hold, 0.5f);
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
        var aim = item.Center();
        yield return PingCooldownOver(pinger);
        yield return FingerGunAim(left, aim, 1f);
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
        var note =
            $"Apple on the ground after 1 s of aiming {keptWhileAiming}, after the ping {kept}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}";
        if (!keptWhileAiming || !kept)
            Fail("aiming a ping at an item picked it up: " + note);
        else
            Note(note);
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
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.3f, 0.5f);
        UI.Affordances.Affordance.ResetGate();
        // As if a jump had just happened: the game's clock counts on from here.
        c.data.sinceClimbJump = 0.2f;
        float poked = Time.time;
        float speed = Mathf.Max(2f * cfg.LungeSpeed, 4f);
        var from = Script(left).Position;
        float mark = CueMark();
        yield return SwingRelease(left, from, from + Vector3.up * speed * 0.15f);
        yield return ExpectCue(left, DenyReason.TooSoon, mark, false, 1f);
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
    /// vr.47: standing 1.9 m from the face (the face check reaches 2 m), a hand stretched onto it touches rock past the game's 1.35 m reach (1.25 m thickened) from the
    /// camera (CharacterClimbing.cs:534; D3 keeps that cap for the hand-cast grab). The grip gets the OutOfReach cue
    /// and no climb.
    /// </summary>
    IEnumerator ClimbDenyReachStep()
    {
        var c = Character.localCharacter;
        if (!Interact.InteractConfig.Instance.PhysicalClimb)
        {
            stepSkip = "Interaction.GrabToClimb off";
            yield break;
        }
        yield return ToWall(back: 1.3f);
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        float eyeToHold = Vector3.Distance(EyeNow(), wallFace.point);
        if (eyeToHold <= TraversalRefusal.GameWallReach + 0.05f)
        {
            stepSkip = F($"the head ended {eyeToHold:0.00} m from the face, inside the reach");
            yield break;
        }
        UI.Affordances.Affordance.ResetGate();
        float mark = CueMark();
        var hold = WallHold;
        yield return MoveTo(left, () => hold, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.OutOfReach, mark, false, 1.5f);
        bool climbed = c.data.isClimbing;
        var note = F(
            $"hold {eyeToHold:0.00} m from the head: {cueNote}; climbing {climbed}; {PeakXR.Interact.PhysicalInteraction.LastGripNote}"
        );
        if (climbed)
            Fail("a hold past the reach was grabbed: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
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
        var spot = Flat(rig.Head.forward) * 0.45f + rig.Head.position;
        spot.y = c.Center.y;
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
        bool InWorld() => second != null && second.gameObject.activeInHierarchy;
        var last = second.Center();
        UI.Affordances.Affordance.ResetGate();
        float mark = CueMark();
        yield return MoveTo(
            true,
            () =>
            {
                if (InWorld())
                    last = second.Center();
                return last;
            },
            0.5f
        );
        yield return GripTo(true, 1f, 0.1f);
        yield return WaitFor(() => !InWorld(), 2f);
        bool pocketed = !InWorld();
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
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Carried, mark, false, 1f);
        bool climbed = c.data.isClimbing;
        c.data.carrierCondor = null;
        var note = $"carried: {cueNote}; climbing {climbed}";
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
        yield return Feel(left, CueFeel.Contact, mark, 0.5f);
        string contact = $"{cueNote} (ring off the wall {drawnBefore})";
        yield return ExpectNoCue(left, mark, 0f);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        bool climbed = c.data.isClimbing;
        bool gone = !ClimbHover.Drawn(left);
        var note = $"ring drawn {drawn}, {contact}; climbing on grip {climbed}, ring gone {gone}";
        if (!drawn)
            Fail("no chalk ring under the open hand: " + note);
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
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.4f, 0.6f);
        yield return ExpectCue(left, DenyReason.NoStamina, mark, true, 1f);
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
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Occupied, mark, true, 1f);
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
        yield return WaitFor(() => piton == null || piton.crack.activeSelf, 8f);
        // The crack starts 1-5 s into the hang: the window runs from the mark to 3 s after it showed (run 40 gave
        // the warning only 1 s from the mark, long gone by the first crack).
        yield return Feel(left, CueFeel.Danger, mark, CueMark() - mark + 3f);
        string warning = cueNote;
        float broke = CueMark();
        yield return WaitFor(() => c.data.currentClimbHandle == null, 8f);
        bool fell = c.data.currentClimbHandle == null;
        if (fell)
            // The break comes up to 4 cracks (6 s) after the warning: the window runs to 1.5 s past it (run 41 gave
            // the Cancel only 1 s from the mark).
            yield return Feel(left, CueFeel.Cancel, broke, CueMark() - broke + 1.5f);
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
        yield return ExpectCue(left, DenyReason.NotHere, mark, tint: false, within: 0f);
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
            Note($"looking around and one lean: no refusal; clear pump: {cue}");
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
        yield return MoveTrack(left, Script(left).Position + Vector3.down * 0.4f, 0.6f);
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
        float mark = CueMark();
        yield return MoveTo(left, () => at.position, 0.4f);
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

    /// <summary>Attaches to <see cref="testVine"/> through the game's own <c>Interact</c> and waits out the grab's
    /// slide (CharacterVineClimbing.cs:167), when input is ignored.</summary>
    IEnumerator AttachVine()
    {
        var c = Character.localCharacter;
        testVine.Interact(c);
        yield return Until(() => c.data.isVineClimbing, 2f, "the game's vine grab did not attach");
        if (!stepOk)
            yield break;
        yield return WaitFor(() => !c.refs.vineClimbing.Sliding(), 4f);
        yield return Wait(0.5f);
    }

    /// <summary>
    /// vr.43b: the game moves along a vine by stick Y times a sign from the look direction
    /// (CharacterVineClimbing.cs:67-68, <c>JungleVine.GetSign</c>, JungleVine.cs:426-435). Turned 180 degrees on the
    /// vine, the same world-space hand pull still moves the body the way the hand pulls (ClimbHands re-signs it).
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
        yield return ToVine();
        if (testVine == null)
            yield break;
        var vine = testVine;
        yield return AttachVine();
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float p0 = c.data.vinePercent;
        var along =
            vine.GetPosition(Mathf.Clamp01(p0 + 0.05f), 1, 1)
            - vine.GetPosition(Mathf.Clamp01(p0 - 0.05f), 1, 1);
        along = along.sqrMagnitude > 1e-6f ? along.normalized : Flat(rig.Head.forward);
        float signBefore = vine.GetSign(c.data.lookDirection_Flat, p0);
        rig.RotateYaw(180f);
        yield return Wait(0.6f);
        float signAfter = vine.GetSign(c.data.lookDirection_Flat, c.data.vinePercent);
        bool left = VRInput.LeftHanded;
        var onVine =
            vine.GetPosition(c.data.vinePercent, 1, 1) + Vector3.down * vine.colliderOffset;
        yield return MoveTo(left, () => onVine, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
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
        yield return ToVine();
        if (testVine == null)
            yield break;
        var vine = testVine;
        var marker = new GameObject("PeakXR_TestVineSpot").transform;
        OnRestore(() => UnityEngine.Object.Destroy(marker.gameObject));
        marker.position = vine.GetPosition(0.5f, 1, 1) + Vector3.down * vine.colliderOffset;
        // Stand on a test ledge under the drawn vine, which hangs 0.3 m above the head. Run 39 warped into the air under
        // it: the body fell, and the hand, placed a frame behind the fall, closed 0.9 m off the vine.
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
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, feet + Vector3.up * 0.1f, false);
        yield return WaitFor(() => !c.warping, 2f);
        yield return WaitFor(() => c.data.isGrounded, 2f);
        yield return Wait(0.5f);
        yield return GripRefusedLine(DenyReason.NoStamina, () => c.data.isVineClimbing, marker);
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
        if (!felt)
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
            yield return GripTo(hand, 1f, 0.08f);
            if (!started)
            {
                yield return WaitFor(() => c.data.isClimbing, 1.5f);
                started = c.data.isClimbing;
                if (!started)
                    break;
            }
            yield return GripTo(!hand, 0f, 0.05f);
            yield return MoveTrack(hand, Script(hand).Position + Vector3.down * 0.45f, 0.45f);
            if (!c.data.isClimbing)
                break;
            hand = !hand;
        }
        yield return Wait(1f);
        float mantleAt = PeakXR.Interact.PhysicalInteraction.LastMantleAt;
        bool mantled = mantleAt >= mark;
        bool onTop = Feet(c).y >= top - 0.3f;
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
        if (!started)
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
        bool added = !testAnchor.TryGetComponent<BreakableRopeAnchor>(out _);
        if (added)
            testAnchor.AddComponent<BreakableRopeAnchor>();
        yield return null;
        bool left = VRInput.LeftHanded;
        yield return GrabRope(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        yield return Feel(left, CueFeel.Danger, mark, 9f);
        Note($"anchor {(added ? "added" : "from the prefab")}: {cueNote}");
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.43c: a fast grab slides along the vine with input ignored (<c>Sliding</c>, CharacterVineClimbing.cs:102-127;
    /// the slide speed <c>attachVel</c> poked to 10). The hands feel and hear the whirr.
    /// </summary>
    IEnumerator VineSlideStep()
    {
        var c = Character.localCharacter;
        if (!ClimbHands.RopeHands || !Interact.InteractConfig.Instance.ClimbSurfaceFeel)
        {
            stepSkip = "Interaction.RopeHandOverHand or Interaction.ClimbSurfaceFeel off";
            yield break;
        }
        yield return ToVine();
        if (testVine == null)
            yield break;
        testVine.Interact(c);
        yield return Until(() => c.data.isVineClimbing, 2f, "the game's vine grab did not attach");
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        float mark = CueMark();
        // CharacterVineClimbing.attachVel (CharacterVineClimbing.cs:32, private): Sliding() is |attachVel| > 3.
        Framework.GameMember.Field<CharacterVineClimbing, float>("attachVel")(c.refs.vineClimbing) =
            10f;
        bool left = VRInput.LeftHanded;
        yield return WaitFor(() => ClimbCues.Since(ClimbCue.Whirr, left, mark, out _), 1f);
        bool felt = ClimbCues.Since(ClimbCue.Whirr, left, mark, out float amplitude);
        var note = F($"sliding: whirr felt {felt} at {amplitude:0.00}");
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
