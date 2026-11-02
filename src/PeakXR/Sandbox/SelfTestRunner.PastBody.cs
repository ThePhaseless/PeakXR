using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact.Physical;
using PeakXR.Inventory;
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
    /// vr.495: Bandages carried from a hold out in front, in past the belly (inside the press zone, as a real hand's arc
    /// is) to their belt slot, at a hand's speed and slowly, rested there and let go: no cast starts, nothing is spent,
    /// and they are in the slot. Then a cast started on the chest and carried on to the slot stops there and stows. Then
    /// drawn and lifted straight up to the chest, held still, they heal (<see cref="BandageHeld"/>).
    /// </summary>
    IEnumerator BandagePastBodyStep(Item prefab)
    {
        var inv = BodyInventory.Instance;
        if (!inv.Valid || !InventoryConfig.Instance.HolsterOnRelease)
        {
            stepSkip = "body slots or HolsterOnRelease off";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        bool left = LocalHold.ItemInLeftHand;
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
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out var right);
        var side = left ? -right : right;
        float consumed = c.data.lastConsumedItem;
        int uses = UsesLeft(HeldItem());
        string carries = "";
        foreach (float speed in new[] { CarryFast, CarrySlow })
        {
            yield return MoveTrack(
                left,
                head + forward * 0.5f + Vector3.down * 0.4f + side * 0.1f,
                0.5f
            );
            var item = HeldItem();
            float cast = 0f;
            float inZone = 0f;
            void Sample()
            {
                if (item != null)
                    cast = Mathf.Max(cast, item.isUsingPrimary ? 1e-3f : 0f, item.castProgress);
                if (HeldGestures.AtBody)
                    inZone += Time.unscaledDeltaTime;
            }
            var belly = head + forward * 0.1f + Vector3.down * 0.5f + side * 0.08f;
            while (Vector3.Distance(Script(left).Position, belly) > 0.01f)
            {
                SetHand(left, ApproachAt(left, belly, speed));
                Sample();
                yield return null;
            }
            while (Vector3.Distance(Script(left).Position, inv.Anchor(slot)) > 0.01f)
            {
                SetHand(left, ApproachAt(left, inv.Anchor(slot), speed));
                Sample();
                yield return null;
            }
            yield return RestAndOpen(left, slot, Sample);
            var at = F($"{speed:0.0} m/s: {inZone:0.00} s in the press zone, cast {cast:0.00}");
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
        yield return BandageHeld(prefab, false, "vr-495-bandage-chest");
        if (stepOk)
            stepNote = carries + stepNote;
    }

    Vector3 ApproachAt(bool left, Vector3 goal, float speed) =>
        HandScript.Approach(Script(left).Position.N(), goal.N(), speed, Time.unscaledDeltaTime).U();

    /// <summary>The hand rests at the slot and opens its grip, as vr.20's does; a player's follows the ghost.</summary>
    IEnumerator RestAndOpen(bool left, int slot, System.Action sample)
    {
        var inv = BodyInventory.Instance;
        for (float start = Now; Now - start < 0.6f; )
        {
            SetHand(left, inv.Anchor(slot));
            Script(left).Grip = Mathf.Clamp01((0.6f - (Now - start)) / 0.3f);
            sample();
            yield return null;
        }
        Script(left).Grip = 0f;
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
        SetHand(left, BodyInventory.Instance.Anchor(slot));
        yield return Wait(0.4f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => HeldItem() != null && HeldItem().itemID == id,
            2f,
            "a grip at the slot did not draw it: " + at
        );
    }
}
