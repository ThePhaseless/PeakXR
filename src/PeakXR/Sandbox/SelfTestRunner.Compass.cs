using System;
using System.Collections;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>
/// Compass checks. The plain and pirate compasses have no use (no main interact, so <c>Item.CanUsePrimary</c> is
/// false, Item.cs:797-805): their needle is the mechanic. The warp compass's hold moves the player to another
/// segment, so it runs in the segment loop in place of the jump to that segment and strands no later step.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Degrees the needle may sit off its heading. It chases the heading at 10/s (CompassPointer.cs:55),
    /// so a still hand leaves it well inside this.</summary>
    const float CompassToleranceDeg = 10f;

    /// <summary>Game time for the needle to swing back from a 90 degree knock: at 10/s it is inside the tolerance
    /// in about 0.25 s.</summary>
    const float CompassSwingSeconds = 0.5f;

    /// <summary>
    /// use.Compass: after a second in the hand the needle points at its heading, and knocked 90 degrees off about
    /// the face normal it swings back. The angle error is noted both times.
    /// </summary>
    IEnumerator CompassHeading(CompassPointer compass)
    {
        var why = compass.needle == null ? "no needle" : CompassTarget(compass, out _);
        if (why != null)
        {
            if (compass.needle == null || compass.compassType == CompassPointer.CompassType.Warp)
                Fail(why);
            else
                stepSkip = why;
            yield break;
        }
        yield return Wait(1f);
        float settled = NeedleError(compass);
        var face = compass.transform.forward;
        compass.needle.rotation = Quaternion.AngleAxis(90f, face) * compass.needle.rotation;
        float knocked = NeedleError(compass);
        float knockAt = Now;
        yield return WaitFor(
            () => NeedleError(compass) <= CompassToleranceDeg,
            GameTiming.Budget(CompassSwingSeconds)
        );
        float error = NeedleError(compass);
        var note = F(
            $"{compass.compassType} needle {settled:0.0} deg off its heading after 1 s in the hand, knocked to {knocked:0} deg, {error:0.0} deg after {Now - knockAt:0.00} s (tolerance {CompassToleranceDeg:0} deg); face normal {Vector3.Angle(face, Vector3.up):0} deg from up"
        );
        if (settled > CompassToleranceDeg)
            Fail("needle not on its heading: " + note);
        else if (error > CompassToleranceDeg)
            Fail("needle did not swing back: " + note);
        else
            stepNote = note;
    }

    /// <summary>Degrees between the needle's forward and its heading; 180 when the heading has no direction.</summary>
    static float NeedleError(CompassPointer compass) =>
        CompassTarget(compass, out var heading) == null
            ? Vector3.Angle(compass.needle.forward, heading)
            : 180f;

    /// <summary>
    /// The heading as <c>CompassPointer.UpdateHeading</c> computes it: world +z, the way up the mountain, for Normal
    /// (CompassPointer.cs:41-42); the nearest suitcase above y 0, levelled, for Pirate (:59-79); projected on the
    /// compass face, whose normal is the pointer's forward (:54). Returns why it has no direction, else null.
    /// </summary>
    static string CompassTarget(CompassPointer compass, out Vector3 heading)
    {
        heading = Vector3.zero;
        var at = compass.transform.position;
        if (compass.compassType == CompassPointer.CompassType.Normal)
            heading = Vector3.forward;
        else if (compass.compassType == CompassPointer.CompassType.Pirate)
        {
            float nearest = float.MaxValue;
            foreach (var luggage in Luggage.ALL_LUGGAGE)
            {
                if (luggage == null || luggage.transform.position.y <= 0f)
                    continue;
                var to = luggage.Center() - at;
                if (to.magnitude < nearest)
                {
                    nearest = to.magnitude;
                    heading = new Vector3(to.x, 0f, to.z).normalized;
                }
            }
            if (heading == Vector3.zero)
                return "no suitcase above y 0 for the pirate needle to point at";
        }
        else
            return "a warp compass spins its needle (CompassPointer.cs:44-46) and has a use of its own";
        heading = Vector3.ProjectOnPlane(heading, compass.transform.forward);
        if (heading.magnitude < 0.2f)
            return "the compass face is edge-on to its heading, so the needle has no direction";
        heading.Normalize();
        return null;
    }

    /// <summary>
    /// The first item whose <c>Action_WarpToBiome</c> warps to a segment the loop jumps to after the beach. The Peak
    /// (its own ground fix-up) and Void (the Nadir load, Action_WarpToBiome.cs:17-27) are left out.
    /// </summary>
    static Item WarpCompass(out Segment target)
    {
        foreach (var item in SandboxActions.Items)
        {
            var warp = item.GetComponentInChildren<Action_WarpToBiome>(true);
            if (
                warp != null
                && warp.segmentToWarpTo != Segment.Peak
                && Array.IndexOf(SandboxActions.Segments, warp.segmentToWarpTo) > 0
            )
            {
                target = warp.segmentToWarpTo;
                return item;
            }
        }
        target = Segment.Void;
        return null;
    }

    /// <summary>Runs <see cref="WarpUse"/> and lets go of the item, whether the step passed, failed or timed out.</summary>
    IEnumerator WarpStep(Item item, Segment target)
    {
        yield return Run(
            "use." + item.name,
            120f + GameTiming.Budget(GameTiming.UseSeconds(item)),
            WarpUse(item, target)
        );
        SelfTestInputPatch.HoldPrimary = false;
        Safe("use.despawn", Despawn);
    }

    /// <summary>
    /// use.(warp compass): hold primary use. <c>Action_WarpToBiome.RunAction</c> (Action_WarpToBiome.cs:8-11, fired
    /// on press, hold, cast end, release or cancel, ItemAction.cs:33-57) RPCs <c>RPC_Warp</c>, which on the master
    /// jumps to <c>segmentToWarpTo</c> through <c>MapHandler.JumpToSegment</c> (:31-33, MapHandler.cs:921). Passes
    /// when the current segment is the target and the body moved; the loop's 06-segment step then checks the landing.
    /// </summary>
    IEnumerator WarpUse(Item item, Segment target)
    {
        var map = Singleton<MapHandler>.Instance;
        var from = map.GetCurrentSegment();
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
        var start = Character.localCharacter.Center;
        float useTime = GameTiming.UseSeconds(held);
        bool started = false;
        float progress = 0f;
        SelfTestInputPatch.HoldPrimary = true;
        float end = Now + GameTiming.Budget(useTime);
        while (Now < end && map.GetCurrentSegment() == from)
        {
            if (held != null)
            {
                started |= held.isUsingPrimary || held.finishedCast;
                progress = Mathf.Max(progress, held.castProgress);
            }
            yield return null;
        }
        SelfTestInputPatch.HoldPrimary = false;
        // A warp on release or cancel fires on this frame's release.
        yield return WaitFor(() => map.GetCurrentSegment() != from, 2f);
        var now = map.GetCurrentSegment();
        if (now != from)
        {
            yield return Wait(3f);
            yield return Until(() => Ready, 100f, "not ready after the warp");
            if (!stepOk)
                yield break;
            yield return Wait(3f);
            Recover();
        }
        float moved = Vector3.Distance(start, Character.localCharacter.Center);
        var note = F(
            $"primary use started {started}, cast progress {progress:0.00} of {useTime:0.0} s, segment {from} to {now} (the item warps to {target}), body moved {moved:0} m"
        );
        if (now != target)
            Fail("hold did not warp to the item's segment: " + note);
        else if (moved < 20f)
            Fail("segment changed but the body stayed: " + note);
        else
            stepNote = note;
        yield return DropHeld();
    }
}
