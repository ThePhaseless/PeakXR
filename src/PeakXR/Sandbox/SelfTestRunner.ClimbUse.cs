using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Which button does what while a hand climbs (tester, v0.1.1: "with the triggers my items get used, with the grips
/// items sometimes get picked up"; "the piton goes in not where I aim, and while climbing it sometimes places itself").
/// A climbing fist's trigger sets no piton (vr.253), the free hand's trigger sets one where it aims (vr.254), and a
/// grip that misses the wall picks nothing up (vr.255).
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Two pitons in the pockets, the hotbar emptied before and after the step; false with the skip or failure set.</summary>
    bool PocketSpikes(Character c, out ClimbingSpikeComponent spike)
    {
        spike = FindItemComponent<ClimbingSpikeComponent>();
        if (spike == null)
        {
            stepSkip = "no climbing spike item";
            return false;
        }
        EmptyHotbar(c);
        OnRestore(() => EmptyHotbar(c));
        var id = spike.GetComponent<Item>().itemID;
        if (
            c.player == null
            || !c.player.AddItem(id, null, out _)
            || !c.player.AddItem(id, null, out _)
        )
        {
            Fail("no free slots for two pitons");
            return false;
        }
        return true;
    }

    static HashSet<int> HandleIds()
    {
        var ids = new HashSet<int>();
        foreach (var h in FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None))
            ids.Add(h.GetInstanceID());
        return ids;
    }

    /// <summary>A climb handle placed since <paramref name="before"/>, kept for the cleanup; null when none.</summary>
    ClimbHandle NewHandle(HashSet<int> before)
    {
        foreach (var h in FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None))
        {
            if (before.Contains(h.GetInstanceID()))
                continue;
            var view = h.GetComponentInParent<Photon.Pun.PhotonView>();
            spawned.Add(view != null ? view.gameObject : h.gameObject);
            return h;
        }
        return null;
    }

    /// <summary>
    /// vr.253: pitons in the pockets, the off hand closes on the wall as a fist, trigger and grip together, and holds
    /// past the piton's use time. That trigger was the game's secondary use, which sets a piton while climbing
    /// (CharacterItems.cs:1019-1025): the piton went in by itself. Now no cast starts, no piton is spent, and a
    /// deliberate pull of that settled hand's trigger gets the UseOtherHand refusal at the hand.
    /// </summary>
    IEnumerator ClimbFistNoPitonStep()
    {
        var c = Character.localCharacter;
        yield return ToWall();
        if (!wallReady || !PocketSpikes(c, out var spike))
            yield break;
        yield return null;
        bool left = !VRInput.LeftHanded;
        var handles = HandleIds();
        yield return MoveTo(left, () => WallHold, 0.5f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        Script(left).Trigger = 1f;
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isClimbing, 2f);
        if (!c.data.isClimbing)
        {
            Fail($"climb did not start ({PhysicalInteraction.LastGripNote})");
            yield return LetGoAll(c);
            yield break;
        }
        int spikes = c.data.climbingSpikeCount;
        float useTime = spike.GetComponent<Item>().usingTimePrimary;
        float maxCast = 0f;
        for (float until = Now + useTime + 1f; Now < until && c.data.isClimbing; )
        {
            maxCast = Mathf.Max(maxCast, c.refs.items.climbingSpikeCastProgress);
            yield return null;
        }
        bool placed = NewHandle(handles) != null;
        Script(left).Trigger = 0f;
        yield return Wait(0.3f);
        UI.Affordances.Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        Script(left).Trigger = 1f;
        yield return ExpectCue(left, DenyReason.UseOtherHand, mark, false, 1f);
        Script(left).Trigger = 0f;
        var note = F(
            $"fist on the wall {useTime + 1f:0.0} s: cast up to {maxCast:0.00}, piton placed {placed}, pitons {spikes} then {c.data.climbingSpikeCount}, still climbing {c.data.isClimbing}; pull: {cueNote}"
        );
        if (placed || maxCast > 0f || c.data.climbingSpikeCount != spikes)
            Fail("the climbing fist's trigger set a piton: " + note);
        else if (!stepOk)
            stepNote = note + "; " + stepNote;
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.254: pitons in the pockets, the off hand on the wall, the dominant hand free and aimed at the face beside it.
    /// Its trigger was the primary use, which sets nothing while climbing, while the piton prompt sat above it: the
    /// piton came only from the off hand, aimed wherever that fist pointed. Now the free hand's pull is the secondary
    /// use: a cast starts and the piton goes in within 0.25 m of where that hand's aim met the wall.
    /// </summary>
    IEnumerator ClimbFreeHandPitonStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        yield return ToWall();
        if (!wallReady || !PocketSpikes(c, out var spike))
            yield break;
        bool climbing = !VRInput.LeftHanded;
        bool free = !climbing;
        yield return GripWall(climbing);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var toWall = -Flat(wallFace.normal);
        var side = Vector3.Cross(Vector3.up, toWall) * (free ? -1f : 1f);
        float gap = wallFace.collider.Raycast(
            new Ray(rig.Head.position, toWall),
            out var headHit,
            3f
        )
            ? headHit.distance
            : 0.6f;
        // The free hand out toward the wall but 0.35 m short of it, aimed at the face beside the climbing hand: well
        // inside the piton's 1 m climbing reach (climbingSpikeStartDistance).
        var handAt =
            rig.Head.position
            + toWall * Mathf.Clamp(gap - 0.35f, 0.1f, 0.3f)
            + side * 0.2f
            + Vector3.down * 0.1f;
        var target = wallFace.point + side * 0.3f + Vector3.up * 0.15f;
        if (Terrain(target - toWall * 0.5f, toWall, 1f, out var onFace))
            target = onFace.point;
        yield return MoveTo(free, () => handAt, 0.4f);
        for (float until = Now + 0.3f; Now < until; )
        {
            AimHand(rig, free, target);
            yield return null;
        }
        var aim = rig.Aim(free);
        if (
            aim == null
            || !Physics.Raycast(
                aim.position,
                aim.forward,
                out var aimed,
                spike.climbingSpikeStartDistance,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
            )
        )
        {
            Fail(
                F(
                    $"the free hand's aim meets no wall within the {spike.climbingSpikeStartDistance:0.00} m climbing reach"
                )
            );
            yield return LetGoAll(c);
            yield break;
        }
        var handles = HandleIds();
        int spikes = c.data.climbingSpikeCount;
        bool secondary = PhysicalInteraction.UseHandLeft(c, secondary: true) == free;
        Script(free).TriggerTouch = true;
        Script(free).Trigger = 1f;
        yield return WaitFor(() => c.refs.items.climbingSpikeCastProgress > 0f, 1f);
        bool cast = c.refs.items.climbingSpikeCastProgress > 0f;
        float useTime = spike.GetComponent<Item>().usingTimePrimary;
        ClimbHandle placed = null;
        for (float until = Now + useTime + 1.5f; Now < until && placed == null; )
        {
            yield return null;
            placed = NewHandle(handles);
        }
        Script(free).Trigger = 0f;
        Script(free).TriggerTouch = false;
        float off = placed != null ? Vector3.Distance(placed.transform.position, aimed.point) : -1f;
        var note = F(
            $"free {(free ? "left" : "right")} hand secondary use {secondary}, cast {cast}, piton {(placed != null ? $"{off:0.00} m from the aimed point" : "not placed")}, pitons {spikes} then {c.data.climbingSpikeCount}, wall {aimed.distance:0.00} m along the aim"
        );
        if (!cast || placed == null)
            Fail("the free hand's trigger set no piton while climbing: " + note);
        else if (off > 0.25f)
            Fail("the piton went in away from the free hand's aim: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.255: one hand on the wall, an Apple on the ground in view within the game's 2 m interact reach, and the free
    /// hand closing on open air with the head and hand aimed at it. That grip fell back to the interact button, which
    /// picked up whatever was aimed at (pocketed while climbing, Item.cs:578-591). Now it presses nothing: the hand's
    /// action is None, the Apple stays on the ground and the climb goes on.
    /// </summary>
    IEnumerator ClimbGripNoPickupStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        if (apple == null)
        {
            Fail("no Apple item");
            yield break;
        }
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool climbing = VRInput.LeftHanded;
        bool free = !climbing;
        yield return GripWall(climbing);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        var away = Flat(wallFace.normal);
        var side = Vector3.Cross(Vector3.up, -away) * (free ? -1f : 1f);
        var spot = wallStand + away * 0.5f + side * 0.6f + Vector3.up * 0.3f;
        if (!SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(SandboxActions.LastMessage);
            yield return LetGoAll(c);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        var item = go != null ? go.GetComponent<Item>() : null;
        if (item == null)
        {
            Fail("spawned Apple has no Item");
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.6f);
        LookAtPoint(item.Center());
        // Open air: out to the side of the body and low, in front of the shoulder (not its pack zone behind it), the
        // reach line from the eye running down past the hand, far from the wall and the ground.
        var handAt = rig.Head.position - away * 0.05f + side * 0.35f + Vector3.down * 0.45f;
        yield return MoveTo(free, () => handAt, 0.4f);
        for (float until = Now + 0.5f; Now < until; )
        {
            AimHand(rig, free, item.Center());
            yield return null;
        }
        var interaction = Interaction.instance;
        bool hovered = interaction != null && ReferenceEquals(interaction.currentHovered, item);
        yield return GripTo(free, 1f, 0.1f);
        yield return null;
        var action = PhysicalInteraction.HandAction(free);
        string grip = PhysicalInteraction.LastGripNote;
        yield return Wait(1f);
        bool kept = item != null && item.gameObject.activeInHierarchy;
        var note = F(
            $"free grip on open air: {action}, Apple hovered {hovered}, still on the ground {kept}, still climbing {c.data.isClimbing}; {grip}"
        );
        EndGlance();
        if (action == GripPressAction.Interact || !kept)
            Fail("a missed climbing grip pressed interact: " + note);
        else if (!c.data.isClimbing)
            Fail("the climb ended: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }
}
