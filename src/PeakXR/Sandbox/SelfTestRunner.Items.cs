using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Interact.Aim;
using PeakXR.Interact.Binoculars;
using PeakXR.Interact.Lights;
using PeakXR.Interact.Physical;
using PeakXR.Interact.Tools;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using STATUS = CharacterAfflictions.STATUSTYPE;
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
        yield return ItemStep("vr.22b-bandage-forearm", "Bandage", 30f, ForearmBandageStep);
        yield return ItemStep("vr.23-parasol", "Parasol", 25f, ParasolStep);
        yield return ItemStep("vr.28-use-while-pointing", AppleFragment, 25f, UsePointingStep);
        yield return HandStep("vr.24-lift-lid", 40f, LiftLidStep());
        yield return WarpBack();
        // Light first: it needs the campfire unlit. Cooking then uses the fire it lit (or lights it when vr.26 failed).
        yield return ItemStep("vr.26-light-with-flame", "Torch", 40f, LightStep);
        yield return WarpBack();
        // The game has no way to put a campfire out again short of Extinguish_Rpc, which leaves it spent; and
        // Light_Rpc is buffered. So the fire stays lit for the rest of the run (vr.27 uses it).
        yield return ItemStep("vr.25-cook-over-fire", AppleFragment, 40f, CookStep);
        yield return WarpBack();
        yield return ItemStep("vr.25b-cook-danger", AppleFragment, 40f, CookDangerStep);
        yield return WarpBack();
        yield return HandStep("vr.27-warm-hands", 30f, WarmStep());
        yield return WarpBack();
        yield return HandStep("vr.160-use-routes", 40f, UseRoutesStep());
        yield return ItemStep("vr.161-drink", "Antidote", 25f, DrinkStep);
        yield return ItemStep("vr.162-build-not-at-mouth", "Checkpoint Flag", 20f, NotAtMouthStep);
        yield return ItemStep("vr.163-build", "Checkpoint Flag", 30f, BuildStep);
        yield return ItemStep("vr.164-commune-guard", "Cursed Skull", 25f, CommuneGuardStep);
        yield return ItemStep("vr.165-read-cancel", "Book of Bones", 20f, ReadCancelStep);
        yield return ItemStep("vr.166-eat-cancel", AppleFragment, 20f, EatCancelStep);
        yield return HandStep("vr.167-secondary-deny", 20f, SecondaryDenyStep());
        yield return ItemStep("vr.168-shoot-hand", "RopeShooter", 30f, ShootHandStep);
        yield return ItemStep("vr.169-zoom-endstop", "Binoculars", 20f, ZoomEndStopStep);
        // vr.169 raises the binoculars in Hold mode; a failed or timed-out step leaves it forced.
        BinocularsConfig.ForceHold = false;
        yield return ItemStep("vr.170-throw-mid-cast", AppleFragment, 25f, ThrowMidCastStep);
        yield return HandStep("vr.171-vine-no-path", 30f, VineNoPathStep());
        yield return HandStep("vr.172-warp-refund", 25f, WarpRefundStep());
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
        // The scripted setup's budget plus the item's own use time.
        yield return HandStep(
            id,
            timeout + GameTiming.Budget(GameTiming.UseSeconds(item)),
            body(item)
        );
    }

    /// <summary>Farthest the holding hand may be from the mouth when the toot starts: HeldGestures' mouth exit radius
    /// (0.22 m) plus the scripted approach's last frame.</summary>
    const float BugleMouthExit = 0.25f;

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
        // Where the toot began: a bugle that toots anywhere would also pass the loop above on its first frame.
        float gap = Vector3.Distance(
            MouthGesture
                .MouthPoint(
                    rig.Head.position.N(),
                    rig.Head.rotation.N(),
                    MouthGesture.DefaultOffset
                )
                .U(),
            rig.Hand(left).position
        );
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
            $"{item.name}: toot {started} with the hand {gap:0.00} m from the mouth, pitch {pitch:0.00} (bell {expected:0.00}), stopped {!sfx.hold}";
        if (!started)
            Fail("no toot at the mouth: " + note);
        else if (gap > BugleMouthExit)
            Fail("the toot started away from the mouth: " + note);
        else if (Mathf.Abs(pitch - expected) > 0.1f)
            Fail("pitch not from the bell: " + note);
        else if (sfx.hold)
            Fail("toot did not stop: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.22: a healing item pressed to the chest holds its primary use (<c>Item.castProgress</c>,
    /// Item.cs:344) until the cast finishes (<c>lastFinishedCast</c>, Item.cs:272) or the item is used up.</summary>
    IEnumerator BandageStep(Item prefab) => Bandage(prefab, false);

    /// <summary>vr.22b: the same use from the other route HeldGestures takes (HeldGestures.cs:182-196), the item
    /// pressed to the free arm's forearm held out in front, well clear of the chest: the torso stays farther than
    /// the press's exit distance, so only the forearm can have made the contact.</summary>
    IEnumerator ForearmBandageStep(Item prefab) => Bandage(prefab, true);

    /// <summary>HeldGestures' torso radius (HeldGestures.cs:41, private); a hand farther than <see cref="BandageChestExit"/>
    /// from that torso is no chest press.</summary>
    const float BandageTorsoRadius = 0.22f;

    /// <summary>The chest press's exit distance (HeldGestures' Body dwell, HeldGestures.cs:33-38; it starts only at
    /// 0).</summary>
    const float BandageChestExit = 0.05f;

    IEnumerator Bandage(Item prefab, bool forearm)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        float finishedBefore = item.lastFinishedCast;
        float consumedBefore = c.data.lastConsumedItem;
        // Room for the status the item lowers (Bandages: Injury -0.3 on cast finished); AddStatus lifts god mode.
        bool measured = StatusEffect(item, out var status, out float change) && change < 0f;
        if (measured)
            SandboxActions.AddStatus(status, Mathf.Min(0.5f, -change));
        var afflictions = c.refs.afflictions;
        float statusBefore = measured ? afflictions.GetCurrentStatus(status) : 0f;
        HeadFrame(out var head, out var forward, out var right);
        var chest = head + Vector3.down * 0.45f + forward * 0.08f + (left ? -right : right) * 0.05f;
        if (forearm)
        {
            // The free hand out in front pointing ahead, its forearm 0.05-0.3 m behind the grip
            // (BodyZones.ForearmStart/End); the item goes to the forearm's middle.
            var arm = head + forward * 0.6f + Vector3.down * 0.35f;
            yield return MoveTrack(!left, arm, 0.4f);
            Script(!left).Rotation = Quaternion.LookRotation(forward, Vector3.up);
            chest = arm - forward * 0.17f;
        }
        yield return MoveTrack(left, chest, 0.6f);
        var rig = VRRig.Instance;
        float nearestTorso = float.MaxValue;
        float progress = 0f;
        float useTime = GameTiming.UseSeconds(item);
        float end = Now + GameTiming.Budget(useTime, 2f);
        bool used = false;
        bool shot = false;
        while (Now < end)
        {
            // Used means the cast finished or the item was consumed; a drop also leaves the hand and is no use.
            if (
                item == null
                || item.lastFinishedCast > finishedBefore
                || c.data.lastConsumedItem != consumedBefore
            )
            {
                used = true;
                break;
            }
            if (c.data.currentItem != item)
                break;
            progress = Mathf.Max(progress, item.castProgress);
            nearestTorso = Mathf.Min(
                nearestTorso,
                BodyZones.TorsoDistance(
                    rig.Hand(left).position.N(),
                    rig.Head.position.N(),
                    Flat(rig.Head.forward).N(),
                    BandageTorsoRadius
                )
            );
            if (!shot && progress > 0.3f)
            {
                // The hold ring sits above the acting hand, which is at the chest, under the view.
                shot = true;
                yield return ShotAt(
                    forearm ? "vr-22b-bandage-forearm" : "vr-22-bandage",
                    VRRig.Instance.Hand(left).position
                );
                continue;
            }
            yield return null;
        }
        yield return Wait(0.5f);
        float statusAfter = measured ? afflictions.GetCurrentStatus(status) : 0f;
        var note =
            $"{prefab.name} on the {(forearm ? "forearm" : "chest")}: cast progress {progress:0.00} of {useTime:0.0} s, used {used}";
        if (forearm)
            note += F($", hand at least {nearestTorso:0.00} m from the torso");
        if (measured)
            note +=
                $", {status} {statusBefore:0.00} -> {statusAfter:0.00} (item changes it by {change:+0.00;-0.00})";
        if (!used)
            Fail($"no use on the {(forearm ? "forearm" : "chest")}: " + note);
        else if (forearm && nearestTorso <= BandageChestExit)
            Fail(
                "the hand came near enough the chest to press there, so the forearm route is unproven: "
                    + note
            );
        // Half the amount: statuses creep between the reads, and the start value caps a large change.
        else if (measured && statusBefore - statusAfter < Mathf.Min(-change, statusBefore) * 0.5f)
            Fail($"used but {status} did not fall: " + note);
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
        // Each stroke is one primary use of the parasol (Action_Parasol), so it takes the item's use time.
        float toggleTime = GameTiming.Budget(GameTiming.UseSeconds(item), 1f);
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
        yield return WaitFor(() => parasol.isOpen != before, toggleTime);
        bool toggled = parasol.isOpen != before;
        if (toggled)
            yield return Shot("vr-23-parasol");
        yield return MoveTrack(free, at - Vector3.up * 0.05f, 0.5f);
        yield return WaitFor(() => parasol.isOpen == before, toggleTime);
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
    /// player came from, and turns the rig to face it (<c>Character.WarpPlayerRPC</c>, Character.cs:1824).</summary>
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

    /// <summary>One frame of steering a carried item point that drifted out of its zone back toward
    /// <paramref name="goal"/>, at 1.5 m/s and only while the hand leads the item by at most
    /// <see cref="MaxCarryLead"/>. Unlike <see cref="CarryToward"/> it never jumps the hand to a remembered lead: that
    /// stall reset re-armed on every swing of the torch and left it 1.25 m out, behind the body (vr25, light hold
    /// 0.38 of 2 s).</summary>
    void SteerBack(bool left, Vector3 part, Vector3 goal)
    {
        var rig = VRRig.Instance;
        var hand = rig.Hand(left).position;
        if ((hand - part).sqrMagnitude > MaxCarryLead * MaxCarryLead)
            return;
        SetHand(
            left,
            rig.Origin.InverseTransformPoint(
                Vector3.MoveTowards(hand, goal + (hand - part), 1.5f * Time.unscaledDeltaTime)
            )
        );
    }

    /// <summary>vr.24: a grip on the top of the nearest closed suitcase holds the open timer at the lid
    /// (<c>Interaction.constantInteractableProgress</c>, Interaction.cs:57) while the hand stays still, and lifting
    /// it opens the case (<c>Luggage.IsOpen</c>, Luggage.cs:37). The nearest closed case anywhere in the active
    /// segment: the daily map puts them anywhere, and the warp reaches any of them.</summary>
    IEnumerator LiftLidStep()
    {
        var c = Character.localCharacter;
        var lug = Nearest<Luggage>(c.Center, l => !l.IsOpen && l is not RespawnChest);
        if (lug == null)
        {
            stepSkip = "no closed suitcase in the active segment";
            yield break;
        }
        if (!Setting("Interaction", "LiftLidToOpen", true))
        {
            stepSkip = "Interaction.LiftLidToOpen is off";
            yield break;
        }
        yield return WarpFacing(lug.Center(), 0.8f);
        yield return LiftLid(lug);
    }

    /// <summary>The body of vr.24 once the character stands at <paramref name="lug"/>.</summary>
    IEnumerator LiftLid(Luggage lug)
    {
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
        // The lid's own open time (timeToOpen, Luggage.cs:214). Still long enough that a timer running without the
        // lift would pass the 0.15 progress limit below twice over.
        float openTime = GameTiming.InteractSeconds(lug, Character.localCharacter);
        yield return Wait(Mathf.Max(1f, 0.3f * openTime));
        float still = interaction.constantInteractableProgress;
        yield return Shot("vr-24-lift-lid");
        var at = Script(left).Position;
        float rate = LidGesture.LiftHeight * 1.4f / Mathf.Max(0.5f, openTime);
        float end = Now + GameTiming.Budget(openTime, 1f);
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
            $"{lug.name}: timeToOpen {openTime:0.0} s, progress with the hand still {still:0.00}, lifted {lifted:0.00} m, open {lug.IsOpen}";
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

    /// <summary>vr.26: a held torch, lit with its trigger, held to an unlit campfire runs the game's light hold on it
    /// (<c>Interaction.currentHeldInteractible</c>, Interaction.cs:121) for its <c>GetInteractTime</c> (Campfire.cs:441)
    /// until the fire is lit. <see cref="SelfTestCampfirePatch"/> lights it without the segment advance.</summary>
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
        float lightUse = GameTiming.Budget(GameTiming.UseSeconds(torch));
        if (!HandLights.IsLit(torch))
        {
            Script(left).Trigger = 1f;
            yield return WaitFor(() => HandLights.IsLit(torch), lightUse);
            Script(left).Trigger = 0f;
            yield return Wait(0.3f);
        }
        if (!HandLights.IsLit(torch))
        {
            Fail($"{torch.name} did not light on its use (trigger held {lightUse:0.0} s)");
            yield break;
        }
        var flame = FireGestures.FlamePoint(torch);
        if (flame == null)
            flame = torch.transform;
        // The near side of the light zone (FireGestures: 0.6 m around the base, -0.1 to 0.6 m up), in arm's reach.
        var goal = fireBase + Flat(c.Center - fireBase) * 0.2f + Vector3.up * 0.2f;
        // Squat to the low zone as a player does, so the arm reaches it.
        yield return StoopOver(goal, 0.6f);
        var interaction = Interaction.instance;
        float progress = 0f;
        // The fire's own hold (GetInteractTime, Campfire.cs:441), after up to 3 s carrying the flame in.
        float holdTime = GameTiming.InteractSeconds(fire, c);
        float end = Now + GameTiming.Budget(holdTime, 3f);
        bool shot = false;
        string state = "";
        bool entered = false;
        int drifts = 0;
        float worst = 0f;
        bool wasOut = false;
        SelfTestCampfirePatch.Hold = true;
        BeginCarry();
        while (Now < end && !fire.Lit)
        {
            // Carry the flame in; from then on hold it there for the whole hold, steering back only on a drift.
            float zone = FireGestures.LightZoneDistance(fire, flame.position);
            entered |= zone <= 0f;
            if (!entered)
                CarryToward(left, flame.position, goal);
            else
            {
                bool outNow = zone > 0f;
                if (outNow && !wasOut)
                    drifts++;
                wasOut = outNow;
                worst = Mathf.Max(worst, zone);
                if (outNow)
                    SteerBack(left, flame.position, goal);
            }
            HoldInReach(left);
            state = FireTrace("vr.26", flame.position, fireBase, left);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire))
                progress = Mathf.Max(progress, interaction.constantInteractableProgress);
            if (!shot && progress > 0.5f)
            {
                shot = true;
                // Between the flame and the fire base, backed off to frame torch, hand and logs.
                yield return ShotAt(
                    "vr-26-light",
                    Vector3.Lerp(flame.position, fireBase, 0.5f),
                    1.2f
                );
                continue;
            }
            yield return null;
        }
        SelfTestCampfirePatch.Hold = false;
        bool lit = fire.Lit;
        if (lit)
            yield return ShotAt("vr-26-lit", fireBase + Vector3.up * 0.3f, 1.5f);
        var note =
            $"{torch.name} at {fire.name}: light hold progress {progress:0.00} of {holdTime:0.0} s, fire {fire.state}; "
            + $"flame reached the light zone {entered}, drifted out {drifts} times (worst {worst:0.00} m out); {ReachNote()}; at the end {state}";
        if (progress <= 0f)
            Fail("flame did not start the light hold: " + note);
        else if (!lit)
            Fail("hold did not light the fire: " + note);
        else
            stepNote = note;
    }

    /// <summary>vr.25: a cookable item held in the heat column over a lit campfire cooks through the game's hold
    /// (<c>DataEntryKey.CookedAmount</c> +1, ItemCooking.cs:264), the player squatting and the hand within reach.
    /// vr.26 lights the fire with the flame gesture; when it failed, this lights the fire with
    /// <c>Campfire.LightWithoutReveal</c> (Campfire.cs:468: no segment advance) and names that shortcut in the note
    /// (vr.26 then fails on its own).</summary>
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
        bool litHere = !fire.Lit;
        if (litHere)
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
        yield return StoopOver(goal, 0.6f);
        var interaction = Interaction.instance;
        float progress = 0f;
        bool shot = false;
        string state = "";
        // The fire's interact hold, cookTime (Campfire.cs:441), after up to 3 s carrying the food into the heat.
        float cookTime = GameTiming.InteractSeconds(fire, c);
        float end = Now + GameTiming.Budget(cookTime, 3f);
        BeginCarry();
        while (Now < end && food != null && Cooked(food) == before)
        {
            CarryToward(left, food.Center(), goal);
            HoldInReach(left);
            state = FireTrace("vr.25", food.Center(), fireBase, left);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire))
                progress = Mathf.Max(progress, interaction.constantInteractableProgress);
            if (!shot && progress > 0.3f)
            {
                shot = true;
                yield return ShotAt("vr-25-cook", food.Center());
                continue;
            }
            yield return null;
        }
        int after = food != null ? Cooked(food) : before;
        var note =
            (
                litHere
                    ? "shortcut: the fire was lit by Campfire.LightWithoutReveal, as vr.26 left it unlit; "
                    : "fire lit by vr.26's flame; "
            )
            + $"{NameOf(food)} over {fire.name}: cook hold progress {progress:0.00} of {cookTime:0.0} s, cooked {before} -> {after}; {ReachNote()}; at the end {state}";
        if (after <= before)
            Fail("not cooked: " + note);
        else
            stepNote = note;

        static string NameOf(Item it) => it != null ? it.name : "item (gone)";
    }

    /// <summary>vr.25b: food already cooked twice (CookedAmount 2, so one more burns it: CookRisk, ItemCooking.cs:174-257)
    /// held over the fire lit by vr.25/26. While the game's cook hold runs, the food hand gets the Danger feel; the
    /// hand then pulls back out of the heat before the hold ends, so the food stays unburnt.</summary>
    IEnumerator CookDangerStep(Item foodPrefab)
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.Lit);
        if (fire == null || !Affordance.Enabled || !Setting("Interaction", "CookOverFire", true))
        {
            stepSkip =
                fire == null ? "no lit campfire (vr.25/26 light it)"
                : !Affordance.Enabled ? "ui.affordance not installed"
                : "Interaction.CookOverFire is off";
            yield break;
        }
        var fireBase = ItemGestures.FireBase(fire);
        yield return ApproachFire(fireBase);
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
        food.GetData<IntItemData>(DataEntryKey.CookedAmount).Value = 2;
        var goal = fireBase + Flat(c.Center - fireBase) * 0.35f + Vector3.up * 0.55f;
        yield return StoopOver(goal, 0.6f);
        float cookTime = GameTiming.InteractSeconds(fire, c);
        float mark = CueMark();
        float end = Now + GameTiming.Budget(cookTime * 0.8f, 3f);
        var interaction = Interaction.instance;
        // The danger has to come from the cook hold (ItemGestures.CookWarning), not a fall or pass-out cue.
        bool holding = false;
        BeginCarry();
        while (
            Now < end
            && food != null
            && Cooked(food) == 2
            && !CueLog.Capture.Felt(left, CueFeel.Danger, mark, out _)
        )
        {
            CarryToward(left, food.Center(), goal);
            HoldInReach(left);
            yield return null;
            holding |=
                interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire);
        }
        var back = c.Center + Flat(c.Center - fireBase) * 0.3f + Vector3.up * 0.3f;
        yield return MoveTo(left, () => back, 0.3f);
        yield return ExpectFeel(left, CueFeel.Danger, mark, within: 0f);
        if (!stepOk)
            yield break;
        int after = food != null ? Cooked(food) : -1;
        var note =
            $"{foodPrefab.name} at cook level 2 over {fire.name}: {cueNote}; game cook hold seen {holding}; cooked after {after}; {ReachNote()}";
        if (!holding)
            Fail("the danger came without the game's cook hold on the fire: " + note);
        else if (after != 2)
            Fail("the danger came too late to pull the food back: " + note);
        else
            Note(note);
    }

    /// <summary>Frames <see cref="HoldInReach"/> held the hand back since the last <see cref="StoopOver"/>.</summary>
    int reachHeld;

    /// <summary>
    /// The fire steps' squat: the head lowers (which also crouches the character, as <see cref="ReachDown"/> does)
    /// until it is at most <see cref="StoopReach"/> over <paramref name="world"/>, leaving the hands where they are.
    /// Resets <see cref="reachHeld"/>. The step's teardown stands back up.
    /// </summary>
    IEnumerator StoopOver(Vector3 world, float seconds)
    {
        reachHeld = 0;
        var rig = VRRig.Instance;
        if (headDrop <= 0f)
            stoopFrom = VRInput.HeadPosition.y;
        float from = headDrop;
        float low = Mathf.Max(
            0f,
            stoopFrom - rig.Origin.InverseTransformPoint(world).y - StoopReach
        );
        float start = Now;
        while (true)
        {
            float t = (Now - start) / seconds;
            headDrop = Mathf.SmoothStep(from, low, t);
            if (t >= 1f)
                yield break;
            yield return null;
        }
    }

    /// <summary>Keeps the scripted hand within <see cref="MaxHandFromEye"/> of the eye: a carry that would stretch
    /// past a player's arm stops at it instead, so the step passes only from a reachable pose.</summary>
    void HoldInReach(bool left)
    {
        ref var s = ref Script(left);
        var off = s.Position - VRInput.HeadPosition;
        if (off.magnitude <= MaxHandFromEye)
            return;
        s.Position = VRInput.HeadPosition + off.normalized * MaxHandFromEye;
        reachHeld++;
    }

    string ReachNote() =>
        F(
            $"stooped {headDrop:0.00} m, hand held back at the {MaxHandFromEye:0.0} m reach for {reachHeld} frames"
        );

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
        var hand = VRRig.Instance.Hand(left).position;
        float lead = Vector3.Distance(hand, part);
        // Arm's reach is about 0.6-0.75 m from the shoulder, some 0.8 m from the eye: past that the pose is no player's.
        float reach = Vector3.Distance(VRRig.Instance.Head.position, hand);
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
            + $"out and {off.y:0.00} m up from the fire base, hand {lead:0.00} m ahead of it and {reach:0.00} m from the eye, near "
            + $"[{string.Join(", ", touching)}], body {FlatDistance(c.Center, fireBase):0.00} m out; interact "
            + $"{c.input.interactIsPressed}, hovered {Label(interaction != null ? interaction.currentHovered : null)}, "
            + $"interact hold on {Label(interaction != null ? interaction.currentHeldInteractible : null)}";
        Plugin.Log.LogDebug($"[selftest.fire] {id}: {fireTraceText}");
        return fireTraceText;
    }

    static int Cooked(Item item) => item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value;

    /// <summary>vr.27: an open palm held toward a lit campfire sends interact.items' warm pulses
    /// (<c>FireGestures.WarmPulses</c>, counted where <c>Haptics.Pulse</c> is called). The count is one for both
    /// hands, so each palm is tried alone with the other hand's back to the fire, and each must warm; the backs of
    /// both hands send none. Uses the campfire vr.25 lit; skips when none is lit.</summary>
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
        // The heat point FireGestures.UpdateWarmth measures from (0.3 m over the fire base).
        var heat = ItemGestures.FireBase(fire) + Vector3.up * 0.3f;
        // Walk in as a player does, then hold both hands out low in front, in reach: 0.45 m ahead of the eyes and
        // 0.65 m below them is about 0.65 m from the shoulder. The old pose put them 0.3 m over the fire base from
        // 1.3 m out, some 1.3 m from a standing shoulder.
        yield return ApproachFire(ItemGestures.FireBase(fire));
        HeadFrame(out var head, out var forward, out var right);
        var low = head + forward * 0.45f + Vector3.down * 0.65f;
        yield return MoveTrack(true, low - right * 0.15f, 0.5f);
        yield return MoveTrack(false, low + right * 0.15f, 0.5f);
        // Palms to the fire: +X of the left grip pose, -X of the right (HandRig.cs:62), in tracking space.
        var dir = Flat(rig.Origin.InverseTransformDirection(heat - c.Center));
        var leftRot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
        var rightRot = Quaternion.LookRotation(Vector3.Cross(-dir, Vector3.up), Vector3.up);
        string far = OverReach(true) ?? OverReach(false);
        if (far != null)
        {
            Fail(far);
            yield break;
        }
        // Left palm to the fire, right back to it (leftRot on the right hand turns its palm away), then the reverse.
        int before = FireGestures.WarmPulses;
        yield return HoldPalms(leftRot, leftRot, 1.5f);
        int leftPulses = FireGestures.WarmPulses - before;
        before = FireGestures.WarmPulses;
        yield return HoldPalms(rightRot, rightRot, 1.5f);
        int rightPulses = FireGestures.WarmPulses - before;
        float leftGap = Vector3.Distance(rig.Hand(true).position, heat);
        float rightGap = Vector3.Distance(rig.Hand(false).position, heat);
        yield return Shot("vr-27-warm-hands");
        // Backs of the hands to the fire: no warmth (PalmWarmth.MinFacing), so the pulses come from the palms.
        int away = FireGestures.WarmPulses;
        yield return HoldPalms(rightRot, leftRot, 1.5f);
        away = FireGestures.WarmPulses - away;
        var note =
            $"{fire.name}: left palm alone {leftPulses} warm pulses in 1.5 s, right palm alone {rightPulses}; palms {leftGap:0.00} m (left) and {rightGap:0.00} m (right) from the heat point; backs to the fire {away} pulses in 1.5 s";
        if (leftPulses <= 0 || rightPulses <= 0)
            Fail($"no warm pulse from the {(leftPulses <= 0 ? "left" : "right")} palm: " + note);
        else if (away > 0)
            Fail("warm pulses with the backs of the hands to the fire: " + note);
        else
            stepNote = note;
    }

    /// <summary>Holds both scripted hands at these rotations for <paramref name="seconds"/>.</summary>
    IEnumerator HoldPalms(Quaternion leftRot, Quaternion rightRot, float seconds)
    {
        float end = Now + seconds;
        while (Now < end)
        {
            Script(true).Rotation = leftRot;
            Script(false).Rotation = rightRot;
            yield return null;
        }
    }

    // ---------------------------------------------------------------- use.* (one item per category)

    /// <summary>
    /// use.*: give to the hand (<c>GameUtils.InstantiateAndGrab</c>), hold primary use through the item's own use
    /// time (<c>Item.StartUsePrimary</c>/<c>ContinueUsePrimary</c>, Item.cs:849-901), release, and prove what the use
    /// did in the game, in VR and flat. The use is the player's (<see cref="UseWay"/>): in VR the holding hand's
    /// trigger, or a mouth item raised to the mouth, so interact.physical's bindings and gates run; flat play's use
    /// button otherwise.
    /// <list type="bullet">
    /// <item>food: eaten (<c>CharacterData.lastConsumedItem</c>, CharacterData.cs:170) and Hunger down, after
    /// <c>AddStatus</c> (CharacterAfflictions.cs:712) gave it room;</item>
    /// <item>lantern and torch: lit (<c>FlareActive</c>, Lantern.cs:207-208) and burning fuel
    /// (<c>DataEntryKey.Fuel</c>, Lantern.cs:162-171);</item>
    /// <item>flare: lit (Flare.cs:57) with its flame spawned (<c>TrackableNetworkObject.hasTracker</c>,
    /// Flare.cs:32-34);</item>
    /// <item>rope cannon and chain launcher: aimed where their own <c>WillAttach</c> accepts (RopeShooter.cs:122-138,
    /// VineShooter.cs:71-92, read through <c>Item.CanUsePrimary</c>, Item.cs:797-801), a new rope anchor
    /// (RopeShooter.cs:104) with its rope spawned and anchored to it (<see cref="KeepRope"/>), or a vine
    /// (VineShooter.cs:61);</item>
    /// <item>spool (<see cref="SpoolUse"/>), compass (<see cref="CompassHeading"/>) and binoculars (Hold mode in VR)
    /// get checks of their own.</item>
    /// </list>
    /// </summary>
    IEnumerator UseItem(Item item)
    {
        yield return DropHeld();
        if (!SandboxActions.GiveItem(item))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Until(() => Holding(item), 5f, "not in hand");
        if (!stepOk)
            yield break;
        var held = HeldItem();
        spawned.Add(held.gameObject);
        yield return Wait(0.5f);
        // A rope spool's use is the tie-off, which needs an anchor in aim (RopeTier.Update).
        if (held.TryGetComponent<RopeTier>(out var tier))
        {
            yield return SpoolUse(held, tier);
            yield return DropHeld();
            yield break;
        }
        // An item with no primary use (Item.CanUsePrimary, Item.cs:797-805) gets a check of its own mechanic.
        var compass = held.GetComponentInChildren<CompassPointer>();
        if (compass != null && !held.CanUsePrimary())
        {
            yield return CompassHeading(compass);
            yield return DropHeld();
            yield break;
        }
        if (!held.UIData.hasMainInteract)
        {
            Fail(
                "no primary use (UIData.hasMainInteract off, Item.cs:803) and no check of its own mechanic"
            );
            yield return DropHeld();
            yield break;
        }
        var c = Character.localCharacter;
        var afflictions = c.refs.afflictions;
        bool vr = VRSession.Active && VRRig.Instance != null;
        bool left = LocalHold.ItemInLeftHand;
        // The player's own use: the holding hand's trigger, or for a mouth item the hand at the mouth (VR); flat
        // play's use button otherwise.
        var way = WayOf(held);
        if (vr)
            yield return ReadyHand(left);

        // Food gets room to lower Hunger (god mode keeps it at 0; Run restores god mode after the step).
        bool food =
            StatusEffect(held, out var status, out float change)
            && status == STATUS.Hunger
            && change < 0f;
        if (food)
            SandboxActions.AddStatus(status, Mathf.Min(0.5f, -change));
        float hungerBefore = food ? afflictions.GetCurrentStatus(status) : 0f;
        float consumedBefore = c.data.lastConsumedItem;
        var lantern = held.GetComponent<Lantern>();
        var flare = held.GetComponent<Flare>();
        var ropeShooter = held.GetComponent<RopeShooter>();
        var vineShooter = held.GetComponent<VineShooter>();
        bool shooter = ropeShooter != null || vineShooter != null;
        HashSet<int> shotsBefore = null;
        if (shooter)
        {
            shotsBefore = ropeShooter != null ? Ids<RopeAnchorProjectile>() : Ids<JungleVine>();
            yield return AimHeld(c, left, ShooterPitches, AllYaws, () => held.CanUsePrimary());
            if (!aimFound)
            {
                float range = ropeShooter != null ? ropeShooter.maxLength : vineShooter.maxLength;
                Fail(
                    $"no terrain within {range:0} m that {held.name} would attach to (its WillAttach refused every aim; grounded {c.data.isGrounded})"
                );
                ResetFlatLook(c, vr);
                yield return DropHeld();
                yield break;
            }
        }

        // Item.StartUsePrimary sets isUsingPrimary (Item.cs:849-855); an eaten or spent item leaves the hand.
        // Held through the item's own use time, so a slow cast gets to finish (finishedCast, Item.cs:883).
        bool started = false;
        float progress = 0f;
        float useTime = GameTiming.UseSeconds(held);
        // Binoculars in VR run in Hold mode: raised while use is held, lowered by the release alone.
        var binoculars = vr ? held.GetComponentInChildren<Action_ShowBinocularOverlay>() : null;
        BinocularsConfig.ForceHold = binoculars != null;
        // A mouth item first travels up to the mouth (about 0.4 m at 1.5 m/s) and dwells there.
        float end = Now + GameTiming.Budget(useTime, way == UseWay.Mouth ? 1.5f : 0f);
        while (Now < end && !held.finishedCast)
        {
            if (held == null || HeldItem() != held)
            {
                started = true;
                break;
            }
            HoldUse(way, left);
            started |= held.isUsingPrimary || held.finishedCast;
            progress = Mathf.Max(progress, held.castProgress);
            yield return null;
        }
        // A use with no cast time starts and finishes in one frame (Item.cs:893-898), which ends the loop unsampled.
        if (held != null && held.finishedCast)
        {
            started = true;
            progress = Mathf.Max(progress, held.castProgress);
        }
        bool raisedWhileHeld = false;
        if (binoculars != null)
        {
            yield return Wait(0.3f);
            raisedWhileHeld = binoculars != null && binoculars.binocularsActive;
        }
        ReleaseUse(way, left);
        ResetFlatLook(c, vr);
        bool lit = held != null && (lantern != null || flare != null) && HandLights.IsLit(held);
        float fuelBefore = Fuel(held);
        yield return Wait(1f);
        float fuelAfter = Fuel(held);
        bool flame =
            flare != null
            && held != null
            && held.TryGetComponent<TrackableNetworkObject>(out var track)
            && track.hasTracker;
        Component shot = null;
        if (shooter)
        {
            shot =
                ropeShooter != null
                    ? Fresh<RopeAnchorProjectile>(shotsBefore)
                    : Fresh<JungleVine>(shotsBefore);
            if (shot != null)
                spawned.Add(shot.gameObject);
            yield return KeepRope(shot);
        }
        bool gone = held == null || HeldItem() != held;
        var note =
            $"used by {UseWayText(way)}: primary use started {started}, cast progress {progress:0.00} of {useTime:0.0} s, left the hand {gone}";
        BinocularsConfig.ForceHold = false;
        if (binoculars != null)
        {
            note +=
                $", Hold mode: raised while held {raisedWhileHeld}, raised after release {binoculars != null && binoculars.binocularsActive}; {BinocularGiveNre}";
        }
        bool consumed = c.data.lastConsumedItem != consumedBefore;
        float hungerAfter = food ? afflictions.GetCurrentStatus(status) : 0f;
        if (food)
            note +=
                $", eaten {consumed}, Hunger {hungerBefore:0.00} -> {hungerAfter:0.00} (item changes it by {change:+0.00;-0.00})";
        if (lantern != null || flare != null)
            note += $", lit {lit}";
        if (lantern != null)
            note += $", fuel {fuelBefore:0.0} -> {fuelAfter:0.0} in 1 s";
        if (flare != null)
            note += $", flame {flame}";
        if (shooter)
            note += $", {(ropeShooter != null ? "rope anchor" : "vine")} spawned {shot != null}";
        if (ropeShooter != null && shot != null)
            note += $", rope {ropeMissing ?? "spawned anchored to it"}";
        if (!started)
            Fail("primary use never started: " + note);
        else if (binoculars != null && (!raisedWhileHeld || binoculars.binocularsActive))
            Fail("binoculars Hold mode: " + note);
        else if (food && !consumed)
            Fail("not eaten: " + note);
        // Half the amount: statuses creep between the reads, and the start value caps a large change.
        else if (food && hungerBefore - hungerAfter < Mathf.Min(-change, hungerBefore) * 0.5f)
            Fail("eaten but Hunger did not fall: " + note);
        else if ((lantern != null || flare != null) && !lit)
            Fail("not lit: " + note);
        else if (lantern != null && !(fuelAfter < fuelBefore))
            Fail("lit but burns no fuel: " + note);
        else if (flare != null && !flame)
            Fail("flare lit but no flame spawned: " + note);
        else if (shooter && shot == null)
            Fail("nothing fired where it was aimed: " + note);
        else if (ropeShooter != null && ropeMissing != null)
            Fail("the anchor brought no rope: " + note);
        else
            stepNote = note;
        // Eaten or spent items leave the hand on their own.
        yield return DropHeld();
    }

    /// <summary>Why the give logs a vanilla NullReferenceException in <c>ItemRenderFeatureManager.setFeatureActive</c>
    /// (VR run 46): <c>GameUtils.InstantiateAndGrabRPC</c> (GameUtils.cs:270) instantiates the binoculars and picks
    /// them up in the same call, and the pickup deactivates the new world object before its <c>Start</c> has looked
    /// up the renderer feature (ItemRenderFeatureManager.cs:17-28), so <c>OnDisable</c> (:49-52) dereferences a null
    /// feature (:42-47). The only PeakXR frame is the Harmony wrapper of Item.Interact (ItemGestures.BeforePickup, a
    /// prefix that deactivates nothing); the SetActive is the game's own. Vanilla gives through the same call
    /// (Action_ConsumeAndSpawn, Action_SpawnGuidebookPage).</summary>
    const string BinocularGiveNre =
        "the give's ItemRenderFeatureManager NullReferenceException is vanilla (InstantiateAndGrab disables the new item before its Start)";

    /// <summary>Set by <see cref="KeepRope"/>: null when the rope anchor spawned its rope tied to it, else what is
    /// missing.</summary>
    string ropeMissing;

    /// <summary>A rope cannon's anchor flies to the hit (distance x 0.01 s, RopeShooter.cs:105), then spawns its rope
    /// and ties it to itself (<c>SpawnRope</c>, <c>AttachToAnchor_Rpc</c>; RopeAnchorProjectile.cs:40-68,
    /// RopeAnchorWithRope.cs:26-37): waits for that rope, hands it to teardown with the anchor and sets
    /// <see cref="ropeMissing"/>.</summary>
    IEnumerator KeepRope(Component shot)
    {
        ropeMissing = null;
        if (shot is not RopeAnchorProjectile)
            yield break;
        var withRope = shot.GetComponent<RopeAnchorWithRope>();
        if (withRope == null)
        {
            ropeMissing = "missing: the anchor has no RopeAnchorWithRope";
            yield break;
        }
        bool Tied() =>
            withRope.rope != null && withRope.rope.attachmenState == Rope.ATTACHMENT.anchored;
        yield return WaitFor(() => withRope == null || Tied(), 2f);
        if (withRope != null && withRope.ropeInstance != null)
            spawned.Add(withRope.ropeInstance);
        ropeMissing =
            withRope == null ? "missing: the anchor was destroyed"
            : withRope.ropeInstance == null ? "missing: none spawned on the anchor within 2 s"
            : !Tied()
                ? $"spawned but {(withRope.rope != null ? withRope.rope.attachmenState.ToString() : "with no Rope")}, not anchored"
            : null;
    }

    /// <summary>How the step gives the held item's use.</summary>
    enum UseWay
    {
        /// <summary>Flat play's use button (<see cref="SelfTestInputPatch.HoldPrimary"/>, the player's input there).</summary>
        Button,

        /// <summary>VR: the holding hand's trigger through the scripted controller, so interact.physical's use path
        /// and its gates run (PhysicalInteraction.Inject).</summary>
        Trigger,

        /// <summary>VR, a mouth item with <c>Interaction.EatAtMouth</c> on: the holding hand raised to the mouth, as
        /// vr.12 eats (interact.physical's mouth route).</summary>
        Mouth,

        /// <summary>VR, an item whose bare trigger interact.items refuses (commune or read,
        /// <c>ItemGestures.TriggerRefused</c>): held at its face spot (<see cref="FaceSpot"/>) for the cast.</summary>
        Face,
    }

    static UseWay WayOf(Item held) =>
        !(VRSession.Active && VRRig.Instance != null) ? UseWay.Button
        : ItemGestures.TriggerRefused(held) ? UseWay.Face
        : InteractConfig.Instance.EatAtMouth && ItemGestures.AtMouth(held) ? UseWay.Mouth
        : UseWay.Trigger;

    static string UseWayText(UseWay way) =>
        way switch
        {
            UseWay.Trigger => "the holding hand's trigger",
            UseWay.Mouth => "the hand at the mouth",
            UseWay.Face => "the item held at the face",
            _ => "the use button",
        };

    /// <summary>VR: scripted hands, the holding hand closed on the item in front of the chest, and a moment for the
    /// grip and trigger edges to see it.</summary>
    IEnumerator ReadyHand(bool left)
    {
        BeginHands();
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
    }

    /// <summary>One frame of giving the held item's use <paramref name="way"/>.</summary>
    void HoldUse(UseWay way, bool left)
    {
        if (way == UseWay.Button)
            SelfTestInputPatch.HoldPrimary = true;
        else if (way == UseWay.Trigger)
        {
            Script(left).TriggerTouch = true;
            Script(left).Trigger = 1f;
        }
        else if (way == UseWay.Mouth)
            ApproachHand(left, MouthGoal());
        else
        {
            var rig = VRRig.Instance;
            ApproachHand(
                left,
                rig.Origin.InverseTransformPoint(
                    FaceSpot(rig.Head, ItemGestures.Use == UseGesture.Read)
                )
            );
        }
    }

    /// <summary>Stops giving the use: button up, trigger released, or the hand back from the mouth or face.</summary>
    void ReleaseUse(UseWay way, bool left)
    {
        SelfTestInputPatch.HoldPrimary = false;
        if (way == UseWay.Trigger)
            Script(left).Trigger = 0f;
        else if (way != UseWay.Button)
            PullAway(left);
    }

    /// <summary>A lantern's fuel entry (<c>DataEntryKey.Fuel</c>, Lantern.cs:171); -1 when it has none.</summary>
    static float Fuel(Item item) =>
        item != null
        && item.data != null
        && item.data.TryGetDataEntry<FloatItemData>(DataEntryKey.Fuel, out var entry)
            ? entry.Value
            : -1f;

    /// <summary>Flat play aims with the look; a use step leaves it level again.</summary>
    static void ResetFlatLook(Character c, bool vr)
    {
        if (!vr)
            c.data.lookValues.y = 0f;
    }

    // ---------------------------------------------------------------- aiming the held item

    /// <summary>Pitches below level (negative: above) and yaws off the head's forward that the aim search tries.</summary>
    static readonly float[] GroundPitches = { 45f, 60f, 30f, 75f };
    static readonly float[] SkyPitches = { -90f, -60f, -45f };

    /// <summary>A launcher shoots far: shallow first, inside the vine's elevation window (VineShooter.cs:82-90).</summary>
    static readonly float[] ShooterPitches = { 15f, 25f, 35f, 45f };

    /// <summary>A build goes on the ground 1.5-2.5 m ahead of a hand at chest height.</summary>
    static readonly float[] BuildPitches = { 30f, 40f, 20f, 50f };

    /// <summary>Level at the test wall straight ahead.</summary>
    static readonly float[] WallPitches = { 0f, 10f, -10f };
    static readonly float[] AllYaws = { 0f, 90f, -90f, 180f };
    static readonly float[] SkyYaws = { 0f, 90f, -90f };
    static readonly float[] AheadYaw = { 0f };

    /// <summary>Set by <see cref="AimHeld"/>: an aim met its condition.</summary>
    bool aimFound;

    /// <summary>Turns the held item's aim (the holding hand in VR, which interact.aim gives the game camera; the look
    /// in flat play) through <paramref name="yaws"/> by <paramref name="pitches"/> until <paramref name="ok"/> holds
    /// 0.15 s after the turn. Sets <see cref="aimFound"/>.</summary>
    IEnumerator AimHeld(Character c, bool left, float[] pitches, float[] yaws, Func<bool> ok)
    {
        aimFound = false;
        bool vr = VRSession.Active && VRRig.Instance != null;
        HeadFrame(out _, out var forward, out _);
        float lookYaw = c.data.lookValues.x;
        foreach (float yaw in yaws)
        {
            foreach (float down in pitches)
            {
                // Pitched below level (negative: above) from forward turned by the yaw.
                var heading = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
                var axis = Vector3.Cross(Vector3.up, heading);
                var dir = Quaternion.AngleAxis(down, axis) * heading;
                if (vr)
                    Script(left).Rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, axis));
                else
                    c.data.lookValues = new Vector2(lookYaw + yaw, -down);
                yield return Wait(0.15f);
                if (ok())
                {
                    aimFound = true;
                    yield break;
                }
            }
        }
    }

    /// <summary>The held item's aim meets no terrain within 60 m (farther than any launcher or build reaches).</summary>
    static bool OpenSky(bool vr) =>
        SpoolRay(vr, out var origin, out var dir)
        && !Physics.Raycast(
            origin,
            dir,
            60f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        );

    /// <summary>The ray the held item's game code casts: the camera, which interact.aim moves to the held hand.</summary>
    static bool SpoolRay(bool vr, out Vector3 origin, out Vector3 dir)
    {
        if (vr && HandAim.TryHeld(out origin, out var rotation))
        {
            dir = rotation * Vector3.forward;
            return true;
        }
        var cam = MainCamera.instance;
        origin = cam != null ? cam.transform.position : default;
        dir = cam != null ? cam.transform.forward : default;
        return cam != null;
    }

    // ---------------------------------------------------------------- rope spool

    /// <summary>
    /// use.RopeSpool. In VR first the spool's own mechanic (<see cref="SpoolPayout"/>): the wrist-twist knob pays
    /// rope out and gives an end stop at the minimum. Flat play skips it: the self-test input patch has no scroll
    /// field. Then the tie-off: a spool ties only where its aim (the holding hand in VR, interact.aim; the camera in
    /// flat play) meets terrain within <c>maxAnchorGhostDistance</c> lying within <c>maxAnchorDistance</c> of the
    /// body, after use is held there for <c>castTime</c> (RopeTier.cs:97-128). In VR the spool is first aimed at open
    /// sky, where the trigger press must get interact.physical's NoWall refusal cue; then at ground in reach, where
    /// holding use must tie the rope (<c>Rope.AttachToAnchor_Rpc</c>, Rope.cs:259-269).
    /// </summary>
    IEnumerator SpoolUse(Item held, RopeTier tier)
    {
        var c = Character.localCharacter;
        var spool = held.GetComponent<RopeSpool>();
        bool vr = VRSession.Active && VRRig.Instance != null;
        bool left = LocalHold.ItemInLeftHand;
        string refusal = vr ? "refusal cue off (UI.ActionHints)" : "refusal not checked (flat)";
        string payout = "payout not checked (flat: the self-test input patch has no scroll field)";
        if (vr)
        {
            BeginHands();
            HeadFrame(out var head, out var forward, out _);
            SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
            yield return SpoolPayout(spool, left);
            payout = spoolNote;
            if (Affordance.Enabled)
            {
                yield return AimSpool(c, tier, left, valid: false);
                if (!aimFound)
                    refusal = "no aim free of terrain for the refusal";
                else
                {
                    yield return PressAndExpect(left, DenyReason.NoWall);
                    refusal = "aimed at nothing: " + stepNote;
                    stepNote = null;
                }
            }
        }
        yield return AimSpool(c, tier, left, valid: true);
        if (!aimFound)
        {
            Fail(
                $"no terrain within {tier.maxAnchorDistance:0.0} m of the body to aim the spool at; {payout}; {refusal}"
            );
            yield break;
        }
        var rope = spool.rope;
        var anchors = Ids<RopeAnchor>();
        float quietFrom = CueMark();
        if (vr)
        {
            Script(left).TriggerTouch = true;
            Script(left).Trigger = 1f;
        }
        else
            SelfTestInputPatch.HoldPrimary = true;
        float progress = 0f;
        float end = Now + GameTiming.Budget(tier.castTime);
        bool Tied() => rope != null && rope.attachmenState == Rope.ATTACHMENT.anchored;
        while (Now < end && !Tied() && held != null)
        {
            progress = Mathf.Max(progress, held.progress);
            yield return null;
        }
        bool tied = Tied();
        // A tie that works gets no refusal cue on the spool's hand.
        if (vr && tied)
            yield return ExpectNoCue(left, quietFrom, 0f);
        yield return Shot("use-RopeSpool");
        if (vr)
        {
            Script(left).Trigger = 0f;
            Script(left).TriggerTouch = false;
        }
        else
        {
            SelfTestInputPatch.HoldPrimary = false;
            c.data.lookValues.y = 0f;
        }
        if (rope != null)
            spawned.Add(rope.gameObject);
        var anchor = Fresh<RopeAnchor>(anchors);
        if (anchor != null)
            spawned.Add(anchor.gameObject);
        yield return Wait(0.3f);
        if (vr)
            EndHands();
        var note =
            $"tied {tied}, tie progress {progress:0.00} of {tier.castTime:0.0} s, anchor {(anchor != null ? Vector3.Distance(anchor.transform.position, c.Center).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m from the body" : "none")}; {payout}; {refusal}";
        if (!tied)
            Fail("the rope was not tied: " + note);
        else
            Note(note);
    }

    /// <summary>What <see cref="SpoolPayout"/> measured, for the spool step's note.</summary>
    string spoolNote;

    /// <summary>
    /// The spool's knob in VR: the holding hand's twist about its forward (interact.tools, one scroll step per
    /// <see cref="ToolsConfig.TwistStepDegrees"/>) holds the game's scroll buttons (RopeSpool.cs:113-120). Twisted
    /// inward with the rope at <c>minSegments</c> (RopeSpool.cs:9, clamped at :140) it must give one end-stop thunk
    /// (<see cref="Affordance.EndStop"/>); twisted outward it must pay rope out (<c>Segments</c>, RopeSpool.cs:58,
    /// grown at :139). Twisted faster than the knob's hold rate (<c>TwistKnob.HoldRate</c>, 90 deg/s) so the scroll
    /// button stays held. Sets <see cref="spoolNote"/>.
    /// </summary>
    IEnumerator SpoolPayout(RopeSpool spool, bool left)
    {
        // The rope spawns on the spool's first held Update (RopeSpool.cs:102-111) and is clamped to minSegments on
        // the next FixedUpdate (RopeSpool.cs:140).
        yield return WaitFor(() => spool.rope != null, 1f);
        yield return Wait(0.2f);
        float rate = Mathf.Max(180f, ToolsConfig.Instance.TwistStepDegrees * 6f);
        float start = spool.Segments;
        // Out first: the twist then turns round and runs the rope back into the minimum, a fresh push into the
        // limit. (A push already held against it when the hands were scripted, or the knob's own 0.3 s repeat
        // window, would rightly stay quiet.)
        yield return TwistHand(left, rate, () => spool.Segments > spool.minSegments + 1f, 1.5f);
        float paid = spool.Segments;
        // Let the knob's twist rate fall back to rest before turning the other way.
        yield return Wait(0.4f);
        string stop = "end stop not checked (ui.affordance off)";
        if (Affordance.Enabled && paid > spool.minSegments + 0.5f)
        {
            float mark = CueMark();
            yield return TwistHand(
                left,
                -rate,
                () => CueLog.Capture.Felt(left, CueFeel.EndStop, mark, out _),
                2f
            );
            yield return ExpectFeel(left, CueFeel.EndStop, mark, 0f);
            stop = CueLog.Capture.Felt(left, CueFeel.EndStop, mark, out _)
                ? $"wound back to {spool.Segments:0.0}: end stop at the minimum"
                : $"wound back to {spool.Segments:0.0}: no end stop at the minimum";
        }
        spoolNote =
            $"twist payout {start:0.0} -> {paid:0.0} segments (min {spool.minSegments:0.0}); {stop}";
        if (paid <= spool.minSegments + 0.5f)
            Fail("twisting the spool paid out no rope: " + spoolNote);
    }

    /// <summary>Turns the scripted hand about its own forward at <paramref name="degreesPerSecond"/> until
    /// <paramref name="done"/> or <paramref name="seconds"/> pass. The knobs read that twist
    /// (<c>TwistKnob.TwistAngle</c> about the controller's forward): positive pays rope out and zooms out.</summary>
    IEnumerator TwistHand(bool left, float degreesPerSecond, Func<bool> done, float seconds)
    {
        float end = Now + seconds;
        while (Now < end && !done())
        {
            Script(left).Rotation *= Quaternion.AngleAxis(
                degreesPerSecond * Time.unscaledDeltaTime,
                Vector3.forward
            );
            yield return null;
        }
    }

    /// <summary>Turns the spool's aim until the game's own ray (RopeTier.cs:99-111) would take the anchor
    /// (<paramref name="valid"/>), or until it meets no terrain within 60 m. Sets <see cref="aimFound"/>.</summary>
    IEnumerator AimSpool(Character c, RopeTier tier, bool left, bool valid)
    {
        bool vr = VRSession.Active && VRRig.Instance != null;
        if (!valid)
            return AimHeld(c, left, SkyPitches, SkyYaws, () => OpenSky(vr));
        return AimHeld(
            c,
            left,
            GroundPitches,
            AllYaws,
            () =>
                SpoolRay(vr, out var origin, out var aim)
                && Physics.Raycast(
                    origin,
                    aim,
                    out var at,
                    tier.maxAnchorGhostDistance,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                && Vector3.Distance(at.point, c.Center) < tier.maxAnchorDistance - 0.3f
        );
    }

    // ---------------------------------------------------------------- use routes (interact.items)

    /// <summary>One item per use route, by name (the first one found), and the route it must get.</summary>
    static readonly (string[] Names, UseGesture Route)[] Routes =
    {
        (new[] { "Granola", AppleFragment }, UseGesture.Eat),
        (new[] { "Antidote" }, UseGesture.Drink),
        (new[] { "Bandage" }, UseGesture.Apply),
        (new[] { "Heat Pack" }, UseGesture.Apply),
        (new[] { "Book of Bones" }, UseGesture.Read),
        (new[] { "Cursed Skull" }, UseGesture.Commune),
        (new[] { "Checkpoint Flag" }, UseGesture.Build),
        (new[] { "RopeShooter" }, UseGesture.Aim),
    };

    /// <summary>A cast shorter than this cannot be stopped part way with margin: the cancel steps skip it rather
    /// than risk finishing a lethal or skeleton cast.</summary>
    const float MinCancelSeconds = 0.5f;

    /// <summary>vr.160: each route's item, given to the hand, gets that route from <see cref="ItemGestures.UseOf"/>,
    /// which reads the game's own data (<c>UIData.hasMainInteract</c>/<c>mainInteractPrompt</c>, Item.cs:39-75;
    /// <c>ItemUseFeedback.useAnimation</c>, ItemUseFeedback.cs:5; the item's actions and components). Items missing
    /// from this game version are listed, not failed.</summary>
    IEnumerator UseRoutesStep()
    {
        var all = new List<Item>(SandboxActions.Items);
        var right = new List<string>();
        var wrong = new List<string>();
        var missing = new List<string>();
        foreach (var (names, route) in Routes)
        {
            Item prefab = null;
            foreach (var fragment in names)
                prefab ??= FindItem(all, fragment);
            if (prefab == null)
            {
                missing.Add(names[0]);
                continue;
            }
            yield return GiveAndHold(prefab);
            if (!stepOk)
                yield break;
            var held = HeldItem();
            var got = ItemGestures.UseOf(held);
            if (got == route)
                right.Add($"{held.name} {got}");
            else
                wrong.Add($"{held.name} {got} (expected {route})");
            yield return DropHeld();
            if (!stepOk)
                yield break;
        }
        var note =
            $"right [{string.Join(", ", right)}], wrong [{string.Join(", ", wrong)}], not in the game [{string.Join(", ", missing)}]";
        if (wrong.Count > 0)
            Fail("wrong use route: " + note);
        else if (right.Count == 0)
            stepSkip = "none of the route items exist: " + note;
        else
            stepNote = note;
    }

    /// <summary>The mouth point interact.physical routes eating to, in tracking space (<see cref="MouthGesture"/>).</summary>
    static Vector3 MouthGoal() =>
        MouthGesture
            .MouthPoint(
                VRInput.HeadPosition.N(),
                VRInput.HeadRotation.N(),
                MouthGesture.DefaultOffset
            )
            .U();

    /// <summary>One frame of the scripted hand's 1.5 m/s approach to a tracking-space point.</summary>
    void ApproachHand(bool left, Vector3 tracking) =>
        SetHand(
            left,
            HandScript
                .Approach(Script(left).Position.N(), tracking.N(), 1.5f, Time.unscaledDeltaTime)
                .U()
        );

    /// <summary>The hand straight back to its rest beside the body, in one frame.</summary>
    void PullAway(bool left)
    {
        HeadFrame(out var head, out var forward, out var right);
        SetHand(left, Rest(head, forward, left ? -right : right).Position);
    }

    /// <summary>interact.items' face spots in world space (HeldGestures: a book 0.35 m ahead of the eyes and 0.12 m
    /// below, a commune item 0.12 m ahead).</summary>
    static Vector3 FaceSpot(Transform head, bool read) =>
        read
            ? ReadZone.Spot(head.position.N(), head.forward.N()).U()
            : head.position + head.forward * 0.12f;

    /// <summary>Highest cast progress <see cref="FaceCastUntil"/> saw.</summary>
    float faceProgress;

    /// <summary><see cref="HeldGestures.AtFace"/> was on during <see cref="FaceCastUntil"/>.</summary>
    bool faceSeen;

    /// <summary>The danger warning (<see cref="CueFeel.Danger"/>) came as the face hold started.</summary>
    bool faceDanger;

    /// <summary>Carries the held item to its face spot and holds it there until the cast (<c>Item.castProgress</c>,
    /// Item.cs:344) passes <paramref name="stopAt"/>, then pulls the hand away in that same frame, long before the
    /// cast could finish (a finished Cursed Skull kills, Action_Die.cs:7; a finished Book of Bones makes a skeleton,
    /// Action_BecomeSkeleton.cs:7). Sets <see cref="faceProgress"/> and <see cref="faceSeen"/>.</summary>
    IEnumerator FaceCastUntil(Item item, bool left, bool read, float stopAt)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        faceProgress = 0f;
        faceSeen = false;
        faceDanger = false;
        float dangerMark = CueMark();
        float end = Now + GameTiming.Budget(GameTiming.UseSeconds(item) * stopAt, 2f);
        while (
            Now < end && item != null && c.data.currentItem == item && item.castProgress < stopAt
        )
        {
            ApproachHand(left, rig.Origin.InverseTransformPoint(FaceSpot(rig.Head, read)));
            faceSeen |= HeldGestures.AtFace;
            faceProgress = Mathf.Max(faceProgress, item.castProgress);
            yield return null;
        }
        if (item != null)
            faceProgress = Mathf.Max(faceProgress, item.castProgress);
        faceDanger = CueLog.Capture.Felt(left, CueFeel.Danger, dangerMark, out _);
        PullAway(left);
    }

    /// <summary>Skips a face step when a feature it needs is off. True when it can run.</summary>
    bool FaceReady(bool read)
    {
        if (!Affordance.Enabled)
            stepSkip = "ui.affordance not installed";
        else if (!ItemGestures.Installed)
            stepSkip = "interact.items not installed";
        else if (
            read
                ? !ItemGesturesConfig.Instance.ReadAtFace
                : !ItemGesturesConfig.Instance.CommuneAtFace
        )
            stepSkip = (read ? "ReadAtFace" : "CommuneAtFace") + " is off";
        return stepSkip == null;
    }

    /// <summary>vr.161: a drink raised to the mouth (interact.physical's mouth route for Drink) is drunk through its
    /// cast (<c>lastFinishedCast</c>, Item.cs:272): the status it cures (Poison for the Antidote,
    /// <c>Action_ModifyStatus</c>, Action_ModifyStatus.cs:21, set up with <c>AddStatus</c>,
    /// CharacterAfflictions.cs:712) falls, and no refusal cue fires.</summary>
    IEnumerator DrinkStep(Item prefab)
    {
        if (!InteractConfig.Instance.EatAtMouth)
        {
            stepSkip = "EatAtMouth is off";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        if (!StatusEffect(item, out var status, out float change) || change >= 0f)
        {
            Fail($"{item.name} lowers no status (Action_ModifyStatus), nothing to measure");
            yield break;
        }
        if (!ItemGestures.AtMouth(item))
        {
            Fail($"{item.name} routes to {ItemGestures.UseOf(item)}, not to the mouth");
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        var afflictions = c.refs.afflictions;
        SandboxActions.AddStatus(status, Mathf.Min(0.5f, -change));
        float before = afflictions.GetCurrentStatus(status);
        float finishedBefore = item.lastFinishedCast;
        float useTime = GameTiming.UseSeconds(item);
        float quietFrom = CueMark();
        float progress = 0f;
        bool drunk = false;
        // The item's own use time after up to 3 s bringing it to the mouth.
        float end = Now + GameTiming.Budget(useTime, 3f);
        while (Now < end)
        {
            if (
                item == null
                || c.data.currentItem != item
                || item.lastFinishedCast > finishedBefore
            )
            {
                drunk = true;
                break;
            }
            ApproachHand(left, MouthGoal());
            progress = Mathf.Max(progress, item.castProgress);
            yield return null;
        }
        PullAway(left);
        yield return ExpectNoCue(left, quietFrom, 0f);
        yield return Wait(0.5f);
        float after = afflictions.GetCurrentStatus(status);
        var note =
            $"{prefab.name}: drunk {drunk}, cast progress {progress:0.00} of {useTime:0.0} s, {status} {before:0.00} -> {after:0.00} (item changes it by {change:+0.00;-0.00})";
        if (!drunk)
            Fail("not drunk at the mouth: " + note);
        // Half the amount: statuses creep between the reads, and the start value caps a large change.
        else if (before - after < Mathf.Min(-change, before) * 0.5f)
            Fail($"drunk but {status} did not fall: " + note);
        else
            Note(note);
    }

    /// <summary>vr.162: a build item (Checkpoint Flag) held at the mouth for longer than its use time starts no cast
    /// (<c>Item.isUsingPrimary</c>, Item.cs:301; <c>castProgress</c>, Item.cs:344) and stays in hand: builds go to
    /// the hand-aimed preview, never the mouth.</summary>
    IEnumerator NotAtMouthStep(Item prefab)
    {
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        var route = ItemGestures.UseOf(item);
        yield return GripTo(left, 1f, 0.1f);
        // Bringing it up, the mouth dwell, then a whole cast and more.
        float hold = GameTiming.UseSeconds(item) + 1f;
        bool used = false;
        float progress = 0f;
        float end = Now + hold;
        while (Now < end && item != null && c.data.currentItem == item)
        {
            ApproachHand(left, MouthGoal());
            used |= item.isUsingPrimary;
            progress = Mathf.Max(progress, item.castProgress);
            yield return null;
        }
        bool kept = item != null && c.data.currentItem == item;
        PullAway(left);
        var note =
            $"{prefab.name} ({route}) at the mouth {hold:0.0} s: use started {used}, cast progress {progress:0.00}, still held {kept}";
        if (used || progress > 0f)
            Fail("a build started at the mouth: " + note);
        else if (!kept)
            Fail("left the hand at the mouth: " + note);
        else
            Note(note);
    }

    /// <summary>vr.163: a Checkpoint Flag aimed with the holding hand at open sky is refused on the trigger
    /// (OutOfReach, its red preview on the item: <c>Constructable.CurrentHitIsValid</c>, Constructable.cs:137-148,
    /// measured along the hand aim); aimed at ground in reach, where the game's preview check passes
    /// (<c>overrideUsability</c>, Constructable.cs:62-69), holding the trigger through its use time builds a flag
    /// (<c>FinishConstruction</c>, Constructable.cs:158-191; <c>CheckpointFlag.Initialize</c>,
    /// CheckpointConstructable.cs:7-10) and the item leaves the hand (Constructable.cs:185-190).</summary>
    IEnumerator BuildStep(Item prefab)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        var build = item.GetComponent<Constructable>();
        if (build == null)
        {
            Fail(item.name + " has no Constructable");
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
        yield return AimHeld(c, left, SkyPitches, SkyYaws, () => OpenSky(true));
        if (!aimFound)
        {
            Fail("no aim free of terrain for the refusal");
            yield break;
        }
        yield return PressAndExpect(left, DenyReason.OutOfReach);
        string refusal = "aimed at the sky: " + stepNote;
        stepNote = null;
        yield return AimHeld(
            c,
            left,
            BuildPitches,
            AllYaws,
            () => item != null && item.CanUsePrimary() && build.currentPreview != null
        );
        if (!aimFound)
        {
            Fail(
                $"no ground within {build.maxConstructDistance:0.0} m that the build would take; {refusal}"
            );
            yield break;
        }
        var flags = Ids<CheckpointFlag>();
        float quietFrom = CueMark();
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        float useTime = GameTiming.UseSeconds(item);
        float progress = 0f;
        CheckpointFlag flag = null;
        float end = Now + GameTiming.Budget(useTime, 1f);
        while (Now < end && (flag = Fresh<CheckpointFlag>(flags)) == null)
        {
            if (item != null)
                progress = Mathf.Max(progress, item.castProgress);
            yield return null;
        }
        if (flag != null)
            spawned.Add(flag.gameObject);
        yield return ExpectNoCue(left, quietFrom, 0f);
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        yield return Wait(0.3f);
        bool gone = item == null || HeldItem() != item;
        var note =
            $"{prefab.name}: build progress {progress:0.00} of {useTime:0.0} s, flag {(flag != null ? Vector3.Distance(flag.transform.position, c.Center).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m from the body" : "none")}, item left the hand {gone}; {refusal}";
        if (flag == null)
            Fail("no checkpoint flag built: " + note);
        else if (!gone)
            Fail("built but the item stayed in hand: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.164: a Cursed Skull (Commune: <c>Action_Die</c>, Action_Die.cs:7) refuses the bare trigger with the danger
    /// cue and starts no cast (<c>Item.isUsingPrimary</c>, Item.cs:301); held eye to eye its cast runs
    /// (<c>castProgress</c>, Item.cs:344) with the progress feel and <see cref="HeldGestures.AtFace"/>; pulled away
    /// at 0.4 it is called off (<c>CancelUsePrimary</c> on release, CharacterItems.cs:209-211, Item.cs:918) with the
    /// cancel feel, the player alive (<c>CharacterData.dead</c>, CharacterData.cs:336) and the skull still held.
    /// The cast is never let near 1.
    /// </summary>
    IEnumerator CommuneGuardStep(Item prefab)
    {
        if (!FaceReady(read: false))
            yield break;
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        float useTime = GameTiming.UseSeconds(item);
        var route = ItemGestures.UseOf(item);
        if (route != UseGesture.Commune)
        {
            Fail($"{item.name} routes to {route}, not Commune");
            yield break;
        }
        if (useTime < MinCancelSeconds)
        {
            stepSkip = $"{item.name}'s cast ({useTime:0.00} s) is too short to stop safely";
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);

        // The bare trigger: refused at the hand, and released at once should a cast start anyway.
        Affordance.ResetGate();
        float mark = CueMark();
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        bool Casting() => item.isUsingPrimary || item.castProgress > 0f;
        float until = Now + 1f;
        while (
            Now < until
            && !Casting()
            && !CueLog.Capture.Fired(left, DenyReason.DangerSoon, mark, out _)
        )
            yield return null;
        // A moment longer: the refused press must still start nothing.
        until = Now + 0.3f;
        while (Now < until && !Casting())
            yield return null;
        bool castOnTrigger = Casting();
        Script(left).Trigger = 0f;
        yield return ExpectCue(left, DenyReason.DangerSoon, mark, tint: true, within: 0f);
        string triggerCue = cueNote ?? "no cue";
        yield return Wait(0.3f);
        if (castOnTrigger)
        {
            Fail($"the bare trigger started {item.name}'s cast; trigger cue: {triggerCue}");
            yield break;
        }

        // Eye to eye: the cast runs; pulled away at 0.4 it is called off.
        float faceMark = CueMark();
        yield return FaceCastUntil(item, left, read: false, 0.4f);
        float cancelMark = CueMark();
        yield return ExpectFeel(left, CueFeel.Cancel, cancelMark, 1f);
        yield return ExpectFeel(left, CueFeel.Progress, faceMark, 0f);
        yield return Wait(0.3f);
        bool kept = item != null && c.data.currentItem == item;
        var note =
            $"{prefab.name}: trigger refused ({triggerCue}); eye to eye AtFace {faceSeen}, cast {faceProgress:0.00} of {useTime:0.0} s, pulled away: alive {!c.data.dead}, still held {kept}";
        if (faceProgress < 0.4f)
            Fail("the cast did not run eye to eye: " + note);
        else if (Affordance.Enabled && !faceDanger)
            Fail("no danger warning as the face hold started: " + note);
        else if (!faceSeen)
            Fail("HeldGestures.AtFace never set: " + note);
        else if (c.data.dead)
            Fail("died: " + note);
        else if (!kept)
            Fail("the skull left the hand: " + note);
        else
            Note(note);
    }

    /// <summary>vr.165: a Book of Bones (<c>ItemTags.BookOfBones</c>, Item.cs:31) held open in front of the eyes
    /// runs its cast (<c>castProgress</c>, Item.cs:344) with <see cref="HeldGestures.AtFace"/>; lowered at 0.3 it is
    /// called off (<c>CancelUsePrimary</c>, Item.cs:918) with the cancel feel, the player not a skeleton
    /// (<c>CharacterData.isSkeleton</c>, CharacterData.cs:308, set by Action_BecomeSkeleton.cs:7) and the book
    /// still held.</summary>
    IEnumerator ReadCancelStep(Item prefab)
    {
        if (!FaceReady(read: true))
            yield break;
        var c = Character.localCharacter;
        if (c.data.isSkeleton)
        {
            stepSkip = "the player is already a skeleton";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        float useTime = GameTiming.UseSeconds(item);
        if (useTime < MinCancelSeconds)
        {
            stepSkip = $"{item.name}'s cast ({useTime:0.00} s) is too short to stop safely";
            yield break;
        }
        bool canUse = item.CanUsePrimary();
        yield return GripTo(left, 1f, 0.1f);
        yield return FaceCastUntil(item, left, read: true, 0.3f);
        float cancelMark = CueMark();
        yield return ExpectFeel(left, CueFeel.Cancel, cancelMark, 1f);
        yield return Wait(0.3f);
        bool kept = item != null && c.data.currentItem == item;
        var note =
            $"{prefab.name} ({ItemGestures.UseOf(item)}, usable {canUse}): AtFace {faceSeen}, cast {faceProgress:0.00} of {useTime:0.0} s, lowered: skeleton {c.data.isSkeleton}, still held {kept}";
        if (faceProgress < 0.3f)
            Fail("the cast did not run in front of the eyes: " + note);
        else if (Affordance.Enabled && !faceDanger)
            Fail("no danger warning as the face hold started: " + note);
        else if (!faceSeen)
            Fail("HeldGestures.AtFace never set: " + note);
        else if (c.data.isSkeleton)
            Fail("became a skeleton: " + note);
        else if (!kept)
            Fail("the book left the hand: " + note);
        else
            Note(note);
    }

    /// <summary>vr.166: food at the mouth runs its cast (<c>castProgress</c>, Item.cs:344); pulled away at half it is
    /// called off (<c>CancelUsePrimary</c>, Item.cs:918) with the cancel feel: not eaten
    /// (<c>CharacterData.lastConsumedItem</c>, CharacterData.cs:170), still held, Hunger not lowered.</summary>
    IEnumerator EatCancelStep(Item prefab)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        float useTime = GameTiming.UseSeconds(item);
        if (useTime < MinCancelSeconds)
        {
            stepSkip = $"{item.name}'s cast ({useTime:0.00} s) is too short to stop part way";
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        var afflictions = c.refs.afflictions;
        SandboxActions.AddStatus(STATUS.Hunger, 0.3f);
        float before = afflictions.GetCurrentStatus(STATUS.Hunger);
        float consumedBefore = c.data.lastConsumedItem;
        float progress = 0f;
        float end = Now + GameTiming.Budget(useTime * 0.5f, 3f);
        while (Now < end && item != null && c.data.currentItem == item && item.castProgress < 0.5f)
        {
            ApproachHand(left, MouthGoal());
            progress = Mathf.Max(progress, item.castProgress);
            yield return null;
        }
        if (item != null)
            progress = Mathf.Max(progress, item.castProgress);
        PullAway(left);
        float cancelMark = CueMark();
        yield return ExpectFeel(left, CueFeel.Cancel, cancelMark, 1f);
        yield return Wait(0.5f);
        float after = afflictions.GetCurrentStatus(STATUS.Hunger);
        bool eaten = c.data.lastConsumedItem != consumedBefore;
        bool kept = item != null && c.data.currentItem == item;
        var note =
            $"{prefab.name}: cast {progress:0.00} of {useTime:0.0} s at the mouth, pulled away: eaten {eaten}, still held {kept}, Hunger {before:0.00} -> {after:0.00}";
        if (progress < 0.5f)
            Fail("the cast did not reach half at the mouth: " + note);
        else if (eaten)
            Fail("eaten although pulled away: " + note);
        else if (!kept)
            Fail("left the hand: " + note);
        else if (after < before - 0.02f)
            Fail("Hunger fell although not eaten: " + note);
        else
            Note(note);
    }

    /// <summary>vr.167: food that must be fed to a friend (<c>canUseOnFriend</c> and <c>mustUseOnFriend</c>,
    /// Item.cs:260-262) held with nobody in front (<c>Interaction.hasValidTargetCharacter</c>, Interaction.cs:71):
    /// the off-hand trigger (the game's secondary use, refused by <c>Item.CanUseSecondary</c>, Item.cs:819-845)
    /// gets the NeedsFriend cue on the off hand with the item red, and starts no cast.</summary>
    IEnumerator SecondaryDenyStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        var prefab =
            FirstItem(i =>
                i.canUseOnFriend
                && i.mustUseOnFriend
                && i.GetComponent<Action_RestoreHunger>() != null
            ) ?? FirstItem(i => i.canUseOnFriend && i.mustUseOnFriend);
        if (prefab == null)
        {
            stepSkip = "no item that must be used on a friend";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        bool off = !left;
        yield return GripTo(left, 1f, 0.1f);
        Affordance.ResetGate();
        float mark = CueMark();
        Script(off).TriggerTouch = true;
        Script(off).Trigger = 1f;
        yield return ExpectCue(off, DenyReason.NeedsFriend, mark, tint: true);
        // A moment longer with the trigger held: the refused press must still start nothing.
        bool cast = false;
        float until = Now + 0.3f;
        do
        {
            cast |=
                item != null
                && (item.isUsingSecondary || item.isUsingPrimary || item.castProgress > 0f);
            yield return null;
        } while (Now < until && !cast);
        Script(off).Trigger = 0f;
        yield return Wait(0.3f);
        var note =
            $"{prefab.name}: off-hand trigger with nobody in front, cast started {cast}; {cueNote ?? "no cue"}";
        if (cast)
            Fail("the refused secondary started a cast: " + note);
        else
            Note(note);
    }

    /// <summary>vr.168: the rope cannon aimed with the holding hand at open sky is refused on the trigger (NoWall:
    /// <c>RopeShooter.WillAttach</c>, RopeShooter.cs:122-138, along the hand aim); aimed at terrain (or the test
    /// wall) within <c>maxLength</c> (RopeShooter.cs:29), holding the trigger through its use time fires a rope
    /// anchor (<c>OnPrimaryFinishedCast</c>, RopeShooter.cs:72-120) with no refusal cue.</summary>
    IEnumerator ShootHandStep(Item prefab)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        var shooter = item.GetComponent<RopeShooter>();
        if (shooter == null)
        {
            Fail(item.name + " has no RopeShooter");
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
        yield return AimHeld(c, left, SkyPitches, SkyYaws, () => OpenSky(true));
        if (!aimFound)
        {
            Fail("no aim free of terrain for the refusal");
            yield break;
        }
        yield return PressAndExpect(left, DenyReason.NoWall);
        string refusal = "aimed at the sky: " + stepNote;
        stepNote = null;
        yield return AimHeld(c, left, ShooterPitches, AllYaws, () => item.CanUsePrimary());
        string target = "terrain";
        if (!aimFound)
        {
            // No terrain in reach: the sandbox's test wall ahead (SandboxActions.PlaceWall).
            var wall = SandboxActions.PlaceWall();
            if (wall != null)
                spawned.Add(wall);
            target = "the test wall";
            yield return AimHeld(c, left, WallPitches, AheadYaw, () => item.CanUsePrimary());
        }
        if (!aimFound)
        {
            Fail(
                $"no terrain or test wall within {shooter.maxLength:0} m that the cannon would attach to (grounded {c.data.isGrounded}); {refusal}"
            );
            yield break;
        }
        var anchors = Ids<RopeAnchorProjectile>();
        float quietFrom = CueMark();
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        float useTime = GameTiming.UseSeconds(item);
        RopeAnchorProjectile anchor = null;
        float end = Now + GameTiming.Budget(useTime, 1f);
        while (Now < end && (anchor = Fresh<RopeAnchorProjectile>(anchors)) == null)
            yield return null;
        yield return ExpectNoCue(left, quietFrom, 0f);
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        if (anchor != null)
            spawned.Add(anchor.gameObject);
        yield return KeepRope(anchor);
        var note =
            $"{prefab.name} at {target}: anchor {(anchor != null ? Vector3.Distance(anchor.transform.position, c.Center).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m from the body" : "none")} within {GameTiming.Budget(useTime, 1f):0.0} s; {refusal}";
        if (anchor == null)
            Fail("no rope anchor fired: " + note);
        else
            Note(note);
    }

    /// <summary>vr.169: binoculars raised in Hold mode (the trigger held, <c>binocularsActive</c>,
    /// Action_ShowBinocularOverlay.cs:7, :23-25) and the holding hand twisted outward: the zoom runs to
    /// <c>maxFov</c> (<c>AdjustFOV</c> clamps <c>lerpedFOV</c>, CameraOverride_Binoculars.cs:27-31) and twisting on
    /// past it gives one end-stop thunk.</summary>
    IEnumerator ZoomEndStopStep(Item prefab)
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        if (!BinocularsConfig.TwistZoom)
        {
            stepSkip = "TwistZoom is off";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        var binoculars = item.GetComponentInChildren<Action_ShowBinocularOverlay>();
        var zoom =
            binoculars != null ? binoculars.cameraOverride as CameraOverride_Binoculars : null;
        if (zoom == null)
        {
            Fail(item.name + " has no binocular zoom (CameraOverride_Binoculars)");
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.25f + Vector3.down * 0.05f);
        BinocularsConfig.ForceHold = true;
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        yield return WaitFor(
            () => binoculars.binocularsActive,
            GameTiming.Budget(GameTiming.UseSeconds(item), 1f)
        );
        if (!binoculars.binocularsActive)
        {
            Script(left).Trigger = 0f;
            BinocularsConfig.ForceHold = false;
            Fail("binoculars did not raise with the trigger held (Hold mode)");
            yield break;
        }
        yield return Wait(0.3f);
        float fovBefore = zoom.lerpedFOV;
        float mark = CueMark();
        // 270 deg/s: past the knob's hold rate, and the whole range (8 steps of 30 deg) in about a second.
        yield return TwistHand(
            left,
            270f,
            () => CueLog.Capture.Felt(left, CueFeel.EndStop, mark, out _),
            4f
        );
        float fovAt = zoom.lerpedFOV;
        yield return ExpectFeel(left, CueFeel.EndStop, mark, 0f);
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        yield return Wait(0.3f);
        BinocularsConfig.ForceHold = false;
        var note =
            $"{prefab.name}: zoom {fovBefore:0.0} -> {fovAt:0.0} deg (range {zoom.minFov:0.0}-{zoom.maxFov:0.0}); {BinocularGiveNre}";
        if (fovAt < zoom.maxFov - 0.01f)
            Fail("the end stop came before the widest zoom: " + note);
        else
            Note(note);
    }

    /// <summary>vr.171: a vine or chain shooter aimed where the game's aim check passes (WillAttach,
    /// VineShooter.cs:71-92) but every path try fails (:49-69). The body turns round looking for terrain that refuses
    /// every hang itself (<see cref="NoVinePath"/>); only when none is in view are the tries forced to fail
    /// (<c>AimPatches.SelfTestNoPath</c>), a shortcut the note names. The vanilla cast ends with nothing built; VR gets
    /// NoPath with the shooter red, and no vine appears.</summary>
    IEnumerator VineNoPathStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        var prefab = FirstItem(i => i.GetComponent<VineShooter>() != null);
        if (prefab == null)
        {
            stepSkip = "no vine or chain shooter item";
            yield break;
        }
        var c = Character.localCharacter;
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        var shooter = item.GetComponent<VineShooter>();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => c.data.isGrounded, 3f);
        // A full turn for terrain that refuses every path; failing that, back to an aim WillAttach takes.
        bool natural = false;
        bool attachable = false;
        for (int i = 0; i < 24 && !natural; i++)
        {
            Turn(c, i == 0 ? 0f : 15f, 0f);
            yield return null;
            yield return null;
            if (shooter.WillAttach(out var hit))
            {
                attachable = true;
                natural = NoVinePath(shooter, hit.point);
            }
        }
        bool aimed = natural;
        for (int i = 0; i < 24 && attachable && !aimed; i++)
        {
            Turn(c, 15f, 0f);
            yield return null;
            yield return null;
            aimed = shooter.WillAttach(out _);
        }
        if (!aimed)
        {
            stepSkip =
                $"no terrain within the shooter's {shooter.maxLength:0} m and elevation window here";
            yield break;
        }
        var before = Ids<JungleVine>();
        AimPatches.SelfTestNoPath = !natural;
        Affordance.ResetGate();
        float mark = CueMark();
        // The holding hand's trigger, as a player fires it (the input patch would skip interact.physical's use path).
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        float end = Now + GameTiming.Budget(GameTiming.UseSeconds(item), 1f);
        while (Now < end && !CueLog.Capture.Fired(left, DenyReason.NoPath, mark, true, out _))
            yield return null;
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        AimPatches.SelfTestNoPath = false;
        yield return ExpectCue(left, DenyReason.NoPath, mark, tint: true, within: 0.5f);
        var cue = cueNote;
        var vine = Fresh<JungleVine>(before);
        if (vine != null)
            spawned.Add(vine.gameObject);
        if (!stepOk)
            yield break;
        string how = natural
            ? "terrain here refuses every hang"
            : "shortcut: every path try forced to fail (AimPatches.SelfTestNoPath); no aim in a full turn met terrain that refuses a path";
        if (vine != null)
            Fail($"a vine was built although every path try failed ({how})");
        else
            Note($"{prefab.name}, {how}: {cue}");
    }

    /// <summary>True when no hang the shot tries (0 to <c>maxDown</c> below the straight line, VineShooter.cs:47-50)
    /// clears the terrain from the vine start the shot takes (1 m ahead of the aim, 1.5 m over the ground there,
    /// :52-57) to <paramref name="to"/> (<c>JungleVine.CheckVinePath</c>, JungleVine.cs:234-248).</summary>
    static bool NoVinePath(VineShooter shooter, Vector3 to)
    {
        var vine =
            shooter.vinePrefab != null ? shooter.vinePrefab.GetComponent<JungleVine>() : null;
        if (
            vine == null
            || !SpoolRay(VRSession.Active && VRRig.Instance != null, out var origin, out var dir)
        )
            return false;
        var start = origin - Vector3.up * 0.2f;
        if (
            Physics.Raycast(
                origin + dir,
                Vector3.down,
                out var ground,
                4f,
                HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)
            )
        )
            start = ground.point + Vector3.up * 1.5f;
        // The game draws each hang at random inside a narrowing window; 21 even steps cover it.
        for (int i = 0; i <= 20; i++)
        {
            if (JungleVine.CheckVinePath(start, to, -vine.maxDown * i / 20f, out _))
                return false;
        }
        return true;
    }

    /// <summary>vr.172: the warp compass (Action_WarpToRandomPlayer) used with nobody alive farther than its
    /// <c>minimumDistance</c>: RunAction gives the use back (IncreaseUsesRPC, Action_WarpToRandomPlayer.cs:27-31)
    /// after its warp sound. VR gets NeedsFriend at the hand with the compass red; the player does not move.
    /// SKIP when someone is far enough to warp to.</summary>
    IEnumerator WarpRefundStep()
    {
        if (!Affordance.Enabled)
        {
            stepSkip = "ui.affordance not installed";
            yield break;
        }
        var prefab = FirstItem(i => i.GetComponent<Action_WarpToRandomPlayer>() != null);
        if (prefab == null)
        {
            stepSkip = "no warp compass item";
            yield break;
        }
        var c = Character.localCharacter;
        float min = prefab.GetComponent<Action_WarpToRandomPlayer>().minimumDistance;
        foreach (var other in Character.AllCharacters)
        {
            if (other != c && !other.data.dead && Vector3.Distance(c.Center, other.Center) > min)
            {
                stepSkip = $"{other.characterName} is a warp target";
                yield break;
            }
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        var from = c.Center;
        int usesBefore = UsesLeft(item);
        Affordance.ResetGate();
        float mark = CueMark();
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        float end = Now + GameTiming.Budget(GameTiming.UseSeconds(item), 1f);
        while (Now < end && !CueLog.Capture.Fired(left, DenyReason.NeedsFriend, mark, true, out _))
            yield return null;
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        yield return ExpectCue(left, DenyReason.NeedsFriend, mark, tint: true, within: 0.5f);
        var cue = cueNote;
        float moved = Vector3.Distance(from, c.Center);
        if (!stepOk)
            yield break;
        // The game gives the use back (IncreaseUsesRPC, Action_WarpToRandomPlayer.cs:27-31) a moment after the try.
        var now = HeldItem();
        yield return WaitFor(() => now != null && UsesLeft(now) == usesBefore, 1f);
        int usesAfter = now != null ? UsesLeft(now) : -2;
        if (moved > 2f)
            Fail($"warped {moved:0.0} m with nobody to warp to");
        else if (usesAfter != usesBefore)
            Fail($"the use was not given back: uses {usesBefore} -> {usesAfter}; {cue}");
        else
            Note($"{prefab.name}: moved {moved:0.0} m, uses {usesBefore} -> {usesAfter}; {cue}");
    }
}
