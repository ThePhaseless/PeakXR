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
using PeakXR.VR;
using UnityEngine;
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
        yield return HandStep("vr.83-cook-cant-use", 40f, CookCantUseStep());
        yield return WarpBack();
        yield return ItemStep("vr.83b-cook-not-lit", AppleFragment, 40f, CookNotLitStep);
        yield return WarpBack();
        yield return HandStep("vr.84-book-of-bones", 30f, BookStep(usedUp: false));
        yield return HandStep("vr.84b-book-used-up", 30f, BookStep(usedUp: true));
        yield return ItemStep("vr.87-fuse-lit", "Torch", 40f, FuseLitStep);
        yield return WarpBack();
        yield return HandStep("vr.89-altar-full", 20f, AltarFullStep());
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
            float end = Now + 4f;
            BeginCarry();
            while (Now < end && held != null && (usedUp || progress < 0.1f))
            {
                CarryToward(left, held.Center(), skeleton.Center() + Vector3.up * 0.25f);
                if (
                    interaction != null
                    && ReferenceEquals(interaction.currentHeldInteractible, skeleton)
                )
                    progress = Mathf.Max(progress, interaction.constantInteractableProgress);
                if (usedUp && CueLog.Capture.Fired(left, DenyReason.UsedUp, mark, true, out _))
                    break;
                yield return null;
            }
            float above = held != null ? (held.Center() - skeleton.Center()).y : 0f;
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
                    $"book over the bones did not start the revive hold: book {above:0.00} m above the hips, "
                        + $"over {(WorldHolds.BookTarget != null ? "the skeleton" : "nothing")}"
                );
                yield break;
            }
            yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0.5f);
            if (stepOk)
                stepNote = $"revive hold {progress:0.00}, book {above:0.00} m above; {cueNote}";
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
}
