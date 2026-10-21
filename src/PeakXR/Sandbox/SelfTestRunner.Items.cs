using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact.Physical;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>Scripted-hand steps for <c>interact.items</c> gestures: bugle at the mouth, a healing item on the chest,
/// the parasol runner, and at level props found at test time (beach luggage, the nearest campfire) the lid lift,
/// flame lighting, cooking and warm hands. Prop steps warp next to the prop and back to spawn.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator ItemGestureSteps()
    {
        yield return ItemStep("vr.21-bugle", "Bugle", 25f, BugleStep);
        yield return ItemStep("vr.22-bandage", "Bandage", 30f, BandageStep);
        yield return ItemStep("vr.23-parasol", "Parasol", 25f, ParasolStep);
        yield return HandStep("vr.24-lift-lid", 40f, LiftLidStep());
        yield return WarpBack();
        // Light first: it needs the campfire unlit and stops short of lighting it; cooking then lights it.
        yield return ItemStep("vr.26-light-with-flame", "Torch", 40f, LightStep);
        yield return WarpBack();
        // The game has no way to put a campfire out again short of Extinguish_Rpc, which leaves it spent; and
        // Light_Rpc is buffered. So the fire vr.25 lights stays lit for the rest of the run (vr.27 uses it).
        yield return ItemStep("vr.25-cook-over-fire", AppleFragment, 40f, CookStep);
        yield return WarpBack();
        yield return HandStep("vr.27-warm-hands", 30f, WarmStep());
        yield return WarpBack();
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

    // ---------------------------------------------------------------- level props

    bool warpedForProp;

    /// <summary>Teardown after a prop step's <see cref="HandStep"/>, outside its guarded run, so a pass, fail, throw
    /// or timeout all end with the player back at spawn.</summary>
    IEnumerator WarpBack()
    {
        if (!warpedForProp)
            yield break;
        warpedForProp = false;
        SandboxActions.WarpToSpawn();
        yield return Wait(2f);
    }

    static T Nearest<T>(Vector3 from, Func<T, bool> ok)
        where T : Component
    {
        T best = null;
        float bestSq = float.MaxValue;
        foreach (var it in UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            if (it == null || !ok(it))
                continue;
            float d = (it.transform.position - from).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = it;
            }
        }
        return best;
    }

    /// <summary>Warps onto the ground <paramref name="distance"/> from <paramref name="target"/>, on the side the
    /// player came from, and turns the rig to face it (<c>Character.WarpPlayerRPC</c>, Character.cs:1822).</summary>
    IEnumerator WarpFacing(Vector3 target, float distance)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var stand = target + Flat(c.Center - target) * distance;
        if (
            Physics.Raycast(
                stand + Vector3.up * 3f,
                Vector3.down,
                out var ground,
                8f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            stand = ground.point + Vector3.up * 0.1f;
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        rig.RotateYaw(
            Vector3.SignedAngle(Flat(rig.Head.forward), Flat(target - c.Center), Vector3.up)
        );
        yield return Wait(0.6f);
    }

    /// <summary>Moves the scripted hand toward the point where <paramref name="part"/> (an item point the hand
    /// carries) reaches <paramref name="goal"/>; one frame of a 1.5 m/s approach.</summary>
    void CarryToward(bool left, Vector3 part, Vector3 goal)
    {
        var rig = VRRig.Instance;
        var hand = rig.Hand(left).position;
        var target = goal + (hand - part);
        SetHand(
            left,
            rig.Origin.InverseTransformPoint(
                Vector3.MoveTowards(hand, target, 1.5f * Time.unscaledDeltaTime)
            )
        );
    }

    /// <summary>vr.24: a grip on the top of the nearest closed suitcase holds the open timer at the lid
    /// (<c>Interaction.constantInteractableProgress</c>, Interaction.cs:57) while the hand stays still, and lifting
    /// it opens the case (<c>Luggage.IsOpen</c>, Luggage.cs:37).</summary>
    IEnumerator LiftLidStep()
    {
        var c = Character.localCharacter;
        var lug = Nearest<Luggage>(c.Center, l => !l.IsOpen && l is not RespawnChest);
        if (lug == null || (lug.transform.position - c.Center).sqrMagnitude > 200f * 200f)
        {
            stepSkip = "no closed suitcase within 200 m";
            yield break;
        }
        if (!Setting("Interaction", "LiftLidToOpen", true))
        {
            stepSkip = "Interaction.LiftLidToOpen is off";
            yield break;
        }
        yield return WarpFacing(lug.Center(), 0.8f);
        var top = LidPoint(lug);
        bool left = VRInput.LeftHanded;
        yield return MoveTo(left, () => top, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
        var interaction = Interaction.instance;
        yield return Until(
            () => interaction != null && ReferenceEquals(interaction.currentHeldInteractible, lug),
            2f,
            "grip did not hold the suitcase"
        );
        if (!stepOk)
            yield break;
        yield return Wait(1f);
        float still = interaction.constantInteractableProgress;
        yield return Shot("vr-24-lift-lid");
        var at = Script(left).Position;
        float rate = LidGesture.LiftHeight * 1.4f / Mathf.Max(0.5f, lug.timeToOpen);
        float end = Now + lug.timeToOpen + 3f;
        float lifted = 0f;
        while (!lug.IsOpen && Now < end)
        {
            lifted = Mathf.Min(
                LidGesture.LiftHeight + 0.1f,
                lifted + rate * Time.unscaledDeltaTime
            );
            SetHand(left, at + Vector3.up * lifted);
            yield return null;
        }
        yield return GripTo(left, 0f, 0.1f);
        var note =
            $"{lug.name}: timeToOpen {lug.timeToOpen:0.0} s, progress with the hand still {still:0.00}, lifted {lifted:0.00} m, open {lug.IsOpen}";
        if (still > 0.15f)
            Fail("timer ran without a lift: " + note);
        else if (!lug.IsOpen)
            Fail("lifting did not open it: " + note);
        else
            stepNote = note;
    }

    /// <summary>Top centre of the suitcase's collider bounds, just under the lid surface and above its centre.</summary>
    static Vector3 LidPoint(Luggage lug)
    {
        var center = lug.Center();
        var cols = lug.GetComponentsInChildren<Collider>();
        if (cols.Length == 0)
            return center + Vector3.up * 0.1f;
        var b = cols[0].bounds;
        for (int i = 1; i < cols.Length; i++)
            b.Encapsulate(cols[i].bounds);
        return new Vector3(b.center.x, Mathf.Max(b.max.y - 0.03f, center.y + 0.02f), b.center.z);
    }

    /// <summary>vr.26: a held torch touched to an unlit campfire holds the game's light hold on it
    /// (<c>Interaction.currentHeldInteractible</c>, Interaction.cs:121); pulling away before the hold completes
    /// cancels it (Interaction.cs:109-112) and leaves the fire unlit, so vr.25 finds it as the level made it.</summary>
    IEnumerator LightStep(Item torchPrefab)
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state == Campfire.FireState.Off);
        if (fire == null)
        {
            stepSkip = "no unlit campfire loaded";
            yield break;
        }
        if (!Setting("Interaction", "LightWithFlame", true))
        {
            stepSkip = "Interaction.LightWithFlame is off";
            yield break;
        }
        yield return WarpFacing(ItemGestures.FireBase(fire), 1.1f);
        yield return GiveAndHold(torchPrefab);
        if (!stepOk)
            yield break;
        var torch = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        var action = torch.GetComponent<Action_Torch>();
        var flame =
            action != null && action.torchLight != null
                ? action.torchLight.transform
                : torch.transform;
        var goal = ItemGestures.FireBase(fire) + Vector3.up * 0.15f;
        var interaction = Interaction.instance;
        float progress = 0f;
        float end = Now + 5f;
        while (Now < end && progress < 0.3f)
        {
            CarryToward(left, flame.position, goal);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire))
                progress = interaction.constantInteractableProgress;
            yield return null;
        }
        if (progress >= 0.3f)
            yield return Shot("vr-26-light");
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(
            left,
            head + (left ? -right : right) * 0.25f + Vector3.down * 0.35f + forward * 0.2f,
            0.3f
        );
        yield return Wait(0.5f);
        var note =
            $"{torch.name} at {fire.name}: light hold progress {progress:0.00}, fire {fire.state} after pulling away";
        if (progress < 0.3f)
            Fail("flame did not hold the light: " + note);
        else if (fire.state != Campfire.FireState.Off)
            Fail("hold did not cancel on pulling away: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.25: a cookable item held in the heat column over a lit campfire cooks through the game's hold
    /// (<c>DataEntryKey.CookedAmount</c> +1, ItemCooking.cs:264). Lights the nearest campfire first with
    /// <c>Campfire.LightWithoutReveal</c> (Campfire.cs:456: no segment advance) when it is still unlit.</summary>
    IEnumerator CookStep(Item foodPrefab)
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state != Campfire.FireState.Spent);
        if (fire == null)
        {
            stepSkip = "no campfire loaded";
            yield break;
        }
        if (!Setting("Interaction", "CookOverFire", true))
        {
            stepSkip = "Interaction.CookOverFire is off";
            yield break;
        }
        yield return WarpFacing(ItemGestures.FireBase(fire), 1.2f);
        if (!fire.Lit)
        {
            fire.LightWithoutReveal();
            yield return Until(() => fire.Lit, 3f, "campfire did not light");
            if (!stepOk)
                yield break;
        }
        yield return GiveAndHold(foodPrefab);
        if (!stepOk)
            yield break;
        var food = HeldItem();
        if (food.cooking == null || !food.cooking.canBeCooked)
        {
            stepSkip = food.name + " cannot be cooked";
            yield break;
        }
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        int before = Cooked(food);
        var goal = ItemGestures.FireBase(fire) + Vector3.up * 0.6f;
        var interaction = Interaction.instance;
        float progress = 0f;
        bool shot = false;
        float end = Now + fire.cookTime + 4f;
        while (Now < end && food != null && Cooked(food) == before)
        {
            CarryToward(left, food.Center(), goal);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire))
                progress = Mathf.Max(progress, interaction.constantInteractableProgress);
            if (!shot && progress > 0.3f)
            {
                shot = true;
                yield return Shot("vr-25-cook");
                continue;
            }
            yield return null;
        }
        int after = food != null ? Cooked(food) : before;
        var note =
            $"{NameOf(food)} over {fire.name}: cook hold progress {progress:0.00} of {fire.cookTime:0.0} s, cooked {before} -> {after}";
        if (after <= before)
            Fail("not cooked: " + note);
        else
            stepNote = note;

        static string NameOf(Item it) => it != null ? it.name : "item (gone)";
    }

    static int Cooked(Item item) => item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value;

    /// <summary>vr.27: open palms held toward a lit campfire send interact.items' warm pulses
    /// (<c>FireGestures.WarmPulses</c>, counted where <c>Haptics.Pulse</c> is called). Uses the campfire vr.25 lit;
    /// skips when none is lit.</summary>
    IEnumerator WarmStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var fire = Nearest<Campfire>(c.Center, f => f.Lit);
        if (fire == null)
        {
            stepSkip = "no lit campfire loaded";
            yield break;
        }
        if (!Setting("Interaction", "WarmHandsAtFire", true))
        {
            stepSkip = "Interaction.WarmHandsAtFire is off";
            yield break;
        }
        var heat = ItemGestures.FireBase(fire) + Vector3.up * 0.3f;
        yield return WarpFacing(heat, 1.3f);
        var toFire = Flat(heat - c.Center);
        var side = Vector3.Cross(Vector3.up, toFire);
        var leftAt = heat - toFire * 0.55f - side * 0.15f;
        var rightAt = heat - toFire * 0.55f + side * 0.15f;
        yield return MoveTo(true, () => leftAt, 0.5f);
        yield return MoveTo(false, () => rightAt, 0.5f);
        // Palms to the fire: +X of the left grip pose, -X of the right (HandRig.cs:62), in tracking space.
        var dir = rig.Origin.InverseTransformDirection(toFire);
        var leftRot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
        var rightRot = Quaternion.LookRotation(Vector3.Cross(-dir, Vector3.up), Vector3.up);
        int before = FireGestures.WarmPulses;
        float end = Now + 2f;
        while (Now < end)
        {
            Script(true).Rotation = leftRot;
            Script(false).Rotation = rightRot;
            yield return null;
        }
        int pulses = FireGestures.WarmPulses - before;
        yield return Shot("vr-27-warm-hands");
        var note = $"{fire.name}: {pulses} warm pulses in 2 s, both palms 0.55 m from the fire";
        if (pulses <= 0)
            Fail("no warm pulse: " + note);
        else
            stepNote = note;
    }
}
