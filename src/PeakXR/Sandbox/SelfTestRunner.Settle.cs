using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
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
    /// <summary>Net horizontal hip travel (m) over <see cref="SettleHold"/> that still counts as standing
    /// (<see cref="StillWindow"/>, shared with the MP settle).</summary>
    const float SettleDrift = 0.03f;

    /// <summary>Seconds over which the hip's net travel is judged (<see cref="StillWindow.Hold"/>).</summary>
    const float SettleHold = 2f;

    /// <summary>Seconds each end of that travel is averaged over: longer than PEAK 3.0's ~1 s idle sway.</summary>
    const float SettleSmooth = 1f;

    /// <summary>Farther than this (m) from the home spot, the step starts with a warp back to it.</summary>
    const float SettleRadius = 2f;

    /// <summary>Steepest ground (deg from up) a home spot may have.</summary>
    const float SettleSlope = 20f;

    /// <summary>Clearance (m) around the body, at chest height, a home spot needs.</summary>
    const float SettleClear = 0.6f;

    /// <summary>The longest <see cref="Settle"/> that still ends settled: a revive (5 s), one warp home (3.5 s), and on
    /// a poor spot two rounds of recentre (3 s, the eye guard's 4 s, the 0.5 s fade) and stand-still (its budget on
    /// flat ground plus a stillness that starts just before it runs out). Step budgets start from it, so a slow
    /// settle ends in its own "not settled" note instead of the step's timeout (run 46 settled in 2-5 s).</summary>
    internal static readonly float SettleSeconds =
        5f
        + 3.5f
        + 2f
            * (
                3f
                + 4f
                + 0.5f
                + GameTiming.Budget(SettleHold + SettleSmooth, 1f)
                + SettleHold
                + SettleSmooth
                + SelfTestChecks.GameMargin
            );

    /// <summary>Per segment: the flat, open ground spot hand steps start from, found by the first settle there.</summary>
    readonly Dictionary<Segment, (Vector3 Spot, Vector3 Facing)> settleHome = new();

    /// <summary>
    /// Releases grips, triggers, sticks and the scripted use; ends any wall, rope, vine or piton climb; warps back
    /// to the segment's home spot when the body strayed from it (the game's <c>WarpPlayerRPC</c>, Character.cs:1824,
    /// whose <c>WarpPlayer</c> also stops climbing, :1853); then waits until the body is grounded
    /// (<c>CharacterData.isGrounded</c>, CharacterData.cs:59), not sprinting (:88) and still (<see cref="StillTick"/>)
    /// for <see cref="StillWindow.Required"/>, and recentres the rig. Without a home spot yet,
    /// the spot the body settles on becomes it when flat and open; otherwise the segment's base camp spawn
    /// (<c>MapHandler.CurrentBaseCampSpawnPoint</c>, MapHandler.cs:231, the target of the game's own segment jump,
    /// :1076) is tried. A body that never settles fails the step with "not settled".
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
        yield return RestoreBody(c);
        if (!stepOk)
            yield break;
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
        ClearAround(c);
        // Recentre before waiting: a head off the avatar's eyes leans the body, and the lean's hold rocks it (run 32:
        // from vr.26 on, every settle saw the hip at 0.05-0.12 m/s on the home spot and never got to its recentre).
        if (hasHome)
            rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), saved.Facing, Vector3.up));
        yield return SettleRecenter(rig);
        yield return StandStill(c);
        string why = SettleProblem(c, needSpot: !hasHome);
        if (
            why != null
            && settledFor < stillWindow.Required
            && hasHome
            && Vector3.Distance(Feet(c), home) <= SettleRadius
        )
        {
            // The home spot itself does not hold a body still. Run 47: TheKiln's home put the eye in rock after each
            // warp ("Eye inside rock"), and the body crept off it at 1-1.5 cm/s, so vr.85, vr.90 and vr.91 failed
            // with the drift at the 3 cm limit (the window restarts the frame it crosses, so the note always reads
            // about 0.030). Drop it and find open ground instead of warping back onto it.
            settleHome.Remove(segment);
            hasHome = false;
            warped = false;
            Note(F($"home spot {home} dropped: the body would not stay still on it ({why})"));
        }
        if (why != null && !warped)
        {
            // Off the home spot, or no good one yet: the home spot, else open ground nearby, else the game's own
            // spawn for this segment.
            var spawn = MapHandler.CurrentBaseCampSpawnPoint;
            Vector3? target = hasHome
                ? home
                : OpenSpotNear(Feet(c)) ?? (spawn != null ? spawn.position : null);
            if (target is { } to)
            {
                yield return SettleWarp(c, to);
                ClearAround(c);
                yield return SettleRecenter(rig);
                yield return StandStill(c);
                why = SettleProblem(c, needSpot: !hasHome);
            }
        }
        if (why != null && settledFor < stillWindow.Required)
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
    }

    /// <summary>
    /// A normal living body before every step (run 33: ui.05's Cold was refused by a body left a skeleton, whose
    /// statuses only take Injury, Curse, Petrify, FlyTrap and Web, CharacterAfflictions.cs:685-692, :722-727). Dead
    /// or passed out: the game's revive (<see cref="SandboxActions.Revive"/>). A skeleton: the game's own
    /// <c>CharacterData.SetSkeleton(false)</c> (CharacterData.cs:699, as a skeleton's death does, Character.cs:751).
    /// Statuses and afflictions (an invincibility one refuses statuses too, :742): cleared. A local zombie body has no
    /// game path back, so the step fails. The note says what was restored.
    /// </summary>
    IEnumerator RestoreBody(Character c)
    {
        var restored = new List<string>();
        var d = c.data;
        if (d.dead || d.fullyPassedOut)
        {
            restored.Add(d.dead ? "revived from dead" : "woke from passed out");
            SandboxActions.Revive();
            yield return WaitFor(() => !c.data.dead && !c.data.fullyPassedOut, 5f);
            if (c.data.dead || c.data.fullyPassedOut)
            {
                Fail("not settled: the revive did not take: " + BodyState(c));
                yield break;
            }
        }
        if (d.isSkeleton)
        {
            restored.Add("skeleton back to flesh");
            d.SetSkeleton(false);
        }
        if (c.isZombie)
        {
            Fail("not settled: the local body is a zombie: " + BodyState(c));
            yield break;
        }
        if (Invincible(c))
            restored.Add("invincibility cleared");
        SandboxActions.ClearAll(c);
        if (restored.Count > 0)
        {
            yield return null;
            Note(
                "settle restored the body: " + string.Join(", ", restored) + "; now " + BodyState(c)
            );
        }
    }

    /// <summary>CharacterData.isInvincible (internal) as RecalculateInvincibility sets it (CharacterData.cs:542-556).</summary>
    static bool Invincible(Character c) =>
        c.refs.afflictions.HasAfflictionType(
            Peak.Afflictions.Affliction.AfflictionType.BingBongShield,
            out _
        )
        || c.refs.afflictions.HasAfflictionType(
            Peak.Afflictions.Affliction.AfflictionType.Invincibility,
            out _
        );

    /// <summary>What decides whether the game takes a status (CharacterAfflictions.AddStatus, :712-760).</summary>
    internal static string BodyState(Character c)
    {
        var d = c.data;
        return F(
            $"skeleton {d.isSkeleton}, zombie {c.isZombie}, dead {d.dead}, passed out {d.passedOut}/{d.fullyPassedOut}, invincible {Invincible(c)}, statuses locked {c.statusesLocked}, scoutmaster {c.isScoutmaster}"
        );
    }

    /// <summary>Recentres the rig (its fade included) and fails the step if it never does.</summary>
    IEnumerator SettleRecenter(VRRig rig)
    {
        bool recentered = false;
        Action onRecenter = () => recentered = true;
        rig.Recentered += onRecenter;
        rig.Recenter();
        yield return WaitFor(() => recentered, 3f);
        rig.Recentered -= onRecenter;
        if (!recentered)
            Fail("not settled: the rig did not recentre within 3 s");
        // The eye guard watches for 3 s after this very recentre (run 45: 47 of vr.32's 123 settle frames were
        // "view settling"); wait it out once, so the stillness window judges the body, not our own recentre.
        yield return WaitFor(() => !Rig.RigDriver.ViewSettling, 4f);
        // The recentre runs under a short fade; let it clear before the step looks.
        yield return Wait(0.5f);
    }

    /// <summary>Loose world items (<c>Item.itemState</c> Ground, Item.cs:286) within this of the feet go before a
    /// step: a prop step's spill or a dropped item underfoot would push the body.</summary>
    const float SettleClearRadius = 1.5f;

    /// <summary>Removes loose items near the feet; notes how many.</summary>
    void ClearAround(Character c)
    {
        var feet = Feet(c);
        int n = 0;
        foreach (var item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (
                item.itemState != ItemState.Ground
                || (item.Center() - feet).sqrMagnitude > SettleClearRadius * SettleClearRadius
            )
                continue;
            var view = item.GetComponent<PhotonView>();
            if (view != null && (view.IsMine || PhotonNetwork.IsMasterClient))
            {
                PhotonNetwork.Destroy(item.gameObject);
                n++;
            }
        }
        if (n > 0)
            Note(
                F($"settle removed {n} loose item(s) within {SettleClearRadius:0.0} m of the feet")
            );
    }

    /// <summary>Seconds the body has been grounded and still, as of the last <see cref="StandStill"/>.</summary>
    float settledFor;

    /// <summary>Waits until the body has been still (<see cref="StillTick"/>) for <see cref="StillWindow.Required"/>; the
    /// deadline is the fall to the ground under it plus that hold, pushed back while a stillness that started in time
    /// runs (run 30's vr.32 ran out still 0.6 of 1 s, every condition holding).</summary>
    IEnumerator StandStill(Character c)
    {
        float drop = GroundBelow(c.Center, 200f);
        float deadline =
            Now
            + GameTiming.Budget(
                GameTiming.FallSeconds(Mathf.Max(0f, drop)) + stillWindow.Required,
                1f
            );
        settledFor = 0f;
        stillWindow.Reset();
        System.Array.Clear(blocked, 0, blocked.Length);
        standFrames = 0;
        hipPeak = 0f;
        while (
            Now
            < SelfTestChecks.SettleDeadline(
                deadline,
                Now,
                settledFor,
                stillWindow.Required,
                SelfTestChecks.GameMargin
            )
        )
        {
            bool still = StillTick(c, out _);
            settledFor = stillWindow.StillFor;
            if (still)
                yield break;
            yield return null;
        }
    }

    /// <summary>The one stillness rule of both settles (offline <see cref="StandStill"/>, MP
    /// <c>MpStandStill</c>).</summary>
    readonly StillWindow stillWindow = new(SettleDrift, SettleHold, SettleSmooth);

    /// <summary>
    /// One frame of the shared stillness rule: grounded (<c>CharacterData.isGrounded</c>), under control
    /// (<c>currentRagdollControll</c> 0.9), not climbing or sprinting, the view's eye guard done with a spawn or warp
    /// (<see cref="Rig.RigDriver.ViewSettling"/>), and the hip's net horizontal travel within
    /// <see cref="SettleDrift"/>. Not hip speed: PEAK 3.0's idle sway moves a standing hip at 0.05-0.10 m/s around a
    /// fixed point (run 42: vr.32, vr.40 and vr.41 "not settled"; MP run 9 the same), while a walk, follow, slide or
    /// lean push carries it past the drift and restarts the window. <paramref name="why"/> is what restarted it this
    /// frame, or null. Counts frames per reason for the note. True once still for <see cref="StillWindow.Required"/>.
    /// </summary>
    bool StillTick(Character c, out string why)
    {
        var d = c.data;
        var hip = c.refs.hip.Rig;
        var v = hip.linearVelocity;
        standFrames++;
        hipPeak = Mathf.Max(hipPeak, new Vector2(v.x, v.z).magnitude);
        why =
            !d.isGrounded ? "not grounded"
            : d.currentRagdollControll < 0.9f ? F($"control {d.currentRagdollControll:0.00}")
            : d.isClimbingAnything ? "climbing"
            : d.isSprinting ? "sprinting"
            : Rig.RigDriver.ViewSettling ? "view settling"
            : null;
        int reason =
            !d.isGrounded ? 0
            : d.currentRagdollControll < 0.9f ? 1
            : d.isClimbingAnything ? 2
            : d.isSprinting ? 3
            : Rig.RigDriver.ViewSettling ? 5
            : -1;
        var p = hip.position;
        bool still = stillWindow.Update(new System.Numerics.Vector2(p.x, p.z), why == null, Now);
        if (why == null && stillWindow.Drift > SettleDrift)
        {
            why = F($"hip travelled {stillWindow.Drift:0.000} m");
            reason = 4;
        }
        if (reason >= 0)
            blocked[reason]++;
        return still;
    }

    /// <summary>Frames of the last stillness wait each <see cref="StillTick"/> reason restarted the window: grounded,
    /// control, climbing, sprinting, hip drift, view settling.</summary>
    readonly int[] blocked = new int[6];
    int standFrames;
    float hipPeak;

    /// <summary>Why the body is not settled on a usable spot, or null. <paramref name="needSpot"/> also asks for flat,
    /// open ground (a home spot candidate).</summary>
    string SettleProblem(Character c, bool needSpot)
    {
        var d = c.data;
        var v = c.refs.hip.Rig.linearVelocity;
        if (settledFor < stillWindow.Required)
        {
            var driver = AvatarPoseDriver.Of(c);
            var rig = VRRig.Instance;
            float eyeGap =
                rig != null && rig.Head != null ? Vector3.Distance(rig.Head.position, c.Head) : -1f;
            return F(
                $"grounded {d.isGrounded}, control {d.currentRagdollControll:0.00}, climbing {d.isClimbingAnything}, sprinting {d.isSprinting}, hip {new Vector2(v.x, v.z).magnitude:0.00} m/s (peak {hipPeak:0.00}), net drift {stillWindow.Drift:0.000} m, view settling {Rig.RigDriver.ViewSettling}, lean {(driver != null ? driver.LeanDegrees : 0f):0.0} deg, body hold {driver != null && driver.BodyHeld}, eye {eyeGap:0.00} m from the head, still {settledFor:0.00} of {stillWindow.Required:0.0} s at {c.Center}; frames of {standFrames} blocked by grounded {blocked[0]}, control {blocked[1]}, climbing {blocked[2]}, sprinting {blocked[3]}, hip drift {blocked[4]}, view settling {blocked[5]}"
            );
        }
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
        return OpenProblem(ground);
    }

    /// <summary>Heights (m over the ground) the open-ground ring is cast at: chest, and the head, which a chest-only
    /// check missed under TheKiln's overhang (run 47).</summary>
    static readonly float[] ClearHeights = { 1.1f, 1.75f };

    /// <summary>Head room (m over the ground) a spot needs straight up.</summary>
    const float SettleHeadroom = 2.2f;

    /// <summary>Why <paramref name="ground"/> is not flat, open ground to stand on, or null.</summary>
    static string OpenProblem(RaycastHit ground)
    {
        float slope = Vector3.Angle(ground.normal, Vector3.up);
        if (slope > SettleSlope)
            return F($"ground {slope:0} deg from level at {ground.point}");
        RaycastHit hit;
        foreach (float h in ClearHeights)
        {
            var at = ground.point + Vector3.up * h;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (
                    Physics.Raycast(
                        at,
                        dir,
                        out hit,
                        SettleClear,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                    return F($"{hit.collider.name} {hit.distance:0.00} m away {h:0.0} m up");
            }
        }
        if (
            Physics.Raycast(
                ground.point + Vector3.up * 0.5f,
                Vector3.up,
                out hit,
                SettleHeadroom - 0.5f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return F($"{hit.collider.name} overhead, {hit.distance + 0.5f:0.00} m up");
        return null;
    }

    static readonly float[] OpenSpotRings = { 1.5f, 3f, 5f };

    /// <summary>Flat, open ground (<see cref="OpenProblem"/>) in rings 1.5, 3 and 5 m around
    /// <paramref name="around"/>, nearest first, or null.</summary>
    static Vector3? OpenSpotNear(Vector3 around)
    {
        foreach (float r in OpenSpotRings)
        {
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (
                    Physics.Raycast(
                        around + dir * r + Vector3.up * 3f,
                        Vector3.down,
                        out var ground,
                        6f,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                    && OpenProblem(ground) == null
                )
                    return ground.point;
            }
        }
        return null;
    }

    /// <summary>The game's warp (as its own segment jump sends it, MapHandler.cs:1094), then its end.</summary>
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
