using System.Collections;
using PeakXR.Avatar;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>vr.246: a held item stays welded while the stick walks the body into a wall.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// vr.246: an apple carried low in the right hand, the move stick forward for 3 s toward a wall 1.2 m ahead, then
    /// released: the apple never leaves its hand weld. The game pushes the held item with the walk on its own
    /// (CharacterItems.AddMovementForce); with the body stopped by a wall that push held the welded hand 0.3 m off its
    /// controller and the weld let go on every walk (vr run of e7b3cd9, the airport's spawn corner).
    /// </summary>
    IEnumerator WalkHeldStep()
    {
        var c = Character.localCharacter;
        var driver = AvatarPoseDriver.Of(c);
        bool move = OffLeft;
        if (driver == null)
        {
            stepSkip = "no pose driver";
            yield break;
        }
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        if (apple == null || !SandboxActions.GiveItem(apple, false))
        {
            Fail("no apple to hold");
            yield break;
        }
        var forward = Flat(c.data.lookDirection);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "PeakXR_TestWallAhead";
        wall.layer = LayerMask.NameToLayer("Map");
        wall.transform.SetPositionAndRotation(
            c.data.groundPos + forward * 1.35f + Vector3.up * 1f,
            Quaternion.LookRotation(forward)
        );
        wall.transform.localScale = new Vector3(3f, 2f, 0.3f);
        Physics.SyncTransforms();
        bodyRestore = () =>
        {
            Script(move).Stick = Vector2.zero;
            Object.Destroy(wall);
        };
        // Low beside the body, as an item is carried while walking: the wall stops the chest before it meets the hand.
        HeadFrame(out var head, out var ahead, out var right);
        SetHand(false, head + ahead * 0.15f + right * 0.2f + Vector3.down * 0.45f);
        yield return WaitFor(() => c.data.currentItem != null, 3f);
        if (c.data.currentItem == null)
        {
            Fail("the apple never reached the hand");
            yield break;
        }
        // Past the equip's attach and the weld gate's own wait (AvatarPoseDriver.AttachSettle).
        yield return Wait(1.2f);
        bool released = driver.HeldReleased;
        float worstHand = 0f;
        float worstItem = 0f;
        var hand = c.refs.ragdoll.partDict[BodypartType.Hand_R].Rig;
        var from = c.Center;
        float start = Now;
        Script(move).Stick = new Vector2(0f, 1f);
        while (Now - start < 4f)
        {
            if (Now - start >= 3f)
                Script(move).Stick = Vector2.zero;
            released |= driver.HeldReleased;
            if (driver.HandTarget(false, out var target))
                worstHand = Mathf.Max(worstHand, Vector3.Distance(hand.position, target));
            worstItem = Mathf.Max(worstItem, driver.HeldItemGap);
            yield return null;
        }
        Script(move).Stick = Vector2.zero;
        var note = F(
            $"walked {FlatDistance(c.Center, from):0.00} m toward a wall 1.2 m ahead, hand up to {worstHand:0.00} m from its controller, item {worstItem:0.00} m from its place, weld released {released}"
        );
        if (c.data.currentItem == null)
            Fail("the apple left the hand: " + note);
        else if (released)
            Fail("the apple left its hand weld during the walk: " + note);
        else
            stepNote = note;
    }
}
