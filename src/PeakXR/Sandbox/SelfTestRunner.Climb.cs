using System;
using System.Collections;
using PeakXR.Core;
using PeakXR.Interact.Climb;
using PeakXR.Locomotion;
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
        yield return HandStep("vr.46-lunge", 40f, LungeStep());
    }

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
        if (!FindWall(c, out var wall, out var stand))
        {
            stepSkip = NoWall;
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
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

        // The throw: 0.15 s straight up, let go 70 % of the way at twice the lunge speed (at least 4 m/s).
        float speed = Mathf.Max(2f * cfg.LungeSpeed, 4f);
        var from = Script(left).Position;
        float jumpBefore = c.data.sinceClimbJump;
        float y0 = c.Center.y;
        yield return SwingRelease(left, from, from + Vector3.up * speed * 0.15f);
        float released = Now;
        yield return WaitFor(() => c.data.sinceClimbJump < Mathf.Min(jumpBefore, 0.5f), 0.4f);
        bool jumped = c.data.sinceClimbJump < Mathf.Min(jumpBefore, 0.5f);
        float jumpAfter = Now - released;
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
        // A climb that ended and started again on the catch restarts the game's own clock (CharacterClimbing.cs:107).
        var sinceStart = PeakXR.Interact.PhysicalInteraction.SinceLastClimbStarted;
        bool unbroken = sinceStart == null || sinceStart(c.refs.climbing) > caughtAfter - 0.05f;
        float rise = c.Center.y - y0;
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
            // Every channel of the stamina refusal on the throwing hand.
            yield return ExpectCue(left, DenyReason.NoStamina, mark, 1.5f);
            bool denied = Denied();
            string cue = denied ? CueCheck() : null;
            bool noJump = c.data.sinceClimbJump >= noJumpBefore;
            SandboxActions.SetGodMode(true);
            refusal =
                $"out of stamina {c.data.outOfStaminaFor:0.0} s (climbing {climbingAtThrow}): denied {denied}"
                + $" ({UI.Affordances.Affordance.LastReason}), no jump {noJump}; {cueNote}";
            if (!denied)
                Fail("no NoStamina refusal for a lunge without stamina: " + refusal);
            else if (cue != null)
                Fail($"{refusal}; {cue}");
            else if (!noJump)
                Fail("the game took the climb jump without stamina: " + refusal);
        }
        var note = F(
            $"climb jump {jumped} {jumpAfter:0.00} s after the release ({speed:0.0} m/s throw, lunge from {cfg.LungeSpeed:0.0}), caught {caught} after {caughtAfter:0.00} s (grace 0.6 s), climb unbroken {unbroken}, rose {rise:0.00} m; {refusal}"
        );
        if (!jumped)
            Fail("the throw did not make the game's climb jump: " + note);
        else if (!caught)
            Fail("the hand did not catch the wall after the lunge: " + note);
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
        var rig = VRRig.Instance;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
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
        var top = c.Center + forward * 0.7f + Vector3.up * 2.6f;
        var go = PhotonNetwork.Instantiate(
            shooter.ropeAnchorWithRopePref.name,
            top,
            Quaternion.identity,
            0
        );
        spawned.Add(go);
        go.GetComponent<RopeAnchorProjectile>()
            .photonView.RPC("GetShot", RpcTarget.AllBuffered, top, 0.05f, shooter.length, -forward);
        var anchor = go.GetComponent<RopeAnchorWithRope>();
        yield return WaitFor(() => anchor.rope != null, 3f);
        if (anchor.rope == null)
        {
            Fail("the anchor spawned no rope");
            yield break;
        }
        spawned.Add(anchor.ropeInstance);
        var rope = anchor.rope;
        // Spool-out (RopeAnchorWithRope.spoolOutTime) and settling.
        yield return Wait(anchor.spoolOutTime + 1.5f);

        bool left = VRInput.LeftHanded;
        var reach = rig.Origin.TransformPoint(Script(left).Position) + Vector3.up * 0.2f;
        Transform segment = null;
        float best = float.MaxValue;
        foreach (var t in rope.GetRopeSegments())
        {
            float d = t != null ? (t.position - reach).sqrMagnitude : float.MaxValue;
            if (d < best)
            {
                best = d;
                segment = t;
            }
        }
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
        c.photonView.RPC(
            "WarpPlayerRPC",
            RpcTarget.All,
            vine.GetPosition(0.5f, 1, 1) + Vector3.down * 1.2f,
            false
        );
        yield return Wait(0.2f);
        yield return WaitFor(() => !c.warping, 3f);
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

    /// <summary>
    /// vr.44: a hammered piton (the climbing spike item's <c>hammeredVersionPrefab</c>, placed like
    /// CharacterItems.cs:1080) on the wall vr.15 climbs. A grip on its handle hangs (<c>RPCA_Hang</c>,
    /// ClimbHandle.cs:54-64); opening the hand lets go (<c>CancelHandle</c>, CharacterClimbing.cs:198).
    /// </summary>
    IEnumerator PitonStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ClimbHands.HandleHands)
        {
            stepSkip = "Interaction.PitonGripHang off";
            yield break;
        }
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
            yield return LetGoAll(c);
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
            yield return LetGoAll(c);
            yield break;
        }
        spawned.Add(go);
        var handle = go.GetComponentInChildren<ClimbHandle>();
        if (handle == null)
        {
            Fail("the hammered piton has no ClimbHandle");
            yield return LetGoAll(c);
            yield break;
        }
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
    }
}
