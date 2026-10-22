using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>
/// Step isolation for the scripted-hands steps: each one used to start with whatever the step before left, so vr.45
/// (after the arm-swing sprint) measured "standing: body moved 0.551 m" and vr.42 reached 0.83 m past its rope (run
/// 29), and vr.29 dropped its pack into the crashed plane because vr.15 left the view turned to its wall.
/// <see cref="Settle"/> lets go of everything, stands the player still on flat open ground at this segment's home
/// spot, facing the home heading, and recentres the view before the step body runs.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Horizontal hip speed (m/s) that counts as standing still.</summary>
    const float SettleSpeed = 0.05f;

    /// <summary>Seconds the body must stay grounded and still.</summary>
    const float SettleHold = 1f;

    /// <summary>Farther than this (m) from the home spot, the step starts with a warp back to it.</summary>
    const float SettleRadius = 2f;

    /// <summary>Steepest ground (deg from up) a home spot may have.</summary>
    const float SettleSlope = 20f;

    /// <summary>Clearance (m) around the body, at chest height, a home spot needs.</summary>
    const float SettleClear = 0.6f;

    /// <summary>Per segment: the flat, open ground spot hand steps start from, found by the first settle there.</summary>
    readonly Dictionary<Segment, (Vector3 Spot, Vector3 Facing)> settleHome = new();

    /// <summary>
    /// Releases grips, triggers, sticks and the scripted use; ends any wall, rope, vine or piton climb; warps back
    /// to the segment's home spot when the body strayed from it (the game's <c>WarpPlayerRPC</c>, Character.cs:1822,
    /// whose <c>WarpPlayer</c> also stops climbing, :1851); then waits until the body is grounded
    /// (<c>CharacterData.isGrounded</c>, CharacterData.cs:58), not sprinting (:87) and at rest (<see cref="AtRest"/>)
    /// for <see cref="SettleHold"/>, and recentres the rig. Without a home spot yet,
    /// the spot the body settles on becomes it when flat and open; otherwise the segment's base camp spawn
    /// (<c>MapHandler.CurrentBaseCampSpawnPoint</c>, MapHandler.cs:231, the target of the game's own segment jump,
    /// :960) is tried. A body that never settles fails the step with "not settled".
    /// </summary>
    IEnumerator Settle()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (c == null || rig == null)
        {
            Fail("not settled: no character or rig");
            yield break;
        }
        // Idle hands press nothing: grips, triggers and sticks at rest.
        VRInput.TestOverride = IdleHands;
        SelfTestInputPatch.HoldPrimary = false;
        var d = c.data;
        if (d.isClimbing || d.isRopeClimbing || d.isVineClimbing || d.currentClimbHandle != null)
            c.refs.climbing.StopAnyClimbing();

        var map = Singleton<MapHandler>.Instance;
        var segment = map != null ? map.GetCurrentSegment() : Segment.Beach;
        bool hasHome = settleHome.TryGetValue(segment, out var saved);
        var home = saved.Spot;
        bool warped = false;
        if (hasHome && Vector3.Distance(Feet(c), home) > SettleRadius)
        {
            yield return SettleWarp(c, home);
            warped = true;
        }
        yield return StandStill(c);
        string why = SettleProblem(c, needSpot: !hasHome);
        if (why != null && !warped)
        {
            // Off the home spot, or no good one yet: the home spot, else the game's own spawn for this segment.
            var spawn = MapHandler.CurrentBaseCampSpawnPoint;
            Vector3? target =
                hasHome ? home
                : spawn != null ? spawn.position
                : null;
            if (target is { } to)
            {
                yield return SettleWarp(c, to);
                yield return StandStill(c);
                why = SettleProblem(c, needSpot: !hasHome);
            }
        }
        if (why != null && settledFor < SettleHold)
        {
            Fail($"not settled: {why}");
            yield break;
        }
        if (!hasHome)
        {
            // A spot that is not flat or open still starts the step; it is not kept as home and the note says so.
            if (why == null)
                settleHome[segment] = (Feet(c), Flat(rig.Head.forward));
            else
                Note($"settled on poor ground ({why})");
        }
        else
        {
            // A step that turned to its wall or prop keeps that yaw through a warp: face the home heading again.
            rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), saved.Facing, Vector3.up));
        }

        bool recentered = false;
        Action onRecenter = () => recentered = true;
        rig.Recentered += onRecenter;
        rig.Recenter();
        yield return WaitFor(() => recentered, 3f);
        rig.Recentered -= onRecenter;
        if (!recentered)
            Fail("not settled: the rig did not recentre within 3 s");
        // The recentre runs under a short fade; let it clear before the step looks.
        yield return Wait(0.5f);
    }

    /// <summary>Seconds the body has been grounded and still, as of the last <see cref="StandStill"/>.</summary>
    float settledFor;

    /// <summary>Waits until the body has been grounded, not sprinting and still for <see cref="SettleHold"/>; the
    /// deadline is the fall to the ground under it plus that hold, pushed back while a stillness that started in time
    /// runs (run 30's vr.32 ran out still 0.6 of 1 s, every condition holding).</summary>
    IEnumerator StandStill(Character c)
    {
        float drop = GroundBelow(c.Center, 200f);
        float deadline =
            Now + GameTiming.Budget(GameTiming.FallSeconds(Mathf.Max(0f, drop)) + SettleHold, 1f);
        settledFor = 0f;
        while (
            Now
            < SelfTestChecks.SettleDeadline(
                deadline,
                Now,
                settledFor,
                SettleHold,
                SelfTestChecks.GameMargin
            )
        )
        {
            settledFor = AtRest(c) ? settledFor + Time.unscaledDeltaTime : 0f;
            if (settledFor >= SettleHold)
                yield break;
            yield return null;
        }
    }

    /// <summary>Grounded, under control (<c>currentRagdollControll</c>), not climbing or sprinting, hip slower than
    /// <see cref="SettleSpeed"/> horizontally, and the view's eye guard done with a spawn or warp
    /// (<see cref="Rig.RigDriver.ViewSettling"/>).</summary>
    static bool AtRest(Character c)
    {
        var d = c.data;
        var v = c.refs.hip.Rig.linearVelocity;
        return d.isGrounded
            && d.currentRagdollControll >= 0.9f
            && !d.isClimbingAnything
            && !d.isSprinting
            && new Vector2(v.x, v.z).magnitude < SettleSpeed
            && !Rig.RigDriver.ViewSettling;
    }

    /// <summary>Why the body is not settled on a usable spot, or null. <paramref name="needSpot"/> also asks for flat,
    /// open ground (a home spot candidate).</summary>
    string SettleProblem(Character c, bool needSpot)
    {
        var d = c.data;
        var v = c.refs.hip.Rig.linearVelocity;
        if (settledFor < SettleHold)
            return F(
                $"grounded {d.isGrounded}, control {d.currentRagdollControll:0.00}, climbing {d.isClimbingAnything}, sprinting {d.isSprinting}, hip {new Vector2(v.x, v.z).magnitude:0.00} m/s, view settling {Rig.RigDriver.ViewSettling}, still {settledFor:0.00} of {SettleHold:0.0} s at {c.Center}"
            );
        if (!needSpot)
            return null;
        if (
            !Physics.Raycast(
                c.Center,
                Vector3.down,
                out var ground,
                3f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return "no ground within 3 m under the body";
        float slope = Vector3.Angle(ground.normal, Vector3.up);
        if (slope > SettleSlope)
            return F($"ground {slope:0} deg from level at {ground.point}");
        var chest = ground.point + Vector3.up * 1.1f;
        for (int i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (
                Physics.Raycast(
                    chest,
                    dir,
                    out var hit,
                    SettleClear,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
                return F($"{hit.collider.name} {hit.distance:0.00} m away at chest height");
        }
        return null;
    }

    /// <summary>The game's warp (as its own segment jump sends it, MapHandler.cs:971), then its end.</summary>
    static IEnumerator SettleWarp(Character c, Vector3 to)
    {
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, to + Vector3.up * 0.1f, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
    }

    /// <summary>Ground under the body, or its centre when none is near.</summary>
    static Vector3 Feet(Character c) =>
        Physics.Raycast(
            c.Center,
            Vector3.down,
            out var hit,
            3f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point
            : c.Center;
}
