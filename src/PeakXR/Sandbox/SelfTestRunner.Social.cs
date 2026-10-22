using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Framework;
using Photon.Pun;
using UnityEngine;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakXR.Sandbox;

/// <summary>
/// WP4 (social) steps that need no second player, run solo with the sandbox: interact.social picks thorns and ticks
/// by hand wherever the head looks, refuses a pluck while stuck, feels each struggle stroke and guards giving up while
/// passed out. The steps with a friend (help-up, carry, kick, pack, feed) are in the two-client script
/// (SelfTestRunner.MpTest.cs).
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Off the vanilla thorn cone: 5° plus 15° solo (ThornOnMe.cs:131-133), with margin.</summary>
    const float OffViewDegrees = 25f;

    IEnumerator SocialSteps()
    {
        yield return SocialStep("vr.100-thorn-pluck", 30f, ThornPluckStep());
        yield return SocialStep("vr.101-thorn-stuck", 30f, ThornStuckStep());
        yield return SocialStep("vr.102-tick-pluck", 30f, TickPluckStep());
        yield return SocialStep("vr.104-struggle", 30f, StruggleStep());
        yield return SocialStep("vr.105-give-up", 40f, GiveUpStep());
    }

    /// <summary>A hand step with god mode off (its tick clears thorns and statuses), then the body put back: no
    /// thorns, ticks, webs or stuck parts, god mode on. Runs whether the step passed, failed or timed out.</summary>
    IEnumerator SocialStep(string id, float timeout, IEnumerator body)
    {
        if (VRRun && VR.VRSession.Active)
            SandboxActions.SetGodMode(false);
        yield return HandStep(id, timeout, body);
        Safe("social.restore", RestoreSocial);
    }

    static void RestoreSocial()
    {
        var c = Character.localCharacter;
        if (c == null)
            return;
        if (c.IsStuck())
            c.refs.view.RPC("RPCA_Unstick", RpcTarget.All);
        var bugs = new List<Bugfix>();
        foreach (var bug in Bugfix.AllAttachedBugs)
        {
            if (bug.Key != null && bug.Value == c)
                bugs.Add(bug.Key);
        }
        foreach (var bug in bugs)
            PhotonNetwork.Destroy(bug.gameObject);
        c.refs.afflictions.RemoveAllThorns();
        SandboxActions.ClearAll(c);
        SandboxActions.SetGodMode(true);
    }

    /// <summary>A thorn stuck in the local body farther than <paramref name="minAngle"/> from the camera's aim,
    /// adding thorns (CharacterAfflictions.AddThorn, :1695) until one is.</summary>
    static ThornOnMe OffViewThorn(Character c, float minAngle, out float angle)
    {
        angle = 0f;
        var cam = MainCamera.instance != null ? MainCamera.instance.transform : null;
        if (cam == null)
            return null;
        for (int tries = 0; tries < 6; tries++)
        {
            foreach (var thorn in c.refs.afflictions.physicalThorns)
            {
                if (thorn == null || !thorn.stuckIn || thorn.isArrow)
                    continue;
                angle = Vector3.Angle(thorn.transform.position - cam.position, cam.forward);
                if (angle > minAngle)
                    return thorn;
            }
            c.refs.afflictions.AddThorn(0);
        }
        return null;
    }

    /// <summary>
    /// vr.100: a thorn on the player's own body, well off the view, is pinched and pulled back 14 cm; it comes out
    /// in the game's hold time (ThornOnMe.GetInteractTime, :192; removal :201). Vanilla's 5° camera cone
    /// (ThornOnMe.cs:121-138) would refuse it; the hand pick (SocialPatches.HandPick) aims the check at the thorn.
    /// Asserts the contact tick on the pinch, the progress ramp while it comes out, and the thorn gone.
    /// </summary>
    IEnumerator ThornPluckStep()
    {
        var c = Character.localCharacter;
        var thorn = OffViewThorn(c, OffViewDegrees, out float angle);
        if (thorn == null)
        {
            stepSkip = "no thorn off the view (AddThorn refused: invincible or none free)";
            yield break;
        }
        const bool left = false;
        yield return MoveTo(left, () => thorn.transform.position, 0.6f);
        float touch = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectFeel(left, CueFeel.Contact, touch);
        if (!stepOk)
            yield break;
        var away = thorn.transform.position - c.Center;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
        float pull = CueMark();
        yield return MoveTo(left, () => thorn.transform.position + away * 0.14f, 0.3f);
        yield return Until(
            () => !thorn.stuckIn,
            GameTiming.Budget(GameTiming.InteractSeconds(thorn, c), 1f),
            $"thorn {angle:0}° off the view still stuck after the pull"
        );
        if (!stepOk)
            yield break;
        yield return ExpectFeel(left, CueFeel.Progress, pull);
        if (stepOk)
            stepNote = $"thorn {angle:0}° off the view plucked by pinch and pull; {cueNote}";
    }

    /// <summary>
    /// vr.101: stuck (a foot held by the game's own stick, Character.TryStickBodypart, :2168), a pinch on a thorn is
    /// refused as the game refuses it (ThornOnMe.cs:123): StuckToHand at that hand, no tint (a body part, D10), and
    /// the thorn stays.
    /// </summary>
    IEnumerator ThornStuckStep()
    {
        var c = Character.localCharacter;
        var thorn = OffViewThorn(c, 0f, out _);
        if (thorn == null)
        {
            stepSkip = "no thorn could be added";
            yield break;
        }
        var stick = GameMember.Method(
            typeof(Character),
            "TryStickBodypart",
            [typeof(Bodypart), typeof(Vector3), typeof(STATUS), typeof(float)]
        );
        if (!c.refs.ragdoll.partDict.TryGetValue(BodypartType.Foot_R, out var foot))
        {
            Fail("no right foot");
            yield break;
        }
        // The game refuses a new stick within 3 s of the last unstick (Character.cs:2170).
        float end = Now + 5f;
        while (!c.IsStuck() && Now < end)
        {
            stick.Invoke(c, [foot, foot.transform.position, STATUS.Injury, 0f]);
            yield return Wait(0.5f);
        }
        if (!c.IsStuck())
        {
            Fail("could not stick the foot");
            yield break;
        }
        const bool left = false;
        yield return MoveTo(left, () => thorn.transform.position, 0.6f);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.StuckToHand, mark, false);
        if (!stepOk)
            yield break;
        yield return Wait(1.5f);
        if (!thorn.stuckIn)
            Fail("the thorn came out while stuck");
        else
            stepNote = "stuck: pinch refused; " + cueNote;
    }

    /// <summary>
    /// vr.102: a tick on the player's knee (sandbox BugfixOnYou, Bugfix.AttachBug) is pinched off by hand wherever the
    /// head looks: vanilla's 2° camera cone (Bugfix.cs:123-132) follows the hand, the game's Interact
    /// (Bugfix.cs:134) puts the bug item in the hand and removes the tick.
    /// </summary>
    IEnumerator TickPluckStep()
    {
        var c = Character.localCharacter;
        if (!SandboxActions.SpawnMob("BugfixOnYou"))
        {
            stepSkip = "could not spawn a tick: " + SandboxActions.LastMessage;
            yield break;
        }
        Bugfix tick = null;
        yield return Until(
            () =>
            {
                foreach (var bug in Bugfix.AllAttachedBugs)
                {
                    if (bug.Key != null && bug.Value == c)
                        tick = bug.Key;
                }
                return tick != null;
            },
            5f,
            "the tick never attached"
        );
        if (!stepOk)
            yield break;
        var cam = MainCamera.instance.transform;
        float angle = Vector3.Angle(tick.transform.position - cam.position, cam.forward);
        const bool left = true;
        yield return MoveTo(left, () => tick != null ? tick.transform.position : c.Center, 0.6f);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectFeel(left, CueFeel.Contact, mark);
        if (!stepOk)
            yield break;
        yield return Until(() => tick == null, 3f, $"tick {angle:0}° off the view not taken");
        if (stepOk)
            stepNote =
                $"tick {angle:0}° off the view pinched off, holding {(HeldItem() != null ? HeldItem().name : "nothing")}";
    }

    /// <summary>
    /// vr.104: webbed (sandbox AddStatus Web 0.5; isStruggling, CharacterAfflictions.cs:199), hands shaken up and
    /// down: each stroke is a jump press, which tears 0.05 of the web (CharacterMovement.cs:663), and each is felt as
    /// a progress tick.
    /// </summary>
    IEnumerator StruggleStep()
    {
        var c = Character.localCharacter;
        var a = c.refs.afflictions;
        if (!SandboxActions.AddStatus(STATUS.Web, 0.5f))
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => a.isStruggling, 3f, "not struggling after the web");
        if (!stepOk)
            yield break;
        float before = a.GetCurrentStatus(STATUS.Web);
        float mark = CueMark();
        HeadFrame(out var head, out var forward, out var right);
        var l = Rest(head, forward, -right).Position;
        var r = Rest(head, forward, right).Position;
        var up = Vector3.up * 0.15f;
        for (int i = 0; i < 12 && a.isStruggling; i++)
        {
            var dy = i % 2 == 0 ? up : -up;
            // Both hands in about 0.1 s: ~3 m/s, well over the stroke speed (StruggleGesture.MinSpeed).
            float start = Now;
            var l0 = Script(true).Position;
            var r0 = Script(false).Position;
            while (Now - start < 0.1f)
            {
                float t = (Now - start) / 0.1f;
                SetHand(true, Vector3.Lerp(l0, l + dy, t));
                SetHand(false, Vector3.Lerp(r0, r + dy, t));
                yield return null;
            }
            yield return Wait(0.05f);
        }
        float after = a.GetCurrentStatus(STATUS.Web);
        if (after >= before && a.isStruggling)
        {
            Fail($"web {before:0.00} did not drop ({after:0.00}) from the shaking");
            yield break;
        }
        yield return ExpectFeel(true, CueFeel.Progress, mark, 0f);
        if (stepOk)
            stepNote = $"web {before:0.00} to {after:0.00} by shaking; {cueNote}";
    }

    /// <summary>
    /// vr.105: passed out (sandbox PassOut, Character.PassOut), a single grip must not speed up dying
    /// (Character.cs:1177-1181): it is refused with NeedsTwoHands and interact stays up. Both grips and both triggers
    /// give up on purpose: interact is held and the progress ramp follows the death timer.
    /// </summary>
    IEnumerator GiveUpStep()
    {
        var c = Character.localCharacter;
        if (!SandboxActions.PassOut())
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => c.data.fullyPassedOut, 6f, "never fully passed out");
        if (!stepOk)
            yield break;
        float mark = CueMark();
        yield return GripTo(false, 1f, 0.1f);
        yield return ExpectCue(false, DenyReason.NeedsTwoHands, mark, false);
        if (!stepOk)
            yield break;
        for (int i = 0; i < 20; i++)
        {
            if (c.input.interactIsPressed)
            {
                Fail("a single grip held interact while passed out");
                yield break;
            }
            yield return null;
        }
        float squeeze = CueMark();
        Script(true).Grip = 1f;
        Script(true).Trigger = Script(false).Trigger = 1f;
        yield return Until(
            () => c.input.interactIsPressed,
            1f,
            "all four inputs did not hold interact"
        );
        if (stepOk)
            yield return ExpectFeel(true, CueFeel.Progress, squeeze);
        Script(true).Trigger = Script(false).Trigger = 0f;
        Script(true).Grip = Script(false).Grip = 0f;
        if (stepOk)
            stepNote =
                $"single grip refused, squeeze gives up (death timer {c.data.deathTimer:0.00})";
    }
}
