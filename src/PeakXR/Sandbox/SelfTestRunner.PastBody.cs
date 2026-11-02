using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Interact.Physical;
using PeakXR.Inventory;
using PeakXR.VR;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>vr.495: a held bandage on its way to the belt is not a bandage on the chest (#147).</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>A hand's speed putting something away, and a careful one (m/s).</summary>
    const float CarryFast = 0.8f;
    const float CarrySlow = 0.2f;

    /// <summary>
    /// vr.495 (Bandages), vr.495b (FirstAidKit, the big one): given to the hand that did not hold the last item while it
    /// rests on the chest, it waits for the hand to move. Held just under the belt, the item itself on the body and
    /// the hand not counting, it starts nothing. Carried from there, across the belly (hand and item in the press zone for
    /// longer than the dwell, at either speed) to each hotbar slot, rested there and let go: no cast starts, nothing is spent, and it
    /// is in its slot. Then a cast started on the chest and carried on to the slot stops there and stows. Then
    /// drawn and lifted straight up to the chest, held still, it heals (<see cref="BandageHeld"/>).
    /// </summary>
    IEnumerator BandagePastBodyStep(Item prefab)
    {
        var inv = BodyInventory.Instance;
        if (!inv.Valid || !InventoryConfig.Instance.HolsterOnRelease)
        {
            stepSkip = "body slots or HolsterOnRelease off";
            yield break;
        }
        var c = Character.localCharacter;
        bool left = !LocalHold.ItemInLeftHand;
        HeadFrame(out var head, out var forward, out var right);
        var side = left ? -right : right;
        // The free hand hangs out of the way: its forearm counts as body too.
        yield return MoveTrack(!left, head + Vector3.down * 0.85f - side * 0.35f, 0.3f);
        yield return MoveTrack(
            left,
            head + Vector3.down * 0.45f + forward * 0.08f + side * 0.05f,
            0.4f
        );
        PhysicalInteraction.ExpectItemIn(left);
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        if (LocalHold.ItemInLeftHand != left)
        {
            Fail(
                "given to the other hand, so an arrival read with the last item's hand is unproven"
            );
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectNoCast(left, "arriving in a resting hand on the chest");
        if (!stepOk)
            yield break;
        yield return FrontHold(left, head, forward, side);
        if (!stepOk)
            yield break;
        var selected = c.refs.items.currentSelectedSlot;
        int slot = -1;
        for (int i = 0; selected.IsSome && i < inv.Count; i++)
        {
            if (inv.Slot(i).GameSlot == selected.Value)
                slot = i;
        }
        if (slot < 0)
        {
            Fail("no body slot for the held Bandages' hotbar slot");
            yield break;
        }
        float consumed = c.data.lastConsumedItem;
        int uses = UsesLeft(HeldItem());
        string carries = frontHoldNote + "; ";
        foreach (float speed in new[] { CarryFast, CarrySlow })
        {
            for (int to = 0; to < inv.Count; to++)
            {
                if (inv.Slot(to).GameSlot >= BodySlotLayout.MaxHotbar)
                    continue;
                yield return MoveTrack(
                    left,
                    head + forward * 0.5f + Vector3.down * 0.4f + side * 0.1f,
                    0.5f
                );
                var item = HeldItem();
                float cast = 0f;
                float inZone = 0f;
                float atSlot = 0f;
                void SampleCast()
                {
                    if (item != null)
                        cast = Mathf.Max(cast, item.isUsingPrimary ? 1e-3f : 0f, item.castProgress);
                }
                // Only what could start a cast counts: the item on the body with the hand there too (HeldGestures.UpdateBody).
                bool PressZone() => HeldGestures.AtBody && HeldGestures.HandAtBody;
                void Sample()
                {
                    SampleCast();
                    if (PressZone())
                        inZone += Time.unscaledDeltaTime;
                }
                // From the far side of the belly and across it: the straight way to the holding side's slot met the zone for
                // 0.31 s at 0.8 m/s, and over the belly's middle to the kit's slot 1 for 0.59 s, under the dwell, so a carry
                // there could not have started a cast whatever the rule (#147).
                var far = head + forward * 0.1f + Vector3.down * 0.5f - side * 0.3f;
                var belly = head + forward * 0.1f + Vector3.down * 0.5f + side * 0.2f;
                // Square corners: a straight reversal stays inside HandStill's 0.05 m for up to 0.15 m of path, 0.75 s at
                // 0.2 m/s, past the dwell, so the route's own turn read as a stop (cast 0.05 at slot 2).
                foreach (
                    var point in new[]
                    {
                        far + forward * 0.12f,
                        far,
                        belly,
                        belly + Vector3.down * 0.12f,
                    }
                )
                {
                    while (Vector3.Distance(Script(left).Position, point) > 0.01f)
                    {
                        SetHand(left, ApproachAt(left, point, speed));
                        Sample();
                        yield return null;
                    }
                }
                while (Vector3.Distance(Script(left).Position, inv.Anchor(to)) > 0.01f)
                {
                    SetHand(left, ApproachAt(left, inv.Anchor(to), speed));
                    Sample();
                    yield return null;
                }
                yield return RestAndOpen(
                    left,
                    to,
                    () =>
                    {
                        SampleCast();
                        if (PressZone())
                            atSlot += Time.unscaledDeltaTime;
                    }
                );
                var at = F(
                    $"slot {to} at {speed:0.0} m/s: {inZone:0.00} s in the press zone on the way, {atSlot:0.00} s resting at the slot, cast {cast:0.00}"
                );
                if (inZone < SelfUseDwell.BodyDwell)
                {
                    Fail("the hand barely met the press zone, so this proves nothing: " + at);
                    yield break;
                }
                if (cast > 0f)
                {
                    Fail("the carry to the belt started the use: " + at);
                    yield break;
                }
                yield return StowedThenDraw(left, slot, selected.Value, prefab.itemID, at);
                if (!stepOk)
                    yield break;
                carries += at + "; ";
            }
        }

        var drawn = HeldItem();
        float useTime = GameTiming.UseSeconds(drawn);
        if (useTime >= MinCancelSeconds)
        {
            yield return MoveTrack(
                left,
                head + Vector3.down * 0.45f + forward * 0.08f + side * 0.05f,
                0.6f
            );
            float end = Now + GameTiming.Budget(useTime, 2f);
            while (drawn != null && drawn.castProgress < 0.25f && Now < end)
                yield return null;
            float started = drawn != null ? drawn.castProgress : 0f;
            if (started < 0.25f)
            {
                Fail($"held on the chest, the cast only reached {started:0.00}");
                yield break;
            }
            int ring = UI.HandWidgets.HandWidgetsRuntime.RingHand;
            if (ring != (left ? 0 : 1))
            {
                Fail($"mid-cast on the chest the ring is at {ring}, not over the holding hand");
                yield break;
            }
            while (Vector3.Distance(Script(left).Position, inv.Anchor(slot)) > 0.01f)
            {
                SetHand(left, ApproachAt(left, inv.Anchor(slot), CarryFast));
                yield return null;
            }
            float still = 0f;
            yield return RestAndOpen(
                left,
                slot,
                () =>
                {
                    if (drawn != null)
                        still = Mathf.Max(still, drawn.castProgress);
                }
            );
            var at = F($"cast {started:0.00} on the chest, {still:0.00} resting at the slot");
            if (still > 0f)
            {
                Fail("the slot did not stop the cast: " + at);
                yield break;
            }
            yield return StowedThenDraw(left, slot, selected.Value, prefab.itemID, at);
            if (!stepOk)
                yield break;
            carries += at + "; ";
        }

        if (c.data.lastConsumedItem != consumed || UsesLeft(HeldItem()) != uses)
        {
            Fail($"an item was spent on the way: {carries}");
            yield break;
        }
        yield return BandageHeld(prefab, false, $"vr-495-{prefab.name}-chest");
        if (stepOk)
            stepNote = carries + stepNote;
    }

    Vector3 ApproachAt(bool left, Vector3 goal, float speed) =>
        HandScript.Approach(Script(left).Position.N(), goal.N(), speed, Time.unscaledDeltaTime).U();

    /// <summary>The hand rests at the slot with its grip closed for longer than the body's dwell, then opens it, as vr.20's
    /// does; a player's follows the ghost.</summary>
    IEnumerator RestAndOpen(bool left, int slot, System.Action sample)
    {
        var inv = BodyInventory.Instance;
        for (float start = Now; Now - start < 1.3f; )
        {
            SetHand(left, inv.Anchor(slot));
            Script(left).Grip = Mathf.Clamp01((1.3f - (Now - start)) / 0.3f);
            sample();
            yield return null;
        }
        Script(left).Grip = 0f;
    }

    /// <summary>The hand stays in the slot while it waits and closes, as a player's follows the ghost: the slots ride the
    /// body, which can be pushing under the head, and a hand left where it arrived missed the slot (#109: 0.29 m in that
    /// time, with the stance pushing).</summary>
    IEnumerator GripAtSlot(bool left, int slot)
    {
        var inv = BodyInventory.Instance;
        for (float start = Now; Now - start < 0.5f; )
        {
            SetHand(left, inv.Anchor(slot));
            Script(left).Grip = Mathf.Clamp01((Now - start - 0.4f) / 0.1f);
            yield return null;
        }
        Script(left).Grip = 1f;
    }

    /// <summary>What the last <see cref="ExpectNoCast"/> measured.</summary>
    string noCastNote = "";

    float noCastHandSeconds;

    /// <summary>The hand keeps still for a second, longer than the body's dwell; no cast may start. The item must be on
    /// the body throughout, or this proves nothing.</summary>
    IEnumerator ExpectNoCast(bool left, string where)
    {
        var item = HeldItem();
        var at = Script(left).Position;
        float cast = 0f;
        float touching = 0f;
        float gap = float.PositiveInfinity;
        noCastHandSeconds = 0f;
        for (float start = Now; Now - start < 1f; )
        {
            SetHand(left, at);
            noCastHandSeconds += HeldGestures.HandAtBody ? Time.unscaledDeltaTime : 0f;
            cast = Mathf.Max(cast, item.isUsingPrimary ? 1e-3f : 0f, item.castProgress);
            gap = Mathf.Min(gap, HeldGestures.BodyGap);
            if (HeldGestures.AtBody)
                touching += Time.unscaledDeltaTime;
            yield return null;
        }
        var note = F(
            $"{where}: {touching:0.00} s on the body, the item's gap {gap:0.000} m, cast {cast:0.00}"
        );
        noCastNote = note;
        if (touching < SelfUseDwell.BodyDwell)
            Fail("the item barely touched the body, so this proves nothing: " + note);
        else if (cast > 0f)
            Fail("it started the use: " + note);
    }

    /// <summary>What the last <see cref="FrontHold"/> held, for the pass note.</summary>
    string frontHoldNote = "";

    /// <summary>The hand 4 cm under the torso's bottom, where it never counts, and the item turned until it meets the body:
    /// the hold most likely to start a use by mistake, whatever the item's size. The hand's turn has to be chosen: re-aimed
    /// from the head, a hand under the belt looks straight down, and the item hung away from the body (#147: 0.8 m under
    /// the eyes, AtBody false throughout). Straight in at chest height the hand counted first: the Bandages' near face
    /// stops 0.19 m off the grip, under the 0.2 m the hand gets (<see cref="BodyZones.HandOffTorso"/>). Then a second's
    /// rest.</summary>
    IEnumerator FrontHold(bool left, Vector3 head, Vector3 forward, Vector3 side)
    {
        var under = head + Vector3.down * (BodyZones.TorsoBottom + 0.04f) + side * 0.1f;
        yield return MoveTrack(left, under + forward * 0.6f, 0.5f);
        var c = Character.localCharacter;
        var turns = new[] { Vector3.up, forward, -forward, -side, side, Vector3.down };
        float best = float.PositiveInfinity;
        string found = null;
        foreach (float ahead in new[] { 0.25f, 0.15f, 0.05f, 0.35f })
        {
            var at = under + forward * ahead;
            yield return MoveTrack(left, at, 0.3f);
            foreach (var fingers in turns)
            {
                HoldTurn(
                    left,
                    HandLook(fingers, Mathf.Abs(fingers.y) > 0.5f ? forward : Vector3.up)
                );
                SetHand(left, at);
                // The hand's pose reaches the gesture a frame later.
                yield return null;
                yield return null;
                best = Mathf.Min(best, HeldGestures.BodyGap);
                if (
                    HeldGestures.BodyGap <= 0f
                    && !HeldGestures.HandAtBody
                    && BodyInventory.Instance.HeldSlot(c, left) < 0
                )
                {
                    found = F($"{ahead:0.00} m ahead of the eyes, fingers {fingers}");
                    break;
                }
            }
            if (found != null)
                break;
        }
        if (found == null)
        {
            FreeTurn(left);
            Fail(
                F(
                    $"setup: no turn under the belt put the item on the body with the hand not counting, nearest {best:0.000} m"
                )
            );
            yield break;
        }
        yield return ExpectNoCast(left, "at the front hold");
        if (stepOk && noCastHandSeconds > 0f)
            Fail(
                "setup: the hand counted at the front hold, so this proves nothing: " + noCastNote
            );
        frontHoldNote = noCastNote + ", " + found;
        FreeTurn(left);
    }

    /// <summary>
    /// vr.22c: the body steps and the hand goes with it, at 0.3 m/s, over what a hand may wander in tracking space during
    /// a dwell: a physical step or lean is no reason to restart a press on the chest (#147).
    /// </summary>
    IEnumerator BandageBodyStepsStep(Item prefab)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out var right);
        var rot = VRInput.HeadRotation;
        var chest = head + Vector3.down * 0.45f + forward * 0.08f + (left ? -right : right) * 0.05f;
        yield return MoveTrack(left, chest, 0.6f);
        float start = Now;
        while (Now - start < 3f && item != null && item.castProgress <= 0f)
        {
            var step = right * Mathf.PingPong((Now - start) * 0.3f, 0.3f);
            ScriptHead(head + step, rot);
            SetHand(left, chest + step);
            yield return null;
        }
        ReleaseHead();
        if (item == null || item.castProgress <= 0f)
            Fail("a hand resting on a stepping body never counted as resting");
        else
            stepNote = F($"cast started after {Now - start:0.00} s of stepping at 0.3 m/s");
        yield return MoveTrack(left, head + forward * 0.5f + Vector3.down * 0.4f, 0.3f);
    }

    /// <summary>The held item goes into its hotbar slot, and a grip there draws it back.</summary>
    IEnumerator StowedThenDraw(bool left, int slot, byte gameSlot, ushort id, string at)
    {
        yield return Until(() => HeldItem() == null, 2f, "not stowed on release: " + at);
        if (!stepOk)
            yield break;
        var itemSlot = Character.localCharacter.player.GetItemSlot(gameSlot);
        if (itemSlot == null || itemSlot.IsEmpty() || itemSlot.prefab.itemID != id)
        {
            Fail($"hotbar slot {gameSlot} does not hold the item: " + at);
            yield break;
        }
        yield return GripAtSlot(left, slot);
        yield return Until(
            () => HeldItem() != null && HeldItem().itemID == id,
            2f,
            "a grip at the slot did not draw it: " + at
        );
    }
}
