using System;
using System.Collections;
using System.Collections.Generic;
using Peak;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact;
using PeakXR.Interact.Lights;
using PeakXR.Interact.Physical;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.RunFlow;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;
using Object = UnityEngine.Object;

namespace PeakXR.Sandbox;

/// <summary>
/// Steps owned by WP5 (world objects and run flow): the campfire friends-far refusal, the respawn chest's two-hand
/// lid, lid progress, cook refusals, the Book of Bones over a skeleton, a flame on a lit fuse, the full altar and
/// the effigy with nobody down. Prop steps warp to the prop and back (<see cref="WarpBack"/>); a prop the loaded map
/// lacks skips the step with the reason.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator WorldSteps()
    {
        yield return SettleIn();
        yield return HandStep("vr.80-campfire-far", 30f, CampfireFarStep());
        yield return WarpBack();
        yield return HandStep("vr.81-chest-two-hands", 75f, ChestTwoHandsStep());
        yield return WarpBack();
        yield return HandStep("vr.82-lid-progress", 40f, LidProgressStep());
        yield return WarpBack();
        yield return HandStep("vr.82b-lid-low-grip", 30f, LidLowGripStep());
        yield return WarpBack();
        yield return HandStep("vr.83-cook-cant-use", 40f, CookCantUseStep());
        yield return WarpBack();
        yield return ItemStep("vr.83b-cook-not-lit", AppleFragment, 40f, CookNotLitStep);
        yield return WarpBack();
        yield return ItemStep("vr.83c-cook-warmth", AppleFragment, 40f, CookWarmthStep);
        yield return WarpBack();
        yield return HandStep("vr.84-book-of-bones", 30f, BookStep(usedUp: false));
        yield return HandStep("vr.84b-book-used-up", 30f, BookStep(usedUp: true));
        yield return ItemStep("vr.87-fuse-lit", "Torch", 40f, FuseLitStep);
        yield return WarpBack();
        yield return ItemStep("vr.88-scout-cannon", "Torch", 40f, CannonStep);
        yield return WarpBack();
        yield return HandStep("vr.92-checkpoint", 40f, CheckpointStep());
        yield return HandStep("vr.94-effigy-nobody-down", 20f, EffigyStep());
    }

    /// <summary>The steps before this package can leave the player passed out (vr.105 gives up); the revive warps,
    /// and a step whose settle starts during that warp fails it (vr33: "a new warp before the old one wrapped up").
    /// Revive first and wait for the warp and the body to finish.</summary>
    static IEnumerator SettleIn()
    {
        Recover();
        var c = Character.localCharacter;
        if (c == null)
            yield break;
        yield return WaitFor(
            () => !c.warping && !c.data.passedOut && !c.data.fullyPassedOut && !c.data.dead,
            10f
        );
        yield return Wait(2f);
    }

    /// <summary>
    /// vr.80: a bare-hand grip on an unlit campfire's logs with a scout outside <c>moraleBoostRadius</c>
    /// (Campfire.cs:327-338, EveryoneInRange :387-398) is refused (FriendsFar, logs red) and the fire stays unlit.
    /// Solo, the radius is shrunk for the step so the player counts as the one away (a named shortcut: the real case,
    /// another scout outside the radius, needs a second player and is the MP fire step's). The hand grips the near
    /// side of the log pile with the head stooped, as a player kneels at a fire (<see cref="GripLogs"/>).
    /// </summary>
    IEnumerator CampfireFarStep()
    {
        var fire = UnlitCampfire(_ => true, out var was);
        if (fire == null)
        {
            stepSkip = "no campfire loaded";
            yield break;
        }
        float radius = fire.moraleBoostRadius;
        try
        {
            yield return ApproachFire(ItemGestures.FireBase(fire));
            fire.moraleBoostRadius = 0.05f;
            bool left = !VRInput.LeftHanded;
            yield return GripLogs(left, fire);
            if (!stepOk)
                yield break;
            Affordance.ResetGate();
            LookAtHand(left);
            float mark = CueMark();
            yield return GripTo(left, 1f, 0.1f);
            float eyeHand = Vector3.Distance(
                VRRig.Instance.Head.position,
                VRRig.Instance.Hand(left).position
            );
            yield return ExpectCue(left, DenyReason.FriendsFar, mark, tint: true, within: 2f);
            var cue = cueNote;
            yield return GripTo(left, 0f, 0.1f);
            if (!stepOk)
                yield break;
            if (fire.state != Campfire.FireState.Off)
                Fail($"{fire.name} changed to {fire.state} with a scout away");
            else
                stepNote = F(
                    $"{fire.name}: {cue}; eye-hand {eyeHand:0.00} m, stooped {headDrop:0.00} m; shortcut: moraleBoostRadius shrunk to 0.05 m so the lone player counts as the scout away"
                );
        }
        finally
        {
            fire.moraleBoostRadius = radius;
            RestoreFire(fire, was);
        }
    }

    /// <summary>
    /// The nearest campfire a step can use unlit: an unlit one, else a lit one set back to unlit locally
    /// (<c>Campfire.state</c>, Campfire.cs:105-116; its flames stay drawn). vr.26 lights the beach's own fire and every
    /// segment jump lights the one behind it (<c>PreviousCampfire.LightWithoutReveal</c>, MapHandler.cs:927), so a
    /// run otherwise has no unlit fire left. Restore with <see cref="RestoreFire"/>.
    /// </summary>
    static Campfire UnlitCampfire(
        Func<Campfire, bool> ok,
        out (Campfire.FireState State, bool Forced) was
    )
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(
            c.Center,
            f => !f.Lit && f.state == Campfire.FireState.Off && ok(f)
        );
        if (fire == null)
            fire = Nearest<Campfire>(c.Center, f => f.state != Campfire.FireState.Spent && ok(f));
        was = fire != null ? (fire.state, fire.forceLit) : (Campfire.FireState.Off, false);
        if (fire != null)
        {
            // Lit is state Lit or forceLit (Campfire.cs:120-130): clear both (vr40 still cooked on a reset fire).
            fire.state = Campfire.FireState.Off;
            fire.forceLit = false;
        }
        return fire;
    }

    /// <summary>
    /// The bare hand onto an unlit fire's logs the way a player reaches them: the near side of the log pile (its
    /// renderer, <c>Campfire.mainRenderer</c>, the first child renderer, Campfire.cs:143, whose bounds centre is
    /// <c>Center</c>, :268) toward the eyes, at the pile's mid-height and 8 cm in so the grip sphere takes a log, with
    /// the head stooped over it (<see cref="ReachDown"/>). The pile's centre from the stand distance was some 1.3 m from
    /// a standing eye (round 1 audit). Fails past a player's reach (<see cref="OverReach"/>).
    /// </summary>
    IEnumerator GripLogs(bool left, Campfire fire)
    {
        var logs = fire.GetComponentInChildren<Renderer>();
        var b = logs != null ? logs.bounds : new Bounds(fire.Center(), Vector3.zero);
        var eye = VRRig.Instance.Head.position;
        var near = b.ClosestPoint(new Vector3(eye.x, b.center.y, eye.z));
        var grip = Vector3.MoveTowards(near, b.center, 0.08f);
        yield return ReachDown(left, () => grip, 0.6f);
        if (OverReach(left) is { } far)
            Fail(
                F(
                    $"{far}: logs' near side {FlatDistance(Character.localCharacter.Center, grip):0.00} m from the body, stooped {headDrop:0.00} m"
                )
            );
    }

    /// <summary>Puts back what <see cref="UnlitCampfire"/> changed, unless the step lit the fire for real.</summary>
    static bool HasFireAhead(Segment from)
    {
        foreach (var f in FindObjectsByType<Campfire>(FindObjectsSortMode.None))
        {
            if (f != null && f.advanceToSegment > from && f.state != Campfire.FireState.Spent)
                return true;
        }
        return false;
    }

    static void RestoreFire(Campfire fire, (Campfire.FireState State, bool Forced) was)
    {
        if (fire == null || fire.state != Campfire.FireState.Off)
            return;
        fire.state = was.State;
        fire.forceLit = was.Forced;
    }

    /// <summary>
    /// vr.81: one hand on a closed respawn chest's lid holds the timer at 0 and after 0.6 s gets NeedsTwoHands
    /// (chest red); both hands lifting run the timer (<c>Interaction.constantInteractableProgress</c>).
    /// </summary>
    IEnumerator ChestTwoHandsStep()
    {
        var c = Character.localCharacter;
        var chest = Nearest<RespawnChest>(c.Center, r => !r.IsOpen);
        if (chest == null)
        {
            stepSkip = "no closed respawn chest in the active segment";
            yield break;
        }
        if (!Setting("Interaction", "LiftLidToOpen", true))
        {
            stepSkip = "Interaction.LiftLidToOpen is off";
            yield break;
        }
        yield return WarpFacing(chest.Center(), 0.8f);
        HeadFrame(out _, out _, out var right);
        var top = LidPoint(chest);
        bool left = VRInput.LeftHanded;
        yield return MoveTo(left, () => top + (left ? -right : right) * 0.15f, 0.5f);
        Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.NeedsTwoHands, mark, tint: true, within: 2f);
        var cue = cueNote;
        var interaction = Interaction.instance;
        // Read through a further second of the one-hand hold, not only at the cue: the timer must stay held after it.
        float alone = 0f;
        for (float end1 = Now + 1f; stepOk && Now < end1; )
        {
            if (interaction != null)
                alone = Mathf.Max(alone, interaction.constantInteractableProgress);
            yield return null;
        }
        if (!stepOk)
            yield break;
        if (alone > 0.05f)
        {
            Fail($"one hand ran the chest timer to {alone:0.00}");
            yield break;
        }
        yield return MoveTo(!left, () => top + (left ? right : -right) * 0.15f, 0.5f);
        yield return GripTo(!left, 1f, 0.1f);
        yield return Wait(0.3f);
        var l = Script(true).Position;
        var r = Script(false).Position;
        float progress = 0f;
        float end = Now + 3f;
        for (float lifted = 0f; Now < end && progress < 0.1f && !chest.IsOpen; )
        {
            lifted = Mathf.Min(LidGesture.LiftHeight, lifted + 0.15f * Time.unscaledDeltaTime);
            SetHand(true, l + Vector3.up * lifted);
            SetHand(false, r + Vector3.up * lifted);
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, chest))
                progress = Mathf.Max(progress, interaction.constantInteractableProgress);
            yield return null;
        }
        yield return GripTo(true, 0f, 0.1f);
        yield return GripTo(false, 0f, 0.1f);
        if (progress <= 0f && !chest.IsOpen)
        {
            Fail($"two hands lifting did not run the timer; one hand: {cue}");
            yield break;
        }
        var lifted2 = $"two hands progress {progress:0.00}";
        if (chest.IsOpen)
        {
            stepNote =
                $"{chest.name}: one hand {cue}; {lifted2}; opened, so no pointed or grab-off phase";
            yield break;
        }
        // A hold no hand is on: a pointed grip from out of reach (the hand-aimed interact) is held at 0 like one
        // hand, and refused at the chest (LidGesture, LidCap.RefusesUntracked) instead of doing nothing.
        yield return Wait(0.6f);
        bool point = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var side);
        Script(point).Position =
            head + forward * 0.4f + (point ? -side : side) * 0.15f + Vector3.down * 0.25f;
        var lid = top;
        Script(point).Rotation = HandLook(
            rig()
                .Origin.InverseTransformDirection(
                    lid - rig().Origin.TransformPoint(Script(point).Position)
                ),
            Vector3.up
        );
        Script(point).TriggerTouch = false;
        Script(point).StickTouch = true;
        LookAtPoint(lid);
        yield return Wait(0.4f);
        Affordance.ResetGate();
        float pointMark = CueMark();
        yield return GripTo(point, 1f, 0.1f);
        yield return ExpectCue(point, DenyReason.NeedsTwoHands, pointMark, tint: true, within: 2f);
        var pointed = cueNote;
        float pointedProgress = interaction != null ? interaction.constantInteractableProgress : 0f;
        yield return GripTo(point, 0f, 0.1f);
        Script(point).TriggerTouch = true;
        Script(point).StickTouch = false;
        if (!stepOk)
            yield break;
        if (pointedProgress > 0.05f)
        {
            Fail($"a pointed grip ran the chest timer to {pointedProgress:0.00}");
            yield break;
        }
        // Grabbing by hand off (Interaction.GrabToPickUp): nothing can lift a lid, so the plain vanilla hold opens
        // the chest (LidCap.TryFraction).
        if (!Plugin.Settings.TryGetEntry<bool>("Interaction", "GrabToPickUp", out var grab))
        {
            stepNote =
                $"{chest.name}: one hand {cue}; {lifted2}; pointed {pointed}; no GrabToPickUp setting";
            yield break;
        }
        bool was = grab.Value;
        try
        {
            grab.Value = false;
            yield return Wait(0.3f);
            yield return MoveTo(point, () => top, 0.4f);
            yield return GripTo(point, 1f, 0.1f);
            float openBy =
                Now
                + GameTiming.Budget(
                    GameTiming.InteractSeconds(chest, Character.localCharacter),
                    2f
                );
            yield return WaitFor(() => chest.IsOpen, openBy - Now);
            yield return GripTo(point, 0f, 0.1f);
        }
        finally
        {
            grab.Value = was;
        }
        if (!chest.IsOpen)
            Fail(
                $"with grabbing by hand off the vanilla hold did not open {chest.name}; pointed: {pointed}"
            );
        else
            stepNote =
                $"{chest.name}: one hand {cue}; {lifted2}; pointed {pointed}; grab off: opened by the vanilla hold";

        static VRRig rig() => VRRig.Instance;
    }

    /// <summary>vr.82: lifting a suitcase lid (vr.24's motion, on the next closed case) is felt as the progress
    /// ramp, and the open as a contact tick (WS-W8).</summary>
    IEnumerator LidProgressStep()
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
        bool left = VRInput.LeftHanded;
        if (lug.IsOpen)
        {
            Fail($"{lug.name} was already open at the start");
            yield break;
        }
        float mark = CueMark();
        int ringTicks = UI.HandWidgets.HandWidgetsRuntime.StepTicks;
        int ringYielded = UI.HandWidgets.HandWidgetsRuntime.StepsYielded;
        yield return LiftLid(lug);
        if (!stepOk)
            yield break;
        var opened = stepNote;
        // Open with no lift: the grip did not take the lid, so the plain hold ran (run 65).
        if (!CueLog.Capture.Felt(left, CueFeel.Progress, mark, out _))
        {
            Fail($"{opened}; no lift ramp: {LidGesture.LastGrab}");
            yield break;
        }
        yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
        var progress = cueNote;
        yield return ExpectFeel(left, CueFeel.Contact, mark, within: 0f);
        // One hold, one progress haptic: the ramp owns it, the ring sends no quarter ticks.
        int extra = UI.HandWidgets.HandWidgetsRuntime.StepTicks - ringTicks;
        int yielded = UI.HandWidgets.HandWidgetsRuntime.StepsYielded - ringYielded;
        if (stepOk && extra > 0)
            Fail($"the progress ring ticked {extra} quarter steps beside the lid's ramp");
        else if (stepOk)
            stepNote = $"{opened}; {progress}; {cueNote}; ring left {yielded} quarter steps to it";
    }

    /// <summary>vr.83: an item that cannot cook (<c>ItemCooking.canBeCooked</c> false) held in the heat column of a
    /// lit campfire is refused with CantUse, the item red (Campfire.cs:353-368). Lights the fire without the segment
    /// advance first when needed (<c>LightWithoutReveal</c>, Campfire.cs:468).</summary>
    IEnumerator CookCantUseStep()
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state != Campfire.FireState.Spent);
        var raw = Uncookable();
        if (fire == null || raw == null)
        {
            stepSkip = fire == null ? "no campfire loaded" : "no item with cooking disabled";
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
        yield return HoldInHeat(raw, fireBase, DenyReason.CantUse);
    }

    /// <summary>The first item prefab whose <c>ItemCooking.disableCooking</c> is set (<c>canBeCooked</c> false,
    /// ItemCooking.cs:48), leaving out held lights (over a fire they are the light gesture) and placeables.</summary>
    static Item Uncookable()
    {
        foreach (var item in SandboxActions.Items)
        {
            if (
                item != null
                && item.TryGetComponent(out ItemCooking cooking)
                && !cooking.canBeCooked
                && !item.TryGetComponent(out Flare _)
                && !item.TryGetComponent(out Candle _)
                && !item.TryGetComponent(out Lantern _)
                && item.GetComponentInChildren<Action_Torch>() == null
                && !item.TryGetComponent(out Constructable _)
            )
                return item;
        }
        return null;
    }

    /// <summary>vr.83b: food held where the heat column of an unlit campfire would be is refused with NotLit, the
    /// fire red, and does not cook.</summary>
    IEnumerator CookNotLitStep(Item food)
    {
        var fire = UnlitCampfire(_ => true, out var was);
        if (fire == null)
        {
            stepSkip = "no campfire loaded";
            yield break;
        }
        try
        {
            var fireBase = ItemGestures.FireBase(fire);
            yield return ApproachFire(fireBase);
            yield return HoldInHeat(food, fireBase, DenyReason.NotLit);
            if (stepOk && fire.state != Campfire.FireState.Off)
                Fail($"{fire.name} changed to {fire.state}");
        }
        finally
        {
            RestoreFire(fire, was);
        }
    }

    /// <summary>Holds <paramref name="prefab"/> in the near side of the heat column over <paramref name="fireBase"/>
    /// and expects the <paramref name="reason"/> cue with the red preview. The goal is 0.45 m out and 0.85 m up, inside
    /// FireGestures' column (0.6 m around the base, 0.2-1 m up): about waist height half an arm ahead, where a
    /// standing player holds food over a fire. The old 0.55 m-up goal was some 1.1 m from a standing shoulder.</summary>
    IEnumerator HoldInHeat(Item prefab, Vector3 fireBase, DenyReason reason)
    {
        if (!Setting("Interaction", "CookOverFire", true))
        {
            stepSkip = "Interaction.CookOverFire is off";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        var c = Character.localCharacter;
        var goal = fireBase + Flat(c.Center - fireBase) * 0.45f + Vector3.up * 0.85f;
        Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        float end = Now + 4f;
        BeginCarry();
        while (Now < end && item != null && !CueLog.Capture.Fired(left, reason, mark, true, out _))
        {
            CarryToward(left, item.Center(), goal);
            yield return null;
        }
        var trace = FireTrace("vr.83", item != null ? item.Center() : goal, fireBase, left);
        if (OverReach(left) is { } far)
        {
            Fail($"{far}: {trace}");
            yield break;
        }
        yield return ExpectCue(left, reason, mark, tint: true, within: 0.5f);
        if (stepOk)
            stepNote = $"{prefab.name}: {cueNote}";
        else
            stepNote = trace;
    }

    /// <summary>
    /// vr.84 / vr.84b: the Book of Bones held open just above a skeleton's hips (a local <c>Resources/Skeleton</c>,
    /// as Character.cs:775 spawns one, with no owner so a finished hold revives nobody) starts the game's revive hold
    /// (<c>Skelleton.GetInteractTime</c>, Skelleton.cs:113) with the progress ramp on the book hand. With no uses left
    /// (Skelleton.cs:45-55) it is refused with UsedUp, the book red.
    /// </summary>
    IEnumerator BookStep(bool usedUp)
    {
        if (!Setting("Interaction", "BookOverBones", true))
        {
            stepSkip = "Interaction.BookOverBones is off";
            yield break;
        }
        Item book = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it != null && it.CompareTag("BookOfBones"))
            {
                book = it;
                break;
            }
        }
        var prefab = Resources.Load("Skeleton") as GameObject;
        if (book == null || prefab == null)
        {
            stepSkip = book == null ? "no Book of Bones item" : "no Resources/Skeleton prefab";
            yield break;
        }
        // 0.55 m ahead: kneeling beside the bones with the book 0.4 m over the hips is in reach (0.7 m ahead with a
        // standing head was 1.0-1.1 m from the shoulder).
        var spot = SandboxActions.FrontPoint(0.55f);
        if (Terrain(spot + Vector3.up * 2f, Vector3.down, 5f, out var ground))
            spot = ground.point;
        var skeleton = Object
            .Instantiate(prefab, spot + Vector3.up * 0.3f, Quaternion.identity)
            .GetComponent<Skelleton>();
        var me = Character.localCharacter;
        bool wasSkeleton = me != null && me.data.isSkeleton;
        try
        {
            yield return Wait(1f);
            yield return GiveAndHold(book);
            if (!stepOk)
                yield break;
            var held = HeldItem();
            bool left = LocalHold.ItemInLeftHand;
            yield return GripTo(left, 1f, 0.1f);
            // A given item can have its ItemUses entry created empty (HasData false, Value 0) by a GetData call
            // before Item.Start fills it (Item.cs:458-468, GetData :1223 creates the entry), and Skelleton wants
            // Value > 0 (Skelleton.cs:45-55): vr33 refused a fresh book UsedUp. Fill it as Start would.
            var uses = held.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (!uses.HasData && held.totalUses > 0)
            {
                uses.HasData = true;
                uses.Value = held.totalUses;
            }
            if (usedUp)
                uses.Value = 0;
            string usesNote = $"uses {(uses.HasData ? uses.Value : -1)} of {held.totalUses}";
            Affordance.ResetGate();
            LookAtHand(left);
            float mark = CueMark();
            var interaction = Interaction.instance;
            float progress = 0f;
            bool over = false;
            // The gesture reads the book hand, as a player holds the book over the body: the hand goes 0.4 m above
            // the hips and stays (the book itself moves when the revive opens it).
            var rig = VRRig.Instance;
            yield return ReachDown(left, () => skeleton.Center() + Vector3.up * 0.4f, 0.6f);
            if (OverReach(left) is { } far)
            {
                Fail(
                    F(
                        $"{far}: bones {FlatDistance(me.Center, skeleton.Center()):0.00} m from the body, stooped {headDrop:0.00} m"
                    )
                );
                yield break;
            }
            float end = Now + 4f;
            while (Now < end && held != null && (usedUp || progress < 0.1f))
            {
                SetHand(
                    left,
                    rig.Origin.InverseTransformPoint(skeleton.Center() + Vector3.up * 0.4f)
                );
                over |= WorldHolds.BookTarget != null;
                if (
                    interaction != null
                    && ReferenceEquals(interaction.currentHeldInteractible, skeleton)
                )
                    progress = Mathf.Max(progress, interaction.constantInteractableProgress);
                if (usedUp && CueLog.Capture.Fired(left, DenyReason.UsedUp, mark, true, out _))
                    break;
                yield return null;
            }
            float above = (rig.Hand(left).position - skeleton.Center()).y;
            if (usedUp)
            {
                yield return ExpectCue(left, DenyReason.UsedUp, mark, tint: true, within: 0.5f);
                if (stepOk && progress > 0f)
                    Fail($"a used-up book ran the revive hold to {progress:0.00}");
                if (stepOk)
                    stepNote = cueNote;
                yield break;
            }
            if (progress <= 0f)
            {
                Fail(
                    $"book over the bones did not start the revive hold: book hand {above:0.00} m above the hips, "
                        + $"gesture over the skeleton {over}, hold on {Label(interaction != null ? interaction.currentHeldInteractible : null)}, {usesNote}, last cue {CueLog.Capture.Hand(left).Reason}"
                );
                yield break;
            }
            yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0.5f);
            if (stepOk)
                stepNote = F(
                    $"revive hold {progress:0.00}, book hand {above:0.00} m above, stooped {headDrop:0.00} m; {cueNote}"
                );
        }
        finally
        {
            if (skeleton != null)
                Object.Destroy(skeleton.gameObject);
            // Holding the book over bones must never turn the holder into a skeleton (a finished read does,
            // Action_BecomeSkeleton.cs:7; a finished revive does to the bones' owner, Skelleton.cs:118). Run 33 left
            // the body bone-white between vr.65 and vr.110, the window holding these book steps.
            if (me != null && !wasSkeleton && me.data.isSkeleton)
            {
                Fail("the book turned the local body into a skeleton: " + BodyState(me));
                me.data.SetSkeleton(false);
            }
        }
    }

    /// <summary>
    /// vr.87: a lit torch touched to a fuse or ghost fire that no longer takes a flame (a lit cannon fuse,
    /// ScoutCannonFuse.cs:104; a lit ghost fire, GhostFire.cs:139; a burning or held rocket pack, RocketFuse.cs:113-139)
    /// is refused, the fuse red. Skips when the loaded map has none.
    /// </summary>
    IEnumerator FuseLitStep(Item torchPrefab)
    {
        var c = Character.localCharacter;
        Component fuse = Nearest<GhostFire>(c.Center, g => !g.IsConstantlyInteractable(c));
        if (fuse == null)
            fuse = Nearest<ScoutCannonFuse>(c.Center, f => !f.IsConstantlyInteractable(c));
        if (fuse == null)
            fuse = Nearest<RocketFuse>(c.Center, f => !f.IsConstantlyInteractable(c));
        GameObject placed = null;
        ScoutCannon burning = null;
        if (fuse == null)
        {
            // None in the level: place a cannon and mark its fuse burning (ScoutCannon.lit, read by
            // ScoutCannonFuse.IsConstantlyInteractable, ScoutCannonFuse.cs:104-107), as for the 3 s after a light.
            burning = SpawnCannon(out placed);
            fuse = burning != null ? burning.GetComponentInChildren<ScoutCannonFuse>() : null;
            if (fuse == null)
            {
                DestroySpawned(placed);
                stepSkip = "no lit fuse or ghost fire loaded, and no scout cannon to place";
                yield break;
            }
            burning.lit = true;
        }
        try
        {
            yield return FuseRefused(torchPrefab, fuse);
        }
        finally
        {
            if (burning != null)
                burning.lit = false;
            DestroySpawned(placed);
        }
    }

    /// <summary>Where a given item went when it left the hand: destroyed, or its state, place and holder.</summary>
    static string Whereabouts(Item item, Transform entry)
    {
        if (item == null)
            return "the given item was destroyed";
        var p = item.transform.position;
        var c = Character.localCharacter;
        return $"{item.name} {item.itemState} at {p}, {Vector3.Distance(p, c.Center):0.00} m from the body"
            + (
                entry != null
                    ? $", {Vector3.Distance(p, entry.position):0.00} m from the cannon entry"
                    : ""
            )
            + $", holder {(item.holderCharacter != null ? item.holderCharacter.name : "none")}";
    }

    /// <summary>The hand that held the torch in the last <see cref="FlameToFuse"/>, the cue mark taken just before its
    /// carry, and its pose for the note.</summary>
    bool torchLeft;
    float carryMark;
    string flameNote = "";

    /// <summary>Farthest a held torch's flame sits from the hand holding it (m): the torch's length and grip.</summary>
    const float TorchInHand = 0.45f;

    /// <summary>Seconds a carried flame may stop closing on the fuse before the hand gives up. Since 1899a19 the held
    /// torch rides the ragdoll hand, which follows the controller on a spring: the flame trails, and the items' 0.4 s
    /// (<see cref="CarryStallSeconds"/>) read that lag as blocked (run 58, vr.88: given up 0.49 m from the fuse).</summary>
    const float FlameStall = 1.2f;

    /// <summary>The level direction the fuse sticks out from its cannon's body; zero for a fuse with no cannon.</summary>
    Vector3 fuseOut;

    /// <summary>Within this of the fuse (m) the hand stops steering and holds the flame there: inside the light's
    /// 0.12 m touch (FireGestures.TouchEnter) with room for the ragdoll hand's sway.</summary>
    const float FuseHold = 0.08f;

    /// <summary>The fuse carry's waypoints and dwell, for the note.</summary>
    string fuseWay = "";

    /// <summary>Wrist pitches (deg down from level) tried to tip a held torch toward a low fuse.</summary>
    static readonly float[] TorchTips = { 30f, 60f, 90f };

    /// <summary>Farthest a held item may sit from its controller-held pose (m), standing or stooped.</summary>
    const float ItemOnHand = 0.15f;

    /// <summary>
    /// A lit torch's flame brought to a fuse the way a player lights one. The torch is given and lit where the player
    /// stands, as vr.26 lights it (vr41 lost it given and lit beside the placed cannon); the body warps 0.8 m beside the
    /// fuse, on the side away from a cannon's entry (it pulls in and launches whoever is within 1 m of it, dropping
    /// what they hold, ScoutCannon.cs:66-110); the head stoops until it is at most <see cref="StoopReach"/> over the
    /// hand's goal, as a player kneels at a fuse low on its prop (vr45 and vr46: a standing body's ragdoll arm stalled
    /// the flame 0.96 m short and the hand never moved); then the hand carries the flame onto the fuse's collider
    /// (FireGestures: within 0.12 m of it; vr43 aimed at its transform) until <paramref name="done"/> or
    /// <paramref name="seconds"/>. Fails when the torch is lost or the hand ends past a player's reach.
    /// </summary>
    IEnumerator FlameToFuse(Item torchPrefab, Component fuse, Func<bool> done, float seconds)
    {
        var c = Character.localCharacter;
        var at = fuse.transform.position;
        var cannon = fuse.GetComponentInParent<ScoutCannon>();
        var entry =
            cannon != null ? GameMember.Field<ScoutCannon, Transform>("entry")(cannon) : null;
        yield return GiveAndHold(torchPrefab);
        if (!stepOk)
            yield break;
        var given = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        torchLeft = left;
        yield return GripTo(left, 1f, 0.1f);
        float lightUse = GameTiming.Budget(GameTiming.UseSeconds(given));
        Script(left).Trigger = 1f;
        // The torch in hand each time: a given trackable item can be swapped for its reconnected copy, which left
        // the first reference destroyed (vr40 threw on it; vr38 lost a flare the same way).
        yield return WaitFor(() => HeldItem() != null && HandLights.IsLit(HeldItem()), lightUse);
        Script(left).Trigger = 0f;
        if (HeldItem() == null)
        {
            Fail("no torch in hand after lighting it: " + Whereabouts(given, entry));
            yield break;
        }
        // The side the fuse sticks out on, away from the cannon's body: carried in from elsewhere the torch met the
        // body and stopped 0.29 m short (runs 56-61, vr.88). Stand there unless it brings the body within 1.2 m of
        // the cannon's entry, which pulls in and launches whoever comes near it (ScoutCannon.cs:66-110).
        fuseOut = Vector3.zero;
        if (cannon != null)
        {
            var body = new Bounds(cannon.transform.position, Vector3.zero);
            foreach (var r in cannon.GetComponentsInChildren<Renderer>())
                if (r.enabled && !r.transform.IsChildOf(fuse.transform))
                    body.Encapsulate(r.bounds);
            var outward = at - body.center;
            outward.y = 0f;
            if (outward.sqrMagnitude > 1e-4f)
                fuseOut = outward.normalized;
        }
        bool outSide =
            fuseOut != Vector3.zero
            && (entry == null || Vector3.Distance(at + fuseOut * 0.8f, entry.position) > 1.2f);
        var side =
            outSide ? fuseOut
            : entry != null ? at - entry.position
            : default;
        if (side != default)
            yield return WarpBeside(at, 0.8f, side);
        else
            yield return WarpFacing(at, 0.8f);
        var torch = HeldItem();
        if (torch == null)
        {
            Fail(
                "the lit torch left the hand on the warp to the fuse: " + Whereabouts(given, entry)
            );
            yield break;
        }
        // The copy in hand after the warp may sit in the other hand: carry with the hand that holds it now.
        left = LocalHold.ItemInLeftHand;
        torchLeft = left;
        var flame = FireGestures.FlamePoint(torch);
        if (flame == null)
            flame = torch.transform;
        var fuseCol = fuse.GetComponentInChildren<Collider>();
        Vector3 Touch() => fuseCol != null ? fuseCol.bounds.center : at;
        var rig = VRRig.Instance;
        // The torch must ride the hand before the hand can carry it: run 48 carried a controller that ended 1.17 m
        // from the flame, so the flame never reached the fuse. Give the held copy up to 1.5 s to settle in the hand.
        // The measure is the item's own distance from its controller-held pose (avatar.local), not flame to hand,
        // which is the torch's length.
        var driver = AvatarPoseDriver.Of(c);
        float Gap() => driver != null ? driver.HeldItemGap : -1f;
        yield return WaitFor(() => flame != null && Gap() >= 0f && Gap() <= ItemOnHand, 1.5f);
        if (flame == null)
        {
            Fail("the torch left the hand after the warp to the fuse");
            yield break;
        }
        // A player tips the torch down toward a low fuse, so the flame leads the hand instead of standing 0.5 m over
        // it (run 53: a flame upright over the hand put the hand 0.27 m under the floor to reach a fuse at the feet).
        // Of the wrist turns a player can make, keep the one that brings the flame lowest under the hand.
        var keep = Script(left).Rotation;
        float lowest = flame.position.y - rig.Hand(left).position.y;
        HeadFrame(out _, out var level, out var across);
        foreach (float tip in TorchTips)
        {
            var turn = Quaternion.AngleAxis(tip, across) * HandLook(level, Vector3.up);
            Script(left).Rotation = turn;
            yield return null;
            yield return null;
            if (flame == null)
                break;
            float drop = flame.position.y - rig.Hand(left).position.y;
            if (drop < lowest)
            {
                lowest = drop;
                keep = turn;
            }
        }
        Script(left).Rotation = keep;
        yield return null;
        yield return null;
        if (flame == null)
        {
            Fail("the torch left the hand while tipping it toward the fuse");
            yield break;
        }
        float inHand = Vector3.Distance(rig.Hand(left).position, flame.position);
        // Where the hand puts the flame on the fuse, stooping on the way; the carry then closes what is left. A
        // flame not yet at the hand leads by nothing, so the goal stays at the fuse.
        var lead = inHand < TorchInHand ? rig.Hand(left).position - flame.position : Vector3.zero;
        // The cue window opens before the reach: the reach can already put the flame on the fuse, and FireGestures
        // refuses a touch once, as it begins, so a refusal during the reach would land before a later mark (vr57: flame
        // 0.00 m from the fuse, no UsedUp after the mark).
        Affordance.ResetGate();
        LookAtHand(torchLeft);
        carryMark = CueMark();
        yield return ReachDown(left, () => Touch() + lead, 0.6f);
        if (flame == null)
        {
            Fail("the torch left the hand while reaching for the fuse");
            yield break;
        }
        // A held item must stay on its stooped hand (avatar.local FollowHeld lets it leave a ragdoll arm that cannot
        // reach): within ItemOnHand of its controller-held pose once the hand has settled.
        float inHandGap = Gap();
        yield return WaitFor(() => flame == null || (Gap() >= 0f && Gap() <= ItemOnHand), 0.5f);
        float stoopedGap = Gap();
        if (flame == null || stoopedGap < 0f || stoopedGap > ItemOnHand)
        {
            // A held item that does not follow its hand cannot be brought anywhere: say so instead of a fuse miss.
            Fail(
                F(
                    $"the held {torch.name} is {stoopedGap:0.00} m from its place in the {(left ? "left" : "right")} hand after the stoop ({inHandGap:0.00} m before it, stooped {headDrop:0.00} m, free of the ragdoll hand {driver != null && driver.HeldReleased}); the held item does not follow a stooped hand"
                )
            );
            yield break;
        }
        // Runs 61 and 69 ended up on the cannon's base, 1 m over the ground beside it: even fully stooped the fuse lay
        // past a player's reach (the hand 1.28 m from the eye). Step down beside it once and reach again.
        if (!InStoopReach(c, Touch()))
        {
            yield return StandUp(left);
            yield return WarpBeside(at, 0.8f, side);
            if (flame == null || HeldItem() != torch)
            {
                Fail("the torch left the hand on the step back beside the fuse");
                yield break;
            }
            yield return ReachDown(left, () => CapToReach(Touch() + lead), 0.6f);
            if (!InStoopReach(c, Touch()))
            {
                Fail(
                    F(
                        $"the fuse stays past a stooped reach after stepping beside it again: body {FlatDistance(c.Center, Touch()):0.00} m from it, standing {TrackedFloorY() - Feet(c).y:0.00} m over the ground"
                    )
                );
                yield break;
            }
        }
        float end = Now + seconds;
        BeginCarry();
        // Over the fuse first, then down onto it, as a player lowers a flame onto a fuse: carried in level, the torch
        // met the cannon's body and the flame stalled 0.25 m short (run 56, vr.88) of the 0.12 m touch the light needs
        // (FireGestures.TouchEnter; ScoutCannonFuse.Interact_CastFinished lights it, ScoutCannonFuse.cs:99-101).
        float above = Now + Mathf.Min(1.5f, seconds * 0.4f);
        // Over the fuse and out on its exposed side, then in and down onto it.
        Vector3 Approach() => Touch() + Vector3.up * 0.25f + fuseOut * 0.2f;
        while (
            Now < above
            && flame != null
            && !done()
            && Vector3.Distance(flame.position, Approach()) > 0.08f
        )
        {
            CarryToward(left, flame.position, Approach(), FlameStall);
            StoopToFollow(left);
            CapHandToReach(left);
            yield return null;
        }
        var approachAt = Approach();
        BeginCarry();
        // The last leg ends on the fuse's own touch point, and once the flame is inside the light's touch distance
        // (FireGestures.TouchEnter, 0.12 m) the hand holds still for the light's hold: run 63 got the flame within
        // 0.03 m and kept steering, and it ended 0.56 m off, unlit.
        float heldAt = -1f;
        while (Now < end && flame != null && !done())
        {
            float off =
                fuseCol != null
                    ? Vector3.Distance(fuseCol.ClosestPointOnBounds(flame.position), flame.position)
                    : Vector3.Distance(Touch(), flame.position);
            if (off <= FuseHold)
            {
                if (heldAt < 0f)
                    heldAt = Now;
            }
            else
            {
                heldAt = -1f;
                CarryToward(left, flame.position, Touch(), FlameStall);
            }
            StoopToFollow(left);
            CapHandToReach(left);
            yield return null;
        }
        fuseWay = F(
            $"waypoints: over and out at {approachAt}, then the fuse at {Touch()}; held at the fuse {(heldAt < 0f ? 0f : Now - heldAt):0.0} s"
        );
        if (flame == null)
        {
            Fail("the torch left the hand while carried to the fuse");
            yield break;
        }
        float gap =
            fuseCol != null
                ? Vector3.Distance(fuseCol.ClosestPointOnBounds(flame.position), flame.position)
                : Vector3.Distance(at, flame.position);
        flameNote = F(
            $"flame {gap:0.00} m from {(fuseCol != null ? fuseCol.name : fuse.name)} ({Touch().y - Feet(c).y:0.00} m over the feet), hand {Vector3.Distance(rig.Head.position, rig.Hand(left).position):0.00} m from the eye and {Vector3.Distance(rig.Hand(left).position, flame.position):0.00} m from the flame, stooped {headDrop:0.00} m, body {FlatDistance(c.Center, Touch()):0.00} m from the fuse standing {TrackedFloorY() - Feet(c).y:0.00} m over the ground, closest {carryBest:0.00} m to the carry goal; {fuseWay}"
        );
        if (OverReach(left) is { } far)
            Fail($"{far}: {flameNote}");
    }

    /// <summary>A stooping head reaches <paramref name="target"/>: the body stands on the ground (not up on a prop)
    /// and the eye, lowered to the deepest stoop, is within a player's reach of it.</summary>
    bool InStoopReach(Character c, Vector3 target)
    {
        var eye = VRRig.Instance.Head.position;
        eye.y -= MaxStoop - headDrop;
        return TrackedFloorY() - Feet(c).y <= 0.5f
            && Vector3.Distance(eye, target) <= MaxHandFromEye * 0.95f;
    }

    /// <summary>The scripted hand held within a player's reach of the scripted eye (<see cref="OverReach"/>'s measure),
    /// keeping its turn: a carry heading past it stops at the edge.</summary>
    void CapHandToReach(bool left)
    {
        var eye = ScriptedEye();
        ref var hand = ref Script(left);
        hand.Position = eye + Vector3.ClampMagnitude(hand.Position - eye, MaxHandFromEye * 0.97f);
    }

    IEnumerator FuseRefused(Item torchPrefab, Component fuse)
    {
        var c = Character.localCharacter;
        var reason =
            fuse is GhostFire || ((IInteractible)fuse).IsInteractible(c)
                ? DenyReason.UsedUp
                : DenyReason.Occupied;
        yield return FlameToFuse(
            torchPrefab,
            fuse,
            () => CueLog.Capture.Fired(torchLeft, reason, carryMark, true, out _),
            6f
        );
        if (!stepOk)
            yield break;
        if (!CueLog.Capture.Fired(torchLeft, reason, carryMark, true, out var why))
        {
            var cannon = fuse.GetComponentInParent<ScoutCannon>();
            var held = HeldItem();
            Fail(
                $"no {reason} cue: {flameNote} ({why}); fuse takes a flame {((IInteractibleConstant)fuse).IsConstantlyInteractable(c)}, cannon lit {(cannon != null && cannon.lit)}, torch lit {(held != null && HandLights.IsLit(held))}"
            );
            yield break;
        }
        yield return ExpectCue(torchLeft, reason, carryMark, tint: true, within: 0.5f);
        if (stepOk)
            stepNote = $"{fuse.name}: {cueNote}; {flameNote}";
    }

    /// <summary>
    /// vr.89: an amulet carried up to a socket of a full scout statue altar (every <c>hasAmulets</c> slot set,
    /// Peak/ScoutStatue.cs:10) is refused with Occupied, the statue red; a vanilla hold finishing there
    /// (Interact_CastFinished, :302-310) is refused too, and the amulet stays in the hand instead of being consumed.
    /// The slots are filled locally for the step and restored. Peak segment only.
    /// </summary>
    IEnumerator AltarFullStep()
    {
        if (!Setting("Interaction", "GuardWastedItems", true))
        {
            stepSkip = "Interaction.GuardWastedItems is off";
            yield break;
        }
        var statue = Object.FindFirstObjectByType<ScoutStatue>();
        AmuletBase amulet = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it != null && it.TryGetComponent(out AmuletBase a) && a.amuletIndex != 99)
            {
                amulet = a;
                break;
            }
        }
        if (statue == null || amulet == null || statue.amuletObjects is not { Length: > 0 })
        {
            stepSkip =
                statue == null ? "no scout statue loaded (Peak only)"
                : amulet == null ? "no amulet item"
                : "the scout statue has no sockets";
            yield break;
        }
        var slots = GameMember.Field<ScoutStatue, int[]>("hasAmulets")(statue);
        var saved = (int[])slots.Clone();
        try
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i] = i;
            // The player's path first: the amulet carried up to a socket of the full altar (WorldHolds.Summit
            // AltarNear, 0.35 m). vr43 only called Interact_CastFinished, the guard behind a finished vanilla hold.
            int socket = Mathf.Clamp(amulet.amuletIndex, 0, statue.amuletObjects.Length - 1);
            var socketAt = WorldHolds.SocketCenter(statue.amuletObjects[socket]);
            yield return WarpBeside(socketAt, 0.7f);
            yield return GiveAndHold(amulet.GetComponent<Item>());
            if (!stepOk)
                yield break;
            var held = HeldItem();
            bool left = LocalHold.ItemInLeftHand;
            yield return GripTo(left, 1f, 0.1f);
            Affordance.ResetGate();
            LookAtHand(left);
            float mark = CueMark();
            yield return MoveTo(left, () => AmuletOffer(socketAt), 0.6f);
            yield return ExpectCue(left, DenyReason.Occupied, mark, tint: true, within: 2f);
            var cue = cueNote;
            if (!stepOk)
                yield break;
            // Then the guard a vanilla hold reaching its end meets (Interact_CastFinished, :302-310).
            Affordance.ResetGate();
            float guardMark = CueMark();
            statue.Interact_CastFinished(Character.localCharacter);
            yield return ExpectCue(left, DenyReason.Occupied, guardMark, tint: true);
            yield return Wait(1f);
            if (stepOk && !ReferenceEquals(HeldItem(), held))
                Fail("the amulet left the hand at a full altar");
            else if (stepOk)
                stepNote = $"carried to socket {socket}: {cue}; finished hold guarded";
        }
        finally
        {
            Array.Copy(saved, slots, slots.Length);
        }
    }

    /// <summary>vr.94: a scout effigy held with nobody down (Character.PlayerIsDeadOrDown, Character.cs:1975) stays
    /// unusable (ScoutEffigy.cs:8-21, guarded against Constructable.Update's overwrite): the trigger gets NotInTrouble
    /// ("nobody is down", UseDenial.Other) with the effigy red, and the effigy stays in the hand.</summary>
    IEnumerator EffigyStep()
    {
        if (!Setting("Interaction", "GuardWastedItems", true))
        {
            stepSkip = "Interaction.GuardWastedItems is off";
            yield break;
        }
        var effigy = FindItemComponent<ScoutEffigy>();
        if (effigy == null || Character.PlayerIsDeadOrDown())
        {
            stepSkip = effigy == null ? "no scout effigy item" : "someone is down";
            yield break;
        }
        yield return GiveAndHold(effigy.GetComponent<Item>());
        if (!stepOk)
            yield break;
        var held = HeldItem();
        yield return Wait(0.3f);
        if (held.CanUsePrimary())
        {
            Fail("effigy usable with nobody down");
            yield break;
        }
        yield return PressAndExpect(LocalHold.ItemInLeftHand, DenyReason.NotInTrouble);
        if (stepOk && !ReferenceEquals(HeldItem(), held))
            Fail("effigy left the hand");
    }

    /// <summary>
    /// Ground 0.45 m out from <paramref name="lug"/>'s solid bounds, on the first of eight headings (from the body's
    /// side round) whose floor lies within 0.25 m of the case's base and is not the case itself, so a squat reaches
    /// low on it. False when none is.
    /// </summary>
    static bool LevelSpotBeside(Luggage lug, out Vector3 spot)
    {
        spot = default;
        var c = Character.localCharacter;
        Bounds? solid = null;
        foreach (var col in lug.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled)
                continue;
            if (solid is { } b)
            {
                b.Encapsulate(col.bounds);
                solid = b;
            }
            else
                solid = col.bounds;
        }
        if (solid is not { } box || c == null)
            return false;
        var from = Flat(c.Center - box.center);
        for (int i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, i * 45f, 0f) * from;
            float reach =
                Mathf.Abs(dir.x) * box.extents.x + Mathf.Abs(dir.z) * box.extents.z + 0.45f;
            var probe = new Vector3(box.center.x, box.max.y + 0.5f, box.center.z) + dir * reach;
            if (
                Terrain(probe, Vector3.down, box.size.y + 1.5f, out var ground)
                && ground.collider.GetComponentInParent<Luggage>() == null
                && Mathf.Abs(ground.point.y - box.min.y) <= 0.25f
            )
            {
                spot = ground.point;
                return true;
            }
        }
        return false;
    }

    /// <summary>vr.82b: a grip low on a closed slow suitcase (open time at least <see cref="LidGesture.LowGripMinOpen"/>)
    /// keeps the vanilla hold and, after 0.5 s, gets the "grab the lid" notice (AimHigher, ring on the lid edge above
    /// the hand). A would-work hint: needs UI.ActionHints.</summary>
    IEnumerator LidLowGripStep()
    {
        var c = Character.localCharacter;
        var lug = Nearest<Luggage>(
            c.Center,
            l =>
                !l.IsOpen
                && l is not RespawnChest
                && l.GetInteractTime(c) >= LidGesture.LowGripMinOpen
        );
        if (lug == null || !Affordance.Hints || !Setting("Interaction", "LiftLidToOpen", true))
        {
            stepSkip =
                lug == null ? "no slow closed suitcase in the active segment"
                : !Affordance.Hints ? "UI.ActionHints is off"
                : "Interaction.LiftLidToOpen is off";
            yield break;
        }
        // 0.6 m out and stooping, as a player crouches at a case on the ground: 8 cm under its centre is about knee
        // height, 1.4 m under a standing eye.
        // Run 48 warped onto higher ground (or the case) 0.25 m from it, so even a full squat left the hand 1.03 m
        // down: stand on ground level with the case's base, as a player walks round to its open side.
        if (LevelSpotBeside(lug, out var levelSpot))
        {
            yield return WarpTo(levelSpot);
            var r = VRRig.Instance;
            r.RotateYaw(
                Vector3.SignedAngle(Flat(r.Head.forward), Flat(lug.Center() - c.Center), Vector3.up)
            );
            yield return Wait(0.6f);
        }
        else
            yield return WarpFacing(lug.Center(), 0.6f);
        bool left = VRInput.LeftHanded;
        // The side the eyes face it from: vr43 put the hand 0.9 m behind the head from the body's side.
        var rig = VRRig.Instance;
        var low = NearSide(lug, rig.Head.position, -0.08f);
        // The level spot sits 0.45 m off the case's bounds along its heading, which on a diagonal or a long case
        // left the low grip 1.25 m out (run 50): step in until it is 0.5 m ahead of the eye, as a player would.
        // Up to twice, onto the floor under the new spot (WarpTo's ray from 3 m up can land on the case or a ledge;
        // run 71's step left the low grip 1.30 m from the body), until the grip is 0.6 m out or less.
        string stepped = "";
        for (int step = 0; step < 2; step++)
        {
            var gap = low - rig.Head.position;
            gap.y = 0f;
            if (gap.magnitude <= 0.6f)
                break;
            var to =
                new Vector3(c.Center.x, TrackedFloorY() + 0.1f, c.Center.z)
                + gap.normalized * (gap.magnitude - 0.5f);
            warpedForProp = true;
            c.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, to, false);
            yield return Wait(0.5f);
            yield return WaitFor(() => !c.warping, 3f);
            yield return Wait(1f);
            rig.RotateYaw(
                Vector3.SignedAngle(
                    Flat(rig.Head.forward),
                    Flat(low - rig.Head.position),
                    Vector3.up
                )
            );
            yield return Wait(0.6f);
            low = NearSide(lug, rig.Head.position, -0.08f);
            stepped += F($"stepped in from {gap.magnitude:0.00} m; ");
        }
        if (stepped.Length > 0)
            Note(stepped);
        yield return ReachDown(left, () => low, 0.5f);
        // A player whose arm runs out stops at full reach: the hand is capped there, and the grip then takes the case
        // or the step says it did not.
        if (OverReach(left) != null)
        {
            Note(
                F(
                    $"hand capped at reach, {Vector3.Distance(Script(left).Position, ScriptedEye()):0.00} m from the eye before"
                )
            );
            SetHand(left, InReach(Script(left).Position));
        }
        if (OverReach(left) is { } far)
        {
            Fail(
                F(
                    $"{far}: low grip {FlatDistance(c.Center, low):0.00} m from the body, stooped {headDrop:0.00} m"
                )
            );
            yield break;
        }
        Affordance.ResetGate();
        LookAtHand(left);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        var interaction0 = Interaction.instance;
        yield return WaitFor(
            () => ReferenceEquals(PhysicalInteraction.HandTarget(left), lug) || lug.IsOpen,
            1f
        );
        if (!ReferenceEquals(PhysicalInteraction.HandTarget(left), lug) && !lug.IsOpen)
        {
            Fail(
                $"the low grip did not take {lug.name}: grip {PhysicalInteraction.HandAction(left)} on "
                    + $"{Label(PhysicalInteraction.HandTarget(left))}, hand {Gap(rig.Hand(left).position, lug):0.00} m "
                    + $"from it, {Vector3.Distance(low, rig.Head.position):0.00} m from the eyes, hovered "
                    + $"{Label(interaction0 != null ? interaction0.currentHovered : null)}; {PhysicalInteraction.LastGripNote}"
            );
            yield break;
        }
        yield return ExpectCue(left, DenyReason.AimHigher, mark, tint: false, within: 2f);
        var cue = cueNote;
        var interaction = Interaction.instance;
        // Kept: still held, or the plain vanilla hold already finished and opened it.
        bool holding =
            lug.IsOpen
            || interaction != null && ReferenceEquals(interaction.currentHeldInteractible, lug);
        yield return GripTo(left, 0f, 0.1f);
        if (!stepOk)
            yield break;
        if (!holding)
        {
            Fail("the low grip did not keep the vanilla hold on " + lug.name);
            yield break;
        }
        stepNote = F($"{lug.name}: {cue}; stooped {headDrop:0.00} m");
        if (lug.IsOpen)
            yield break;
        // A tracked hand sunk into the case, which nothing stops: the grip still takes it (VR run 75's vr.82b met no
        // triangle from inside a concave mesh and did "Interact on nothing").
        // The centre lies half the case further out than the low grip (run 76: past arm's reach), so step in and stoop
        // as for the low grip, until it is in reach or no closer stance is left.
        var inside = lug.Center() + Vector3.down * 0.05f;
        float closest = float.MaxValue;
        for (int step = 0; ; step++)
        {
            yield return ReachDown(left, () => inside, 0.3f);
            if (OverReach(left) == null)
                break;
            closest = Mathf.Min(closest, Vector3.Distance(Script(left).Position, ScriptedEye()));
            var gap = inside - rig.Head.position;
            gap.y = 0f;
            // 0.5 m out keeps the body clear of the case it stands at.
            if (step >= 2 || gap.magnitude <= 0.55f)
            {
                stepNote += F(
                    $"; sunk-in grip skipped: no stance reached it, closest {closest:0.00} m from the eye ({gap.magnitude:0.00} m out)"
                );
                yield break;
            }
            var to =
                new Vector3(c.Center.x, TrackedFloorY() + 0.1f, c.Center.z)
                + gap.normalized * (gap.magnitude - 0.5f);
            warpedForProp = true;
            c.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, to, false);
            yield return Wait(0.5f);
            yield return WaitFor(() => !c.warping, 3f);
            yield return Wait(1f);
            rig.RotateYaw(
                Vector3.SignedAngle(
                    Flat(rig.Head.forward),
                    Flat(inside - rig.Head.position),
                    Vector3.up
                )
            );
            yield return Wait(0.6f);
            stepNote += F($"; stepped in to sink the hand from {gap.magnitude:0.00} m");
        }
        LookAtHand(left);
        yield return Wait(0.2f);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(
            () => ReferenceEquals(PhysicalInteraction.HandTarget(left), lug) || lug.IsOpen,
            1f
        );
        bool took = ReferenceEquals(PhysicalInteraction.HandTarget(left), lug) || lug.IsOpen;
        var sunkNote = PhysicalInteraction.LastGripNote;
        yield return GripTo(left, 0f, 0.1f);
        if (!took)
            Fail(
                $"a hand sunk into {lug.name} took nothing: grip {PhysicalInteraction.HandAction(left)}, hovered "
                    + $"{Label(Interaction.instance != null ? Interaction.instance.currentHovered : null)}; {sunkNote}"
            );
        else
            stepNote += "; a hand sunk into it takes it too";
    }

    /// <summary>The point of <paramref name="lug"/>'s solid colliders nearest a probe on the side toward
    /// <paramref name="from"/>, <paramref name="up"/> metres from its render centre, pulled 2 cm out.</summary>
    static Vector3 NearSide(Luggage lug, Vector3 from, float up)
    {
        var center = lug.Center();
        var probe = center + Flat(from - center) * 1f + Vector3.up * up;
        var best = probe;
        float bestSq = float.MaxValue;
        foreach (var col in lug.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger)
                continue;
            // A concave mesh has no closest-point query, and its bounds can sit well off the real surface (run 60:
            // LuggageAncient's box put the hand where the 0.12 m grab sphere met no triangle, so the grip went to
            // interact and no low-grip hint came). Cast at it from the player's side instead to find the surface.
            Vector3 p;
            if (col is MeshCollider { convex: false })
            {
                var outward = Flat(from - center);
                var ray = new Ray(probe + outward * 1.5f, -outward);
                p = col.Raycast(ray, out var hit, 3f) ? hit.point : col.bounds.ClosestPoint(probe);
            }
            else
                p = col.ClosestPoint(probe);
            float d = (p - probe).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }
        return best + Flat(from - center) * 0.02f;
    }

    /// <summary>vr.83c's food distance from the fire base: 0.25 m outside the cook column (FireGestures' 0.6 m), well
    /// inside its 0.5 m warmth margin.</summary>
    const float WarmthOut = 0.85f;

    /// <summary>vr.83c: food carried toward a lit fire's heat column but held 0.25 m short of it feels the warmth ramp
    /// (progress feel) without the game holding the fire or cooking.</summary>
    IEnumerator CookWarmthStep(Item foodPrefab)
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state != Campfire.FireState.Spent);
        if (fire == null || !Setting("Interaction", "CookOverFire", true))
        {
            stepSkip = fire == null ? "no campfire loaded" : "Interaction.CookOverFire is off";
            yield break;
        }
        var fireBase = ItemGestures.FireBase(fire);
        if (!fire.Lit)
        {
            fire.LightWithoutReveal();
            yield return Until(() => fire.Lit, 3f, "campfire did not light");
            if (!stepOk)
                yield break;
        }
        // Take the food well clear of the column (an earlier fire step may have left the body at the fire).
        yield return WarpFacing(fireBase, 2.5f);
        yield return GiveAndHold(foodPrefab);
        if (!stepOk)
            yield break;
        var food = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        // Stand so the idle hold already puts the food in the warmth band, measured from this hold: a fixed stand
        // left it in the cook column at 0.95 m (run 17: food 0.72 m ahead of the body, 0.28 m out) or out of the band
        // at 2.01 m (run 18: the carry then reached 1.10 m from the eye).
        var ahead = Flat(VRRig.Instance.Head.forward);
        var held = food != null ? food.Center() - c.Center : Vector3.zero;
        float lead = Vector3.Dot(new Vector3(held.x, 0f, held.z), ahead);
        yield return ApproachFire(fireBase, WarmthOut + lead);
        if (food == null)
            yield break;
        float idleOut = FlatDistance(food.Center(), fireBase);
        if (idleOut < WarmthOut - 0.2f || idleOut > WarmthOut + 0.2f)
        {
            Fail(
                F(
                    $"could not stand with the idle food in the warmth band: food {lead:0.00} m ahead of the body, stand {WarmthOut + lead:0.00} m asked, body {FlatDistance(c.Center, fireBase):0.00} m out, food {idleOut:0.00} m out"
                )
            );
            yield break;
        }
        int before = Cooked(food);
        // FireGestures: the column is 0.6 m around the base, 0.2 to 1 m up; the warmth reaches 0.5 m past it. 0.85 m
        // up is waist height, in reach of a standing player (0.55 m up was knee height, past an arm).
        var goal = fireBase + Flat(c.Center - fireBase) * WarmthOut + Vector3.up * 0.85f;
        var interaction = Interaction.instance;
        bool heldFire = false;
        float mark = CueMark();
        float end = Now + 4f;
        BeginCarry();
        while (
            Now < end && food != null && !CueLog.Capture.Felt(left, CueFeel.Progress, mark, out _)
        )
        {
            CarryToward(left, food.Center(), goal);
            heldFire |=
                interaction != null && ReferenceEquals(interaction.currentHeldInteractible, fire);
            yield return null;
        }
        var trace = FireTrace("vr.83c", food != null ? food.Center() : goal, fireBase, left);
        if (OverReach(left) is { } far)
        {
            Fail($"{far}: {trace}");
            yield break;
        }
        yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
        if (!stepOk)
            stepNote = trace;
        else if (heldFire || (food != null && Cooked(food) != before))
            Fail("the food reached the heat column, so the ramp may be the cook's: " + trace);
        else
            stepNote = cueNote;
    }

    /// <summary>
    /// vr.88: an unlit scout cannon's fuse lit by a held flame (<see cref="FlameToFuse"/>: the light gesture holds
    /// interact on the fuse, whose finished hold calls <c>ScoutCannon.Light</c>, ScoutCannonFuse.cs:48-51, :99-102).
    /// Standing beside the fuse, away from the entry's 1 m pull (ScoutCannon.cs:66-110), within interact.world's 4 m:
    /// the countdown is felt as a rising progress ramp on the free hand (the torch hand also feels the light hold's
    /// own ramp), and the shot that leaves you behind gives the notice (contact tick, notice note).
    /// </summary>
    IEnumerator CannonStep(Item torchPrefab)
    {
        var c = Character.localCharacter;
        var cannon = Nearest<ScoutCannon>(c.Center, k => !k.lit);
        GameObject placed = null;
        if (cannon == null)
            cannon = SpawnCannon(out placed);
        var entry =
            cannon != null ? GameMember.Field<ScoutCannon, Transform>("entry")(cannon) : null;
        var fuse = cannon != null ? cannon.GetComponentInChildren<ScoutCannonFuse>() : null;
        if (entry == null || fuse == null)
        {
            DestroySpawned(placed);
            stepSkip =
                entry == null
                    ? "no scout cannon loaded or placeable"
                    : "the scout cannon has no fuse";
            yield break;
        }
        try
        {
            yield return CannonFires(torchPrefab, cannon, fuse, entry);
        }
        finally
        {
            DestroySpawned(placed);
        }
    }

    /// <summary>
    /// A scout cannon placed offline as the Scout Cannon item places one (<c>Constructable.FinishConstruction</c>,
    /// Constructable.cs:158-191: <c>PhotonNetwork.Instantiate</c> of its <c>constructedPrefab</c>), on the ground 3 m
    /// ahead, its barrel facing away. Null when no item builds one. Remove it with <see cref="DestroySpawned"/>.
    /// </summary>
    static ScoutCannon SpawnCannon(out GameObject placed)
    {
        placed = null;
        GameObject prefab = null;
        foreach (var it in SandboxActions.Items)
        {
            if (
                it != null
                && it.TryGetComponent(out Constructable build)
                && build.constructedPrefab != null
                && build.constructedPrefab.GetComponentInChildren<ScoutCannon>(true) != null
            )
            {
                prefab = build.constructedPrefab;
                break;
            }
        }
        var rig = VRRig.Instance;
        if (prefab == null || rig == null || rig.Head == null)
            return null;
        var forward = Flat(rig.Head.forward);
        var at = Character.localCharacter.Center + forward * 3f;
        if (Terrain(at + Vector3.up * 3f, Vector3.down, 8f, out var ground))
            at = ground.point;
        placed = Photon.Pun.PhotonNetwork.Instantiate(
            prefab.name,
            at,
            Quaternion.LookRotation(forward, Vector3.up),
            0
        );
        return placed != null ? placed.GetComponentInChildren<ScoutCannon>() : null;
    }

    static void DestroySpawned(GameObject placed)
    {
        if (placed != null)
            Photon.Pun.PhotonNetwork.Destroy(placed);
    }

    IEnumerator CannonFires(
        Item torchPrefab,
        ScoutCannon cannon,
        ScoutCannonFuse fuse,
        Transform entry
    )
    {
        var c = Character.localCharacter;
        int before = WorldHolds.Notices;
        yield return FlameToFuse(
            torchPrefab,
            fuse,
            () => cannon.lit,
            GameTiming.Budget(fuse.GetInteractTime(c), 3f)
        );
        if (!stepOk)
            yield break;
        if (!cannon.lit)
        {
            Fail("the flame did not light the fuse: " + flameNote);
            yield break;
        }
        float mark = CueMark();
        bool free = !torchLeft;
        float fromEntry = Vector3.Distance(c.Center, entry.position);
        // Pull the torch back off the fuse, as a player steps back from a lit cannon.
        yield return StandUp(torchLeft);
        yield return ExpectFeel(free, CueFeel.Progress, mark, within: 1.5f);
        var ramp = cueNote;
        if (!stepOk)
            yield break;
        yield return Until(
            () => WorldHolds.Notices > before,
            cannon.fireTime + 3f,
            "no notice after the shot"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(true, CueFeel.Contact, mark, within: 0f);
        if (stepOk && CueLog.Capture.LastSound(CueSound.Notice).At < mark)
            Fail("no notice note after the shot");
        if (stepOk)
            stepNote = F(
                $"{cannon.name} lit by the flame ({flameNote}), body {fromEntry:0.00} m from the entry: {ramp}; {cueNote}"
            );
    }

    /// <summary>
    /// vr.92: a checkpoint flag planted by the hand-aimed use (CheckpointConstructable, CheckpointFlag.Initialize
    /// :25-41) and then used (<c>Character.TryCheckpoint</c>, Character.cs:641, the call a death makes at :597) gives
    /// the notice at the flag after the warp. The death itself is not staged: DeathSteps owns that.
    /// </summary>
    IEnumerator CheckpointStep()
    {
        var flagItem = FindItemComponent<CheckpointConstructable>();
        if (flagItem == null)
        {
            stepSkip = "no checkpoint flag item";
            yield break;
        }
        var c = Character.localCharacter;
        int flags = c.data.checkpointFlags.Count;
        yield return GiveAndHold(flagItem.GetComponent<Item>());
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        // The constructable aims from the hand (interact.aim): turn it until the game's own preview check passes
        // (Constructable.cs:62-69), as vr.163 does; a fixed aim at the ground was refused in vr33.
        var build = item.GetComponent<Constructable>();
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.3f + Vector3.down * 0.2f);
        yield return AimHeld(
            c,
            left,
            BuildPitches,
            AllYaws,
            () => item != null && item.CanUsePrimary() && build.currentPreview != null
        );
        if (!aimFound)
        {
            Fail($"no ground within {build.maxConstructDistance:0.0} m that the flag would take");
            yield break;
        }
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        yield return WaitFor(
            () => c.data.checkpointFlags.Count > flags,
            GameTiming.Budget(GameTiming.UseSeconds(item), 3f)
        );
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        if (c.data.checkpointFlags.Count <= flags)
        {
            Fail("the flag was not planted");
            yield break;
        }
        var flag = c.data.checkpointFlags[c.data.checkpointFlags.Count - 1];
        var at = flag.transform.position;
        int before = WorldHolds.Notices;
        float mark = CueMark();
        if (!c.TryCheckpoint())
        {
            Fail("TryCheckpoint did not use the flag");
            yield break;
        }
        yield return Until(() => WorldHolds.Notices > before, 3f, "no notice after the checkpoint");
        if (!stepOk)
            yield break;
        yield return ExpectFeel(left, CueFeel.Contact, mark, within: 0f);
        var sound = CueLog.Capture.LastSound(CueSound.Notice);
        if (stepOk && (sound.At < mark || Vector3.Distance(sound.Position.U(), at) > 0.5f))
            Fail("no notice note at the flag");
        else if (stepOk)
            stepNote = F(
                $"flag at {at}, body {Vector3.Distance(c.Center, at):0.0} m from it; {cueNote}; shortcut: Character.TryCheckpoint called directly, the death that calls it (Character.cs:597) is not staged"
            );
    }

    // ---------------------------------------------------------------- end of run

    /// <summary>
    /// The end-of-run slot (<see cref="Script"/>, after every hand, segment and death step): steps that advance or
    /// end the run. A real campfire advances the segment; then the summit props on the Peak; last the end screen,
    /// which wins the run and returns to the airport, where the summary and quit run as after any test.
    /// </summary>
    /// <summary>99-save-untouched (last step of the self-test and the MP script): the player's quicksave is exactly
    /// as it was when the guard armed (<see cref="SelfTestProgressGuard"/>): same presence, size, last write and
    /// SHA-256, after a run that lit a real campfire and won.</summary>
    IEnumerator SaveUntouched()
    {
        yield return null;
        if (!SelfTestProgressGuard.Active)
        {
            Fail("progress and save guard not armed");
            yield break;
        }
        var now = FileFingerprint.Of(SelfTestProgressGuard.SavePath);
        var note =
            $"{SelfTestProgressGuard.SavePath}: at start {SelfTestProgressGuard.SaveAtStart}, now {now}";
        if (!SelfTestProgressGuard.SaveAtStart.Same(now))
            Fail("the player's quicksave changed: " + note);
        else
            Note(note);
    }

    /// <summary>Opt-in for the end-of-run slot when the progress guard is not in place.</summary>
    const string EndRunFlag = "-peakxr-selftest-endrun";

    static readonly string[] EndOfRunIds =
    {
        "vr.85-campfire-segment",
        "vr.89-to-peak",
        "vr.89-altar-full",
        "vr.89b-amulet-socket",
        "vr.90-soul-pillar",
        "vr.91-portal",
        "vr.93-summit-flare",
        "vr.93b-summit-countdown",
        "vr.86-end-screen",
        "vr.86b-no-flat-screens",
    };

    IEnumerator EndOfRunSteps()
    {
        if (!VRRun || !Ready || Singleton<MapHandler>.Instance == null)
            yield break;
        // These win the run, light a real campfire, summon the helicopter and visit the summit: never without the
        // self-test's progress guard, unless the user opts in to granting real progress.
        if (
            !SelfTestProgressGuard.Active
            && Array.IndexOf(Environment.GetCommandLineArgs(), EndRunFlag) < 0
        )
        {
            foreach (var id in EndOfRunIds)
                Skip(id, $"needs {EndRunFlag} (grants real progress)");
            yield break;
        }
        flatScreens.Clear();
        watchFlat = true;
        StartCoroutine(WatchFlatScreens());
        yield return HandStep("vr.85-campfire-segment", 240f, CampfireSegmentStep());
        yield return WarpBack();
        yield return Run("vr.89-to-peak", 150f, Jump(Segment.Peak, "vr-89-peak"));
        // The summit steps start from the Peak itself (PeakHandStep): the shared settle would warp them to TheKiln's
        // home spot, the base camp 375 m below, where the body crept 2 cm/s off the spot in vr46 (vr.90 "not settled",
        // net drift at the 3 cm limit after the warp; vr.91's settle there needed a second warp).
        yield return PeakHandStep("vr.89-altar-full", 40f, AltarFullStep());
        yield return PeakHandStep("vr.89b-amulet-socket", 120f, AmuletSocketStep());
        // Without the scene object there is nothing to settle or jump for: skip before the setup.
        if (Object.FindFirstObjectByType<ScoutmasterSoulPillar>() == null)
            Skip("vr.90-soul-pillar", NoPillar());
        else
            yield return PeakHandStep("vr.90-soul-pillar", 50f, PillarStep());
        if (!PortalActive(out _))
            Skip("vr.91-portal", NoPortal());
        else
            yield return PeakHandStep("vr.91-portal", 50f, PortalStep());
        yield return PeakHandStep("vr.93-summit-flare", 180f, FlareStep());
        yield return Run("vr.93b-summit-countdown", 40f, SummitCountdownStep());
        yield return PeakHandStep("vr.86-end-screen", 420f, EndScreenStep());
        watchFlat = false;
        yield return Run("vr.86b-no-flat-screens", 5f, FlatScreensVerdict());
    }

    /// <summary>
    /// A scripted-hands step on the summit (<see cref="HandStep"/> without its settle): the Peak sits in TheKiln's
    /// segment slot, so the shared settle warps a body that strayed back to TheKiln's home spot, 375 m down (vr38:
    /// vr.93 and vr.86 ran there, the latter ungrounded). Jumps to the Peak instead, whose landing the jump itself
    /// checks, then runs <paramref name="body"/> with the usual teardown.
    /// </summary>
    IEnumerator PeakHandStep(string id, float timeout, IEnumerator body) =>
        ScriptedHandsStep(id, timeout, PeakSetup(id, body));

    IEnumerator PeakSetup(string id, IEnumerator body)
    {
        Recover();
        ClosePause();
        yield return DropHeld();
        if (!stepOk)
            yield break;
        yield return Jump(Segment.Peak, id.Replace('.', '-') + "-peak");
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        EmptyHotbar(c);
        SnapshotItems();
        BeginHands();
        yield return Wait(0.3f);
        yield return body;
    }

    readonly HashSet<string> flatScreens = new(StringComparer.Ordinal);
    bool watchFlat;

    /// <summary>
    /// Watches the end-of-run slot for screens that are not placed in the world: in VR every menu and screen is a
    /// world panel or the view is in the 3D scene. Twice a second, while <see cref="watchFlat"/>: the cinema screen
    /// showing a camera feed though comfort.events puts the view in the cutscene (the credits did, user report); a
    /// screen-space canvas ui.world should have placed (not its mirror-only Hidden or Fade kinds) visible for over
    /// 1 s; and a RawImage of a RenderTexture covering most of a screen-space canvas.
    /// </summary>
    IEnumerator WatchFlatScreens()
    {
        var since = new Dictionary<int, float>();
        while (watchFlat)
        {
            if (VRSession.Active)
                Safe("world.flat-scan", () => ScanFlat(since));
            yield return Wait(0.5f);
        }
    }

    void ScanFlat(Dictionary<int, float> since)
    {
        var where = SceneManager.GetActiveScene().name;
        if (
            RunFlowRuntime.CinemaShowing
            && PeakXR.Comfort.ComfortEventsConfig.Cutscene != PeakXR.Comfort.CutsceneView.Screen
        )
            flatScreens.Add($"cinema screen showing a camera feed ({where})");
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            int id = canvas.GetInstanceID();
            // A screen-space-camera canvas whose camera draws into a texture is not on screen: the texture is shown
            // elsewhere (Canvas_Hero draws the biome title through heroCamera into heroImage, GUIManager.cs:144-160,
            // which ui.spatial places as a world sign). That texture's RawImage is checked below where it is shown.
            if (
                !canvas.isActiveAndEnabled
                || !canvas.isRootCanvas
                || canvas.renderMode == RenderMode.WorldSpace
                || (
                    canvas.renderMode == RenderMode.ScreenSpaceCamera
                    && canvas.worldCamera != null
                    && canvas.worldCamera.targetTexture != null
                )
            )
            {
                since.Remove(id);
                continue;
            }
            var kind = CanvasRules.Classify(
                canvas,
                canvas.GetComponentInChildren<Selectable>() != null,
                false,
                out _
            );
            if (kind is CanvasKind.Hidden or CanvasKind.Fade || !Drawn(canvas))
            {
                since.Remove(id);
                continue;
            }
            if (!since.TryGetValue(id, out float first))
                since[id] = Now;
            else if (Now - first > 1f)
                flatScreens.Add(
                    $"'{canvas.name}' {canvas.renderMode} ({kind}, camera {(canvas.worldCamera != null ? canvas.worldCamera.name : "none")}) in {where}"
                );
            foreach (var raw in canvas.GetComponentsInChildren<RawImage>())
            {
                if (!raw.isActiveAndEnabled || raw.texture is not RenderTexture)
                    continue;
                var canvasRect = ((RectTransform)canvas.transform).rect;
                var rect = raw.rectTransform.rect;
                if (rect.width * rect.height >= 0.6f * canvasRect.width * canvasRect.height)
                    flatScreens.Add($"camera feed '{raw.name}' on '{canvas.name}' in {where}");
            }
        }
    }

    /// <summary>Some graphic of <paramref name="canvas"/> actually draws (enabled, not faded out).</summary>
    static bool Drawn(Canvas canvas)
    {
        foreach (var g in canvas.GetComponentsInChildren<Graphic>())
        {
            if (
                g.isActiveAndEnabled
                && g.color.a > 0.01f
                && g.canvasRenderer.GetInheritedAlpha() > 0.01f
            )
                return true;
        }
        return false;
    }

    /// <summary>vr.86b: nothing in the end-of-run slot was a flat screen (see <see cref="WatchFlatScreens"/>).</summary>
    IEnumerator FlatScreensVerdict()
    {
        yield return null;
        if (flatScreens.Count > 0)
            Fail("flat screens in VR: " + string.Join("; ", flatScreens));
        else
            Note("no flat screen, overlay or camera feed during the end-of-run slot");
    }

    /// <summary>
    /// vr.85: a bare-hand grip on the next unlit campfire with everyone in range lights it for real
    /// (Campfire.cs:327-338 <c>Light_Rpc(true)</c>, :473-508): the segment advances (<c>MapHandler.GoToSegment</c>,
    /// MapHandler.cs:680) under ui.runflow's fade, and no refusal cue fires.
    /// </summary>
    IEnumerator CampfireSegmentStep()
    {
        var map = Singleton<MapHandler>.Instance;
        var c = Character.localCharacter;
        var from = map.GetCurrentSegment();
        // The end slot starts in TheKiln's slot, which has no fire ahead (vr40): go back to the Alpine, whose end
        // fire advances to the Caldera.
        if (!HasFireAhead(from))
        {
            yield return Jump(Segment.Alpine, "vr-85-alpine");
            if (!stepOk)
                yield break;
            from = map.GetCurrentSegment();
        }
        var ahead = from;
        // The fire that advances past this segment; the segment loop's jumps light every fire behind them, so it is
        // usually lit by now and gets set back to unlit locally first (UnlitCampfire). It is then lit for real.
        var fire = UnlitCampfire(f => f.advanceToSegment > ahead, out var was);
        if (fire == null)
        {
            stepSkip = "no campfire ahead of " + from;
            yield break;
        }
        stepNote = $"{fire.name} was {was}";
        yield return ApproachFire(ItemGestures.FireBase(fire));
        SelfTestCampfirePatch.Hold = false;
        bool left = !VRInput.LeftHanded;
        yield return GripLogs(left, fire);
        if (!stepOk)
        {
            RestoreFire(fire, was);
            yield break;
        }
        // An unlit fire is lit by holding interact on it (Campfire.cs:321-337: Off, everyone in range, Light_Rpc with
        // the segment advance; IsInteractible is true while Off, :353-357). With no log under the grip sphere the grip
        // is the head-aimed interact (run 62: candidate None, the fire not looked at), so look at the logs as a player
        // does when reaching for them.
        LookAtPoint(fire.Center());
        yield return Wait(0.3f);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        float eyeHand = Vector3.Distance(
            VRRig.Instance.Head.position,
            VRRig.Instance.Hand(left).position
        );
        yield return Until(
            () => fire.Lit,
            GameTiming.Budget(GameTiming.InteractSeconds(fire, c), 3f),
            "the grip hold did not light " + fire.name + "; " + PhysicalInteraction.LastGripNote
        );
        yield return GripTo(left, 0f, 0.1f);
        // Not lit: put back what the local reset changed.
        RestoreFire(fire, was);
        if (!stepOk)
            yield break;
        if (CueLog.Capture.Fired(left, DenyReason.FriendsFar, mark, out _))
        {
            Fail("FriendsFar fired with everyone in range");
            yield break;
        }
        // Any refusal on the lighting hand is wrong, not only FriendsFar (notices are fine).
        var grip = CueLog.Capture.Hand(left);
        if (grip.ReasonAt >= mark && !grip.Notice)
        {
            Fail($"{grip.Reason} refused the grip that lit {fire.name}");
            yield break;
        }
        // The segment index moves at the start of the game's swap coroutine, but the view swaps only after the fog
        // catches up plus 1 s (MapHandler.cs:578-597), and run flow fades for that moment: watch the fade through
        // its whole arm-and-hold (vr41 stopped looking when the index moved and saw no fade). The drawn fade
        // (FadeView.Alpha), not the requested target, and only once the swap armed, so a leftover warp fade from
        // the approach cannot pass for it.
        float fade = 0f;
        float target = 0f;
        bool armed = false;
        for (float end = Now + 3f; Now < end && !armed; )
        {
            armed = RunFlowRuntime.SegmentPending;
            yield return null;
        }
        for (float until = Now + 60f; Now < until && RunFlowRuntime.SegmentPending; )
        {
            fade = Mathf.Max(fade, FadeView.Alpha);
            target = Mathf.Max(target, VRFade.Target);
            yield return null;
        }
        for (float t = Now + 1f; armed && Now < t; )
        {
            fade = Mathf.Max(fade, FadeView.Alpha);
            target = Mathf.Max(target, VRFade.Target);
            yield return null;
        }
        if (!armed && Setting("UI.RunFlow", "FadeOnSegmentChange", true))
        {
            Fail($"run flow never armed its segment fade for {fire.name}");
            yield break;
        }
        var note =
            $"{fire.name} (was {was}): {from} -> {map.GetCurrentSegment()}, fade peak {fade:0.00} drawn ({target:0.00} asked), eye-hand {eyeHand:0.00} m at the grip, stooped {headDrop:0.00} m";
        if (map.GetCurrentSegment() <= from)
            Fail("segment did not advance: " + note);
        else if (Setting("UI.RunFlow", "FadeOnSegmentChange", true) && fade < 0.5f)
            Fail("no fade over the segment swap: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.89b: with an amulet in one hand, the free hand pointed at the altar socket it would fill (<see
    /// cref="AltarSocket"/>, the slot order of RPC_InsertAmulet, Peak/ScoutStatue.cs:172-185) from within arm's reach
    /// (reach guard: 1 m from the eye) hovers the statue within vanilla's 2 m interact reach and its grip runs the
    /// statue's hold with the progress ramp until the amulet goes in. The altar is emptied locally and restored.
    /// Peak only.
    /// </summary>
    IEnumerator AmuletSocketStep()
    {
        if (!Setting("Interaction", "AmuletInSocket", true))
        {
            stepSkip = "Interaction.AmuletInSocket is off";
            yield break;
        }
        var statue = Object.FindFirstObjectByType<ScoutStatue>();
        var slots =
            statue != null ? GameMember.Field<ScoutStatue, int[]>("hasAmulets")(statue) : null;
        AmuletBase amulet = null;
        // An amulet the altar takes: the ScoutAmulet tag (ScoutStatue.cs:288-295) and a socket index
        // (RPC_InsertAmulet ignores any other, :172-175).
        foreach (var it in SandboxActions.Items)
        {
            if (
                slots != null
                && it != null
                && (it.itemTags & Item.ItemTags.ScoutAmulet) != 0
                && it.TryGetComponent(out AmuletBase a)
                && a.amuletIndex >= 0
                && a.amuletIndex < slots.Length
            )
            {
                amulet = a;
                break;
            }
        }
        if (statue == null || amulet == null || statue.amuletObjects == null)
        {
            stepSkip =
                statue == null
                    ? "no scout statue loaded (Peak only)"
                    : "no amulet with a socket index";
            yield break;
        }
        var saved = (int[])slots.Clone();
        try
        {
            // Two stands: 1.3 m back, clear of the statue's rock, and 0.7 m back against it, where a pointed grip must
            // still insert rather than climb (GripContext.PointedAtTaker; vr50's grip there pressed nothing).
            var notes = new List<string>();
            foreach (float back in new[] { 1.3f, 0.7f })
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = -1;
                yield return DropHeld();
                if (!stepOk)
                    yield break;
                yield return PointInsert(statue, amulet, back, notes);
                if (!stepOk)
                    yield break;
            }
            stepNote = string.Join(" | ", notes);
        }
        finally
        {
            Array.Copy(saved, slots, slots.Length);
        }
    }

    /// <summary>One vr.89b stand: <paramref name="back"/> metres from the socket's foot, the amulet in one hand, the
    /// free hand pointed at the socket from within arm's reach, gripping until the statue's hold puts it in.</summary>
    IEnumerator PointInsert(ScoutStatue statue, AmuletBase amulet, float back, List<string> notes)
    {
        // Stand beside the socket it will fill (an empty altar: its own type's, AltarSocket), not the statue's
        // root: vr34 stood by the root and ended 2.16 m short.
        var target = WorldHolds.SocketCenter(statue.amuletObjects[amulet.amuletIndex]);
        // The player draws the amulet with the grip already closed, never closing a hand fresh beside the statue, whose
        // body falls in that hand's grab sphere (vr72: the amulet hand's grip resolved to GrabInteractible). So the
        // amulet is given and gripped 2.5 m out, aimed at the ground with the head looking down (a scripted grip reads
        // as a point, GripIntent.IsPointing, vr69), and carried in.
        yield return WarpBeside(target, 2.5f);
        var me = Character.localCharacter;
        yield return Landed(me);
        yield return GiveAndHold(amulet.GetComponent<Item>());
        if (!stepOk)
            yield break;
        var held = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        HeadFrame(out _, out var level, out _);
        Script(left).Rotation = HandLook(Vector3.down, level);
        LookAtPoint(Feet(me));
        yield return Wait(0.3f);
        int holdsAtGive = WorldHolds.StatueHolds;
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.2f);
        yield return WarpBeside(target, back);
        // Landed and still before the point (vr69 failed with the torso still moving after the warp).
        yield return Landed(me);
        if (WorldHolds.StatueHolds > holdsAtGive || !ReferenceEquals(HeldItem(), held))
        {
            Fail(
                $"the amulet hand's own grip started the statue's hold before the point ({PhysicalInteraction.LastGripNote})"
            );
            yield break;
        }
        yield return Until(
            () => WorldHolds.SocketTarget >= 0,
            2f,
            $"no socket picked for {amulet.name} (index {amulet.amuletIndex}, statue enabled {statue.isActiveAndEnabled}, {statue.amuletObjects.Length} sockets)"
        );
        if (!stepOk)
            yield break;
        var socketAt = WorldHolds.SocketCenter(statue.amuletObjects[WorldHolds.SocketTarget]);
        float socketUp = socketAt.y - Feet(Character.localCharacter).y;
        var rig = VRRig.Instance;
        var interaction = Interaction.instance;
        // The player path: the socket sits past a hand's reach (2.7 m up, vr36), so as vanilla's camera ray takes
        // the statue within Interaction.distance (field default 2 m, Interaction.cs:7, :203; 2.5 m in the scene),
        // the free hand points at it, held out at shoulder height clear of the body's slots, and grips
        // (interact.physical's hand-aimed interact). The hand never leaves arm's reach of the eye.
        bool free = !left;
        HeadFrame(out var head, out var forward, out var right);
        var handAt = head + forward * 0.45f + (free ? -right : right) * 0.1f + Vector3.down * 0.15f;
        // Aimed a little into the statue from the socket, so the ray rests on its body, not on the socket's edge,
        // where a ray grazing off it for one frame drops the game's hold (Interaction.cs:128-132).
        var into = Flat(statue.transform.position - socketAt);
        var aimAt = socketAt + (into.sqrMagnitude > 1e-4f ? into.normalized * 0.15f : Vector3.zero);
        void Aim()
        {
            var from = rig.Origin.TransformPoint(Script(free).Position);
            Script(free).Rotation = HandLook(
                rig.Origin.InverseTransformDirection(aimAt - from),
                Vector3.up
            );
        }
        // A deliberate point: index off the trigger, thumb resting on the stick. A lifted thumb is the finger gun
        // (GripIntent.IsFingerGun), whose interact waits for a possible ping.
        Script(free).TriggerTouch = false;
        Script(free).StickTouch = true;
        Script(free).Trigger = 0f;
        yield return MoveTrack(free, handAt, 0.4f);
        Aim();
        yield return Wait(0.4f);
        float fromEye = Vector3.Distance(rig.Hand(free).position, rig.Head.position);
        float camToSocket = Vector3.Distance(MainCamera.instance.transform.position, socketAt);
        var where =
            $"socket {socketUp:0.00} m over the feet, {camToSocket:0.00} m from the camera (vanilla reach "
            + $"{interaction.distance:0.0} m); pointing hand {fromEye:0.00} m from the eye";
        if (fromEye > 1f)
        {
            Fail("reach guard: the pointing hand is out of arm's reach: " + where);
            yield break;
        }
        // The pointing pose is the grip itself (middle and ring closed, index off the trigger: GripIntent.IsPointing),
        // so before the grip nothing aims from the hand and the hover is the head's: no gate here (vr57 failed one at
        // 2.1 m). The player looks where they point, then grips; the hover is checked once the hand aims.
        LookAtPoint(socketAt);
        yield return Wait(0.3f);
        float progress = 0f;
        float mark = CueMark();
        // Counted by the mod as each hold starts: the statue's own hold can be short enough to start and finish
        // inside the grip's ramp, before the loop below samples it (vr60: held at the grip, done 0.3 s on).
        int holdsBefore = WorldHolds.StatueHolds;
        yield return GripTo(free, 1f, 0.1f);
        yield return Wait(0.15f);
        var hoveredAtGrip = interaction.currentHovered;
        float end =
            Now
            + GameTiming.Budget(GameTiming.InteractSeconds(statue, Character.localCharacter), 3f);
        while (Now < end && held != null && ReferenceEquals(HeldItem(), held))
        {
            Aim();
            if (ReferenceEquals(interaction.currentHeldInteractible, statue))
                progress = Mathf.Max(progress, interaction.constantInteractableProgress);
            yield return null;
        }
        yield return GripTo(free, 0f, 0.1f);
        Script(free).TriggerTouch = true;
        Script(free).StickTouch = false;
        // The amulet going in is the outcome (ConsumeDelayed takes it from the hand, Peak/ScoutStatue.cs:308); a hold
        // that started but ended with the amulet still in hand was dropped, which is a failure too.
        bool inserted = !ReferenceEquals(HeldItem(), held);
        if (!inserted && WorldHolds.StatueHolds > holdsBefore)
        {
            Fail(
                $"the statue's hold started at {back:0.0} m back but ended without the amulet going in (hold {progress:0.00}; {PhysicalInteraction.LastGripNote}): {where}"
            );
            yield break;
        }
        if (!inserted)
        {
            Fail(
                $"the pointed grip did not start the statue's hold at {back:0.0} m back ({PhysicalInteraction.LastGripNote}; hovered at the grip {Label(hoveredAtGrip)}, aim hand {PhysicalInteraction.AimHand}): {where}"
            );
            yield break;
        }
        // The ramp belongs on the gripping (pointing) hand. Since the mark, so the whole hold counts; 1 s more for a
        // hold that ended as the loop did.
        if (
            !CueLog.Capture.Felt(free, CueFeel.Progress, mark, out _)
            && CueLog.Capture.Felt(!free, CueFeel.Progress, mark, out _)
        )
        {
            Fail(
                $"the hold's progress ramp went to the amulet hand, not the pointing one: {where}"
            );
            yield break;
        }
        // A hold over in a few frames may give the contact tick of its start and no ramp tick: either is the
        // press felt on the pointing hand.
        if (CueLog.Capture.Felt(free, CueFeel.Contact, mark, out var contact))
            cueNote = contact;
        else
            yield return ExpectFeel(free, CueFeel.Progress, mark, within: CueMark() - mark + 1f);
        if (stepOk)
            notes.Add(
                $"{back:0.0} m back, pointed at socket {WorldHolds.SocketTarget}: hold {progress:0.00}, amulet {(ReferenceEquals(HeldItem(), held) ? "still held" : "inserted")}; {where}; {cueNote}"
            );
    }

    static IEnumerator Landed(Character me) =>
        WaitFor(() => me.data.isGrounded && me.data.avarageVelocity.sqrMagnitude < 0.04f, 3f);

    /// <summary>Where a player holds an amulet up to <paramref name="socketAt"/>: at the socket when it is within
    /// reach, else raised over the head straight under it (AltarSocket.Reach); the gesture reads the tracked hand,
    /// so the scripted hand goes there.</summary>
    static Vector3 AmuletOffer(Vector3 socketAt)
    {
        float headY = VRRig.Instance.Head.position.y;
        return socketAt.y <= headY + AltarSocket.RaisedReach
            ? socketAt
            : new Vector3(socketAt.x, headY + 0.25f, socketAt.z);
    }

    /// <summary>vr.90: both open palms laid on the soul pillar (ScoutmasterSoulPillar.cs:51-106) start its 2 s hold
    /// with the progress ramp on both hands; taking them off cancels before it breaks. Nadir only.</summary>
    IEnumerator PillarStep()
    {
        var pillar = Object.FindFirstObjectByType<ScoutmasterSoulPillar>();
        if (pillar == null || !Setting("Interaction", "PalmsOnPillar", true))
        {
            stepSkip = pillar == null ? NoPillar() : "Interaction.PalmsOnPillar is off";
            yield break;
        }
        var col =
            pillar.colliderToDisable != null
                ? pillar.colliderToDisable
                : pillar.GetComponentInChildren<Collider>();
        yield return WarpFacing(col.bounds.center, 0.6f);
        yield return PressPalms(col, pillar);
    }

    static string NoPillar() =>
        "no soul pillar loaded: it is a Nadir scene object (Segment.Void, ascent 8+ runs, Ascents.cs:48) with no prefab to place offline; "
        + NadirReach<ScoutmasterSoulPillar>();

    static string NoPortal() =>
        "no active summit portal: a scene object only the level switches on (no code enables PeakGatePortal) for the Nadir route, with no prefab to place offline; "
        + NadirReach<PeakGatePortal>();

    /// <summary>An active summit portal with a collider, the one <see cref="PortalStep"/> pushes a hand into.</summary>
    static bool PortalActive(out PeakGatePortal portal)
    {
        portal = Object.FindFirstObjectByType<PeakGatePortal>();
        return portal != null
            && portal.isActiveAndEnabled
            && portal.GetComponentInChildren<Collider>() != null;
    }

    /// <summary>For a Nadir-only SKIP: whether an inactive <typeparamref name="T"/> is in the loaded scene and whether
    /// the level has the Nadir (<c>VoidBiome.instance</c>, which <c>JumpToSegment(Void)</c> activates,
    /// MapHandler.cs:997-1000), so a later run can tell if a jump there would reach it.</summary>
    static string NadirReach<T>()
        where T : Object
    {
        var inactive = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return $"inactive one in the scene {(inactive != null ? inactive.name : "none")}, Nadir in the level {VoidBiome.instance != null}";
    }

    /// <summary>Both scripted hands open, palms flat on <paramref name="col"/> at chest height a shoulder apart,
    /// until the game's hold on <paramref name="target"/> passes 0.3; then they come off.</summary>
    IEnumerator PressPalms(Collider col, IInteractible target)
    {
        var rig = VRRig.Instance;
        HeadFrame(out var head, out _, out var right);
        var interaction = Interaction.instance;
        float mark = CueMark();
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            var probe = head + Vector3.down * 0.35f + (left ? -right : right) * 0.2f;
            var surface = col.bounds.ClosestPoint(probe);
            var into = Flat(surface - head);
            // Palm normal: +X of the left grip pose, -X of the right (HandRig.cs:62).
            var x = left ? into : -into;
            var world = HandLook(Vector3.Cross(x, Vector3.up), Vector3.up);
            Script(left).Grip = 0f;
            Script(left).Rotation = Quaternion.Inverse(rig.Origin.rotation) * world;
            yield return MoveTo(left, () => surface - into * 0.02f, 0.4f);
        }
        float progress = 0f;
        float end = Now + 2f;
        while (Now < end && progress < 0.3f)
        {
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, target))
                progress = interaction.constantInteractableProgress;
            yield return null;
        }
        HeadFrame(out var back, out var forward, out _);
        SetHand(
            true,
            rig.Origin.InverseTransformPoint(back - forward * 0.1f + Vector3.down * 0.4f)
        );
        SetHand(
            false,
            rig.Origin.InverseTransformPoint(back - forward * 0.1f + Vector3.down * 0.4f)
        );
        if (progress <= 0f)
        {
            Fail("palms on it did not start the hold");
            yield break;
        }
        yield return ExpectFeel(true, CueFeel.Progress, mark, within: 0f);
        if (stepOk)
            stepNote = $"hold {progress:0.00}; {cueNote}";
    }

    /// <summary>vr.91: a hand pushed into the summit portal (Peak/PeakGatePortal.cs:28-128) runs its hold with the
    /// progress ramp and ui.runflow's fade; pulled out at 0.3, the hold cancels and the run goes on.</summary>
    IEnumerator PortalStep()
    {
        bool active = PortalActive(out var portal);
        if (!active || !Setting("Interaction", "HandIntoPortal", true))
        {
            stepSkip = !active ? NoPortal() : "Interaction.HandIntoPortal is off";
            yield break;
        }
        var col = portal.GetComponentInChildren<Collider>();
        yield return WarpFacing(col.bounds.center, 0.6f);
        bool left = !VRInput.LeftHanded;
        var into = col.bounds.center;
        var interaction = Interaction.instance;
        float mark = CueMark();
        float fade = 0f;
        yield return MoveTo(left, () => into, 0.4f);
        float progress = 0f;
        float end = Now + 2f;
        while (Now < end && progress < 0.3f)
        {
            if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, portal))
                progress = interaction.constantInteractableProgress;
            fade = Mathf.Max(fade, VRFade.Target);
            yield return null;
        }
        HeadFrame(out var head, out var forward, out _);
        SetHand(
            left,
            rig().Origin.InverseTransformPoint(head - forward * 0.1f + Vector3.down * 0.4f)
        );
        yield return Wait(0.5f);
        if (progress <= 0f)
        {
            Fail("a hand in the portal did not start the hold");
            yield break;
        }
        if (Character.localCharacter.data.dead || LoadingScreenHandler.loading)
        {
            Fail($"the run ended from a hold pulled out at {progress:0.00}");
            yield break;
        }
        yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
        if (stepOk)
            stepNote = $"hold {progress:0.00}, fade peak {fade:0.00}; {cueNote}";

        static VRRig rig() => VRRig.Instance;
    }

    /// <summary>
    /// vr.93: a lit flare held at the peak summons the helicopter by itself (Flare.Update, Flare.cs:36-39: holder
    /// <c>IsAtPeak</c>, MountainProgressHandler.cs:198-205, and no Nadir required, Ascents.cs:48), and its
    /// TriggerHelicopter (PeakHandler.SummonHelicopter :58-62) gives the notice at the flare. The body first stands on
    /// the summit just past the last progress point (<see cref="SummitSide"/>): the Peak jump lands short of it (vr43
    /// to vr46 "not at the peak"). Only when the flare's own call still does not come is it sent, as a named shortcut.
    /// Afterwards the body steps back off the peak, so the summit countdown cannot end the run before vr.86.
    /// </summary>
    IEnumerator FlareStep()
    {
        var flare = FindItemComponent<Flare>();
        var peak = Singleton<PeakHandler>.Instance;
        if (flare == null || peak == null || peak.summonedHelicopter)
        {
            stepSkip =
                flare == null ? "no flare item"
                : peak == null ? "no PeakHandler"
                : "helicopter already summoned";
            yield break;
        }
        yield return SummitSide(past: true);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var progress = Singleton<MountainProgressHandler>.Instance;
        yield return GiveAndHold(flare.GetComponent<Item>());
        if (!stepOk)
            yield break;
        var held = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        int before = WorldHolds.Notices;
        float mark = CueMark();
        Script(left).Trigger = 1f;
        yield return WaitFor(
            () => HandLights.IsLit(held),
            GameTiming.Budget(GameTiming.UseSeconds(held))
        );
        Script(left).Trigger = 0f;
        yield return WaitFor(() => peak.summonedHelicopter, 2f);
        string sent = null;
        // The flare in hand now: a given trackable item can be replaced by its reconnected copy (vr38: "Reconnected
        // to trackable object ... timed out. Destroying"), leaving the first reference dead.
        var flareNow = HeldItem() != null ? HeldItem().GetComponent<Flare>() : null;
        if (!peak.summonedHelicopter && flareNow != null)
        {
            sent = F(
                $"shortcut: the flare's TriggerHelicopter sent by the step (holder at the peak {progress.IsAtPeak(c.Center)}, Nadir required {Ascents.shouldRequireNadir}, lit {HandLights.IsLit(HeldItem())})"
            );
            flareNow.item.photonView.RPC("TriggerHelicopter", Photon.Pun.RpcTarget.AllBuffered);
        }
        yield return Until(
            () => WorldHolds.Notices > before,
            3f,
            $"no notice for the summon (helicopter summoned {peak.summonedHelicopter}, flare in hand {(flareNow != null ? flareNow.name : "none")}, {sent ?? "summoned by the flare"})"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(left, CueFeel.Contact, mark, within: 0f);
        var feel = cueNote;
        if (stepOk && CueLog.Capture.LastSound(CueSound.Notice).At < mark)
            Fail("no notice note at the flare");
        yield return SummitSide(past: false);
        if (stepOk)
            stepNote = (sent ?? "summoned by the flare held at the peak") + "; " + feel;
    }

    /// <summary>Metres past (or short of) the last progress point's z that <see cref="SummitSide"/> tries.</summary>
    static readonly float[] SummitSteps = { 2f, 4f, 7f };

    /// <summary>
    /// Warps onto summit ground just past (<paramref name="past"/>) or short of the last progress point in z, the line
    /// the game's <c>IsAtPeak</c> draws (MountainProgressHandler.cs:198-205), and checks the body is on that side.
    /// Ground more than 3 m off the height of the ground on the line is a ledge or the void and is not used. Probed
    /// along the body's x first, where the Peak landing stands on summit ground, then along the point's own x: 2-7 m
    /// past the point at its x 0 no ground lay within 3 m while the landing stood grounded at x 6.1 (vr.93, run17).
    /// </summary>
    IEnumerator SummitSide(bool past)
    {
        var progress = Singleton<MountainProgressHandler>.Instance;
        var points = progress != null ? progress.progressPoints : null;
        var c = Character.localCharacter;
        if (points == null || points.Length == 0 || points[points.Length - 1] == null)
        {
            Fail("no progress points: IsAtPeak cannot be met");
            yield break;
        }
        var last = points[points.Length - 1].transform.position;
        var tried = new List<string>();
        foreach (float x in new[] { c.Center.x, last.x })
        {
            if (!GroundUnder(new Vector3(x, last.y, last.z), out var summit))
            {
                tried.Add(F($"x {x:0.0}: no ground on the line"));
                continue;
            }
            foreach (float dz in SummitSteps)
            {
                var probe = new Vector3(x, last.y, last.z + (past ? dz : -dz));
                bool hit = GroundUnder(probe, out var ground);
                if (!hit || Mathf.Abs(ground.y - summit.y) > 3f)
                {
                    tried.Add(
                        hit
                            ? F($"x {x:0.0} z {probe.z:0.0}: {ground.y - summit.y:+0.0;-0.0} m")
                            : F($"x {x:0.0} z {probe.z:0.0}: no ground")
                    );
                    continue;
                }
                yield return WarpTo(ground);
                if (progress.IsAtPeak(c.Center) == past)
                    yield break;
                tried.Add(F($"x {x:0.0} z {probe.z:0.0}: settled at z {c.Center.z:0.00}"));
            }
        }
        Fail(
            F(
                $"no summit ground {(past ? "past" : "short of")} the last progress point {last} (body at {c.Center}, at the peak {progress.IsAtPeak(c.Center)}; tried {string.Join(", ", tried)})"
            )
        );
    }

    /// <summary>Terrain or map ground straight under <paramref name="p"/>, cast from 200 m above it.</summary>
    static bool GroundUnder(Vector3 p, out Vector3 ground)
    {
        bool hit = Terrain(p + Vector3.up * 200f, Vector3.down, 1000f, out var info);
        ground = hit ? info.point : p;
        return hit;
    }

    /// <summary>
    /// vr.93b: the summit countdown's last ten seconds tick on the wrist hand (interact.world's SummitCountdown on
    /// <c>PeakSequence.RPCUpdateTimer</c>, PeakSequence.cs:166). Driven by the game itself: with the helicopter
    /// summoned (vr.93) and every player at the peak (<c>Character.CheckWinCondition</c>, Character.cs:943-958) the
    /// countdown jumps to its last <c>totalWinningSeconds</c> and ticks once per <c>lengthOfASecond</c>
    /// (PeakSequence.cs:98-150). The counter must show a value of 10 or less with a contact tick on the wrist hand;
    /// then the body steps off the peak before the count ends the run (EndGame at :144-148) and the counter must hide.
    /// </summary>
    IEnumerator SummitCountdownStep()
    {
        var peak = Singleton<PeakHandler>.Instance;
        var endgame = GUIManager.instance != null ? GUIManager.instance.endgame : null;
        var seq =
            peak != null && peak.peakSequence != null
                ? peak.peakSequence.GetComponent<PeakSequence>()
                : null;
        if (!VRSession.Active || peak == null || !peak.summonedHelicopter || endgame == null)
        {
            stepSkip =
                !VRSession.Active ? "VR not active"
                : peak == null || !peak.summonedHelicopter
                    ? "the helicopter was not summoned (vr.93)"
                : "no endgame counter";
            yield break;
        }
        if (seq == null || !seq.isActiveAndEnabled)
        {
            Fail("the summit sequence is not running after the summon");
            yield break;
        }
        var ui = PeakXR.UI.World.UIWorldFeature.Settings;
        bool wrist = ui == null || ui.WristLeft;
        Recover();
        yield return SummitSide(past: true);
        if (!stepOk)
            yield break;
        float mark = CueMark();
        int shown = -1;
        yield return WaitFor(
            () =>
                endgame.counterGroup.gameObject.activeSelf
                && int.TryParse(
                    endgame.counter.text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out shown
                )
                && shown > 0,
            GameTiming.Budget(seq.timeToWait + 2f * seq.lengthOfASecond, 2f)
        );
        bool counting = endgame.counterGroup.gameObject.activeSelf && shown > 0;
        // Off the peak at once: the count would end the run by itself a few seconds on.
        yield return SummitSide(past: false);
        if (!stepOk)
            yield break;
        if (!counting)
        {
            Fail(
                F(
                    $"the summit countdown never showed (at the peak {Singleton<MountainProgressHandler>.Instance.IsAtPeak(Character.localCharacter.Center)}, waited {seq.timeToWait:0} s plus 2 counts)"
                )
            );
            yield break;
        }
        if (shown > 10)
        {
            Fail($"the countdown showed {shown}, not its last ten seconds");
            yield break;
        }
        yield return ExpectFeel(wrist, CueFeel.Contact, mark, within: 0f);
        if (!stepOk)
            yield break;
        var tick = cueNote;
        yield return WaitFor(() => !endgame.counterGroup.gameObject.activeSelf, 3f);
        if (endgame.counterGroup.gameObject.activeSelf)
            Fail("the countdown kept running after the body left the peak");
        else
            stepNote =
                $"counter at {shown}, {(wrist ? "left" : "right")} wrist hand {tick}; stopped off the peak";
    }

    /// <summary>
    /// vr.86: the run won (<c>RPCEndGame_ForceWin</c>, Character.cs:882) plays the end cutscene, then the end screen
    /// (EndScreen.cs:293-354). Next pressed by the laser (:286) starts the credits; a press in their first 20 s gets
    /// TooSoon, the moment skipping works both hands get a contact tick, and the next press returns to the airport
    /// (PeakHandler.cs:223-257). Last step of the run.
    /// </summary>
    IEnumerator EndScreenStep()
    {
        var c = Character.localCharacter;
        var end = GUIManager.instance != null ? GUIManager.instance.endScreen : null;
        if (end == null || end.nextButton == null)
        {
            Fail("no end screen");
            yield break;
        }
        c.photonView.RPC("RPCEndGame_ForceWin", Photon.Pun.RpcTarget.All);
        // The report holds on each unlock popup (a cosmetic, new ascents, a promotion) until its own Next is pressed
        // (EndScreen.cs:598-641, PopupNext :573); a forced win unlocks some, and vr34 waited on one for 150 s.
        int popups = 0;
        float until = Now + 150f;
        while (Now < until && !Usable(end.nextButton))
        {
            var popup =
                Usable(end.cosmeticNextButton) ? end.cosmeticNextButton
                : Usable(end.ascentsNextButton) ? end.ascentsNextButton
                : Usable(end.promotionNextButton) ? end.promotionNextButton
                : null;
            if (popup == null)
            {
                yield return null;
                continue;
            }
            // Let the popup's panel settle before aiming at it.
            yield return Wait(0.8f);
            yield return LaserPress(popup);
            popups++;
            yield return Wait(0.6f);
        }
        if (!Usable(end.nextButton))
        {
            Fail($"the end screen's Next did not show ({popups} unlock popups pressed)");
            yield break;
        }
        yield return Wait(1f);
        yield return Shot("vr-86-end-screen");
        // Up to three presses: the panel may still be settling into place on the first (ui.world placement).
        string pressed = "nothing";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            yield return LaserPress(end.nextButton);
            pressed = LaserPointer.Over != null ? LaserPointer.Over.name : "nothing";
            yield return WaitFor(
                () => RunFlowRuntime.CreditsWaiting || LoadingScreenHandler.loading,
                6f
            );
            if (RunFlowRuntime.CreditsWaiting || LoadingScreenHandler.loading)
                break;
        }
        if (!RunFlowRuntime.CreditsWaiting && !LoadingScreenHandler.loading)
        {
            Fail(
                $"Next did not close the end screen after 3 laser presses (laser over {pressed} after the last press, "
                    + $"Next active {end.nextButton.gameObject.activeInHierarchy}, interactable {end.nextButton.interactable})"
            );
            yield break;
        }
        // A won run always rolls the credits (MapHandler.cs:638-643); loading straight away means the win did not
        // register, and the TooSoon and skip checks below would never run.
        if (!RunFlowRuntime.CreditsWaiting)
        {
            Fail(
                $"no credits after a forced win (won {c.refs.stats.won}): the end screen loaded the airport"
            );
            yield break;
        }
        yield return Wait(1f);
        Affordance.ResetGate();
        float early = CueMark();
        yield return Press(false);
        yield return ExpectCue(false, DenyReason.TooSoon, early, tint: false);
        if (!stepOk)
            yield break;
        var tooSoon = cueNote;
        float ready = CueMark();
        yield return Until(
            () => RunFlowRuntime.CreditsAge >= CreditsSkip.Delay + 0.3f,
            CreditsSkip.Delay + 5f,
            "credits never became skippable"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(true, CueFeel.Contact, ready, within: 0f);
        yield return ExpectFeel(false, CueFeel.Contact, ready, within: 0f);
        if (!stepOk)
            yield break;
        var credits = $"early press {tooSoon}; skippable {cueNote}";
        yield return Wait(0.6f);
        float pressedAt = Now;
        yield return Press(false);
        // The credits end on their own 80 s in (PeakHandler.cs:230-257), so the airport alone does not prove the
        // skip: a press must start the load CreditsSkipHold after it (PeakHandler.cs:249).
        yield return WaitFor(
            () => LoadingScreenHandler.loading || !RunFlowRuntime.CreditsWaiting,
            GameTiming.Budget(CreditsSkipHold)
        );
        if (!LoadingScreenHandler.loading && RunFlowRuntime.CreditsWaiting)
        {
            Fail(
                F(
                    $"the press did not skip the credits: no load {Now - pressedAt:0.0} s after it (credits age {RunFlowRuntime.CreditsAge:0.0} s)"
                )
            );
            yield break;
        }
        yield return Until(
            () => SceneManager.GetActiveScene().name == "Airport" && !LoadingScreenHandler.loading,
            90f,
            "no return to the airport"
        );
        if (stepOk)
            stepNote = credits;
    }

    /// <summary>A credits key press ends them once it has been held back this long (PeakHandler.cs:249).</summary>
    const float CreditsSkipHold = 0.5f;

    static bool Usable(Button b) => b != null && b.gameObject.activeInHierarchy && b.interactable;

    /// <summary>Warps onto the ground <paramref name="distance"/> from <paramref name="target"/> on the side the player
    /// came from, centre over the spot (<see cref="CentreOver"/>, the warp moves the body's centre,
    /// Character.cs:1904-1910), facing it. Sets <see cref="warpedForProp"/>.</summary>
    IEnumerator WarpBeside(Vector3 target, float distance, Vector3 side = default)
    {
        var c = Character.localCharacter;
        var stand = target + Flat(side == default ? c.Center - target : side) * distance;
        // From just above the target, not far above: a ray from high up lands on top of the prop itself.
        float top = target.y + 0.5f;
        if (Terrain(new Vector3(stand.x, top, stand.z), Vector3.down, 8f, out var ground))
            stand = ground.point + Vector3.up * 0.1f;
        warpedForProp = true;
        c.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, CentreOver(c, stand), false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1.5f);
        var rig = VRRig.Instance;
        rig.RotateYaw(
            Vector3.SignedAngle(Flat(rig.Head.forward), Flat(target - c.Center), Vector3.up)
        );
        yield return Wait(0.6f);
    }

    /// <summary>One trigger click of the scripted hand.</summary>
    IEnumerator Press(bool left)
    {
        Script(left).Trigger = 1f;
        yield return Wait(0.15f);
        Script(left).Trigger = 0f;
        yield return Wait(0.1f);
    }

    /// <summary>Both aim rays on <paramref name="button"/> and a trigger pull on each (LastUsed pointer mode keeps
    /// whichever is active), as vr.19 does on the pause menu.</summary>
    IEnumerator LaserPress(Button button)
    {
        var rig = VRRig.Instance;
        var rect = (RectTransform)button.transform;
        var target = rect.TransformPoint(rect.rect.center);
        HeadFrame(out var head, out var forward, out var right);
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            var p = head + (left ? -right : right) * 0.15f + Vector3.down * 0.2f + forward * 0.2f;
            var dir = rig.Origin.InverseTransformDirection(target - rig.Origin.TransformPoint(p));
            Script(left).Position = p;
            Script(left).Rotation = HandLook(dir, Vector3.up);
        }
        yield return Wait(0.3f);
        Script(true).Trigger = Script(false).Trigger = 1f;
        yield return Wait(0.15f);
        Script(true).Trigger = Script(false).Trigger = 0f;
    }

    /// <summary>
    /// mp.scene-button (body only; the yield goes in SelfTestRunner.MpTest.cs): a scene button (TriggerOnInteract)
    /// whose PhotonView the host owns, gripped by this non-host client, does nothing in vanilla
    /// (TriggerEvent.cs:46-53) and gets the Blocked refusal, the button red.
    /// </summary>
    IEnumerator MpSceneButton()
    {
        var c = Character.localCharacter;
        var button = Nearest<TriggerOnInteract>(
            c.Center,
            b =>
                b.triggerEvent != null
                && b.triggerEvent.GetComponentInParent<Photon.Pun.PhotonView>() is { IsMine: false }
        );
        if (button == null)
        {
            stepSkip = "no host-owned scene button loaded";
            yield break;
        }
        yield return WalkTo(() => button.transform.position, 1f, false);
        bool left = !VRInput.LeftHanded;
        var at = button.Center();
        yield return MoveTo(left, () => at, 0.5f);
        Affordance.ResetGate();
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Blocked, mark, tint: true, within: 2f);
        yield return GripTo(left, 0f, 0.1f);
        if (stepOk)
            stepNote = $"{button.name}: {cueNote}";
    }
}
