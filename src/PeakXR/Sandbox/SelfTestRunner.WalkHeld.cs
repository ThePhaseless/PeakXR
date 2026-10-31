using System.Collections;
using PeakXR.Avatar;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>vr.246: a held item stays welded while the stick walks the body into a wall, and rejoins its hand after
/// a reach into the wall let it go.</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// vr.246: an apple carried low in the right hand, the move stick forward for 3 s toward a wall 1.2 m ahead, then
    /// released: the apple never leaves its hand weld. Then the controller held out past the arm's reach walks into the
    /// wall for 2 s; with the wall removed and the stick let go, the apple is back in its weld within 1.5 s. The game pushes the held item with the walk on its own
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
            if (wall != null)
                Object.Destroy(wall);
        };
        // At the hip beside the body, as an item is carried while walking, so the face meets the wall first. Held items
        // collide with Map (Item.cs:726), and a controller 0.15 m ahead of the eyes put the 0.42 m apple on the wall
        // while the walk carried the body and its controller on: the hand sat 0.26 m off it and the weld let go (run 7).
        HeadFrame(out var head, out var ahead, out var right);
        SetHand(false, head - ahead * 0.2f + right * 0.3f + Vector3.down * 0.75f);
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
        var wallCollider = wall.GetComponent<Collider>();
        float wallGap = float.PositiveInfinity;
        float ctrlGap = float.PositiveInfinity;
        float headGap = float.PositiveInfinity;
        var from = c.Center;
        float start = Now;
        Script(move).Stick = new Vector2(0f, 1f);
        while (Now - start < 4f)
        {
            if (Now - start >= 3f)
                Script(move).Stick = Vector2.zero;
            released |= driver.HeldReleased;
            if (driver.HandTarget(false, out var target))
            {
                worstHand = Mathf.Max(worstHand, Vector3.Distance(hand.position, target));
                // 0 inside the wall: the controller, not the walk, pressed the apple on it (#17).
                ctrlGap = Mathf.Min(
                    ctrlGap,
                    Vector3.Distance(wallCollider.ClosestPoint(target), target)
                );
            }
            var eyes = VR.VRRig.Instance != null ? VR.VRRig.Instance.Head : null;
            if (eyes != null)
                headGap = Mathf.Min(
                    headGap,
                    Vector3.Distance(wallCollider.ClosestPoint(eyes.position), eyes.position)
                );
            worstItem = Mathf.Max(worstItem, driver.HeldItemGap);
            if (c.data.currentItem != null)
            {
                var middle = c.data.currentItem.Center();
                wallGap = Mathf.Min(
                    wallGap,
                    Vector3.Distance(wallCollider.ClosestPoint(middle), middle)
                );
            }
            yield return null;
        }
        Script(move).Stick = Vector2.zero;
        var note = F(
            $"walked {FlatDistance(c.Center, from):0.00} m toward a wall 1.2 m ahead, hand up to {worstHand:0.00} m from its controller, item {worstItem:0.00} m from its place, apple centre {wallGap:0.00} m from the wall (its controller-held hand {ctrlGap:0.00} m, the eyes {headGap:0.00} m), weld released {released}"
        );
        if (c.data.currentItem == null)
        {
            Fail("the apple left the hand: " + note);
            yield break;
        }
        if (released)
        {
            Fail("the apple left its hand weld during the walk: " + note);
            yield break;
        }
        // Then held out to read, past the arm's reach (vr.245's natural hold), and walked into the wall again: the
        // hand meets the wall and the weld may let go. With the wall gone and the stick let go it must rejoin, the
        // arm still short of its controller (live run of 0e8ad7e: free 22 s).
        SetHand(false, head + ahead * 0.45f + right * 0.15f + Vector3.down * 0.15f);
        yield return Wait(0.5f);
        bool releasedAtWall = false;
        start = Now;
        Script(move).Stick = new Vector2(0f, 1f);
        while (Now - start < 2f)
        {
            releasedAtWall |= driver.HeldReleased;
            yield return null;
        }
        Script(move).Stick = Vector2.zero;
        Object.Destroy(wall);
        start = Now;
        yield return WaitFor(() => !driver.HeldReleased, 1.5f);
        note += F(
            $"; held out to read and walked into the wall: released {releasedAtWall}, back in the weld {(driver.HeldReleased ? "never" : $"{Now - start:0.00} s after the stop")}"
        );
        if (c.data.currentItem == null)
            Fail("the apple left the hand: " + note);
        else if (driver.HeldReleased)
            Fail("the apple did not rejoin its hand after the wall: " + note);
        else
            stepNote = note;
    }
}
