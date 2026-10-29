using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.VR;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>vr.14b: the two-hand pose (<see cref="Core.TwoHandPose"/>) on the real item, in each hand.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Items vr.14b tries, in order: BingBong first (a front the game holds in both hands).</summary>
    static readonly string[] BetweenItems = { "BingBong", "RopeSpool" };

    /// <summary>How far (m) the middle of the item's grips may be from the middle of the hands.</summary>
    const float BetweenMidSlack = 0.06f;

    /// <summary>How much (m) one hand's gap to its grip may exceed the other's: the error is shared, not carried by one.</summary>
    const float BetweenSplitSlack = 0.05f;

    /// <summary>The front of an item in both hands still faces the eyes this much (dot), the join keeping it.</summary>
    const float BetweenFrontFacing = 0.5f;

    /// <summary>Largest jump (m) of the main grip against its hand in one frame while the second hand lets go.</summary>
    const float BetweenReleaseJump = 0.03f;

    /// <summary>
    /// vr.14b: with the main hand held naturally and the free hand closing on the item's drawn second grip, the item
    /// sits between the hands (its grips' middle on the hands' middle), the hands' gap to their grips is shared when
    /// the free hand moves 0.12 m farther out, an item with a front still shows it to the eyes, and letting the second
    /// hand go eases the item back onto the main grip with no jump. Right hand first, then the left.
    /// </summary>
    IEnumerator TwoHandBetweenStep()
    {
        var all = new List<Item>(SandboxActions.Items);
        Item pick = null;
        foreach (var fragment in BetweenItems)
        {
            pick = FindItem(all, fragment);
            if (pick != null)
                break;
        }
        if (pick == null)
        {
            Fail("no two-hand item (" + string.Join(", ", BetweenItems) + ")");
            yield break;
        }
        var notes = new List<string>();
        var failures = new List<string>();
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 1;
            yield return GiveAndHold(pick);
            if (!stepOk)
                yield break;
            LocalHold.ItemInLeftHand = left;
            LocalHold.TwoHand = false;
            yield return Wait(0.8f);
            yield return BetweenHands(left, notes, failures);
            yield return ReleaseHeld();
        }
        if (failures.Count > 0)
            Fail(string.Join("; ", failures) + "; all: " + string.Join("; ", notes));
        else
            stepNote = string.Join("; ", notes);
    }

    IEnumerator BetweenHands(bool left, List<string> notes, List<string> failures)
    {
        var rig = VRRig.Instance;
        var held = HeldItem();
        var info = new ItemHoldInfo();
        var driver = AvatarPoseDriver.Of(Character.localCharacter);
        string hand = left ? "left" : "right";
        if (held == null || !info.Bind(held) || driver == null)
        {
            failures.Add($"nothing in the {hand} hand");
            yield break;
        }
        bool free = !left;
        var rest = Script(free).Position;
        yield return GripTo(left, 1f, 0.1f);
        yield return HoldNaturally(left);
        yield return MoveTo(free, () => info.SecondGrip(left, out var g) ? g : held.Center(), 0.6f);
        yield return GripTo(free, 1f, 0.1f);
        yield return WaitFor(() => LocalHold.TwoHand, 2f);
        if (!LocalHold.TwoHand)
        {
            failures.Add($"{held.name} {hand}: the second hand did not join");
            yield break;
        }
        yield return Wait(0.6f);
        Measure(out float mid, out float e1, out float e2, out float facing);
        // Spacing mismatch: the free hand 0.12 m farther out along the hands' axis.
        var b1 = Bone(left);
        var b2 = Bone(free);
        var away = rig.Origin.InverseTransformVector((b2 - b1).normalized * 0.12f);
        yield return MoveTrack(free, Script(free).Position + away, 0.3f);
        yield return Wait(0.6f);
        Measure(out float mid2, out float f1, out float f2, out _);
        string line = F(
            $"{held.name} {hand}: grips' middle {mid:0.000} m off the hands', gaps {e1:0.000}/{e2:0.000} m; 0.12 m wider: middle {mid2:0.000} m, gaps {f1:0.000}/{f2:0.000} m; front to the eyes {(info.HasFront ? facing.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}"
        );
        // Letting go: the main grip against its hand per frame.
        yield return GripTo(free, 0f, 0.05f);
        float jump = 0f;
        var last = GripOffset();
        float end = Now + 0.5f;
        while (Now < end)
        {
            yield return null;
            var now = GripOffset();
            jump = Mathf.Max(jump, Vector3.Distance(now, last));
            last = now;
        }
        line += F($"; release jump {jump:0.000} m");
        Plugin.Log.LogInfo("[selftest] vr.14b " + line);
        notes.Add(line);
        if (mid > BetweenMidSlack || mid2 > BetweenMidSlack)
            failures.Add("not between the hands: " + line);
        if (Mathf.Abs(f1 - f2) > BetweenSplitSlack)
            failures.Add("one hand carries the spacing: " + line);
        if (info.HasFront && facing < BetweenFrontFacing)
            failures.Add("front turned away: " + line);
        if (jump > BetweenReleaseJump)
            failures.Add("jumped on release: " + line);
        yield return MoveTrack(free, rest, 0.3f);

        Vector3 Bone(bool l)
        {
            var t = rig.Hand(l);
            driver.HandRigOf(l).BoneFromController(t.position, t.rotation, out var p, out _);
            return p;
        }

        // The main grip on the item as it lies, from its hand's controller bone.
        Vector3 GripOffset() =>
            held != null
                ? held.transform.TransformPoint(info.HoldPivot(left)) - Bone(left)
                : default;

        void Measure(out float midOff, out float gap1, out float gap2, out float front)
        {
            midOff = gap1 = gap2 = float.PositiveInfinity;
            front = -1f;
            if (held == null || !info.SecondGrip(left, out var g2))
                return;
            var g1 = held.transform.TransformPoint(info.HoldPivot(left));
            var h1 = Bone(left);
            var h2 = Bone(free);
            midOff = Vector3.Distance((g1 + g2) * 0.5f, (h1 + h2) * 0.5f);
            gap1 = Vector3.Distance(g1, h1);
            gap2 = Vector3.Distance(g2, h2);
            var centre = held.transform.TransformPoint((info.BoundsMin + info.BoundsMax) * 0.5f);
            front = Vector3.Dot(
                held.transform.TransformDirection(info.Front).normalized,
                (rig.Head.position - HeldItemScale.ToVisual(held, centre)).normalized
            );
        }
    }
}
