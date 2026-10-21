using System.Collections;
using System.Collections.Generic;
using Peak;
using UnityEngine;

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
    /// A plain spawn watched for 3 s. It passes on any sign the game ran it: it moved or came closer, it targeted
    /// the player (<c>Scoutmaster.currentTarget</c>, Scoutmaster.cs:61; <c>BeeSwarm.currentAggroCharacter</c>,
    /// BeeSwarm.cs:22; <c>MushroomZombie._currentTarget</c>, MushroomZombie.cs:30), it blinded the player, it grew
    /// (a vine), or it removed itself (an explosion). <see cref="SpawnOnly"/> names only have to spawn.
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
        float end = Now + 3f;
        while (Now < end && go != null)
        {
            var p = go.transform.position;
            moved = Mathf.Max(moved, Vector3.Distance(p, start));
            closest = Mathf.Min(closest, Vector3.Distance(p, c.Center));
            grew = Mathf.Max(grew, BoundsSize(go) - size);
            targeted |=
                (scout != null && scout.currentTarget == c)
                || (swarm != null && swarm.currentAggroCharacter == c)
                || (zombie != null && zombie._currentTarget == c);
            blinded |= c.data.isBlind;
            yield return null;
        }
        bool gone = go == null;
        if (!gone)
            yield return Shot("05-mob-" + entry.Name);
        var note =
            $"moved {moved:0.00} m, closest {closest:0.0} m (from {near:0.0}), targeted {targeted}, "
            + $"blinded the player {blinded}, grew {grew:0.00} m, gone {gone}";
        bool spawnOnly = false;
        foreach (var s in SpawnOnly)
            spawnOnly |= entry.Name.Contains(s, System.StringComparison.Ordinal);
        bool alive =
            moved > 0.3f || near - closest > 0.3f || targeted || blinded || grew > 0.2f || gone;
        if (spawnOnly)
            stepNote = "spawned only (nothing to observe); " + note;
        else if (!alive)
            Fail("no sign of behaviour in 3 s: " + note);
        else
            stepNote = note;
    }

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
    /// grounded within 4 s, and the view sits at the avatar's head (within 0.8 m) and outside terrain.
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
        yield return WaitFor(() => c.data.isGrounded, 4f);
        float ground = Physics.Raycast(
            c.Center,
            Vector3.down,
            out var hit,
            200f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.distance
            : -1f;
        string view = "flat view";
        float eyeGap = 0f;
        bool buried = false;
        if (PeakXR.VR.VRSession.Active && rig != null && rig.Head != null)
        {
            var eye = rig.Head.position;
            eyeGap = Vector3.Distance(eye, c.Head);
            buried = Physics.CheckSphere(
                eye,
                0.1f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
            view = $"eye {eyeGap:0.00} m from the head{(buried ? ", inside terrain" : "")}";
        }
        var note =
            $"{view}, ground {(ground < 0f ? "none within 200 m" : ground.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m")} under the torso, grounded {c.data.isGrounded}";
        if (ground < 0f || ground > 2.5f)
            Fail("no ground under the player: " + note);
        else if (!c.data.isGrounded)
            Fail("not grounded 4 s after the jump: " + note);
        else if (eyeGap > 0.8f)
            Fail("view far from the head: " + note);
        else if (buried)
            Fail("view inside terrain: " + note);
        Note(note);
    }
}
