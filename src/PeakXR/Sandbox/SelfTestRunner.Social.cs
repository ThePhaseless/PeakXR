using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
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

    /// <summary>A thorn a hand can pinch without reaching behind the head (run 34 drew one at 168°).</summary>
    const float OffViewMaxDegrees = 120f;

    IEnumerator SocialSteps()
    {
        yield return SocialStep("vr.100-thorn-pluck", 30f, ThornPluckStep());
        yield return SocialStep("vr.101-thorn-stuck", 30f, ThornStuckStep());
        yield return SocialStep("vr.102-tick-pluck", 30f, TickPluckStep());
        yield return SocialStep("vr.104-struggle", 30f, StruggleStep());
        yield return SocialStep("vr.105-give-up", 40f, GiveUpStep());
        yield return HandStep("vr.106-social-clean", 15f, SocialCleanStep());
    }

    /// <summary>GUIManager.currentInteractable (GUIManager.cs:300, internal getter).</summary>
    static System.Func<GUIManager, IInteractible> guiHovered;

    // Resolved on first use, so a renamed member fails this step only, not the runner's type.
    static IInteractible GuiHovered(GUIManager gui) =>
        (
            guiHovered ??= GameMember.Delegate<System.Func<GUIManager, IInteractible>>(
                typeof(GUIManager),
                "get_currentInteractable"
            )
        )(gui);

    /// <summary>
    /// vr.106: after the social steps the player is clean (no thorn stuck in, no stuck part) and vanilla's hover is
    /// whole: GUIManager has caught up with Interaction's hover (GUIManager.LateUpdate, :363-366, which a throwing
    /// HoverExit would stall every frame), no hold of the game's is left running with both hands open (a pluck the mod
    /// never let go, Interaction.cs:128-143) and no vanilla exception is logged in a quiet 1.5 s. The thorn and stuck
    /// counts only guard <see cref="RestoreSocial"/>, which clears both after every step.
    /// </summary>
    IEnumerator SocialCleanStep()
    {
        var c = Character.localCharacter;
        int stuckThorns = 0;
        foreach (var thorn in c.refs.afflictions.physicalThorns)
        {
            if (thorn != null && thorn.stuckIn)
                stuckThorns++;
        }
        int before = report.VanillaExceptions;
        yield return Wait(1.5f);
        var gui = GUIManager.instance;
        var interaction = Interaction.instance;
        bool caughtUp =
            gui == null
            || interaction == null
            || ReferenceEquals(GuiHovered(gui), interaction.currentHovered);
        int thrown = report.VanillaExceptions - before;
        var lingering = interaction != null ? interaction.currentHeldInteractible : null;
        bool pressed = c.input.interactIsPressed;
        var note =
            $"thorns stuck {stuckThorns}, stuck {c.IsStuck()}, hover caught up {caughtUp}, hold left {(lingering != null ? lingering.GetType().Name : "none")}, interact held {pressed}, vanilla exceptions {thrown}";
        if (
            stuckThorns > 0
            || c.IsStuck()
            || !caughtUp
            || lingering != null
            || pressed
            || thrown > 0
        )
            Fail(note);
        else
            stepNote = note;
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

    /// <summary>
    /// A spot on the player's own body the <paramref name="left"/> hand can pinch: within arm's reach of the head
    /// (<see cref="MpArmReach"/>; a thorn on the shin is 1.2 m down) and not on that hand's own arm, which no hand can
    /// pinch. The nearest ragdoll part (CharacterRagdoll.partDict, :14) names where the spot is.
    /// </summary>
    static bool SelfReachable(Character c, Vector3 at, bool left, Vector3 head)
    {
        if (Vector3.Distance(at, head) > MpArmReach)
            return false;
        Bodypart nearest = null;
        float bestSq = float.MaxValue;
        foreach (var part in c.refs.ragdoll.partDict.Values)
        {
            float sq = part != null ? (part.transform.position - at).sqrMagnitude : float.MaxValue;
            if (sq < bestSq)
            {
                bestSq = sq;
                nearest = part;
            }
        }
        if (nearest == null)
            return true;
        var t = nearest.partType;
        return left
            ? t is not (BodypartType.Arm_L or BodypartType.Elbow_L or BodypartType.Hand_L)
            : t is not (BodypartType.Arm_R or BodypartType.Elbow_R or BodypartType.Hand_R);
    }

    /// <summary>
    /// The only thorn stuck in the local body, between <paramref name="minAngle"/> and <paramref name="maxAngle"/>
    /// from the camera's aim and within reach of the <paramref name="left"/> hand (<see cref="SelfReachable"/>).
    /// Thorns are added (CharacterAfflictions.AddThorn, :1695, a random free spot) and any other stuck thorn is taken
    /// out (RemoveThorn, :1771), so the hand's nearest-thorn pick (SocialHands.Find) cannot take a neighbour instead of
    /// the one the step watches.
    /// </summary>
    static ThornOnMe OffViewThorn(
        Character c,
        float minAngle,
        float maxAngle,
        bool left,
        out float angle
    )
    {
        angle = 0f;
        var cam = MainCamera.instance != null ? MainCamera.instance.transform : null;
        if (cam == null)
            return null;
        var a = c.refs.afflictions;
        var stuck = new List<ThornOnMe>();
        for (int tries = 0; tries < 16; tries++)
        {
            ThornOnMe pick = null;
            stuck.Clear();
            foreach (var thorn in a.physicalThorns)
            {
                if (thorn == null || !thorn.stuckIn)
                    continue;
                stuck.Add(thorn);
                float off = Vector3.Angle(thorn.transform.position - cam.position, cam.forward);
                if (
                    pick == null
                    && !thorn.isArrow
                    && off > minAngle
                    && off <= maxAngle
                    && SelfReachable(c, thorn.transform.position, left, cam.position)
                )
                {
                    pick = thorn;
                    angle = off;
                }
            }
            foreach (var thorn in stuck)
            {
                if (thorn != pick)
                    a.RemoveThorn(thorn, removedByPlayer: false);
            }
            if (pick != null)
                return pick;
            a.AddThorn(0);
        }
        return null;
    }

    /// <summary>
    /// vr.100: a thorn on the player's own body, well off the view, is pinched and pulled back 14 cm; it comes out
    /// in the game's hold time (ThornOnMe.GetInteractTime, :192; removal :201). Vanilla's 5° camera cone
    /// (ThornOnMe.cs:121-138) would refuse it; the hand pick (SocialPatches.HandPick) aims the check at the thorn.
    /// Asserts the contact tick on the pinch, the game's hold on that thorn (a thorn can also pop out by itself,
    /// ThornOnMe.cs:70-90), the progress ramp while it comes out, and the thorn gone.
    /// </summary>
    IEnumerator ThornPluckStep()
    {
        var c = Character.localCharacter;
        const bool left = false;
        var thorn = OffViewThorn(c, OffViewDegrees, OffViewMaxDegrees, left, out float angle);
        if (thorn == null)
        {
            stepSkip =
                "no thorn off the view within the right hand's reach (AddThorn refused: invincible or none free)";
            yield break;
        }
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
        bool held = false;
        float end = Now + GameTiming.Budget(GameTiming.InteractSeconds(thorn, c), 1f);
        while (thorn.stuckIn && Now < end)
        {
            held |=
                Interaction.instance != null
                && ReferenceEquals(Interaction.instance.currentHeldInteractible, thorn);
            yield return null;
        }
        if (thorn.stuckIn)
        {
            var hand = VRRig.Instance.Hand(left).position;
            Fail(
                $"thorn {angle:0}° off the view still stuck after the pull (hand {Vector3.Distance(hand, thorn.transform.position):0.00} m from it, game hold started {held})"
            );
            yield break;
        }
        if (!held)
        {
            Fail(
                $"thorn {angle:0}° off the view came out without the game's hold on it (popped out by itself?)"
            );
            yield break;
        }
        yield return ExpectFeel(left, CueFeel.Progress, pull);
        if (stepOk)
            stepNote = $"thorn {angle:0}° off the view plucked by pinch and pull; {cueNote}";
    }

    /// <summary>
    /// vr.101: stuck (a foot held by the game's own stick, Character.TryStickBodypart, :2168), a pinch on a thorn is
    /// refused as the game refuses it (ThornOnMe.cs:123): Stuck at that hand, no tint (a body part, D10), and
    /// the thorn stays.
    /// </summary>
    IEnumerator ThornStuckStep()
    {
        var c = Character.localCharacter;
        const bool left = false;
        // Any angle a hand can reach, never behind the head.
        var thorn = OffViewThorn(c, 0f, OffViewMaxDegrees, left, out _);
        if (thorn == null)
        {
            stepSkip = "no thorn within the right hand's reach could be added";
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
        // The game refuses a new stick within 3 s of the last unstick (Character.cs:2166).
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
        yield return MoveTo(left, () => thorn.transform.position, 0.6f);
        LookAtHand(left);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.Stuck, mark, false);
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
        // On the right knee (Bugfix.cs:103), about 1.2 m below a standing head: the player bends for it.
        float reach = Vector3.Distance(tick.transform.position, cam.position);
        ushort bugId = tick.bugItem != null ? tick.bugItem.itemID : (ushort)0;
        const bool left = true;
        var knee = tick.transform.position;
        yield return ReachDown(left, () => tick != null ? tick.transform.position : knee, 0.6f);
        var over = OverReach(left);
        if (over != null)
        {
            Fail("stooped to the tick on the knee, " + over);
            yield break;
        }
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectFeel(left, CueFeel.Contact, mark);
        if (!stepOk)
            yield break;
        yield return Until(() => tick == null, 3f, $"tick {angle:0}° off the view not taken");
        if (!stepOk)
            yield break;
        // The pluck is the game's Interact: the master spawns the bug item into the hand (GameUtils.cs:263-277), so a
        // tick that only vanished (destroyed some other way) leaves the hand empty.
        yield return Until(
            () => HeldItem() != null && HeldItem().itemID == bugId,
            2f,
            $"tick gone but the hand holds {(HeldItem() != null ? HeldItem().name : "nothing")}, not the bug"
        );
        if (!stepOk)
            yield break;
        // The pinching hand keeps it (SocialHands.BeginPress, PhysicalInteraction.ExpectItemIn), not the other one.
        if (LocalHold.ItemInLeftHand != left)
            Fail($"the left hand pinched the tick but {HeldItem().name} went to the right hand");
        else
            stepNote =
                $"tick {angle:0}° off the view, {reach:0.00} m from the standing head, stooped and pinched off; holding {HeldItem().name} in the left hand";
    }

    /// <summary>
    /// vr.104: webbed (sandbox AddStatus Web 0.5; isStruggling, CharacterAfflictions.cs:199), hands shaken up and
    /// down: each stroke is a jump press, which tears 0.05 of the web (CharacterMovement.cs:657), and each is felt as
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
        // Hands still first: the web must hold, so a drop later is the strokes' (Web does not decay by itself,
        // CharacterAfflictions.cs:495-525).
        float still = a.GetCurrentStatus(STATUS.Web);
        yield return Wait(0.6f);
        float before = a.GetCurrentStatus(STATUS.Web);
        if (before < still)
        {
            Fail($"web fell {still:0.00} to {before:0.00} with the hands still");
            yield break;
        }
        float mark = CueMark();
        HeadFrame(out var head, out var forward, out var right);
        var l = Rest(head, forward, -right).Position;
        var r = Rest(head, forward, right).Position;
        var up = Vector3.up * 0.15f;
        // Each stroke is one jump press (SocialHands.UpdateStruggle), each jump tears 0.05 (CharacterMovement.cs:657);
        // a drop with no press in the last 0.3 s came from somewhere else.
        int presses = 0;
        int unexplained = 0;
        float lastPress = -10f;
        float web = before;
        for (int i = 0; i < 12 && a.isStruggling; i++)
        {
            var dy = i % 2 == 0 ? up : -up;
            // Both hands in about 0.1 s: ~3 m/s, well over the stroke speed (StruggleGesture.MinSpeed).
            float start = Now;
            var l0 = Script(true).Position;
            var r0 = Script(false).Position;
            for (float end = start + 0.15f; Now < end; )
            {
                float t = Mathf.Clamp01((Now - start) / 0.1f);
                SetHand(true, Vector3.Lerp(l0, l + dy, t));
                SetHand(false, Vector3.Lerp(r0, r + dy, t));
                yield return null;
                if (c.input.jumpWasPressed)
                {
                    presses++;
                    lastPress = Now;
                }
                float now = a.GetCurrentStatus(STATUS.Web);
                if (now < web - 0.001f && Now - lastPress > 0.3f)
                    unexplained++;
                web = now;
            }
        }
        float after = a.GetCurrentStatus(STATUS.Web);
        if (after >= before && a.isStruggling)
        {
            Fail($"web {before:0.00} did not drop ({after:0.00}) from the shaking");
            yield break;
        }
        if (presses == 0 || unexplained > 0)
        {
            Fail(
                $"web {before:0.00} to {after:0.00} with {presses} stroke press(es) and {unexplained} drop(s) no stroke explains"
            );
            yield break;
        }
        // 0.05 per jump; a stroke can land on a frame the jump is refused, never tear more than its share.
        if (before - after > presses * 0.05f + 0.01f)
        {
            Fail(
                $"web fell {before - after:0.00}, more than {presses} stroke(s) tear ({presses * 0.05f:0.00})"
            );
            yield break;
        }
        yield return ExpectFeel(true, CueFeel.Progress, mark, 0f);
        if (stepOk)
            stepNote =
                $"web {before:0.00} to {after:0.00} by {presses} stroke(s), held still with the hands still; {cueNote}";
    }

    /// <summary>
    /// vr.105: passed out (sandbox PassOut, Character.PassOut), a single grip must not speed up dying
    /// (Character.cs:1174-1178): it is refused with NeedsTwoHands and interact stays up. Both grips and both triggers
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
        yield return ExpectCue(false, DenyReason.NeedsTwoHands, mark, false, seen: false);
        if (!stepOk)
            yield break;
        // The death timer's slope (game seconds) is the outcome: 0.33/s while interact is held, else at most 0.1/s
        // (Character.cs:1174-1185).
        float t0 = Time.time;
        float d0 = c.data.deathTimer;
        for (float end = Now + 0.6f; Now < end; )
        {
            if (c.input.interactIsPressed)
            {
                Fail("a single grip held interact while passed out");
                yield break;
            }
            yield return null;
        }
        float gripRate = (c.data.deathTimer - d0) / Mathf.Max(Time.time - t0, 1e-3f);
        if (gripRate > GiveUpRate * 0.5f)
        {
            Fail(
                $"the death timer ran {gripRate:0.00}/s under a single grip (giving up runs {GiveUpRate:0.00}/s)"
            );
            yield break;
        }
        float squeeze = CueMark();
        Script(true).Grip = 1f;
        Script(true).Trigger = Script(false).Trigger = 1f;
        yield return Until(
            () => c.input.interactIsPressed,
            1f,
            "all four inputs did not hold interact"
        );
        float squeezeRate = 0f;
        if (stepOk)
        {
            float t1 = Time.time;
            float d1 = c.data.deathTimer;
            yield return Wait(0.6f);
            squeezeRate = (c.data.deathTimer - d1) / Mathf.Max(Time.time - t1, 1e-3f);
            if (squeezeRate < GiveUpRate * 0.8f)
                Fail(
                    $"the squeeze held interact but the death timer ran only {squeezeRate:0.00}/s (giving up runs {GiveUpRate:0.00}/s)"
                );
        }
        if (stepOk)
            yield return ExpectFeel(true, CueFeel.Progress, squeeze);
        Script(true).Trigger = Script(false).Trigger = 0f;
        Script(true).Grip = Script(false).Grip = 0f;
        if (stepOk)
            stepNote =
                $"single grip refused (death timer {gripRate:0.00}/s), squeeze gives up ({squeezeRate:0.00}/s, timer {c.data.deathTimer:0.00})";
    }

    /// <summary>Death timer per game second while a passed-out player holds interact (Character.cs:1177).</summary>
    const float GiveUpRate = 0.33f;
}
