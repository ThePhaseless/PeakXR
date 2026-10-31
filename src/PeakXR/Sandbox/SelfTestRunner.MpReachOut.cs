using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Social;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The reach-out MP steps (#70): an open hand stretched toward the webbed or climbing host 2.5-3.5 m away, past
/// touch range (<see cref="SocialConfig.Reach"/>) and the hand glow, arms the game's own reach
/// (<see cref="SocialHands"/> UpdateReachOut). Bots never enter <c>Character.AllCharacters</c> (Character.cs:314-322),
/// so only the two-client test can show it.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The host is helpable (CharacterGrabbing.TargetCanBeHelped, :293): asked to get webbed when not, as
    /// <see cref="MpReach"/> does. SKIP when that has no effect.</summary>
    IEnumerator ReachOutWebbed()
    {
        var c = Character.localCharacter;
        var host = Host();
        if (host != null && !CharacterGrabbing.TargetCanBeHelped(host, c))
        {
            yield return Ask(MpRequest.Web);
            if (stepSkip != null)
                yield break;
            yield return WaitFor(() => CharacterGrabbing.TargetCanBeHelped(host, c), 3f);
        }
        if (host == null || !CharacterGrabbing.TargetCanBeHelped(host, c))
            stepSkip = "the host needs no help (web request had no effect)";
    }

    /// <summary>
    /// Walks back out to about 3 m from the host and faces them: past touch range and the glow, so only the reach-out
    /// can take a grip. <c>WalkTo</c> only closes in on its goal, so the goal is a point 3 m from the host on A's side.
    /// SKIP when A ends outside 2.5-3.5 m (centre to centre).
    /// </summary>
    IEnumerator ReachOutStandOff(bool freeHands = true)
    {
        if (freeHands)
        {
            yield return FreeHands();
            if (!stepOk)
                yield break;
        }
        var c = Character.localCharacter;
        var host = Host();
        if (host == null)
        {
            stepSkip = "no host";
            yield break;
        }
        var away = c.Center - host.Center;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f)
            away = -host.data.lookDirection_Flat;
        away.y = 0f;
        // Up to three walks, the next turned 40 degrees either way when one ends outside the band (MP run 15b: one walk
        // stopped at 2.45 m and vr.424 skipped).
        float d = 0f;
        for (int i = 0; i < ReachOutTurns.Length; i++)
        {
            var goal =
                host.Center + Quaternion.Euler(0f, ReachOutTurns[i], 0f) * away.normalized * 3f;
            yield return MpWalkBounded(() => goal, 0.2f, 8f);
            d = FlatDistance(c.Center, host.Center);
            if (d >= ReachOutNear && d <= ReachOutFar)
                break;
        }
        Face(host.Head);
        yield return Wait(0.3f);
        d = FlatDistance(c.Center, host.Center);
        if (d < ReachOutNear || d > ReachOutFar)
            stepSkip = F(
                $"A stood {d:0.00} m from the host after the walk out, not {ReachOutNear:0.0}-{ReachOutFar:0.0} m"
            );
    }

    /// <summary>The walk-out band (m, level, centre to centre): past touch range and the glow, inside the reach-out.</summary>
    const float ReachOutNear = 2.4f;

    const float ReachOutFar = 3.6f;

    static readonly float[] ReachOutTurns = { 0f, 40f, -40f };

    /// <summary>The <paramref name="left"/> hand on the reach-out's arm line toward <paramref name="world"/>, at
    /// <paramref name="stretch"/> of the arm it judges by, turned <paramref name="yawOff"/> degrees about up.</summary>
    void ReachOutPose(bool left, Vector3 world, float stretch, float yawOff = 0f)
    {
        var social = SocialHands.Instance;
        var shoulder = social.ReachOutShoulder(left);
        var to = VRRig.Instance.Origin.InverseTransformPoint(world) - shoulder;
        var dir = Quaternion.AngleAxis(yawOff, Vector3.up) * to.normalized;
        SetHand(left, shoulder + dir * (stretch * social.ReachOutArm));
    }

    /// <summary>Holds <see cref="ReachOutPose"/> for <paramref name="seconds"/>, re-read every frame, the grip open;
    /// fails when the reach-out picks anyone while <paramref name="mustNotPick"/>.</summary>
    IEnumerator ReachOutHold(
        bool left,
        Func<Vector3> world,
        float stretch,
        float seconds,
        float yawOff = 0f,
        bool mustNotPick = false
    )
    {
        Script(left).Grip = 0f;
        for (float end = Now + seconds; Now < end; )
        {
            ReachOutPose(left, world(), stretch, yawOff);
            yield return null;
            if (mustNotPick && SocialHands.Instance.ReachOutFriend(left) != null)
            {
                Fail(
                    F(
                        $"the reach-out picked the host at {stretch:0.00} stretch, {yawOff:0} deg off"
                    )
                );
                yield break;
            }
        }
    }

    /// <summary>A pick only counts for a grip after the dwell (<see cref="ReachOut.Dwell"/>): hold the pose past it.</summary>
    IEnumerator ReachOutArmed(bool left, Func<Vector3> world) =>
        ReachOutHold(left, world, 0.95f, ReachOut.Dwell + 0.1f);

    /// <summary>After a grip that must not reach: no Extension claim, no reach for 0.5 s, no catch.</summary>
    IEnumerator ReachOutNoCatch(bool left, string what)
    {
        var c = Character.localCharacter;
        if (PhysicalInteraction.HandAction(left) == GripPressAction.Extension)
        {
            Fail($"{what}: an extension took the grip");
            yield break;
        }
        for (float end = Now + 0.5f; Now < end; )
        {
            if (c.data.isReaching || c.data.sinceGrabFriend < 0.3f)
            {
                Fail($"{what}: a reach started (caught {c.data.sinceGrabFriend < 0.3f})");
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>The grip opens, back to rest.</summary>
    IEnumerator ReachOutOpen(bool left)
    {
        yield return GripTo(left, 0f, 0.1f);
        yield return Wait(0.3f);
    }

    static string ReachOutState(bool left)
    {
        var social = SocialHands.Instance;
        var c = Character.localCharacter;
        var host = Host();
        bool busy = PhysicalInteraction.HandBusy(c, left, out var probe);
        string aim = "?";
        if (host != null)
        {
            var shoulder = social.ReachOutShoulder(left);
            var hand = VRInput.Hand(left).Position;
            var target = VRRig.Instance.Origin.InverseTransformPoint(host.Center);
            aim = F(
                $"{ReachOut.AimDegrees(shoulder.N(), hand.N(), target.N()):0} deg off the centre, stretch {ReachOut.Stretch(shoulder.N(), hand.N(), social.ReachOutArm):0.00} of {social.ReachOutArm:0.00} m"
            );
        }
        return $"busy {busy} (probe {probe}), why {social.ReachOutWhy(left)}, picked {social.ReachOutFriend(left) != null}, {aim}";
    }

    /// <summary>vr.421-reach-out-cone: A turns 100 degrees away with the arm still on the webbed host. The pick is
    /// refused WrongAngle; a grip still starts the reach (turning back would catch), and UpdateReachLook cues
    /// WrongAngle with no catch.</summary>
    IEnumerator MpReachOutCone()
    {
        yield return ReachOutWebbed();
        if (stepSkip != null)
            yield break;
        yield return ReachOutStandOff();
        if (stepSkip != null || !stepOk)
            yield break;
        var c = Character.localCharacter;
        var host = Host();
        VRRig.Instance.RotateYaw(-100f);
        yield return Wait(0.5f);
        float off = Vector3.Angle(c.data.lookDirection, host.Center - c.Center);
        if (off <= SocialIntent.ReachConeDegrees + 5f)
        {
            stepSkip = F(
                $"the turn left the look {off:0} deg off the host, inside the cone's margin"
            );
            VRRig.Instance.RotateYaw(100f);
            yield break;
        }
        bool picked = false;
        for (float end = Now + 1f; Now < end && !picked; )
        {
            yield return ReachOutHold(false, () => host.Center, 0.95f, 0.1f);
            picked =
                SocialHands.Instance.ReachOutFriend(false) == host
                && SocialHands.Instance.ReachOutWhy(false) == DenyReason.WrongAngle;
        }
        if (!picked)
        {
            Fail("no WrongAngle pick on the host within 1 s: " + ReachOutState(false));
            VRRig.Instance.RotateYaw(100f);
            yield break;
        }
        yield return ReachOutArmed(false, () => host.Center);
        yield return GripTo(false, 1f, 0.1f);
        // After the press's own contact tick and the pre-press hover, so only UpdateReachLook's refusal counts.
        yield return Wait(0.2f);
        float mark = CueMark();
        if (PhysicalInteraction.HandAction(false) != GripPressAction.Extension)
            Fail($"the grip was not the reach-out's: {PhysicalInteraction.HandAction(false)}");
        // Polled every frame: a catch early in the wait would age past 0.3 s by its end.
        bool caught = false;
        while (
            stepOk
            && !CueLog.Capture.Fired(false, DenyReason.WrongAngle, mark, false, out _)
            && CueLog.Now < mark + 2f
        )
        {
            caught |= c.data.sinceGrabFriend < 0.3f;
            yield return null;
        }
        if (stepOk)
            yield return ExpectCue(false, DenyReason.WrongAngle, mark, false, 2f);
        if (stepOk && (caught || c.data.sinceGrabFriend < 0.3f))
            Fail("the reach caught the host while A looked away");
        var cue = cueNote;
        yield return ReachOutOpen(false);
        VRRig.Instance.RotateYaw(100f);
        if (stepOk)
            stepNote = F($"look {off:0} deg off: picked, WrongAngle; gripped: {cue}");
    }

    /// <summary>vr.422-reach-out-idle: B webbed in the look cone, but a half-bent arm, a full stretch 45 degrees off
    /// B, and a full gesture gripped before the dwell neither pick nor reach.</summary>
    IEnumerator MpReachOutIdle()
    {
        yield return ReachOutWebbed();
        if (stepSkip != null)
            yield break;
        yield return ReachOutStandOff();
        if (stepSkip != null || !stepOk)
            yield break;
        var host = Host();
        LookAtPoint(host.Center);
        try
        {
            yield return ReachOutHold(false, () => host.Center, 0.5f, 0.5f, 0f, true);
            if (!stepOk)
                yield break;
            yield return GripTo(false, 1f, 0.1f);
            yield return ReachOutNoCatch(false, "half-bent arm");
            if (!stepOk)
                yield break;
            yield return ReachOutOpen(false);

            yield return ReachOutHold(false, () => host.Center, 0.95f, 0.5f, 45f, true);
            if (!stepOk)
                yield break;
            yield return GripTo(false, 1f, 0.1f);
            yield return ReachOutNoCatch(false, "arm 45 deg off");
            if (!stepOk)
                yield break;
            yield return ReachOutOpen(false);

            // Snapped onto B and closed within 0.05 s: shorter than the dwell.
            Script(false).Grip = 0f;
            ReachOutPose(false, host.Center, 0.95f);
            yield return GripTo(false, 1f, 0.05f);
            yield return ReachOutNoCatch(false, "snap grip");
            if (!stepOk)
                yield break;
            yield return ReachOutOpen(false);
            stepNote = "half-bent, 45 deg off and a snap grip: no pick, no reach";
        }
        finally
        {
            EndGlance();
        }
    }

    /// <summary>vr.423-reach-out-hands-full: holding an item, the free hand's reach-out at the webbed host is
    /// refused HandsFull (the game's CanGrab, CharacterGrabbing.cs:319-321), the item red, and nothing catches.</summary>
    IEnumerator MpReachOutHandsFull()
    {
        yield return ReachOutWebbed();
        if (stepSkip != null)
            yield break;
        yield return FreeHands();
        if (!stepOk)
            yield break;
        // Any item at hand, passing over one the walk cannot reach (MP run 15b: the walk to the nearest stuck 1.1 m short).
        yield return GrabReachable(
            _ => true,
            null,
            NearItems,
            3,
            nextOnMiss: true,
            spawn: MpPlainItem
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var c = Character.localCharacter;
        if (c.data.currentItem == null)
        {
            stepSkip = "no item to hold";
            yield break;
        }
        yield return ReachOutStandOff(freeHands: false);
        if (stepSkip != null || !stepOk || c.data.currentItem == null)
        {
            stepSkip ??= c.data.currentItem == null ? "the item left the hand on the walk" : null;
            yield return FreeHands();
            yield break;
        }
        bool free = !LocalHold.ItemInLeftHand;
        var host = Host();
        LookAtPoint(host.Center);
        bool refused = false;
        for (float end = Now + 1f; Now < end && !refused; )
        {
            yield return ReachOutHold(free, () => host.Center, 0.95f, 0.1f);
            refused = SocialHands.Instance.ReachOutWhy(free) == DenyReason.HandsFull;
        }
        if (!refused)
            Fail("the free hand's reach-out never read HandsFull: " + ReachOutState(free));
        if (stepOk)
        {
            yield return ReachOutArmed(free, () => host.Center);
            float mark = CueMark();
            yield return GripTo(free, 1f, 0.1f);
            yield return ExpectCue(free, DenyReason.HandsFull, mark, true);
            for (float end = Now + 0.5f; stepOk && Now < end; )
            {
                if (c.data.isReaching || c.data.sinceGrabFriend < 0.3f)
                    Fail("a reach started while holding an item");
                yield return null;
            }
            if (stepOk)
                stepNote = "free hand refused HandsFull: " + cueNote;
        }
        EndGlance();
        yield return ReachOutOpen(free);
        yield return FreeHands();
    }

    /// <summary>vr.424-reach-out-far: the full gesture at the webbed host 3 m away, then a slow squeeze (0.45 s from
    /// open to the press, longer than the grace): the game's reach holds and catches them, and lets go on
    /// release.</summary>
    IEnumerator MpReachOutFar()
    {
        yield return ReachOutWebbed();
        if (stepSkip != null)
            yield break;
        yield return ReachOutStandOff();
        if (stepSkip != null || !stepOk)
            yield break;
        var c = Character.localCharacter;
        var host = Host();
        LookAtPoint(host.Center);
        try
        {
            yield return ReachOutHold(false, () => host.Center, 0.95f, 0.3f);
            if (SocialHands.Instance.ReachOutFriend(false) != host)
            {
                Fail("the gesture did not pick the host: " + ReachOutState(false));
                yield break;
            }
            // GripTo(false, 1f, 1.5f), with the arm held on the line: 0.3 to 0.6 takes 0.45 s.
            float start = Now;
            float pressAt = -1f;
            float reachAt = -1f;
            // The catch lands during the squeeze: Reach resets sinceGrabFriend only while the host is helpable
            // (CharacterGrabbing.cs:252-268), and its first tick clears the web, so by t = 1 it has grown past 0.3 s.
            float caughtAt = -1f;
            while (true)
            {
                float t = Mathf.Clamp01((Now - start) / 1.5f);
                Script(false).Grip = t;
                ReachOutPose(false, host.Center, 0.95f);
                if (pressAt < 0f && t >= 0.6f)
                    pressAt = Now;
                if (reachAt < 0f && c.data.isReaching)
                    reachAt = Now;
                if (caughtAt < 0f && reachAt >= 0f && c.data.sinceGrabFriend < 0.3f)
                    caughtAt = Now;
                if (t >= 1f && (reachAt >= 0f || Now - pressAt > 0.5f))
                    break;
                yield return null;
            }
            if (reachAt < 0f || reachAt - pressAt > 0.5f)
            {
                Fail(
                    F(
                        $"no reach within 0.5 s of the press (grip {PhysicalInteraction.HandAction(false)}): {ReachOutState(false)}"
                    )
                );
                yield break;
            }
            if (caughtAt < 0f)
            {
                yield return Until(
                    () => c.data.sinceGrabFriend < 0.3f,
                    3f,
                    F(
                        $"the reach-out never caught the host (look {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg off)"
                    )
                );
                if (!stepOk)
                    yield break;
                caughtAt = Now;
            }
            yield return WaitFor(
                () => !host.refs.afflictions.isStruggling,
                Mathf.Max(0f, caughtAt + 3f - Now)
            );
            if (host.refs.afflictions.isStruggling)
            {
                Fail("the reach-out caught the host, but the host was still webbed 3 s later");
                yield break;
            }
            float freedIn = Now - caughtAt;
            yield return GripTo(false, 0f, 0.1f);
            yield return WaitFor(() => !c.data.isReaching, 0.5f);
            if (c.data.isReaching)
                Fail("the reach went on 0.5 s after the grip opened");
            else
                stepNote = F(
                    $"slow squeeze from {Vector3.Distance(c.Center, host.Center):0.0} m: reached {reachAt - pressAt:0.00} s after the press, caught, free {freedIn:0.0} s later"
                );
        }
        finally
        {
            EndGlance();
        }
    }

    /// <summary>
    /// vr.420-reach-out-ledge (the issue's pose): A stands at a lip over a 2.3-4 m steep drop, the host warped
    /// onto the face below (<see cref="MpRequest.HangBelow"/>) and climbing (<see cref="MpRequest.StartClimb"/>), so
    /// helpable (TargetCanBeHelped: climbing below, CharacterGrabbing.cs:304). A looks down at them and reaches; the
    /// grip holds the game's reach, which catches and drags them up (CharacterGrabbing.Reach, :269-275).
    /// </summary>
    IEnumerator MpReachOutLedge()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        // Up to three lips, the nearest first, each walked round what blocks it (MP run 13: the straight walk stopped
        // 1.1 m short of the only one tried).
        var tried = new List<Vector3>();
        var stand = Vector3.zero;
        var outward = Vector3.zero;
        // Where A stops: on the level ground behind the lip's ground point that Lip checked, not on the point itself,
        // which can be at the edge (MP run 17, #95: the walk to the point ended 2.29 m under it, off the lip).
        var behind = Vector3.zero;
        float missed = 0f;
        for (int i = 0; i < 3; i++)
        {
            yield return FindLedge(c, tried);
            if (!ledgeFound)
                break;
            stand = ledgeStand;
            outward = ledgeOutward;
            behind = stand - outward * LedgeBack;
            tried.Add(stand);
            yield return MpWalkAround(() => behind, 0.2f, 15f);
            missed = FlatDistance(c.Center, behind);
            if (missed <= 0.6f)
                break;
        }
        if (tried.Count == 0)
        {
            stepSkip = F(
                $"no walkable lip over a {LedgeDropMin:0.0}-{LedgeDropMax:0.0} m steep drop within {LedgeRadius:0} m (1 m grid)"
            );
            yield break;
        }
        if (missed > 0.6f)
        {
            stepSkip = F(
                $"the walk stopped {missed:0.0} m short of the last of {tried.Count} lip(s) tried"
            );
            yield break;
        }
        Face(stand + outward * 3f);
        yield return Wait(0.5f);
        // At the lip when B is asked, not where the walk last stood (MP run 16, #95: A was 1.98 m from the lip it had
        // measured when B searched below it): one more walk in, then a SKIP with the distance.
        if (FlatDistance(c.Center, behind) > LedgeStandBy)
        {
            yield return MpWalkAround(() => behind, 0.2f, 10f);
            Face(stand + outward * 3f);
            yield return Wait(0.5f);
        }
        float atLip = FlatDistance(c.Center, behind);
        // On the lip's own ground too: the torso stands about 1 m over the feet, so well above that is A on something
        // higher next to it.
        if (atLip > LedgeStandBy || c.Center.y - stand.y > 1.4f || c.Center.y < stand.y)
        {
            stepSkip = F(
                $"A stands {atLip:0.00} m from the spot {LedgeBack:0.0} m behind the lip ({c.Center.y - stand.y:0.00} m over the lip's ground), not at it"
            );
            yield break;
        }
        var host = Host();
        // B looks for the face along the lip's own outward direction, not A's look (MP run 14, #95: along the look B
        // found no drop below a lip A had measured), and from the lip's own ground point, not from where B sees A.
        yield return Ask(MpRequest.HangBelow, dir: outward, from: stand);
        if (stepSkip != null)
        {
            stepSkip += F(
                $" (A's lip: {LipNote(stand, outward)} from ({stand.x:0.00}, {stand.y:0.00}, {stand.z:0.00}), A {FlatDistance(c.Center, stand):0.00} m from it on the level, look {Vector3.Angle(Flat(c.data.lookDirection), outward):0} deg off its outward)"
            );
            yield return BringHostBack(c, host);
            yield break;
        }
        yield return Wait(0.3f);
        yield return Ask(MpRequest.StartClimb);
        if (stepSkip != null)
        {
            yield return BringHostBack(c, host);
            yield break;
        }
        yield return WaitFor(() => host.data.isClimbing, 2f);
        if (!host.data.isClimbing || !CharacterGrabbing.TargetCanBeHelped(host, c))
        {
            stepSkip = F(
                $"the host is not climbing below (climbing {host.data.isClimbing}, {c.Center.y - host.Center.y:0.0} m below)"
            );
            yield return BringHostBack(c, host);
            yield break;
        }
        yield return LedgeReach(c, host);
        yield return BringHostBack(c, host);
    }

    /// <summary><see cref="MpReachOutLedge"/> from the lip, the host climbing below.</summary>
    IEnumerator LedgeReach(Character c, Character host)
    {
        LookAtPoint(host.Center);
        try
        {
            bool picked = false;
            for (float end = Now + 1f; Now < end && !picked; )
            {
                yield return ReachOutHold(false, () => NearestPart(host, false), 0.95f, 0.1f);
                picked = SocialHands.Instance.ReachOutFriend(false) == host;
            }
            if (!picked)
            {
                Fail("the reach down over the lip did not pick the host: " + ReachOutState(false));
                yield break;
            }
            yield return ReachOutArmed(false, () => NearestPart(host, false));
            float y = host.Center.y;
            yield return GripTo(false, 1f, 0.1f);
            yield return WaitFor(() => c.data.isReaching, 0.5f);
            if (!c.data.isReaching)
            {
                Fail(
                    $"no reach 0.5 s after the grip ({PhysicalInteraction.HandAction(false)}): {ReachOutState(false)}"
                );
                yield break;
            }
            yield return Until(
                () => c.data.sinceGrabFriend < 0.3f,
                3f,
                F(
                    $"the reach never caught the climbing host (look {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg off)"
                )
            );
            if (!stepOk)
                yield break;
            yield return WaitFor(() => host.Center.y - y >= 0.3f || !host.data.isClimbing, 3f);
            float rise = host.Center.y - y;
            if (rise < 0.3f && host.data.isClimbing)
            {
                Fail(F($"caught, but the host rose only {rise:0.00} m and still climbs"));
                yield break;
            }
            yield return GripTo(false, 0f, 0.1f);
            yield return WaitFor(() => !c.data.isReaching, 0.5f);
            if (c.data.isReaching)
                Fail("the reach went on 0.5 s after the grip opened");
            else
                stepNote = F(
                    $"reached down {c.Center.y - y:0.0} m over the lip: caught, host rose {rise:0.00} m (climbing {host.data.isClimbing})"
                );
        }
        finally
        {
            EndGlance();
            Script(false).Grip = 0f;
        }
    }

    /// <summary>After <see cref="MpRequest.HangBelow"/>, a host left on the face or below the lip is warped back onto
    /// A (<see cref="MpRequest.StandOn"/>) for the next steps.</summary>
    IEnumerator BringHostBack(Character c, Character host)
    {
        if (host == null || (!host.data.isClimbing && c.Center.y - host.Center.y <= 1f))
            yield break;
        var skip = stepSkip;
        yield return Ask(MpRequest.StandOn);
        stepSkip = skip;
    }

    /// <summary>The host's hand nearest this hand's shoulder by distance (the centre without a ragdoll): a target the
    /// arm line can stretch to.</summary>
    static Vector3 NearestPart(Character host, bool left)
    {
        var shoulder = SocialHands.Instance.ReachOutShoulder(left);
        var origin = VRRig.Instance.Origin;
        var best = host.Center;
        float bestSq = float.MaxValue;
        foreach (var type in new[] { BodypartType.Hand_L, BodypartType.Hand_R })
        {
            var p = HostPart(host, type);
            float sq = (origin.InverseTransformPoint(p) - shoulder).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }
        return best;
    }

    /// <summary>The nearest walkable spot within <see cref="LedgeRadius"/> with a lip in one of 8 headings over a steep drop:
    /// down casts on a 1 m grid, in the style of <see cref="FindWall"/>, one grid row per frame. Sets
    /// <see cref="ledgeFound"/>, <see cref="ledgeStand"/> and <see cref="ledgeOutward"/>. A spot within 3 m of one in
    /// <paramref name="tried"/> is passed over.</summary>
    IEnumerator FindLedge(Character c, List<Vector3> tried)
    {
        ledgeFound = false;
        ledgeStand = ledgeOutward = default;
        float best = float.MaxValue;
        var home = c.Center;
        int mask = HelperFunctions.terrainMapMask;
        for (float x = -LedgeRadius; x <= LedgeRadius; x += 1f)
        {
            for (float z = -LedgeRadius; z <= LedgeRadius; z += 1f)
            {
                float sq = x * x + z * z;
                if (sq > LedgeRadius * LedgeRadius || sq >= best)
                    continue;
                if (
                    !Physics.Raycast(
                        home + new Vector3(x, 30f, z),
                        Vector3.down,
                        out var ground,
                        60f,
                        mask,
                        QueryTriggerInteraction.Ignore
                    )
                    || ground.normal.y < 0.8f
                    || Tried(tried, ground.point)
                )
                    continue;
                for (int o = 0; o < 8; o++)
                {
                    var dir = Quaternion.Euler(0f, o * 45f, 0f) * Vector3.forward;
                    if (!Lip(ground.point, dir, mask))
                        continue;
                    best = sq;
                    ledgeStand = ground.point;
                    ledgeOutward = dir;
                    ledgeFound = true;
                    break;
                }
            }
            yield return null;
        }
    }

    static bool Tried(List<Vector3> tried, Vector3 at)
    {
        foreach (var t in tried)
        {
            if ((t - at).sqrMagnitude < 9f)
                return true;
        }
        return false;
    }

    /// <summary>How far from A the lip search looks (m), on a 1 m grid: a lip is within 0.7 m of a grid point, so the
    /// 2 m grid of MP run 12 stepped over most edges and found none within 40 m of the beach spawn.</summary>
    const float LedgeRadius = 60f;

    /// <summary>The drop under the lip (m): B is warped onto the face about 2.5 m under A's centre
    /// (<see cref="MpRequest.HangBelow"/>), so a shallower drop leaves B standing; deeper keeps B 2.5 m from A, inside the
    /// game's 4 m reach (CharacterGrabbing.cs:252), and is still found by the 4.5 m cast in <see cref="Lip"/>.</summary>
    const float LedgeDropMin = 2.3f;

    const float LedgeDropMax = 4f;

    /// <summary>How near (m, level) A stands to its spot behind the lip when B is asked to hang below it.</summary>
    const float LedgeStandBy = 0.5f;

    /// <summary>How far (m) behind the lip's ground point A stands: on the level ground <see cref="Lip"/> checked 0.6 m
    /// back, so a walk that overshoots its stop still ends on top. The edge is 0.3-1.0 m ahead, the 2.3-4 m drop well
    /// inside the game's 4 m reach.</summary>
    const float LedgeBack = 0.3f;

    bool ledgeFound;
    Vector3 ledgeStand;
    Vector3 ledgeOutward;

    /// <summary>What <see cref="Lip"/> measured at <paramref name="ground"/>: the drop 0.7 m out along
    /// <paramref name="dir"/>, for a refused hang-below's note.</summary>
    static string LipNote(Vector3 ground, Vector3 dir)
    {
        var past = ground + dir * 0.7f;
        return Physics.Raycast(
            past + Vector3.up * 0.5f,
            Vector3.down,
            out var low,
            5f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? F($"{ground.y - low.point.y:0.0} m drop 0.7 m out along ({dir.x:0.00}, {dir.z:0.00})")
            : "no ground 0.7 m out";
    }

    /// <summary>Walkable ground behind <paramref name="ground"/>, and 0.7 m out along <paramref name="dir"/> a drop of
    /// <see cref="LedgeDropMin"/>-<see cref="LedgeDropMax"/> m whose middle is a steep face.</summary>
    static bool Lip(Vector3 ground, Vector3 dir, int mask)
    {
        const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
        if (
            !Physics.Raycast(
                ground - dir * 0.6f + Vector3.up,
                Vector3.down,
                out var back,
                2f,
                mask,
                ignore
            )
            || back.normal.y < 0.8f
            || Mathf.Abs(back.point.y - ground.y) > 0.3f
        )
            return false;
        var past = ground + dir * 0.7f;
        if (!Physics.Raycast(past + Vector3.up * 0.5f, Vector3.down, out var low, 5f, mask, ignore))
            return false;
        float drop = ground.y - low.point.y;
        if (drop < LedgeDropMin || drop > LedgeDropMax)
            return false;
        var mid = past + Vector3.down * (drop * 0.5f);
        return Physics.Raycast(mid, -dir, out var face, 1f, mask, ignore)
            && Mathf.Abs(face.normal.y) < 0.5f;
    }
}
