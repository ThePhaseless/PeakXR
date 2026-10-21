using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Interact.Lights;
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
        yield return ItemStep("vr.28-use-while-pointing", AppleFragment, 25f, UsePointingStep);
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

    /// <summary>vr.28: the hand holding an Apple points (grip closed, index off the trigger) at another Apple, then
    /// squeezes the trigger. That is the item's use (<c>Item.isUsingPrimary</c>), never a ping: a holding hand's
    /// squeeze fired interact.ping's finger gun and swallowed the use (vr14, a held torch never lit).</summary>
    IEnumerator UsePointingStep(Item prefab)
    {
        var c = Character.localCharacter;
        var head = VRRig.Instance.Head;
        var spot = head.position + Flat(head.forward) * 0.8f + Vector3.down * 0.2f;
        if (!SandboxActions.SpawnItemAt(prefab, spot) || SandboxActions.LastSpawned == null)
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var target = SandboxActions.LastSpawned.GetComponent<Item>();
        if (target != null && target.rig != null)
            target.rig.isKinematic = true;
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        // Point the holding hand at the other Apple: index straight, off the trigger.
        HeadFrame(out var eye, out var forward, out var right);
        SetHand(
            left,
            eye + (left ? -right : right) * 0.15f + Vector3.down * 0.25f + forward * 0.3f
        );
        Script(left).TriggerTouch = false;
        float end = Now + 0.4f;
        while (Now < end)
        {
            if (target != null)
                Script(left).Rotation = Quaternion.LookRotation(
                    VRRig.Instance.Origin.InverseTransformDirection(
                        target.Center() - VRRig.Instance.Hand(left).position
                    ),
                    Vector3.up
                );
            yield return null;
        }
        var interaction = Interaction.instance;
        bool hovered =
            interaction != null
            && target != null
            && ReferenceEquals(interaction.currentHovered, target);
        int pings = 0;
        bool used = false;
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        end = Now + 0.8f;
        while (Now < end && !used)
        {
            if (c.input.pingWasPressed)
                pings++;
            used = item == null || item.isUsingPrimary || item.castProgress > 0f;
            yield return null;
        }
        Script(left).Trigger = 0f;
        yield return Wait(0.3f);
        var note =
            $"{prefab.name} in hand, pointed at {(hovered ? "the hovered" : "an unhovered")} Apple: used {used}, pings {pings}";
        if (pings > 0)
            Fail("the holding hand's trigger pinged: " + note);
        else if (!used)
            Fail("the holding hand's trigger did not use the item: " + note);
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
        yield return WarpTo(target + Flat(c.Center - target) * distance);
        var rig = VRRig.Instance;
        rig.RotateYaw(
            Vector3.SignedAngle(Flat(rig.Head.forward), Flat(target - c.Center), Vector3.up)
        );
        yield return Wait(0.6f);
    }

    /// <summary>Warps onto the ground under <paramref name="stand"/> and waits for the body to settle. Sets
    /// <see cref="warpedForProp"/>, so the step's <see cref="WarpBack"/> returns to spawn.</summary>
    IEnumerator WarpTo(Vector3 stand)
    {
        var c = Character.localCharacter;
        string on = "no ground hit";
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
        {
            stand = ground.point + Vector3.up * 0.1f;
            on = "on " + ground.collider.name;
        }
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        Plugin.Log.LogDebug(
            $"[selftest] warp to {stand} ({on}): body settled {FlatDistance(c.Center, stand):0.00} m away"
        );
    }

    /// <summary>Farthest the scripted hand may lead the item point it carries. The held item hangs off the ragdoll
    /// arm (0.6 m reach), so past that the item has stopped and the hand would chase it forever (4 m out in vr9).</summary>
    const float MaxCarryLead = 0.8f;

    /// <summary>Seconds without the item point getting closer to the goal that count as a stall.</summary>
    const float CarryStallSeconds = 0.4f;

    float carryBest;
    float carryBestAt;
    Vector3 carryLead;

    /// <summary>Starts a <see cref="CarryToward"/> run.</summary>
    void BeginCarry()
    {
        carryBest = float.MaxValue;
        carryBestAt = Now;
        carryLead = Vector3.zero;
    }

    /// <summary>Moves the scripted hand toward the point where <paramref name="part"/> (an item point the hand
    /// carries) reaches <paramref name="goal"/>; one frame of a 1.5 m/s approach. Once the item stops getting closer
    /// (out of reach or blocked) the hand goes back to where it held the item then and stays, as a player stops
    /// pushing; a hand that kept pushing shoved the body back from the fire (vr11, 0.95 to 1.12 m).</summary>
    void CarryToward(bool left, Vector3 part, Vector3 goal)
    {
        var rig = VRRig.Instance;
        var hand = rig.Hand(left).position;
        float d = Vector3.Distance(part, goal);
        if (d < carryBest - 0.01f)
        {
            carryBest = d;
            carryBestAt = Now;
            carryLead = hand - part;
        }
        else if (Now - carryBestAt > CarryStallSeconds)
        {
            // Once: re-placing it relative to a sagging item every frame would walk the hand down.
            if (carryLead != Vector3.zero)
                SetHand(left, rig.Origin.InverseTransformPoint(part + carryLead));
            carryLead = Vector3.zero;
            return;
        }
        if ((hand - part).sqrMagnitude > MaxCarryLead * MaxCarryLead)
            return;
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
        if (interaction == null)
        {
            Fail("no Interaction instance");
            yield break;
        }
        yield return WaitFor(() => ReferenceEquals(interaction.currentHeldInteractible, lug), 2f);
        if (!ReferenceEquals(interaction.currentHeldInteractible, lug))
        {
            var hand = VRRig.Instance.Hand(left).position;
            Fail(
                $"grip did not hold the suitcase: grip {PhysicalInteraction.HandAction(left)} on "
                    + $"{Label(PhysicalInteraction.HandTarget(left))}, hand {Gap(hand, lug):0.00} m from "
                    + $"{lug.name} (lid point {top - lug.Center()} from its centre), hovered "
                    + $"{Label(interaction.currentHovered)}, held {Label(interaction.currentHeldInteractible)}"
            );
            yield break;
        }
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

    /// <summary>The lid surface straight above the suitcase's centre (<c>Luggage.Center</c>, its renderers,
    /// Luggage.cs:147), just under it, from the case's own solid colliders. The bounds of all its colliders put the
    /// hand 0.1 m above the eye and 1.27 m out in vr9 (a trigger or extra collider widens them), out of grab reach.</summary>
    static Vector3 LidPoint(Luggage lug)
    {
        var center = lug.Center();
        var down = new Ray(center + Vector3.up * 2f, Vector3.down);
        float best = float.MaxValue;
        foreach (var col in lug.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger && col.Raycast(down, out var hit, 2f) && hit.distance < best)
                best = hit.distance;
        }
        return best < float.MaxValue
            ? down.GetPoint(best) + Vector3.down * 0.03f
            : center + Vector3.up * 0.1f;
    }

    /// <summary>Distance from <paramref name="point"/> to the nearest solid collider of <paramref name="lug"/>.</summary>
    static float Gap(Vector3 point, Luggage lug)
    {
        float best = float.MaxValue;
        foreach (var col in lug.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger)
                best = Mathf.Min(best, Vector3.Distance(col.ClosestPoint(point), point));
        }
        return best;
    }

    static string Label(IInteractible it) =>
        it is Component comp && comp != null ? comp.name
        : it == null ? "nothing"
        : it.GetType().Name;

    /// <summary>vr.26: a held torch, lit with its trigger, touched to an unlit campfire holds the game's light hold on it
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
        var fireBase = ItemGestures.FireBase(fire);
        yield return ApproachFire(fireBase);
        yield return GiveAndHold(torchPrefab);
        if (!stepOk)
            yield break;
        var torch = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        // A torch is handed over unlit (a Lantern, FlareActive false; vr13): light it as a player does, with the
        // holding hand's trigger (its primary use, Lantern.cs:193).
        if (!HandLights.IsLit(torch))
        {
            Script(left).Trigger = 1f;
            yield return WaitFor(() => HandLights.IsLit(torch), torch.usingTimePrimary + 2f);
            Script(left).Trigger = 0f;
            yield return Wait(0.3f);
        }
        if (!HandLights.IsLit(torch))
        {
            Fail(
                $"{torch.name} did not light on its use (trigger held {torch.usingTimePrimary + 2f:0.0} s)"
            );
            yield break;
        }
        var flame = FireGestures.FlamePoint(torch);
        if (flame == null)
            flame = torch.transform;
        // The near side of the light zone (FireGestures: 0.6 m around the base, -0.1 to 0.6 m up), in arm's reach.
        var goal = fireBase + Flat(c.Center - fireBase) * 0.2f + Vector3.up * 0.2f;
        var interaction = Interaction.instance;
        float progress = 0f;
        float end = Now + 5f;
        string state = "";
        BeginCarry();
        while (Now < end && progress < 0.3f)
        {
            CarryToward(left, flame.position, goal);
            state = FireTrace("vr.26", flame.position, fireBase, left);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire))
                progress = interaction.constantInteractableProgress;
            yield return null;
        }
        if (progress >= 0.3f)
            yield return Shot("vr-26-light");
        // Torch raised to head height: the flame leaves the light zone (0.6 m up, plus the touch exit margin) even
        // from as close as the body stands.
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(
            left,
            head + (left ? -right : right) * 0.25f + Vector3.up * 0.1f + forward * 0.15f,
            0.3f
        );
        yield return Wait(0.5f);
        var note =
            $"{torch.name} at {fire.name}: light hold progress {progress:0.00}, fire {fire.state} after pulling away; at the end {state}";
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
        var fireBase = ItemGestures.FireBase(fire);
        yield return ApproachFire(fireBase);
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
        // The near side of the heat column (FireGestures: 0.6 m around the base, 0.2 to 1 m up), in arm's reach.
        var goal = fireBase + Flat(c.Center - fireBase) * 0.35f + Vector3.up * 0.55f;
        var interaction = Interaction.instance;
        float progress = 0f;
        bool shot = false;
        string state = "";
        float end = Now + fire.cookTime + 4f;
        BeginCarry();
        while (Now < end && food != null && Cooked(food) == before)
        {
            CarryToward(left, food.Center(), goal);
            state = FireTrace("vr.25", food.Center(), fireBase, left);
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
            $"{NameOf(food)} over {fire.name}: cook hold progress {progress:0.00} of {fire.cookTime:0.0} s, cooked {before} -> {after}; at the end {state}";
        if (after <= before)
            Fail("not cooked: " + note);
        else
            stepNote = note;

        static string NameOf(Item it) => it != null ? it.name : "item (gone)";
    }

    /// <summary>How close the fire steps' body gets to the fire base. The item hangs off a 0.6 m ragdoll arm: from
    /// 1.1-1.2 m (vr9) or 1.8 m (vr10, warped to 0.95 m but ended there) it never reached the zone.</summary>
    const float FireStand = 0.95f;

    /// <summary>Warps a step back from the fire, faces it, and walks in (move stick at half tilt) until the body
    /// is <see cref="FireStand"/> out or stops getting closer: logs, stones or a bench in the way stop it, as they
    /// stop a player. A warp straight to <see cref="FireStand"/> can land on the fire's props and be pushed off.</summary>
    IEnumerator ApproachFire(Vector3 fireBase)
    {
        yield return WarpFacing(fireBase, FireStand + 0.8f);
        var c = Character.localCharacter;
        bool hand = !VRInput.LeftHanded;
        float start = FlatDistance(c.Center, fireBase);
        float best = start;
        float bestAt = Now;
        float end = Now + 4f;
        Script(hand).Stick = new Vector2(0f, 0.5f);
        while (Now < end && best > FireStand && Now - bestAt < 0.5f)
        {
            yield return null;
            float d = FlatDistance(c.Center, fireBase);
            if (d < best - 0.02f)
            {
                best = d;
                bestAt = Now;
            }
        }
        Script(hand).Stick = Vector2.zero;
        yield return Wait(0.5f);
        Plugin.Log.LogDebug(
            $"[selftest.fire] warped {start:0.00} m out from the fire base, walked in to {FlatDistance(c.Center, fireBase):0.00} m"
        );
    }

    static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    float nextFireTrace;
    string fireTraceText = "";

    /// <summary>A fire step's state: the item in hand, where its carried point is against the fire base, how far the
    /// scripted hand leads it, what it rests against, the body's distance, and the game's interact hold. Logged at
    /// Debug twice a second.</summary>
    string FireTrace(string id, Vector3 part, Vector3 fireBase, bool left)
    {
        if (Now < nextFireTrace)
            return fireTraceText;
        nextFireTrace = Now + 0.5f;
        var c = Character.localCharacter;
        var interaction = Interaction.instance;
        var held = c.data.currentItem;
        var off = part - fireBase;
        float lead = Vector3.Distance(VRRig.Instance.Hand(left).position, part);
        var touching = new List<string>();
        foreach (
            var col in Physics.OverlapSphere(
                part,
                0.15f,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            if (held == null || col.GetComponentInParent<Item>() != held)
                touching.Add(col.name);
        }
        fireTraceText =
            $"in hand {(held != null ? held.name : "nothing")}, item point {new Vector2(off.x, off.z).magnitude:0.00} m "
            + $"out and {off.y:0.00} m up from the fire base, hand {lead:0.00} m ahead of it, near "
            + $"[{string.Join(", ", touching)}], body {FlatDistance(c.Center, fireBase):0.00} m out; interact "
            + $"{c.input.interactIsPressed}, hovered {Label(interaction != null ? interaction.currentHovered : null)}, "
            + $"interact hold on {Label(interaction != null ? interaction.currentHeldInteractible : null)}";
        Plugin.Log.LogDebug($"[selftest.fire] {id}: {fireTraceText}");
        return fireTraceText;
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
