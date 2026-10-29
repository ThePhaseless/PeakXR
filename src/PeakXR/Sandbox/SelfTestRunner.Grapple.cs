using System.Collections;
using PeakXR.Avatar;
using PeakXR.Interact.Aim;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>Scripted-hand step for the rescue hook's own pull to a wall (vr.251).</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Barrel pitches (degrees below level) tried to aim the hook at the test wall.</summary>
    static readonly float[] HookPitches = { 0f, -5f, 5f, -10f };

    /// <summary>Barrel yaws (degrees) tried around the heading toward the test wall.</summary>
    static readonly float[] HookYaws = { 0f, -8f, 8f };

    /// <summary>
    /// vr.251 (tester, v0.1.1: "the grappling hook works in third person, and you can't grab on right away"): the
    /// rescue hook fired at the test wall 9 m ahead pulls the body to it (RescueHook.RPCA_RescueWall, RescueHook.cs:398,
    /// with its own <c>Fall</c> :407). The view stays on the body through the pull and its recovery (no tumble or
    /// launch chase view, <see cref="OwnGrapple"/>). The move stick is held forward from the shot (the pull alone stops
    /// short of any climb reach), and the free hand closed on the wall once it is in reach starts a climb, waiting out
    /// the game's ragdoll recovery (CharacterClimbing.cs:146) instead of losing the grip.
    /// <c>Interaction.RequireTwoHands</c> is off for the step so the free hand is free for the wall.
    /// </summary>
    IEnumerator HookPullStep()
    {
        var c = Character.localCharacter;
        var hook = FindItem(SandboxActions.Items, "RescueHook");
        var entry = Interact.InteractConfig.Instance.RequireTwoHandsEntry;
        if (hook == null || entry == null)
        {
            stepSkip = "interact.physical not bound or no RescueHook";
            yield break;
        }
        var forward = Flat(c.data.lookDirection_Flat);
        var placed = SandboxActions.PlaceWall(c.Center + forward * 9.25f, forward);
        if (placed == null)
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        OnRestore(() =>
        {
            if (placed != null)
                Object.Destroy(placed);
        });
        bool was = entry.Value;
        OnRestore(() => entry.Value = was);
        entry.Value = false;
        yield return GiveAndHold(hook);
        if (!stepOk)
            yield break;
        var held = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        var wallCollider = placed.GetComponent<Collider>();
        yield return AimHeld(
            c,
            left,
            HookPitches,
            HookYaws,
            () =>
                held.CanUsePrimary()
                && SpoolRay(true, out var origin, out var dir)
                && Physics.Raycast(origin, dir, out var hit, 30f, HelperFunctions.terrainMapMask)
                && hit.collider == wallCollider
        );
        if (!aimFound)
        {
            // Where the last try's barrel ray went, and how far the hold turns it off the hand's aim.
            var aim = VR.VRRig.Instance.Aim(left);
            string last = SpoolRay(true, out var from, out var along)
                ? F(
                    $"barrel from {from} along {along}, {(aim != null ? Vector3.Angle(aim.forward, along) : -1f):0} deg off the hand aim, meets {(Physics.Raycast(from, along, out var met, 30f, HelperFunctions.terrainMapMask) ? F($"{met.collider.name} at {met.distance:0.00} m") : "nothing within 30 m")}"
                )
                : "no held aim";
            Fail(
                F(
                    $"no aim put the hook's barrel ray on the test wall 9 m ahead ({wallCollider.bounds.center}): {last}, usable {held.CanUsePrimary()}"
                )
            );
            yield break;
        }

        bool usableAtPress = held.CanUsePrimary();
        int refusals = Affordance.Count;
        Script(left).Trigger = 1f;
        float fireBy = Now + GameTiming.Budget(GameTiming.UseSeconds(held), 1f);
        while (Now < fireBy && !OwnGrapple.Covers(c))
            yield return null;
        Script(left).Trigger = 0f;
        if (!OwnGrapple.Covers(c))
        {
            Fail(
                $"the hook did not pull (cast {held.castProgress:0.00}, usable {held.CanUsePrimary()}, {usableAtPress} at the press, {Affordance.Count - refusals} refusal(s) since, last {Affordance.LastReason})"
            );
            yield break;
        }
        float fired = Now;
        // The game lets go of a wall pull 2 m from the hook point (RescueHook.cs:201, the prefab's stopPullDistance 2)
        // and the body coasts on: from 9 m the eye stopped 1.41 m from the face (run 4), past the game's own climb reach
        // from the camera (1.25 m thickened by 0.1 m, CharacterClimbing.cs:534-561), so a flat player has to walk in
        // too. The move stick is held toward the wall from the shot, as such a player holds forward, until in reach.
        bool move = OffLeft;
        OnRestore(() => Script(move).Stick = Vector2.zero);
        Script(move).Stick = new Vector2(0f, 1f);

        // Watch the pull and its recovery; once the wall is in the game's reach from the eye, close the free hand on it.
        bool free = !left;
        int detachedFrames = 0;
        int eventDetachFrames = 0;
        float minControl = 1f;
        float maxFall = 0f;
        float reachedAt = -1f;
        float climbedAt = -1f;
        float nearest = float.MaxValue;
        // The eye's distance to the face when the pull and its fall stopped covering the body (OwnGrapple).
        float pulledTo = -1f;
        var rig = VR.VRRig.Instance;
        for (float end = fired + 4f; Now < end && climbedAt < 0f; )
        {
            if (!RigDriver.ViewOnBody || RigDriver.ViewDetached)
                detachedFrames++;
            if (RigDriver.EventDetach)
                eventDetachFrames++;
            minControl = Mathf.Min(minControl, c.data.currentRagdollControll);
            maxFall = Mathf.Max(maxFall, c.data.fallSeconds);
            var eye = EyeNow();
            if (
                Physics.Raycast(
                    eye,
                    forward,
                    out var face,
                    12f,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                && face.collider == wallCollider
            )
            {
                nearest = Mathf.Min(nearest, face.distance);
                if (pulledTo < 0f && !OwnGrapple.Covers(c))
                    pulledTo = face.distance;
                if (reachedAt < 0f && face.distance <= 1.1f)
                {
                    reachedAt = Now;
                    Script(move).Stick = Vector2.zero;
                    SetHand(
                        free,
                        rig.Origin.InverseTransformPoint(face.point + face.normal * 0.05f)
                    );
                    Script(free).Grip = 1f;
                }
            }
            if (c.data.isClimbing)
                climbedAt = Now;
            yield return null;
        }

        var note = F(
            $"pull from 9 m: view off the body {detachedFrames} frames (launch chase {eventDetachFrames}), lowest ragdoll control {minControl:0.00}, longest fall {maxFall:0.00} s, eye {pulledTo:0.00} m from the wall when the pull ended, nearest wall {nearest:0.00} m, in reach {(reachedAt < 0f ? -1f : reachedAt - fired):0.00} s after the shot, climbing {(climbedAt < 0f ? -1f : climbedAt - reachedAt):0.00} s after the grip; {Interact.PhysicalInteraction.LastGripNote}"
        );
        Script(free).Grip = 0f;
        Script(move).Stick = Vector2.zero;
        if (detachedFrames > 0)
            Fail("the view left the body during the own hook pull: " + note);
        else if (reachedAt < 0f)
            Fail("the pull and the walk in never brought the wall within reach: " + note);
        else if (climbedAt < 0f)
            Fail("the grip at the wall after the pull did not climb: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }
}
