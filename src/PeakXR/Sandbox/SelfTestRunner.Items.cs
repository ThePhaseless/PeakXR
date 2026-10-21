using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>Scripted-hand steps for <c>interact.items</c> gestures that need only a held item: bugle at the mouth,
/// a healing item on the chest and the parasol runner. Fire, fuse and luggage gestures need level props and are
/// checked on the headset.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator ItemGestureSteps()
    {
        yield return ItemStep("vr.21-bugle", "Bugle", 25f, BugleStep);
        yield return ItemStep("vr.22-bandage", "Bandage", 30f, BandageStep);
        yield return ItemStep("vr.23-parasol", "Parasol", 25f, ParasolStep);
    }

    IEnumerator ItemStep(string id, string fragment, float timeout, Func<Item, IEnumerator> body)
    {
        var item = FindItem(new List<Item>(SandboxActions.Items), fragment);
        if (item == null)
        {
            if (VRRun)
                Skip(id, "no item matching " + fragment);
            yield break;
        }
        yield return HandStep(id, timeout, body(item));
    }

    /// <summary>vr.21: the holding hand at the mouth, tilted 30 degrees up, starts the toot
    /// (<c>BugleSFX.hold</c>, BugleSFX.cs:76) with the bell's pitch (<c>currentPitch</c>, BugleSFX.cs:74);
    /// moving away ends it.</summary>
    IEnumerator BugleStep(Item prefab)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        var sfx = item.GetComponent<BugleSFX>();
        if (sfx == null)
        {
            Fail(item.name + " has no BugleSFX");
            yield break;
        }
        var rig = VRRig.Instance;
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out _, out var forward, out _);
        var goal = MouthGesture
            .MouthPoint(
                VRInput.HeadPosition.N(),
                VRInput.HeadRotation.N(),
                MouthGesture.DefaultOffset
            )
            .U();
        var tilt = Quaternion.LookRotation(
            Quaternion.AngleAxis(-30f, Vector3.Cross(Vector3.up, forward)) * forward,
            Vector3.up
        );
        float end = Now + 5f;
        while (!sfx.hold && Now < end)
        {
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.5f, Time.unscaledDeltaTime)
                    .U()
            );
            Script(left).Rotation = tilt;
            yield return null;
        }
        bool started = sfx.hold;
        float pitch = sfx.currentPitch;
        var aim = rig.Aim(left);
        float expected = aim != null ? (Vector3.Dot(aim.forward, Vector3.up) + 1f) / 2f : -1f;
        if (started)
            yield return Shot("vr-21-bugle");
        HeadFrame(out var head, out var fwd, out var right);
        yield return MoveTrack(
            left,
            head + (left ? -right : right) * 0.25f + Vector3.down * 0.35f + fwd * 0.4f,
            0.4f
        );
        yield return WaitFor(() => !sfx.hold, 2f);
        var note =
            $"{item.name}: toot {started}, pitch {pitch:0.00} (bell {expected:0.00}), stopped {!sfx.hold}";
        if (!started)
            Fail("no toot at the mouth: " + note);
        else if (Mathf.Abs(pitch - expected) > 0.1f)
            Fail("pitch not from the bell: " + note);
        else if (sfx.hold)
            Fail("toot did not stop: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.22: a healing item pressed to the chest holds its primary use (<c>Item.castProgress</c>,
    /// Item.cs:344) until the cast finishes (<c>lastFinishedCast</c>, Item.cs:272) or the item is used up.</summary>
    IEnumerator BandageStep(Item prefab)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        float finishedBefore = item.lastFinishedCast;
        HeadFrame(out var head, out var forward, out var right);
        var chest = head + Vector3.down * 0.45f + forward * 0.08f + (left ? -right : right) * 0.05f;
        yield return MoveTrack(left, chest, 0.6f);
        float progress = 0f;
        float end = Now + item.usingTimePrimary + 4f;
        bool used = false;
        while (Now < end)
        {
            if (
                item == null
                || c.data.currentItem != item
                || item.lastFinishedCast > finishedBefore
            )
            {
                used = true;
                break;
            }
            progress = Mathf.Max(progress, item.castProgress);
            yield return null;
        }
        var note = $"{prefab.name}: cast progress {progress:0.00}, used {used}";
        if (!used && progress < 0.2f)
            Fail("no use on the chest: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.23: the free hand grips the held parasol, slides up to open it and down to close it, each through
    /// the parasol's own toggle (<c>Parasol.isOpen</c>, Parasol.cs:21, set by <c>ToggleOpenRPC</c>, Parasol.cs:56).</summary>
    IEnumerator ParasolStep(Item prefab)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        var parasol = item.GetComponent<Parasol>();
        if (parasol == null)
        {
            Fail(item.name + " has no Parasol");
            yield break;
        }
        bool holdLeft = LocalHold.ItemInLeftHand;
        bool free = !holdLeft;
        yield return GripTo(holdLeft, 1f, 0.1f);
        bool before = parasol.isOpen;
        var last = item.Center();
        yield return MoveTo(
            free,
            () =>
            {
                if (item != null)
                    last = item.Center();
                return last;
            },
            0.5f
        );
        yield return GripTo(free, 1f, 0.1f);
        var at = Script(free).Position;
        yield return MoveTrack(free, at + Vector3.up * 0.25f, 0.5f);
        yield return WaitFor(() => parasol.isOpen != before, 2f);
        bool toggled = parasol.isOpen != before;
        if (toggled)
            yield return Shot("vr-23-parasol");
        yield return MoveTrack(free, at - Vector3.up * 0.05f, 0.5f);
        yield return WaitFor(() => parasol.isOpen == before, 2f);
        bool back = parasol.isOpen == before;
        yield return GripTo(free, 0f, 0.1f);
        var note = $"{item.name}: open {before} -> push up {toggled} -> pull down back {back}";
        if (!toggled || !back)
            Fail("runner did not toggle: " + note);
        else
            stepNote = note;
    }
}
