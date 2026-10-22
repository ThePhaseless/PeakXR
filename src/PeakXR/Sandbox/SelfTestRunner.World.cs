using System;
using System.Collections;
using System.Collections.Generic;
using Peak;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact.Lights;
using PeakXR.Interact.Physical;
using PeakXR.UI.Affordances;
using PeakXR.UI.RunFlow;
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
        yield return HandStep("vr.80-campfire-far", 30f, CampfireFarStep());
        yield return WarpBack();
        yield return HandStep("vr.81-chest-two-hands", 40f, ChestTwoHandsStep());
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
        yield return HandStep("vr.88-scout-cannon", 30f, CannonStep());
        yield return WarpBack();
        yield return HandStep("vr.92-checkpoint", 40f, CheckpointStep());
        yield return HandStep("vr.94-effigy-nobody-down", 20f, EffigyStep());
    }

    /// <summary>
    /// vr.80: a bare-hand grip on an unlit campfire's logs with a scout outside <c>moraleBoostRadius</c>
    /// (Campfire.cs:315-326, EveryoneInRange :375-386) is refused (FriendsFar, logs red) and the fire stays unlit.
    /// Solo, the radius is shrunk for the step so the player counts as the one away.
    /// </summary>
    IEnumerator CampfireFarStep()
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state == Campfire.FireState.Off);
        if (fire == null)
        {
            stepSkip = "no unlit campfire loaded";
            yield break;
        }
        var fireBase = ItemGestures.FireBase(fire);
        yield return ApproachFire(fireBase);
        float radius = fire.moraleBoostRadius;
        fire.moraleBoostRadius = 0.05f;
        try
        {
            bool left = !VRInput.LeftHanded;
            var logs = fire.Center();
            yield return MoveTo(left, () => logs, 0.5f);
            Affordance.ResetGate();
            float mark = CueMark();
            yield return GripTo(left, 1f, 0.1f);
            yield return ExpectCue(left, DenyReason.FriendsFar, mark, tint: true, within: 2f);
            var cue = cueNote;
            yield return GripTo(left, 0f, 0.1f);
            if (!stepOk)
                yield break;
            if (fire.state != Campfire.FireState.Off)
                Fail($"{fire.name} changed to {fire.state} with a scout away");
            else
                stepNote = $"{fire.name}: {cue}";
        }
        finally
        {
            fire.moraleBoostRadius = radius;
        }
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
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.NeedsTwoHands, mark, tint: true, within: 2f);
        var cue = cueNote;
        var interaction = Interaction.instance;
        float alone = interaction != null ? interaction.constantInteractableProgress : 0f;
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
            Fail($"two hands lifting did not run the timer; one hand: {cue}");
        else
            stepNote = $"{chest.name}: one hand {cue}; two hands progress {progress:0.00}";
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
        float mark = CueMark();
        yield return LiftLid(lug);
        if (!stepOk)
            yield break;
        var opened = stepNote;
        yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
        var progress = cueNote;
        yield return ExpectFeel(left, CueFeel.Contact, mark, within: 0f);
        if (stepOk)
            stepNote = $"{opened}; {progress}; {cueNote}";
    }

    /// <summary>vr.83: an item that cannot cook (<c>ItemCooking.canBeCooked</c> false) held in the heat column of a
    /// lit campfire is refused with CantUse, the item red (Campfire.cs:341-356). Lights the fire without the segment
    /// advance first when needed (<c>LightWithoutReveal</c>, Campfire.cs:456).</summary>
    IEnumerator CookCantUseStep()
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state != Campfire.FireState.Spent);
        var raw = FindItem(new List<Item>(SandboxActions.Items), "Rope");
        if (fire == null || raw == null || raw.cooking == null || raw.cooking.canBeCooked)
        {
            stepSkip = fire == null ? "no campfire loaded" : "no uncookable Rope item";
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

    /// <summary>vr.83b: food held where the heat column of an unlit campfire would be is refused with NotLit, the
    /// fire red, and does not cook.</summary>
    IEnumerator CookNotLitStep(Item food)
    {
        var c = Character.localCharacter;
        var fire = Nearest<Campfire>(c.Center, f => f.state == Campfire.FireState.Off);
        if (fire == null)
        {
            stepSkip = "no unlit campfire loaded";
            yield break;
        }
        var fireBase = ItemGestures.FireBase(fire);
        yield return ApproachFire(fireBase);
        yield return HoldInHeat(food, fireBase, DenyReason.NotLit);
        if (stepOk && fire.state != Campfire.FireState.Off)
            Fail($"{fire.name} changed to {fire.state}");
    }

    /// <summary>Holds <paramref name="prefab"/> in the near side of the heat column over <paramref name="fireBase"/>
    /// and expects the <paramref name="reason"/> cue with the red preview.</summary>
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
        var goal = fireBase + Flat(c.Center - fireBase) * 0.35f + Vector3.up * 0.55f;
        Affordance.ResetGate();
        float mark = CueMark();
        float end = Now + 4f;
        BeginCarry();
        while (Now < end && item != null && !CueLog.Capture.Fired(left, reason, mark, true, out _))
        {
            CarryToward(left, item.Center(), goal);
            yield return null;
        }
        var trace = FireTrace("vr.83", item != null ? item.Center() : goal, fireBase, left);
        yield return ExpectCue(left, reason, mark, tint: true, within: 0.5f);
        if (stepOk)
            stepNote = $"{prefab.name}: {cueNote}";
        else
            stepNote = trace;
    }

    /// <summary>
    /// vr.84 / vr.84b: the Book of Bones held open just above a skeleton's hips (a local <c>Resources/Skeleton</c>,
    /// as Character.cs:778 spawns one, with no owner so a finished hold revives nobody) starts the game's revive hold
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
        var spot = SandboxActions.FrontPoint(0.7f);
        if (
            Physics.Raycast(
                spot + Vector3.up * 2f,
                Vector3.down,
                out var ground,
                5f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            spot = ground.point;
        var skeleton = Object
            .Instantiate(prefab, spot + Vector3.up * 0.3f, Quaternion.identity)
            .GetComponent<Skelleton>();
        try
        {
            yield return Wait(1f);
            yield return GiveAndHold(book);
            if (!stepOk)
                yield break;
            var held = HeldItem();
            bool left = LocalHold.ItemInLeftHand;
            yield return GripTo(left, 1f, 0.1f);
            if (usedUp)
                held.GetData<OptionableIntItemData>(DataEntryKey.ItemUses).Value = 0;
            Affordance.ResetGate();
            float mark = CueMark();
            var interaction = Interaction.instance;
            float progress = 0f;
            bool over = false;
            // The gesture reads the book hand, as a player holds the book over the body: the hand goes 0.4 m above
            // the hips and stays (the book itself moves when the revive opens it).
            var rig = VRRig.Instance;
            yield return MoveTo(left, () => skeleton.Center() + Vector3.up * 0.4f, 0.6f);
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
                        + $"gesture over the skeleton {over}, hold on {Label(interaction != null ? interaction.currentHeldInteractible : null)}"
                );
                yield break;
            }
            yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0.5f);
            if (stepOk)
                stepNote =
                    $"revive hold {progress:0.00}, book hand {above:0.00} m above; {cueNote}";
        }
        finally
        {
            if (skeleton != null)
                Object.Destroy(skeleton.gameObject);
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
        if (fuse == null)
        {
            stepSkip = "no lit fuse or ghost fire loaded";
            yield break;
        }
        var reason =
            fuse is GhostFire || ((IInteractible)fuse).IsInteractible(c)
                ? DenyReason.UsedUp
                : DenyReason.Occupied;
        var at = fuse.transform.position;
        yield return WarpFacing(at, 0.8f);
        yield return GiveAndHold(torchPrefab);
        if (!stepOk)
            yield break;
        var torch = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        Script(left).Trigger = 1f;
        yield return WaitFor(
            () => HandLights.IsLit(torch),
            GameTiming.Budget(GameTiming.UseSeconds(torch))
        );
        Script(left).Trigger = 0f;
        var flame = FireGestures.FlamePoint(torch);
        if (flame == null)
            flame = torch.transform;
        Affordance.ResetGate();
        float mark = CueMark();
        float end = Now + 4f;
        BeginCarry();
        while (Now < end && !CueLog.Capture.Fired(left, reason, mark, true, out _))
        {
            CarryToward(left, flame.position, at);
            yield return null;
        }
        yield return ExpectCue(left, reason, mark, tint: true, within: 0.5f);
        if (stepOk)
            stepNote = $"{fuse.name}: {cueNote}";
    }

    /// <summary>
    /// vr.89: an amulet put into a full scout statue altar (every <c>hasAmulets</c> slot set, Peak/ScoutStatue.cs:10)
    /// is refused with Occupied, the statue red, and stays in the hand instead of being consumed
    /// (Interact_CastFinished, :302-310). The slots are filled locally for the step and restored. Peak segment only.
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
        if (statue == null || amulet == null)
        {
            stepSkip = statue == null ? "no scout statue loaded (Peak only)" : "no amulet item";
            yield break;
        }
        yield return GiveAndHold(amulet.GetComponent<Item>());
        if (!stepOk)
            yield break;
        var held = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        var slots = GameMember.Field<ScoutStatue, int[]>("hasAmulets")(statue);
        var saved = (int[])slots.Clone();
        try
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i] = i;
            Affordance.ResetGate();
            float mark = CueMark();
            statue.Interact_CastFinished(Character.localCharacter);
            yield return ExpectCue(left, DenyReason.Occupied, mark, tint: true);
            var cue = cueNote;
            yield return Wait(1f);
            if (stepOk && !ReferenceEquals(HeldItem(), held))
                Fail("the amulet left the hand at a full altar");
            else if (stepOk)
                stepNote = cue;
        }
        finally
        {
            Array.Copy(saved, slots, slots.Length);
        }
    }

    /// <summary>vr.94: a scout effigy held with nobody down (Character.PlayerIsDeadOrDown, Character.cs:1979) stays
    /// unusable (ScoutEffigy.cs:8-21, guarded against Constructable.Update's overwrite): the trigger gets CantUse and
    /// the effigy stays in the hand.</summary>
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
        yield return PressAndExpect(LocalHold.ItemInLeftHand, DenyReason.CantUse);
        if (stepOk && !ReferenceEquals(HeldItem(), held))
            Fail("effigy left the hand");
    }

    /// <summary>vr.82b: a grip low on a closed suitcase keeps the vanilla hold and, after 0.5 s, gets the "grab the
    /// lid" notice (AimHigher, ring on the lid edge above the hand). A would-work hint: needs UI.ActionHints.</summary>
    IEnumerator LidLowGripStep()
    {
        var c = Character.localCharacter;
        var lug = Nearest<Luggage>(c.Center, l => !l.IsOpen && l is not RespawnChest);
        if (lug == null || !Affordance.Hints || !Setting("Interaction", "LiftLidToOpen", true))
        {
            stepSkip =
                lug == null ? "no closed suitcase in the active segment"
                : !Affordance.Hints ? "UI.ActionHints is off"
                : "Interaction.LiftLidToOpen is off";
            yield break;
        }
        yield return WarpFacing(lug.Center(), 0.8f);
        bool left = VRInput.LeftHanded;
        var low = NearSide(lug, Character.localCharacter.Center, -0.08f);
        yield return MoveTo(left, () => low, 0.5f);
        Affordance.ResetGate();
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.AimHigher, mark, tint: false, within: 2f);
        var cue = cueNote;
        var interaction = Interaction.instance;
        bool holding =
            interaction != null && ReferenceEquals(interaction.currentHeldInteractible, lug);
        yield return GripTo(left, 0f, 0.1f);
        if (!stepOk)
            yield break;
        if (!holding)
            Fail("the low grip did not keep the vanilla hold on " + lug.name);
        else
            stepNote = $"{lug.name}: {cue}";
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
            var p = col is MeshCollider { convex: false }
                ? col.bounds.ClosestPoint(probe)
                : col.ClosestPoint(probe);
            float d = (p - probe).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }
        return best + Flat(from - center) * 0.02f;
    }

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
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        int before = Cooked(food);
        // FireGestures: the column is 0.6 m around the base, 0.2 to 1 m up; the warmth reaches 0.5 m past it.
        var goal = fireBase + Flat(c.Center - fireBase) * 0.85f + Vector3.up * 0.55f;
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
        yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
        if (!stepOk)
            stepNote = trace;
        else if (heldFire || (food != null && Cooked(food) != before))
            Fail("the food reached the heat column, so the ramp may be the cook's: " + trace);
        else
            stepNote = cueNote;
    }

    /// <summary>
    /// vr.88: standing 2 m from a scout cannon's entry (outside the 0.75 m launch and 1 m pull ranges,
    /// ScoutCannon.cs:66, :197-222) when its fuse is lit (<c>ScoutCannon.Light</c>, :164): the countdown is felt as a
    /// rising progress ramp, and the shot that leaves you behind gives the notice (contact tick, notice note).
    /// </summary>
    IEnumerator CannonStep()
    {
        var c = Character.localCharacter;
        var cannon = Nearest<ScoutCannon>(c.Center, k => !k.lit);
        var entry =
            cannon != null ? GameMember.Field<ScoutCannon, Transform>("entry")(cannon) : null;
        if (entry == null)
        {
            stepSkip = "no scout cannon loaded";
            yield break;
        }
        yield return WarpFacing(entry.position, 2f);
        int before = WorldHolds.Notices;
        float mark = CueMark();
        cannon.Light();
        yield return ExpectFeel(!VRInput.LeftHanded, CueFeel.Progress, mark, within: 1.5f);
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
            stepNote = $"{cannon.name}: {ramp}; {cueNote}";
    }

    /// <summary>
    /// vr.92: a checkpoint flag planted by the hand-aimed use (CheckpointConstructable, CheckpointFlag.Initialize
    /// :25-41) and then used (<c>Character.TryCheckpoint</c>, Character.cs:644, the call a death makes at :600) gives
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
        // The constructable aims from the hand (AimPatches): point it at the ground 1.5 m ahead.
        var rig = VRRig.Instance;
        HeadFrame(out var head, out var forward, out _);
        var ground = head + forward * 1.5f + Vector3.down * 1.6f;
        var hand = rig.Hand(left).position;
        Script(left).Rotation =
            Quaternion.Inverse(rig.Origin.rotation)
            * Quaternion.LookRotation(ground - hand, Vector3.up);
        yield return Wait(0.5f);
        Script(left).Trigger = 1f;
        yield return WaitFor(
            () => c.data.checkpointFlags.Count > flags,
            GameTiming.Budget(GameTiming.UseSeconds(item), 3f)
        );
        Script(left).Trigger = 0f;
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
            stepNote =
                $"flag at {at}, body {Vector3.Distance(c.Center, at):0.0} m from it; {cueNote}";
    }

    // ---------------------------------------------------------------- end of run

    /// <summary>
    /// The end-of-run slot (<see cref="Script"/>, after every hand, segment and death step): steps that advance or
    /// end the run. A real campfire advances the segment; then the summit props on the Peak; last the end screen,
    /// which wins the run and returns to the airport, where the summary and quit run as after any test.
    /// </summary>
    IEnumerator EndOfRunSteps()
    {
        if (!VRRun || !Ready || Singleton<MapHandler>.Instance == null)
            yield break;
        yield return HandStep("vr.85-campfire-segment", 120f, CampfireSegmentStep());
        yield return WarpBack();
        yield return Run("vr.89-to-peak", 150f, Jump(Segment.Peak, "vr-89-peak"));
        yield return HandStep("vr.89-altar-full", 20f, AltarFullStep());
        yield return HandStep("vr.89b-amulet-socket", 40f, AmuletSocketStep());
        yield return HandStep("vr.90-soul-pillar", 30f, PillarStep());
        yield return HandStep("vr.91-portal", 30f, PortalStep());
        yield return HandStep("vr.93-summit-flare", 40f, FlareStep());
        yield return HandStep("vr.86-end-screen", 300f, EndScreenStep());
    }

    /// <summary>
    /// vr.85: a bare-hand grip on the next unlit campfire with everyone in range lights it for real
    /// (Campfire.cs:315-326 <c>Light_Rpc(true)</c>, :461-488): the segment advances (<c>MapHandler.GoToSegment</c>,
    /// MapHandler.cs:578) under ui.runflow's fade, and no refusal cue fires.
    /// </summary>
    IEnumerator CampfireSegmentStep()
    {
        var map = Singleton<MapHandler>.Instance;
        var c = Character.localCharacter;
        var from = map.GetCurrentSegment();
        var fire = Nearest<Campfire>(
            c.Center,
            f => f.state == Campfire.FireState.Off && f.advanceToSegment > from
        );
        if (fire == null)
        {
            stepSkip = "no unlit campfire ahead of " + from;
            yield break;
        }
        yield return ApproachFire(ItemGestures.FireBase(fire));
        SelfTestCampfirePatch.Hold = false;
        bool left = !VRInput.LeftHanded;
        var logs = fire.Center();
        yield return MoveTo(left, () => logs, 0.5f);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => fire.Lit,
            GameTiming.Budget(GameTiming.InteractSeconds(fire, c), 3f),
            "the grip hold did not light " + fire.name
        );
        yield return GripTo(left, 0f, 0.1f);
        if (!stepOk)
            yield break;
        if (CueLog.Capture.Fired(left, DenyReason.FriendsFar, mark, out _))
        {
            Fail("FriendsFar fired with everyone in range");
            yield break;
        }
        float fade = 0f;
        float end = Now + 40f;
        while (Now < end && map.GetCurrentSegment() <= from)
        {
            fade = Mathf.Max(fade, VRFade.Target);
            yield return null;
        }
        // The fade holds through the swap; give it a moment to show after the segment index moves.
        for (float t = Now + 2f; Now < t; )
        {
            fade = Mathf.Max(fade, VRFade.Target);
            yield return null;
        }
        var note = $"{fire.name}: {from} -> {map.GetCurrentSegment()}, fade peak {fade:0.00}";
        if (map.GetCurrentSegment() <= from)
            Fail("segment did not advance: " + note);
        else if (Setting("UI", "FadeOnSegmentChange", true) && fade < 0.5f)
            Fail("no fade over the segment swap: " + note);
        else
            stepNote = note;
    }

    /// <summary>
    /// vr.89b: the held amulet brought to the altar socket it would fill (<see cref="AltarSocket"/>, the slot order
    /// of RPC_InsertAmulet, Peak/ScoutStatue.cs:172-185) holds interact on the statue with the progress ramp until it
    /// goes in. The altar is emptied locally for the step and restored. Peak only.
    /// </summary>
    IEnumerator AmuletSocketStep()
    {
        if (!Setting("Interaction", "AmuletInSocket", true))
        {
            stepSkip = "Interaction.AmuletInSocket is off";
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
        if (statue == null || amulet == null || statue.amuletObjects == null)
        {
            stepSkip = statue == null ? "no scout statue loaded (Peak only)" : "no amulet item";
            yield break;
        }
        var slots = GameMember.Field<ScoutStatue, int[]>("hasAmulets")(statue);
        var saved = (int[])slots.Clone();
        try
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i] = -1;
            yield return WarpFacing(statue.Center(), 1f);
            yield return GiveAndHold(amulet.GetComponent<Item>());
            if (!stepOk)
                yield break;
            var held = HeldItem();
            bool left = LocalHold.ItemInLeftHand;
            yield return GripTo(left, 1f, 0.1f);
            yield return Until(
                () => WorldHolds.SocketTarget >= 0,
                2f,
                "no socket picked for the amulet"
            );
            if (!stepOk)
                yield break;
            var socket = statue.amuletObjects[WorldHolds.SocketTarget].transform.position;
            var interaction = Interaction.instance;
            float progress = 0f;
            float mark = CueMark();
            float end =
                Now
                + GameTiming.Budget(
                    GameTiming.InteractSeconds(statue, Character.localCharacter),
                    3f
                );
            BeginCarry();
            while (Now < end && held != null && ReferenceEquals(HeldItem(), held))
            {
                CarryToward(left, held.Center(), socket);
                if (
                    interaction != null
                    && ReferenceEquals(interaction.currentHeldInteractible, statue)
                )
                    progress = Mathf.Max(progress, interaction.constantInteractableProgress);
                yield return null;
            }
            if (progress <= 0f)
            {
                Fail(
                    $"the amulet at the socket did not start the hold ({(held != null ? Vector3.Distance(held.Center(), socket) : -1f):0.00} m from it)"
                );
                yield break;
            }
            yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
            if (stepOk)
                stepNote =
                    $"socket {WorldHolds.SocketTarget}: hold {progress:0.00}, amulet {(ReferenceEquals(HeldItem(), held) ? "still held" : "inserted")}; {cueNote}";
        }
        finally
        {
            Array.Copy(saved, slots, slots.Length);
        }
    }

    /// <summary>vr.90: both open palms laid on the soul pillar (ScoutmasterSoulPillar.cs:51-106) start its 2 s hold
    /// with the progress ramp on both hands; taking them off cancels before it breaks. Nadir only.</summary>
    IEnumerator PillarStep()
    {
        var pillar = Object.FindFirstObjectByType<ScoutmasterSoulPillar>();
        if (pillar == null || !Setting("Interaction", "PalmsOnPillar", true))
        {
            stepSkip =
                pillar == null
                    ? "no soul pillar loaded (Nadir only)"
                    : "Interaction.PalmsOnPillar is off";
            yield break;
        }
        var col =
            pillar.colliderToDisable != null
                ? pillar.colliderToDisable
                : pillar.GetComponentInChildren<Collider>();
        yield return WarpFacing(col.bounds.center, 0.6f);
        yield return PressPalms(col, pillar);
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
            var world = Quaternion.LookRotation(Vector3.Cross(x, Vector3.up), Vector3.up);
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
        var portal = Object.FindFirstObjectByType<PeakGatePortal>();
        var col = portal != null ? portal.GetComponentInChildren<Collider>() : null;
        if (
            col == null
            || !portal.isActiveAndEnabled
            || !Setting("Interaction", "HandIntoPortal", true)
        )
        {
            stepSkip =
                col == null || !portal.isActiveAndEnabled
                    ? "no active summit portal loaded"
                    : "Interaction.HandIntoPortal is off";
            yield break;
        }
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
    /// vr.93: a lit flare held when the helicopter is summoned (Flare.cs:36-46, PeakHandler.SummonHelicopter
    /// :58-62) gives the notice at the flare. The summon needs the holder at the peak (IsAtPeak,
    /// MountainProgressHandler.cs:198); where the jump lands short of it, the flare's own TriggerHelicopter call is
    /// sent, as the flare would.
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
        bool sent = false;
        if (!peak.summonedHelicopter && held != null)
        {
            sent = true;
            held.photonView.RPC("TriggerHelicopter", Photon.Pun.RpcTarget.AllBuffered);
        }
        yield return Until(() => WorldHolds.Notices > before, 3f, "no notice for the summon");
        if (!stepOk)
            yield break;
        yield return ExpectFeel(left, CueFeel.Contact, mark, within: 0f);
        if (stepOk && CueLog.Capture.LastSound(CueSound.Notice).At < mark)
            Fail("no notice note at the flare");
        else if (stepOk)
            stepNote =
                (sent ? "not at the peak: sent the flare's call; " : "summoned at the peak; ")
                + cueNote;
    }

    /// <summary>
    /// vr.86: the run won (<c>RPCEndGame_ForceWin</c>, Character.cs:885) plays the end cutscene, then the end screen
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
        yield return Until(
            () => end.nextButton.gameObject.activeInHierarchy && end.nextButton.interactable,
            150f,
            "the end screen's Next did not show"
        );
        if (!stepOk)
            yield break;
        yield return Wait(1f);
        yield return Shot("vr-86-end-screen");
        yield return LaserPress(end.nextButton);
        yield return Until(
            () => RunFlowRuntime.CreditsWaiting || LoadingScreenHandler.loading,
            20f,
            "Next did not close the end screen"
        );
        if (!stepOk)
            yield break;
        string credits = "no credits (lost run)";
        if (RunFlowRuntime.CreditsWaiting)
        {
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
            credits = $"early press {tooSoon}; skippable {cueNote}";
            yield return Wait(0.6f);
            yield return Press(false);
        }
        yield return Until(
            () => SceneManager.GetActiveScene().name == "Airport" && !LoadingScreenHandler.loading,
            90f,
            "no return to the airport"
        );
        if (stepOk)
            stepNote = credits;
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
            Script(left).Rotation = Quaternion.LookRotation(dir, Vector3.up);
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
