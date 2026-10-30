using System.Collections;
using PeakXR.Avatar;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>Scripted-hand steps for how a held item sits in the hand and aims: the launchers no use.* step holds,
/// and an item held past the arm's reach.</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator HoldAimSteps()
    {
        // The AntiZooka is a two-handed tool: the step fires it one-handed, as the flat game allows.
        var twoHands = InteractConfig.Instance.RequireTwoHandsEntry;
        bool required = twoHands != null && twoHands.Value;
        if (twoHands != null)
            twoHands.Value = false;
        yield return ItemStep("vr.350-antizooka-barrel", "AntiZooka", 30f, BarrelHoldStep);
        if (twoHands != null)
            twoHands.Value = required;
        yield return ItemStep("vr.351-blowgun-barrel", "HealingDart", 25f, BarrelHoldStep);
        yield return ItemStep("vr.352-held-at-controller", "Flare", 20f, HeldAtControllerStep);
    }

    /// <summary>
    /// vr.350, vr.351 (#24, #25): a launcher no use.* step holds (the AntiZooka; the blowgun, HealingDart), given to the
    /// hand and aimed: with <c>Interaction.AimHoldAlignment</c> on its barrel lies along the hand's aim and its grip
    /// leaves the fist by at most <see cref="MaxGripTilt"/> (<see cref="CheckBarrelOnAim"/>). The AntiZooka then fires:
    /// holding the trigger through its use time spawns its prefab (Action_RaycastSpawnSomething.RunAction,
    /// Peak/Action_RaycastSpawnSomething.cs:35, from the hand in VR) within 1 m of the barrel's ray.
    /// </summary>
    IEnumerator BarrelHoldStep(Item prefab)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null)
        {
            stepSkip = "flat play";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.45f + Vector3.down * 0.15f);
        var spawner = item.GetComponent<global::Peak.Action_RaycastSpawnSomething>();
        // A shot needs rock in range; the hold alone needs only an aim.
        yield return AimHeld(
            c,
            left,
            ShooterPitches,
            AllYaws,
            () =>
                spawner == null
                || (
                    HeldRay(rig, left, out var from, out var along)
                    && Physics.Raycast(
                        from,
                        along,
                        spawner.maxDistance,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
        );
        if (!aimFound)
        {
            Fail($"no aim found for the {item.name}");
            yield break;
        }
        if (!stepOk || spawner == null)
        {
            Note($"{item.name} held along the aim");
            yield break;
        }
        string spawnName = spawner.prefabToSpawn.name + "(Clone)";
        var before = GameObject.Find(spawnName);
        HeldRay(rig, left, out var origin, out var ray);
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        GameObject shot = null;
        for (
            float end = Now + GameTiming.Budget(GameTiming.UseSeconds(item), 1f);
            Now < end && shot == null;

        )
        {
            yield return null;
            var found = GameObject.Find(spawnName);
            if (found != null && found != before)
                shot = found;
        }
        Script(left).Trigger = 0f;
        Script(left).TriggerTouch = false;
        if (shot != null)
            spawned.Add(shot);
        float off =
            shot != null ? Vector3.Cross(ray, shot.transform.position - origin).magnitude : -1f;
        string note = F(
            $"{item.name}: {(shot != null ? $"{spawnName} {off:0.00} m off the barrel's ray" : "nothing spawned")}"
        );
        if (shot == null)
            Fail("the AntiZooka did not fire: " + note);
        else if (off > 1f)
            Fail("the AntiZooka's shot left its barrel's line: " + note);
        else
            Note(note);
    }

    /// <summary>
    /// vr.352 (#23): a one-handed item held 0.5 m ahead of the eyes and 0.1 m down, past the arm's reach from the scout's
    /// low, set-back shoulder (about 0.79 m against 0.6 m): it must sit on its controller-held pose
    /// (<see cref="AvatarPoseDriver.HeldTarget"/>) within 4 cm, still in its weld, and no ghost hand may show for that
    /// hand. The item used to rest 0.12 m short of the controller, the ghost showing the rest.
    /// </summary>
    IEnumerator HeldAtControllerStep(Item prefab)
    {
        var c = Character.localCharacter;
        var driver = AvatarPoseDriver.Of(c);
        if (!VRSession.Active || driver == null)
        {
            stepSkip = "flat play or no pose driver";
            yield break;
        }
        yield return GiveAndHold(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        HeadFrame(out var head, out var forward, out _);
        SetHand(left, head + forward * 0.5f + Vector3.down * 0.1f);
        yield return Wait(1.5f);
        float worst = 0f;
        float ghost = 0f;
        bool released = false;
        for (float until = Now + 0.6f; Now < until; )
        {
            if (item != null && driver.HeldTarget(out var pos, out _))
                worst = Mathf.Max(worst, Vector3.Distance(item.transform.position, pos));
            ghost = Mathf.Max(ghost, ReachGhost.Alpha(left));
            released |= driver.HeldReleased;
            yield return null;
        }
        driver.ReachGap(left, out float gap);
        string note = F(
            $"{(item != null ? item.name : "no item")} {worst:0.000} m off its controller-held pose at most, ghost {ghost:0.00}, weld released {released}, the drawn arm's reach gap {gap:0.00} m"
        );
        if (item == null || worst > 0.04f)
            Fail("the held item sits off its controller: " + note);
        else if (ghost > 0f)
            Fail("a ghost hand showed for a held item the drawn arm reaches: " + note);
        else if (released)
            Fail("the weld let go of an item held still: " + note);
        else
            Note(note);
    }
}
