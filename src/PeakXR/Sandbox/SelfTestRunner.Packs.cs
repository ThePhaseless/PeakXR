using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Inventory;
using PeakXR.Social;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Issue #14 steps: feeding another player by holding food at their mouth (vr.283), and letting a held item go into
/// a backpack on the ground (vr.284) or on another player's back (vr.285). An offline run has no second player: the
/// game feeds no bot (CharacterInteractible.cs:128) and bots wear no pack, so vr.283 and vr.285 skip there and the
/// two-client test covers them.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Another player's character (no bot, alive, in the scene), or null offline.</summary>
    static Character OtherPlayer(bool withPack)
    {
        var self = Character.localCharacter;
        foreach (var ch in Character.AllCharacters)
        {
            if (
                ch != null
                && ch != self
                && !ch.isBot
                && !ch.data.dead
                && ch.gameObject.activeInHierarchy
                && (!withPack || SocialHands.WearsPack(ch))
            )
                return ch;
        }
        return null;
    }

    const string NoSecondPlayer =
        "offline there is no second player: the game feeds no bot (CharacterInteractible.cs:128) and bots wear no "
        + "pack; the two-client test covers it";

    /// <summary>
    /// vr.283: an Apple held at another player's mouth, trigger up, starts the game's use-on-friend
    /// (Item.isUsingSecondary, Item.cs:977) within the mouth dwell plus 1 s, with that player as the feed target.
    /// </summary>
    IEnumerator FeedFriendStep()
    {
        var friend = OtherPlayer(false);
        if (friend == null || friend.refs.ragdoll == null)
        {
            stepSkip = NoSecondPlayer;
            yield break;
        }
        yield return GiveAndHold(FindItem(SandboxActions.Items, AppleFragment));
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        var head = friend.refs.ragdoll.partDict[BodypartType.Head].transform;
        yield return MoveTo(left, () => head.position, 0.6f);
        yield return Until(
            () => HeldItem() == null || HeldItem().isUsingSecondary,
            HandOver.MouthDwell + 1f,
            "the feed did not start with the food held at their mouth"
        );
        if (!stepOk)
            yield break;
        stepNote =
            SocialHands.Instance.FeedTarget != null
            && SocialHands.Instance.FeedTarget.character == friend
                ? "feeding started with the trigger up"
                : "feeding started, feed target not that player";
    }

    /// <summary>
    /// vr.284: an Apple in hand, the hand brought into a backpack lying ahead, shows the would-go-in pin
    /// (<see cref="PackStash.Over"/>, verdict None); opening the grip there stashes it (Backpack.Stash, Backpack.cs:119)
    /// and the Apple is in the pack's pockets, not in hand nor on the ground.
    /// </summary>
    IEnumerator GroundPackStashStep()
    {
        var c = Character.localCharacter;
        Item packPrefab = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it is Backpack b && b.backpackType == BackpackSlot.BackpackType.Backpack)
                packPrefab = it;
        }
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        if (packPrefab == null || apple == null)
        {
            Fail("needs a Backpack and an Apple item");
            yield break;
        }
        var rig = VRRig.Instance;
        if (!ClearHeading(c, rig, out float turn, out string obstacle))
        {
            Fail("no open ground to drop the pack on in any heading: " + obstacle);
            yield break;
        }
        rig.RotateYaw(turn);
        yield return Wait(0.5f);
        if (!SandboxActions.SpawnItemAt(packPrefab, GroundAhead(PropSetOut)))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var pack = SandboxActions.LastSpawned.GetComponent<Backpack>();
        yield return Wait(0.8f);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        yield return ReachDown(left, () => pack.Center(), 0.6f);
        if (OverReach(left) is { } far)
        {
            Fail(far);
            yield break;
        }
        var stash = PackStash.Instance;
        yield return Until(
            () => stash.Over && stash.Verdict == DenyReason.None,
            1f,
            "no would-go-in cue with the Apple in the pack"
        );
        if (!stepOk)
        {
            stepNote = F(
                $"over {stash.Over}, verdict {stash.Verdict}, hand {Vector3.Distance(rig.Hand(left).position, pack.Center()):0.00} m from the pack"
            );
            yield break;
        }
        yield return ShotAt("vr-284-ground-pack-stash", pack.Center(), 1f);
        yield return GripTo(left, 0f, 0.3f);
        yield return Until(() => HeldItem() == null, 2f, "the Apple stayed in hand on release");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        int at = -1;
        var slots = pack.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && !slots[i].IsEmpty() && slots[i].prefab.itemID == apple.itemID)
                at = i;
        }
        var dropped = NewItem(apple.itemID);
        yield return StandUp(left);
        if (at < 0)
            Fail(
                dropped != null
                    ? "the Apple fell to the ground instead"
                    : "the Apple is not in the pack"
            );
        else
            stepNote = $"stashed into pocket {at} of the ground pack";
    }

    /// <summary>
    /// vr.285: an Apple let go at another player's worn pack goes into it (CharacterBackpackHandler.StashInBackpack,
    /// CharacterBackpackHandler.cs:127).
    /// </summary>
    IEnumerator FriendPackStashStep()
    {
        var friend = OtherPlayer(true);
        if (friend == null)
        {
            stepSkip = NoSecondPlayer;
            yield break;
        }
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        yield return GiveAndHold(apple);
        if (!stepOk)
            yield break;
        bool left = LocalHold.ItemInLeftHand;
        var visuals = friend.refs.backpackHandler.activeBackpackVisuals.transform;
        yield return GripTo(left, 1f, 0.1f);
        yield return MoveTo(left, () => visuals.position, 0.6f);
        yield return Until(
            () => PackStash.Instance.Over && PackStash.Instance.Verdict == DenyReason.None,
            1f,
            $"no would-go-in cue at the pack (verdict {PackStash.Instance.Verdict})"
        );
        if (!stepOk)
            yield break;
        yield return GripTo(left, 0f, 0.3f);
        yield return Until(() => HeldItem() == null, 2f, "the Apple stayed in hand on release");
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);
        bool inPack = false;
        foreach (var s in BackpackReference.GetFromEquippedBackpack(friend).GetData().itemSlots)
            inPack |= s != null && !s.IsEmpty() && s.prefab.itemID == apple.itemID;
        if (!inPack)
            Fail("the Apple is not in their pack");
        else
            stepNote = "stashed into their worn pack";
    }
}
