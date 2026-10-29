using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Issue #13: a release right after a pickup must drop the item, and an item whose glow shows under a hand must be in
/// that hand right after the grip closes. Items wait in the air in front of the body, held still, so each step times
/// the grip alone.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Items vr.288 grabs: different shapes and kinds, all one-handed.</summary>
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
    /// frames, which starts eating it; both open 0.1 s after the press. The release must wait out the cast that the
    /// opening ends and drop the Apple within 1 s (<see cref="ReleaseQueue"/>).</summary>
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
