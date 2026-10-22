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
                        + $"gesture over the skeleton {over}, hold on {Label(interaction != null ? interaction.currentHeldInteractible : null)}, {usesNote}, last cue {CueLog.Capture.Hand(left).Reason}"
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
        // Kept: still held, or the plain vanilla hold already finished and opened it.
        bool holding =
            lug.IsOpen
            || interaction != null && ReferenceEquals(interaction.currentHeldInteractible, lug);
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
            stepNote =
                $"flag at {at}, body {Vector3.Distance(c.Center, at):0.0} m from it; {cueNote}";
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
        yield return HandStep("vr.85-campfire-segment", 120f, CampfireSegmentStep());
        yield return WarpBack();
        yield return Run("vr.89-to-peak", 150f, Jump(Segment.Peak, "vr-89-peak"));
        yield return HandStep("vr.89-altar-full", 20f, AltarFullStep());
        yield return HandStep("vr.89b-amulet-socket", 40f, AmuletSocketStep());
        yield return HandStep("vr.90-soul-pillar", 30f, PillarStep());
        yield return HandStep("vr.91-portal", 30f, PortalStep());
        yield return PeakHandStep("vr.93-summit-flare", 180f, FlareStep());
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
    IEnumerator PeakHandStep(string id, float timeout, IEnumerator body)
    {
        if (!VRRun)
            yield break;
        if (!VRSession.Active)
        {
            Skip(id, "VR not active");
            yield break;
        }
        yield return Run(id, timeout, PeakSetup(id, body));
        yield return Guard(
            HandsRelease(),
            Now + 5f,
            e =>
                Plugin.Log.LogWarning(
                    $"[selftest] {id} teardown: {(e == null ? "timeout" : e.Message)}"
                )
        );
        EndHands();
        ClosePause();
        yield return Guard(DropHeld(), Now + 4f, _ => { });
        yield return Wait(0.3f);
        Safe("hands.despawn", Despawn);
        Safe("hands.sweep", SweepNewItems);
        Recover();
        yield return Wait(0.5f);
    }

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
            for (int i = 0; i < slots.Length; i++)
                slots[i] = -1;
            // Stand beside the socket it will fill (an empty altar: its own type's, AltarSocket), not the statue's
            // root: vr34 stood by the root and ended 2.16 m short.
            var target = WorldHolds.SocketCenter(statue.amuletObjects[amulet.amuletIndex]);
            yield return WarpBeside(target, 0.7f);
            yield return GiveAndHold(amulet.GetComponent<Item>());
            if (!stepOk)
                yield break;
            var held = HeldItem();
            bool left = LocalHold.ItemInLeftHand;
            yield return GripTo(left, 1f, 0.1f);
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
            // Where a player holds it: at the socket when it is within reach, else raised over the head straight
            // under it (AltarSocket.Reach); the gesture reads the tracked hand, so the scripted hand goes there.
            Vector3 Offer()
            {
                float headY = rig.Head.position.y;
                return socketAt.y <= headY + AltarSocket.RaisedReach
                    ? socketAt
                    : new Vector3(socketAt.x, headY + 0.25f, socketAt.z);
            }
            var interaction = Interaction.instance;
            float progress = 0f;
            float mark = CueMark();
            yield return MoveTo(left, Offer, 0.6f);
            float end =
                Now
                + GameTiming.Budget(
                    GameTiming.InteractSeconds(statue, Character.localCharacter),
                    3f
                );
            while (Now < end && held != null && ReferenceEquals(HeldItem(), held))
            {
                SetHand(left, rig.Origin.InverseTransformPoint(Offer()));
                if (
                    interaction != null
                    && ReferenceEquals(interaction.currentHeldInteractible, statue)
                )
                    progress = Mathf.Max(progress, interaction.constantInteractableProgress);
                yield return null;
            }
            float reach = WorldHolds.SocketDistance(rig.Hand(left).position, socketAt);
            if (progress <= 0f)
            {
                Fail(
                    $"the amulet at the socket did not start the hold (hand {reach:0.00} m short of offering it; socket {socketUp:0.00} m over the feet, body {FlatDistance(Character.localCharacter.Center, socketAt):0.00} m from it; socket mesh {(socketAt - statue.amuletObjects[WorldHolds.SocketTarget].transform.position).y:0.00} m over its transform)"
                );
                yield break;
            }
            yield return ExpectFeel(left, CueFeel.Progress, mark, within: 0f);
            if (stepOk)
                stepNote =
                    $"socket {WorldHolds.SocketTarget} {socketUp:0.00} m over the feet: hold {progress:0.00}, amulet {(ReferenceEquals(HeldItem(), held) ? "still held" : "inserted")}; {cueNote}";
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
        // The flare in hand now: a given trackable item can be replaced by its reconnected copy (vr38: "Reconnected
        // to trackable object ... timed out. Destroying"), leaving the first reference dead.
        var flareNow = HeldItem() != null ? HeldItem().GetComponent<Flare>() : null;
        if (!peak.summonedHelicopter && flareNow != null)
        {
            sent = true;
            flareNow.item.photonView.RPC("TriggerHelicopter", Photon.Pun.RpcTarget.AllBuffered);
        }
        yield return Until(
            () => WorldHolds.Notices > before,
            3f,
            $"no notice for the summon (helicopter summoned {peak.summonedHelicopter}, flare in hand {(flareNow != null ? flareNow.name : "none")}, call sent {sent})"
        );
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
            pressed =
                LaserPointer.PressedControl != null ? LaserPointer.PressedControl.name : "nothing";
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
                $"Next did not close the end screen after 3 laser presses (last press on {pressed}, "
                    + $"Next active {end.nextButton.gameObject.activeInHierarchy}, interactable {end.nextButton.interactable})"
            );
            yield break;
        }
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

    static bool Usable(Button b) => b != null && b.gameObject.activeInHierarchy && b.interactable;

    /// <summary>Warps onto the ground <paramref name="distance"/> from <paramref name="target"/> on the side the player
    /// came from, centre over the spot (<see cref="CentreOver"/>, the warp moves the body's centre,
    /// Character.cs:1905-1911), facing it. Sets <see cref="warpedForProp"/>.</summary>
    IEnumerator WarpBeside(Vector3 target, float distance)
    {
        var c = Character.localCharacter;
        var stand = target + Flat(c.Center - target) * distance;
        // From just above the target, not far above: a ray from high up lands on top of the prop itself.
        float top = target.y + 0.5f;
        if (
            Physics.Raycast(
                new Vector3(stand.x, top, stand.z),
                Vector3.down,
                out var ground,
                8f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
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
