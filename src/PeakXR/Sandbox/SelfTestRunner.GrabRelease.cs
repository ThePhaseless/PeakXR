using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Inventory;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Issue #13: a release right after a pickup must drop the item, and an item whose glow shows under a hand must be in
/// that hand right after the grip closes. Items wait in the air in front of the body, held still, so each step times
/// the grip alone.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Items vr.288 grabs: different shapes and kinds, all one-hand items.</summary>
    static readonly string[] HighlightItems =
    {
        AppleFragment,
        "Bandages",
        "Airplane Food",
        "Antidote",
    };

    /// <summary>The longest a highlighted item may take to reach the hand after the press (issue #13).</summary>
    const float GlowToHandSeconds = 0.3f;

    /// <summary>When the last <see cref="GlowAndPress"/> grip pressed (the frame its action was set).</summary>
    float grabPressAt;

    /// <summary>Where a held-still test item waits for the <paramref name="left"/> hand: in front of the body, low
    /// at that side, inside a standing reach.</summary>
    static Vector3 HandSpot(bool left)
    {
        HeadFrame(out var head, out var forward, out var right);
        return VRRig.Instance.Origin.TransformPoint(
            head + forward * 0.45f + (left ? -right : right) * 0.2f + Vector3.down * 0.35f
        );
    }

    /// <summary>The hand moves onto <paramref name="world"/> until its grip's glow shows it
    /// (<see cref="PhysicalInteraction.GripPreview"/>), then closes; <see cref="grabPressAt"/> is when the press
    /// landed. Fails, naming what glowed instead, if the glow never comes or the press takes nothing.</summary>
    IEnumerator GlowAndPress(bool left, Item world, bool fist = false)
    {
        var rig = VRRig.Instance;
        var at = world.Center();
        LookAtHand(left);
        yield return MoveTo(left, () => at, 0.4f);
        yield return WaitFor(
            () => ReferenceEquals(PhysicalInteraction.GripPreview(left), world),
            0.5f
        );
        var glow = PhysicalInteraction.GripPreview(left);
        if (!ReferenceEquals(glow, world))
        {
            Fail(
                F(
                    $"{world.name} never glowed under the {(left ? "left" : "right")} hand, {Vector3.Distance(rig.Hand(left).position, at):0.00} m from it: glow on {Label(glow)}"
                )
            );
            yield break;
        }
        Script(left).Grip = 1f;
        yield return WaitFor(
            () => PhysicalInteraction.HandAction(left) != GripPressAction.None,
            0.3f
        );
        grabPressAt = Now;
        if (PhysicalInteraction.HandAction(left) != GripPressAction.GrabItem)
        {
            Fail(
                $"the grip on the glowing {world.name} was {PhysicalInteraction.HandAction(left)}, not a grab: {PhysicalInteraction.LastGripNote}"
            );
            yield break;
        }
        if (!fist)
            yield break;
        // The closing fist's index reaches the trigger a couple of frames after the grip.
        yield return null;
        yield return null;
        Script(left).Trigger = 1f;
    }

    /// <summary>vr.286: each hand grips a glowing Apple and opens 0.1 s after the press: the Apple must leave the
    /// hand within 1 s (<c>Interaction.ReleaseToDrop</c>).</summary>
    IEnumerator QuickReleaseStep()
    {
        if (!InteractConfig.Instance.ReleaseToDrop)
        {
            stepSkip = "Interaction.ReleaseToDrop off";
            yield break;
        }
        var notes = new List<string>();
        foreach (bool left in new[] { false, true })
        {
            yield return QuickRelease(left, fist: false, notes);
            if (!stepOk)
                yield break;
        }
        Note(string.Join("; ", notes));
    }

    /// <summary>vr.287: the right hand closes on a glowing Apple as a fist, the trigger following the grip by two
    /// frames (part of the fist, so no use starts since issue #48); both open 0.1 s after the press. The Apple must
    /// drop within 1 s; a release during a use waits out the cast the opening ends (<see cref="ReleaseQueue"/>).</summary>
    IEnumerator QuickReleaseFistStep()
    {
        if (!InteractConfig.Instance.ReleaseToDrop)
        {
            stepSkip = "Interaction.ReleaseToDrop off";
            yield break;
        }
        var notes = new List<string>();
        yield return QuickRelease(false, fist: true, notes);
        if (stepOk)
            Note(string.Join("; ", notes));
    }

    IEnumerator QuickRelease(bool left, bool fist, List<string> notes)
    {
        string side = left ? "left" : "right";
        var world = SpawnTestItem(AppleFragment, HandSpot(left));
        if (world == null)
            yield break;
        var prefab = FindItem(SandboxActions.Items, AppleFragment);
        yield return Wait(0.2f);
        yield return GlowAndPress(left, world, fist);
        if (!stepOk)
            yield break;
        bool held = false;
        float progress = 0f;
        while (Now - grabPressAt < 0.1f)
        {
            held |= Holding(prefab);
            if (HeldItem() != null)
                progress = Mathf.Max(progress, HeldItem().progress);
            yield return null;
        }
        Script(left).Grip = 0f;
        Script(left).Trigger = 0f;
        float openAt = Now;
        yield return WaitFor(
            () =>
            {
                held |= Holding(prefab);
                return held && HeldItem() == null;
            },
            1f
        );
        bool dropped = held && HeldItem() == null;
        var note = F(
            $"{side}{(fist ? " fist" : "")}: held {held}, use progress {progress:0.00} at the release, dropped {dropped} {Now - openAt:0.00} s after it"
        );
        notes.Add(note);
        if (!held)
            Fail(
                "the grip never took the glowing Apple: "
                    + note
                    + "; "
                    + PhysicalInteraction.LastGripNote
            );
        else if (!dropped)
            Fail("a release 0.1 s after the press kept the Apple: " + note);
        yield return DropHeld();
        yield return Wait(0.4f);
    }

    /// <summary>vr.320 (issue #48): the right hand closes on glowing Bandages as a fist, the trigger following the
    /// grip by two frames, and stays closed 1 s: the Bandages are in hand and their use never starts. The trigger then
    /// opens and is squeezed again with the grip still closed: that fresh press starts the cast within 0.6 s.
    /// Bandages, not food: food's bare trigger is refused by design (Interaction.TriggerEats off,
    /// ItemGestures.TriggerRefused).</summary>
    IEnumerator FistGrabNoUseStep()
    {
        const bool left = false;
        const string bandages = "Bandages";
        var world = SpawnTestItem(bandages, HandSpot(left));
        if (world == null)
            yield break;
        var prefab = FindItem(SandboxActions.Items, bandages);
        yield return Wait(0.2f);
        yield return GlowAndPress(left, world, fist: true);
        if (!stepOk)
            yield break;
        bool held = false,
            used = false;
        for (float until = Now + 1f; Now < until; )
        {
            held |= Holding(prefab);
            var item = HeldItem();
            used |= item != null && (item.isUsingPrimary || item.castProgress > 0f);
            yield return null;
        }
        Script(left).Trigger = 0f;
        yield return Wait(0.15f);
        Script(left).Trigger = 1f;
        bool fresh = false;
        for (float until = Now + 0.6f; Now < until && !fresh; )
        {
            var item = HeldItem();
            fresh = item != null && (item.isUsingPrimary || item.castProgress > 0f);
            yield return null;
        }
        Script(left).Trigger = 0f;
        Script(left).Grip = 0f;
        var note = F(
            $"held {held}, used while the fist stayed closed {used}, a fresh squeeze used it {fresh}"
        );
        if (!held)
            Fail(
                "the fist never took the glowing Bandages: "
                    + note
                    + "; "
                    + PhysicalInteraction.LastGripNote
            );
        else if (used)
            Fail("the fist's own trigger started using the Bandages it picked up: " + note);
        else if (!fresh)
            Fail("a fresh trigger press after the fist did not use the Bandages: " + note);
        else
            Note(note);
        yield return Wait(0.3f);
        yield return DropHeld();
    }

    /// <summary>vr.440 (issue #74): the right hand grips a glowing Apple and squeezes the trigger as soon as the Apple
    /// is in hand, inside the fist's 0.3 s (<see cref="UseTrigger.InFist"/>), and stays closed 1 s. The fist's trigger
    /// is no use, so no refusal may answer it either: no NotHere, ring or pulse on that hand, and the Apple uneaten.</summary>
    IEnumerator FistFoodQuietStep()
    {
        const bool left = false;
        var world = SpawnTestItem(AppleFragment, HandSpot(left));
        if (world == null)
            yield break;
        var prefab = FindItem(SandboxActions.Items, AppleFragment);
        yield return Wait(0.2f);
        float since = CueLog.Now;
        yield return GlowAndPress(left, world);
        if (!stepOk)
            yield break;
        yield return WaitFor(() => Holding(prefab), 0.25f);
        float landed = Now - grabPressAt;
        if (!Holding(prefab))
        {
            stepSkip = F(
                $"the Apple was not in hand {landed:0.00} s after the grip, past the fist's window"
            );
            Script(left).Grip = 0f;
            yield break;
        }
        Script(left).Trigger = 1f;
        bool used = false;
        for (float until = Now + 1f; Now < until; )
        {
            var item = HeldItem();
            used |= item != null && (item.isUsingPrimary || item.castProgress > 0f);
            yield return null;
        }
        bool quiet = CueLog.Capture.Quiet(left, since, out string why);
        // The regrip tick (#48, vr.445) is for an item already in hand, never a pickup.
        if (quiet && CueLog.Capture.Felt(left, CueFeel.Cancel, since, out _))
        {
            quiet = false;
            why = "a cancel tick";
        }
        Script(left).Trigger = 0f;
        Script(left).Grip = 0f;
        var note = F(
            $"in hand {landed:0.00} s after the grip, used {used}, refusal cue {why ?? "none"}"
        );
        if (used)
            Fail("the fist's trigger started eating the Apple: " + note);
        else if (!quiet)
            Fail("the fist's muted trigger got a refusal cue: " + note);
        else
            Note(note);
        yield return Wait(0.3f);
        yield return DropHeld();
    }

    /// <summary>vr.445 (issue #48's gap): with <c>Interaction.ReleaseToDrop</c> off, given Bandages stay in the open
    /// right hand. The grip closes on them again and the trigger follows two frames later, inside the fist's 0.3 s:
    /// that pull is muted, and must say so with a light cancel tick on that hand, and the cast must not start.</summary>
    IEnumerator RegripFistTickStep()
    {
        var entry = InteractConfig.Instance.ReleaseToDropEntry;
        var bandages = FindItem(SandboxActions.Items, "Bandages");
        if (entry == null || bandages == null)
        {
            stepSkip = entry == null ? "no Interaction.ReleaseToDrop setting" : "no Bandages item";
            yield break;
        }
        bool saved = entry.Value;
        entry.Value = false;
        OnRestore(() => entry.Value = saved);
        const bool left = false;
        if (!SandboxActions.GiveItem(bandages, left))
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => Holding(bandages), 5f, "given Bandages not in hand");
        if (!stepOk)
            yield break;
        spawned.Add(HeldItem().gameObject);
        Script(left).Grip = 0f;
        Script(left).Trigger = 0f;
        yield return Wait(0.3f);
        float since = CueLog.Now;
        Script(left).Grip = 1f;
        yield return null;
        yield return null;
        Script(left).Trigger = 1f;
        bool used = false;
        for (float until = Now + 0.5f; Now < until; )
        {
            var item = HeldItem();
            used |= item != null && (item.isUsingPrimary || item.castProgress > 0f);
            yield return null;
        }
        bool ticked = CueLog.Capture.Felt(left, CueFeel.Cancel, since, out string why);
        Script(left).Trigger = 0f;
        Script(left).Grip = 0f;
        var note = F($"held {Holding(bandages)}, used {used}, tick: {why ?? "felt"}");
        if (used)
            Fail("the regrip's fist trigger started the Bandages' cast: " + note);
        else if (!ticked)
            Fail("the muted regrip pull got no tick: " + note);
        else
            Note(note);
        yield return DropHeld();
    }

    /// <summary>vr.451 (issue #49, after vr.321 proved the opposite): a hotbar pocket holds Bandages and an Apple is
    /// held still at that pocket's belt slot, as on a waist-high table's edge. The free right hand right on the Apple
    /// takes it: the Apple glows, the grip grabs it, and the Bandages stay in their pocket.</summary>
    IEnumerator SlotYieldsToItemStep()
    {
        var c = Character.localCharacter;
        var inv = BodyInventory.Instance;
        var bandages = FindItem(SandboxActions.Items, "Bandages");
        if (!inv.Valid || bandages == null || !PhotonNetwork.IsMasterClient)
        {
            stepSkip =
                !inv.Valid ? "body slots off"
                : bandages == null ? "no Bandages item"
                : "filling a pocket needs the master client";
            yield break;
        }
        EmptyHotbar(c);
        if (!c.player.AddItem(bandages.itemID, null, out var pocket) || pocket == null)
        {
            Fail("no free pocket for Bandages");
            yield break;
        }
        int slot = -1;
        for (int i = 0; i < inv.Count; i++)
        {
            if (inv.Slot(i).GameSlot == pocket.itemSlotID)
                slot = i;
        }
        if (slot < 0)
        {
            Fail($"no body slot for pocket {pocket.itemSlotID}");
            yield break;
        }
        const bool left = false;
        var rig = VRRig.Instance;
        var at = rig.Origin.TransformPoint(inv.Anchor(slot));
        var apple = SpawnTestItem(AppleFragment, at);
        if (apple == null)
            yield break;
        yield return Wait(0.2f);
        yield return MoveTo(left, () => at, 0.4f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        yield return Wait(0.3f);
        var hand = rig.Hand(left).position;
        float toApple = float.MaxValue;
        foreach (var col in apple.colliders)
        {
            if (col != null && col.enabled && !col.isTrigger)
                toApple = Mathf.Min(toApple, Vector3.Distance(col.ClosestPoint(hand), hand));
        }
        var glow = PhysicalInteraction.GripPreview(left);
        // The pickup replaces the world Apple with the held one (Item.Interact): compare by id.
        ushort appleId = apple.itemID;
        Script(left).Grip = 1f;
        yield return WaitFor(
            () => PhysicalInteraction.HandAction(left) != GripPressAction.None,
            0.3f
        );
        var action = PhysicalInteraction.HandAction(left);
        yield return WaitFor(() => HeldItem() != null, 2f);
        var held = HeldItem();
        bool tookApple = held != null && held.itemID == appleId;
        bool drew = Holding(bandages);
        Script(left).Grip = 0f;
        var note = F(
            $"Apple {toApple:0.00} m from the hand on slot {slot}, glow on {Label(glow)}, grip {action}, in hand {Label(held)}, drew the Bandages {drew}"
        );
        if (toApple > InteractConfig.Instance.GrabRadius)
            Fail("the Apple is out of the hand's grab reach, the step proves nothing: " + note);
        else if (!ReferenceEquals(glow, apple))
            Fail("the Apple right at the hand did not glow over a filled belt slot: " + note);
        else if (action != GripPressAction.GrabItem || !tookApple || drew)
            Fail(
                $"the grip on an item at a filled belt slot did not take the item: {note}; {PhysicalInteraction.LastGripNote}"
            );
        else
            Note(note);
        yield return Wait(0.3f);
        yield return DropHeld();
        EmptyHotbar(c);
    }

    /// <summary>vr.288: for each of <see cref="HighlightItems"/> and each hand, the hand moves onto the item held still
    /// in front of the body until its grip's glow shows it, then closes: the item must be in that hand within
    /// <see cref="GlowToHandSeconds"/> of the press.</summary>
    IEnumerator HighlightGrabStep()
    {
        var notes = new List<string>();
        var missing = new List<string>();
        int cases = 0;
        foreach (string itemName in HighlightItems)
        {
            var prefab = FindItem(SandboxActions.Items, itemName);
            if (prefab == null)
            {
                missing.Add(itemName);
                continue;
            }
            foreach (bool left in new[] { false, true })
            {
                var world = SpawnTestItem(itemName, HandSpot(left));
                if (world == null)
                    yield break;
                yield return Wait(0.2f);
                yield return GlowAndPress(left, world);
                if (!stepOk)
                    yield break;
                yield return WaitFor(() => Holding(prefab) && LocalHold.ItemInLeftHand == left, 1f);
                float took = Now - grabPressAt;
                bool inHand = Holding(prefab) && LocalHold.ItemInLeftHand == left;
                cases++;
                var note = F(
                    $"{prefab.name} {(left ? "left" : "right")}: {(inHand ? $"in hand {took:0.00} s after the press" : "not in that hand")}"
                );
                notes.Add(note);
                if (!inHand || took > GlowToHandSeconds)
                {
                    Fail(
                        F(
                            $"a glowing item was not in the hand within {GlowToHandSeconds:0.0} s of the press: {note}; {PhysicalInteraction.LastGripNote}"
                        )
                    );
                    yield break;
                }
                Script(left).Grip = 0f;
                yield return Wait(0.3f);
                yield return DropHeld();
                if (!stepOk)
                    yield break;
                // Past the game's equip cooldown, so the next grab times the grip alone.
                yield return Wait(0.4f);
            }
        }
        if (cases < 4)
        {
            Fail($"fewer than two items to grab (missing {string.Join(", ", missing)})");
            yield break;
        }
        if (missing.Count > 0)
            notes.Add("not in the database: " + string.Join(", ", missing));
        Note(string.Join("; ", notes));
    }
}
