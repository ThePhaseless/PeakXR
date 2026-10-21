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
    IEnumerator BodySteps()
    {
        yield return HandStep("vr.40-physical-crouch", 15f, CrouchStep());
        yield return HandStep("vr.41-arm-swing-sprint", 20f, ArmSwingStep());
        yield return HandStep("vr.42-rope-hand-over-hand", 40f, RopeStep());
        yield return HandStep("vr.44-piton-hang", 30f, PitonStep());
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
        var top = c.Center + forward * 0.7f + Vector3.up * 2.6f;
        if (
            Physics.Linecast(
                c.Center,
                top,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            stepSkip = "no clear space above the spawn for a hanging rope";
            yield break;
        }
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
        yield return Until(() => c.data.isRopeClimbing, 2f, "grip on the rope did not grab it");
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
            stepSkip = "no climbable wall with walkable ground in front within 40 m of spawn";
            yield break;
        }
        var normal = Flat(wall.normal);
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -normal, Vector3.up));
        yield return Wait(0.6f);
        if (
            !Physics.Raycast(
                c.Center + Vector3.up * 0.4f,
                -normal,
                out var hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
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
