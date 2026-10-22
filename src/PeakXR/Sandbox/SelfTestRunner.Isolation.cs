using System.Collections;
using System.Collections.Generic;
using Peak;
using Peak.Island;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>
/// Keeps each <c>mob.*</c> step to its own effect (a blind or an AOE from one mob used to carry into every later
/// step), asks plain spawns for a minimal sign of life, and checks where a segment jump puts the player.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Mob names (substrings) with nothing cheap to observe: placed props, anchors, ropes and flags.</summary>
    static readonly string[] SpawnOnly = { "Flag_", "Rope", "_Placed" };

    HashSet<int> aoeBefore = new();
    HashSet<int> fieldsBefore = new();

    /// <summary>Clears the player's statuses, afflictions and screen statuses, then fails the step if the player is
    /// still blinded when it starts.</summary>
    IEnumerator Isolated(IEnumerator body)
    {
        var c = Character.localCharacter;
        SandboxActions.ClearAll(c);
        aoeBefore = Ids<AOE>();
        fieldsBefore = Ids<GhostBallExplosionField>();
        SnapshotItems();
        // IllegalScreenEffect.Update clears isBlind on its next frame (IllegalScreenEffect.cs:58-62).
        yield return null;
        yield return null;
        if (c != null && c.data.isBlind)
        {
            Fail("player blinded before the step started");
            yield break;
        }
        yield return body;
    }

    /// <summary>After a mob step: effects and items it left in the world, and what it left on the player.</summary>
    void ClearLeftovers()
    {
        DestroyFresh<AOE>(aoeBefore);
        DestroyFresh<GhostBallExplosionField>(fieldsBefore);
        SweepNewItems();
        SandboxActions.ClearAll(Character.localCharacter);
    }

    /// <summary>
    /// A plain spawn watched for 3 s, or for the game time its sign needs (<see cref="GameTiming"/>). It passes on
    /// any sign the game ran it: it moved or came closer, it targeted
    /// the player (<c>Scoutmaster.currentTarget</c>, Scoutmaster.cs:61; <c>BeeSwarm.currentAggroCharacter</c>,
    /// BeeSwarm.cs:22; <c>MushroomZombie._currentTarget</c>, MushroomZombie.cs:30), it blinded the player, it grew
    /// (a vine), or it removed itself (an explosion). <see cref="SpawnOnly"/> names only have to spawn.
    /// A level zombie sleeps until a player within 30 m looks at it (MushroomZombie.cs:517), gets up for 5 s
    /// (MushroomZombie.cs:649) and idles 2 s before it picks a target (MushroomZombie.cs:590-596), so it gets its
    /// <c>initialWakeUpTime</c> (MushroomZombie.cs:38) plus that idle. A vine gets the time to grow
    /// <see cref="MinGrowth"/> at its <c>growingSpeed</c>.
    /// A raven only steals the voice of a victim the gloom assigns, so the step assigns the player and starts the theft
    /// as <c>GloomVoiceConfuser</c> does (GloomVoiceConfuser.cs:147, 345-348) and checks the theft took.
    /// </summary>
    IEnumerator MobBehaviour(MobEntry entry, GameObject go)
    {
        var c = Character.localCharacter;
        var start = go.transform.position;
        float size = BoundsSize(go);
        float near = Vector3.Distance(start, c.Center);
        float moved = 0f;
        float closest = near;
        float grew = 0f;
        bool targeted = false;
        bool blinded = false;
        var scout = go.GetComponentInChildren<Scoutmaster>();
        var swarm = go.GetComponentInChildren<BeeSwarm>();
        var zombie = go.GetComponentInChildren<MushroomZombie>();
        var raven = go.GetComponentInChildren<VoiceThief>();
        int actor = c.photonView.Owner.ActorNumber;
        bool stole = false;
        if (raven != null)
        {
            raven.AssignVictim(actor, 10f);
            // SetActivelyStealing overrides the victim's voice transform (VoiceThief.cs:182).
            if (c.GetComponentInChildren<CharacterVoiceTransformProvider>() != null)
                raven.SetActivelyStealing(true);
        }
        // A vine's length is its own state, the origin's y scale (MagicBeanVine.cs:42). Its renderer bounds are not:
        // read at spawn they still held the prefab's full-length pose (vr24 frame), so a growing vine measured
        // "grew 0.00 m" (vr25) while its frame showed it grown.
        var vine = go.GetComponentInChildren<MagicBeanVine>();
        var vineOrigin = vine != null ? vine.vineOriginTransform : null;
        float vineStart = vineOrigin != null ? vineOrigin.localScale.y : 0f;
        // BLIND (explosions, flash plants): in VR the view must never wash out (comfort.events, vr.34).
        var wash = PeakXR.VR.VRSession.Active ? BlindWashRenderer() : null;
        bool washed = false;
        float window =
            zombie != null ? GameTiming.Budget(GameTiming.ZombieWakeSeconds(zombie))
            : vineOrigin != null ? GameTiming.Budget(GameTiming.VineGrowSeconds(vine, MinGrowth))
            : 3f;
        float end = Now + window;
        // The wake waits on the player's look (MushroomZombie.cs:517), so the rise is timed from the wake itself.
        float wokeAfter = -1f;
        float spawnedAt = Now;
        while (
            Now < end
            && go != null
            && !(zombie != null && targeted)
            && !(vineOrigin != null && grew > MinGrowth)
        )
        {
            var p = go.transform.position;
            moved = Mathf.Max(moved, Vector3.Distance(p, start));
            closest = Mathf.Min(closest, Vector3.Distance(p, c.Center));
            grew = Mathf.Max(
                grew,
                vineOrigin != null ? vineOrigin.localScale.y - vineStart : BoundsSize(go) - size
            );
            targeted |=
                (scout != null && scout.currentTarget == c)
                || (swarm != null && swarm.currentAggroCharacter == c)
                || (zombie != null && zombie._currentTarget == c);
            blinded |= c.data.isBlind;
            stole |=
                raven != null
                && raven.CurrentlyStealing
                && VoiceThief.Assignments.TryGetValue(actor, out var thief)
                && thief == raven;
            if (wash != null && c.data.isBlind)
                washed |= BlindWashedOut(wash);
            if (
                zombie != null
                && wokeAfter < 0f
                && zombie.currentState != MushroomZombie.State.Sleeping
            )
            {
                wokeAfter = Now - spawnedAt;
                end = Mathf.Max(end, Now + GameTiming.Budget(GameTiming.ZombieRiseSeconds(zombie)));
                window = end - spawnedAt;
            }
            yield return null;
        }
        bool gone = go == null;
        if (!gone)
            yield return Shot("05-mob-" + entry.Name);
        var note =
            $"moved {moved:0.00} m, closest {closest:0.0} m (from {near:0.0}), targeted {targeted}, "
            + $"blinded the player {blinded}, grew {grew:0.00} m, gone {gone}, watched {window:0.0} s"
            + (
                vine != null
                    ? $", vine {vineStart:0.00} m at the start, growingSpeed {vine.growingSpeed:0.0} m/s"
                    : ""
            )
            + (zombie != null ? $", zombie {zombie.currentState}, {ZombieWoke(wokeAfter)}" : "")
            + (raven != null ? $", stealing the player's voice {stole}" : "");
        bool spawnOnly = false;
        foreach (var s in SpawnOnly)
            spawnOnly |= entry.Name.Contains(s, System.StringComparison.Ordinal);
        bool alive =
            moved > 0.3f
            || near - closest > 0.3f
            || targeted
            || blinded
            || grew > MinGrowth
            || gone
            || stole;
        if (washed)
            Fail("BLIND washed out the view: " + note);
        else if (spawnOnly)
            stepNote = "spawned only (nothing to observe); " + note;
        else if (!alive)
            Fail("no sign of behaviour: " + note);
        else
            stepNote = note;
    }

    static string ZombieWoke(float after) =>
        after < 0f ? "never woke" : F($"woke after {after:0.0} s");

    /// <summary>Growth (m) that counts as a sign of life.</summary>
    const float MinGrowth = 0.2f;

    static float BoundsSize(GameObject go)
    {
        if (go == null)
            return 0f;
        bool any = false;
        var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled)
                continue;
            if (any)
                b.Encapsulate(r.bounds);
            else
                b = r.bounds;
            any = true;
        }
        return any ? b.size.magnitude : 0f;
    }

    /// <summary>
    /// After a segment jump: the body stands on ground (a terrain or map hit within 2.5 m under the torso) and is
    /// grounded once it has had the time to fall to the ground found under it (at least 4 s), and the view sits at
    /// the avatar's head (within 0.8 m) and outside terrain. While the view is detached from a ragdolled body
    /// (Camera.DetachOnRagdoll) it is away from the head by design, so only the terrain check applies.
    /// </summary>
    IEnumerator Landing()
    {
        var c = Character.localCharacter;
        var rig = PeakXR.VR.VRRig.Instance;
        if (c == null)
        {
            Fail("no local character after the jump");
            yield break;
        }
        float drop = GroundBelow(c.Center, 1000f);
        float wait = Mathf.Max(4f, GameTiming.Budget(GameTiming.FallSeconds(drop)));
        float start = Now;
        yield return WaitFor(() => c.data.isGrounded, wait);
        float waited = Now - start;
        float ground = GroundBelow(c.Center, 200f);
        string view = "flat view";
        float eyeGap = 0f;
        bool buried = false;
        bool detached = false;
        if (PeakXR.VR.VRSession.Active && rig != null && rig.Head != null)
        {
            var eye = rig.Head.position;
            eyeGap = Vector3.Distance(eye, c.Head);
            detached = !PeakXR.Rig.RigDriver.ViewOnBody;
            // Inside rock and not hidden behind black (rig.camera's eye guard).
            buried = PeakXR.Rig.RigDriver.EyeInside(eye) && PeakXR.VR.VRFade.Target < 0.99f;
            view =
                $"eye {eyeGap:0.00} m from the head{(detached ? " (view detached)" : "")}{(buried ? ", inside terrain" : "")}";
        }
        // Height, fall speed and time in the air tell a spawn in open air from a body stuck in a pose.
        var note =
            $"{view}, torso at {c.Center.y:0.0} m moving {c.data.avarageVelocity.y:0.0} m/s up, {c.data.sinceGrounded:0.0} s off the ground, ground {(ground < 0f ? "none within 200 m" : ground.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m")} under the torso, grounded {c.data.isGrounded} after {waited:0.0} of {wait:0.0} s (fall {(drop < 0f ? "unknown" : drop.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m")})";
        if (ground < 0f || ground > 2.5f)
            Fail("no ground under the player: " + note);
        else if (!c.data.isGrounded)
            Fail($"not grounded {wait:0.0} s after the jump: " + note);
        else if (eyeGap > 0.8f && !detached)
            Fail("view far from the head: " + note);
        else if (buried)
            Fail("view inside terrain: " + note);
        Note(note);
    }

    /// <summary>Distance from <paramref name="from"/> down to terrain or map geometry; -1 when none within
    /// <paramref name="range"/>.</summary>
    static float GroundBelow(Vector3 from, float range) =>
        Physics.Raycast(
            from,
            Vector3.down,
            out var hit,
            range,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.distance
            : -1f;

    /// <summary>
    /// The game's Peak warp target, <c>MapHandler.respawnThePeak</c> (MapHandler.cs:101, used only by the debug
    /// <c>JumpToSegmentLogic</c>, :1074), sat over no ground in vr24 and vr25: (0.3, 826.8, 1699.0), behind TheKiln's
    /// camp (z 1979), where the body fell for 10 s through 430 m of void. The game itself counts the Peak from its
    /// last progress point on (<c>IsAtPeak</c>: z past <c>progressPoints.Last()</c>, MountainProgressHandler.cs:198-205),
    /// so when the warp target has no ground within a short drop, this stands the player on the ground under that
    /// point.
    /// </summary>
    IEnumerator PeakGround()
    {
        var c = Character.localCharacter;
        var map = Singleton<MapHandler>.Instance;
        if (c == null || map == null || map.respawnThePeak == null)
            yield break;
        var respawn = map.respawnThePeak.position;
        // A spawn drops the body a few metres at most (base camp spawns sit 6-7 m up, CharacterSpawner.cs:65).
        if (GroundBelow(respawn, 20f) >= 0f)
            yield break;
        var progress = Singleton<MountainProgressHandler>.Instance;
        var points = progress != null ? progress.progressPoints : null;
        Transform peak = null;
        if (points != null)
        {
            foreach (var p in points)
            {
                if (p != null && p.transform != null && p.biome == Biome.BiomeType.Peak)
                    peak = p.transform;
            }
        }
        if (peak == null)
        {
            Fail(
                $"respawnThePeak {respawn} has no ground under it and no Peak progress point is listed"
            );
            yield break;
        }
        if (
            !Physics.Raycast(
                peak.position + Vector3.up * 200f,
                Vector3.down,
                out var hit,
                1000f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            Fail(
                $"respawnThePeak {respawn} has no ground under it, nor has the Peak progress point {peak.position}"
            );
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, hit.point + Vector3.up * 0.5f, false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        Note(
            $"respawnThePeak {respawn} has no ground under it; stood on {hit.collider.name} under the Peak progress point {peak.position}"
        );
    }
}
