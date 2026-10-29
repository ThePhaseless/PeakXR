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
/// A climbing fist's trigger sets no piton (vr.253), a hand on the wall that holds its trigger sets one there and the
/// free hand's trigger sets none (vr.268), and a grip that misses the wall picks nothing up (vr.255).
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
    /// (CharacterItems.cs:1019-1025): the piton went in by itself. A trigger squeezed with the closing grip is no
    /// deliberate hold (UseTrigger.ClimbPress), so no cast starts and no piton is spent.
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
        var note = F(
            $"fist on the wall {useTime + 1f:0.0} s: cast up to {maxCast:0.00}, piton placed {placed}, pitons {spikes} then {c.data.climbingSpikeCount}, still climbing {c.data.isClimbing}"
        );
        if (placed || maxCast > 0f || c.data.climbingSpikeCount != spikes)
            Fail("the climbing fist's trigger set a piton: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.268: pitons in the pockets, one hand on the wall. The free hand, aimed at the face beside it, holds its trigger
    /// past the piton's use time: that was the old free-hand piton, and now sets nothing. Then the hand on the wall
    /// pulls its trigger and holds it (Tools.HandPiton): a cast starts and one piton goes in within 0.15 m of where that
    /// hand meets the wall, along the climb's normal from the hand.
    /// </summary>
    IEnumerator ClimbHandPitonStep()
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
        float useTime = spike.GetComponent<Item>().usingTimePrimary;
        var handles = HandleIds();
        int spikes = c.data.climbingSpikeCount;

        // The free hand's trigger, aimed at the face beside the climbing hand inside the 1 m climbing reach.
        var toWall = -Flat(wallFace.normal);
        var side = Vector3.Cross(Vector3.up, toWall) * (free ? -1f : 1f);
        float gap = wallFace.collider.Raycast(
            new Ray(rig.Head.position, toWall),
            out var headHit,
            3f
        )
            ? headHit.distance
            : 0.6f;
        var freeAt =
            rig.Head.position
            + toWall * Mathf.Clamp(gap - 0.35f, 0.1f, 0.3f)
            + side * 0.2f
            + Vector3.down * 0.1f;
        var target = wallFace.point + side * 0.3f + Vector3.up * 0.15f;
        yield return MoveTo(free, () => freeAt, 0.4f);
        Script(free).TriggerTouch = true;
        Script(free).Trigger = 1f;
        float freeCast = 0f;
        // No cue may promise that hand a piton either: no hold ring anywhere, no aim line (HandReticle.OtherCue: the
        // aimed tool's line, laser, throw arc, ping preview), no hand-piton hold.
        int ringFrames = 0;
        int lineFrames = 0;
        int placingFrames = 0;
        for (float until = Now + useTime + 0.5f; Now < until; )
        {
            AimHand(rig, free, target);
            freeCast = Mathf.Max(freeCast, c.refs.items.climbingSpikeCastProgress);
            if (UI.HandWidgets.HandWidgetsRuntime.RingHand >= 0)
                ringFrames++;
            if (UI.World.HandReticle.OtherCue)
                lineFrames++;
            if (Interact.Tools.HandPiton.Placing)
                placingFrames++;
            yield return null;
        }
        Script(free).Trigger = 0f;
        Script(free).TriggerTouch = false;
        bool freePlaced = NewHandle(handles) != null;
        if (
            freeCast > 0f
            || freePlaced
            || c.data.climbingSpikeCount != spikes
            || ringFrames > 0
            || lineFrames > 0
            || placingFrames > 0
        )
        {
            Fail(
                F(
                    $"the free hand's trigger still sets or cues a piton while climbing: cast up to {freeCast:0.00}, placed {freePlaced}, pitons {spikes} then {c.data.climbingSpikeCount}, frames with a hold ring {ringFrames}, an aim line {lineFrames}, a hand piton {placingFrames}"
                )
            );
            yield return LetGoAll(c);
            yield break;
        }

        // The climbing hand's trigger, pulled once the hand has settled on the wall and held.
        var hand = rig.Hand(climbing);
        var normal = c.data.climbNormal.normalized;
        var grip = hand.position;
        if (Terrain(hand.position + normal * 0.3f, -normal, 1f, out var onWall))
            grip = onWall.point;
        Script(climbing).TriggerTouch = true;
        Script(climbing).Trigger = 1f;
        bool placing = false;
        float cast = 0f;
        ClimbHandle placed = null;
        // The cast's hold ring goes over the climbing hand: it went to whichever hand last held an item, the free one.
        int ringOn = 0;
        int ringOff = 0;
        int climbingRing = climbing ? 0 : 1;
        for (
            float until = Now + UseTrigger.HoldSeconds + useTime + 1.5f;
            Now < until && placed == null;

        )
        {
            yield return null;
            placing |= Interact.Tools.HandPiton.Placing;
            cast = Mathf.Max(cast, c.refs.items.climbingSpikeCastProgress);
            placed = NewHandle(handles);
            int ring = UI.HandWidgets.HandWidgetsRuntime.RingHand;
            if (ring == climbingRing)
                ringOn++;
            else if (ring is 0 or 1)
                ringOff++;
        }
        Script(climbing).Trigger = 0f;
        Script(climbing).TriggerTouch = false;
        // The game places the piton's root at its ray's hit (HammerClimbingSpike, CharacterItems.cs:1080).
        var root = placed != null ? placed.GetComponentInParent<Photon.Pun.PhotonView>() : null;
        float off =
            placed == null
                ? -1f
                : Vector3.Distance(
                    root != null ? root.transform.position : placed.transform.position,
                    grip
                );
        var note = F(
            $"{(climbing ? "left" : "right")} hand on the wall: placing {placing}, cast up to {cast:0.00}, piton {(placed != null ? $"{off:0.00} m from the hand's wall point" : "not placed")}, pitons {spikes} then {c.data.climbingSpikeCount}, still climbing {c.data.isClimbing}, hold ring over that hand {ringOn} frames, over the free hand {ringOff}"
        );
        if (placed == null)
            Fail("holding the climbing hand's trigger set no piton: " + note);
        else if (ringOff > 0)
            Fail("the piton's hold ring showed over the free hand: " + note);
        else if (off > 0.15f)
            Fail("the piton went in away from the climbing hand: " + note);
        else if (c.data.climbingSpikeCount != spikes - 1)
            Fail("one hold spent other than one piton: " + note);
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
