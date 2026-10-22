using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakXR.Comfort;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>mob.*</c> entities that break when spawned alone (<see cref="SpawnStyle.Unsupported"/>), each made through
/// the context the game itself creates them in: the item or call that spawns them, the death that leaves them
/// behind, the segment that holds their spawner. Every step removes what it made and puts the player back.
/// Tornado runs after the segment jump that reaches a <c>TornadoSpawner</c>; the three death steps run last.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Resolved by <see cref="SelfTestFeature"/>: <c>BingBongForceAbilities.bingbong</c> (BingBongForceAbilities.cs:10).</summary>
    internal static AccessTools.FieldRef<BingBongForceAbilities, Transform> BingBongRef;

    /// <summary>Resolved by <see cref="SelfTestFeature"/>: <c>Tornado.caughtCharacters</c> (Tornado.cs:44).</summary>
    internal static AccessTools.FieldRef<Tornado, List<Character>> TornadoCaughtRef;

    /// <summary>Resolved by <see cref="SelfTestFeature"/>: private <c>TornadoSpawner.SpawnTornado</c> (TornadoSpawner.cs:43).</summary>
    internal static MethodInfo SpawnTornadoMethod;

    /// <summary>Death steps in run order. Each kills the solo player, so they run after everything else.</summary>
    static readonly string[] DeathMobs =
    {
        "MushroomZombie_Player",
        "PetrifiedScout",
        "PlayerGhost",
    };

    bool tornadoPending;
    bool tornadoSpawnerOff;
    readonly List<string> deathPending = new();

    /// <summary>True when <paramref name="entry"/> runs later in the script (tornado, deaths).</summary>
    bool Deferred(MobEntry entry)
    {
        if (entry.Name == "Tornado")
        {
            tornadoPending = true;
            return true;
        }
        if (Array.IndexOf(DeathMobs, entry.Name) >= 0)
        {
            deathPending.Add(entry.Name);
            return true;
        }
        return false;
    }

    /// <summary>The step for an entity that needs its game context, or null when there is none.</summary>
    IEnumerator ContextStep(MobEntry entry)
    {
        if (entry.Name.StartsWith("Bingbong_", StringComparison.Ordinal))
            return BingBongStep(entry.Name);
        return entry.Name switch
        {
            "ChainShootable" => ChainStep(),
            "ScoutmasterGhostOrbiter" => OrbiterStep(),
            "TiedBalloon" => BalloonStep(),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- bingbong effects

    /// <summary>
    /// mob.Bingbong_*: no game code spawns these. Their only owner, the debug tool <c>BingBongPhysics</c>, maps its
    /// modes to them (<c>GetEffect</c>, BingBongPhysics.cs:64) but its <c>DoEffect</c> (:60) is empty, and nothing
    /// sets the effect's <c>bingbong</c> transform, which LateUpdate follows (BingBongForceAbilities.cs:35). The
    /// held BingBong item only talks (<c>Action_AskBingBong</c>). So the step supplies that transform the way
    /// <c>BingBongPowers</c> would (it poses itself at the camera, BingBongPowers.cs:33-34): 5 m behind the body,
    /// looking at it, which puts the body on the effect's target point (<c>TargetPos</c>, :117). The effect must
    /// follow it, push (Blow, pushes), pull (Suck) or grab the body, and remove itself after 2 s (:39-47).
    /// </summary>
    IEnumerator BingBongStep(string name)
    {
        var c = Character.localCharacter;
        yield return WaitFor(() => c.data.isGrounded, 3f);
        var forward = Flat(SandboxActions.FrontPoint(1f) - c.Center);
        var anchor = new GameObject("PeakXR.SelfTest.BingBong").transform;
        spawned.Add(anchor.gameObject);
        anchor.SetPositionAndRotation(c.Center - forward * 5f, Quaternion.LookRotation(forward));
        var go = PhotonNetwork.Instantiate(name, anchor.position, anchor.rotation, 0);
        if (go == null)
        {
            Fail("instantiate failed");
            yield break;
        }
        spawned.Add(go);
        var fx = go.GetComponent<BingBongForceAbilities>();
        if (fx == null)
        {
            Fail("no BingBongForceAbilities");
            yield break;
        }
        BingBongRef(fx) = anchor;
        var type = fx.physicsType;
        var push = type switch
        {
            BingBongPhysics.PhysicsType.Suck => -forward,
            BingBongPhysics.PhysicsType.ForceGrab => Vector3.zero,
            _ => forward,
        };
        var start = c.Center;
        float best = 0f;
        bool fell = false;
        bool followed = false;
        float t0 = Now;
        while (go != null && Now - t0 < 4f)
        {
            followed |= Vector3.Distance(go.transform.position, anchor.position) < 0.01f;
            best = Mathf.Max(best, Vector3.Dot(c.data.avarageVelocity, push));
            fell |= c.data.fallSeconds > 0f;
            yield return null;
        }
        float moved = Vector3.Dot(c.Center - start, push);
        var note =
            $"{type}: followed {followed}, peak speed along the force {best:0.00} m/s, moved {moved:0.00} m, fall {fell}, "
            + $"removed {go == null} (not spawned by the game: BingBongPhysics.DoEffect is empty)";
        if (go != null)
            Fail("not removed after removeAfterSeconds");
        if (!followed)
            Fail("did not follow its bingbong transform");
        if (push != Vector3.zero && best < 0.2f && !fell)
            Fail("no force on the body");
        Note(note);
    }

    // ---------------------------------------------------------------- chain shooter

    /// <summary>
    /// mob.ChainShootable: the chain shooter (the item whose <c>VineShooter.vinePrefab</c> is ChainShootable) fired
    /// at terrain. The game's own aim check <c>WillAttach</c> (VineShooter.cs:71-92; from the hand in VR, AimPatches)
    /// picks a heading, primary use fires on <c>OnPrimaryFinishedCast</c> (:36), which instantiates the chain and
    /// builds it with <c>ForceBuildVine_RPC</c> (:61, JungleVine.cs:251). Then the interact path
    /// <c>JungleVine.Interact</c> (:110) grabs it: <c>GrabVineRpc</c> (CharacterVineClimbing.cs:155) sets
    /// <c>isVineClimbing</c> and <c>heldVine</c>.
    /// </summary>
    IEnumerator ChainStep()
    {
        var prefab = FirstItem(i =>
            i.TryGetComponent<VineShooter>(out var v)
            && v.vinePrefab != null
            && v.vinePrefab.name == "ChainShootable"
        );
        if (prefab == null)
        {
            stepSkip = "no item fires ChainShootable";
            yield break;
        }
        var c = Character.localCharacter;
        yield return HoldGiven(prefab);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        var shooter = item.GetComponent<VineShooter>();
        yield return WaitFor(() => c.data.isGrounded, 3f);

        bool aimed = false;
        RaycastHit hit = default;
        float[] pitches = VRSession.Active ? new[] { 0f } : new[] { 0f, 12f, -12f, 25f };
        foreach (var pitch in pitches)
        {
            for (int i = 0; i < 24 && !aimed; i++)
            {
                Turn(c, i == 0 ? 0f : 15f, pitch);
                yield return null;
                yield return null;
                aimed = shooter.WillAttach(out hit);
            }
            if (aimed)
                break;
        }
        if (!aimed)
        {
            stepSkip =
                $"no terrain within the shooter's {shooter.maxLength:0} m and elevation window (VineShooter.cs:71-92) here";
            yield break;
        }

        var before = Ids<JungleVine>();
        SelfTestInputPatch.HoldPrimary = true;
        yield return WaitFor(
            () => Fresh<JungleVine>(before) != null,
            GameTiming.Budget(GameTiming.UseSeconds(item), 1f)
        );
        SelfTestInputPatch.HoldPrimary = false;
        var vine = Fresh<JungleVine>(before);
        if (vine == null)
        {
            Fail($"no chain after the cast at {hit.collider.name} {hit.distance:0.0} m");
            yield break;
        }
        spawned.Add(vine.gameObject);
        int colliders = vine.colliderRoot != null ? vine.colliderRoot.childCount : 0;
        yield return Wait(0.5f);

        yield return WaitFor(() => c.refs.vineClimbing.canInteractVine, 1f);
        vine.Interact(c);
        yield return WaitFor(() => c.data.isVineClimbing && c.data.heldVine == vine, 2f);
        bool climbing = c.data.isVineClimbing && c.data.heldVine == vine;
        yield return Shot("05-mob-ChainShootable");
        if (c.data.isVineClimbing)
            c.refs.vineClimbing.Stop();
        yield return Wait(1f);
        yield return DropHeld();
        EmptyHotbar(c);

        var note =
            $"{vine.name} {hit.distance:0.0} m to {hit.collider.name}, {colliders} colliders, climbing {climbing}";
        if (colliders == 0)
            Fail("chain built no colliders (JungleVine.ForceBuildVine, JungleVine.cs:256)");
        if (!climbing)
            Fail("grabbing the chain did not start a vine climb");
        Note(note);
    }

    /// <summary>Turns the view: the rig in VR, the look values in flat play (CharacterMovement.cs:287-289).</summary>
    static void Turn(Character c, float yaw, float pitch)
    {
        if (VRSession.Active && VRRig.Instance != null)
        {
            VRRig.Instance.RotateYaw(yaw);
            return;
        }
        c.data.lookValues = new Vector2(c.data.lookValues.x + yaw, pitch);
    }

    // ---------------------------------------------------------------- orbiter, balloon

    /// <summary>
    /// mob.ScoutmasterGhostOrbiter: the soul pillar's own call as master (ScoutmasterSoulPillar.cs:176-178),
    /// <c>SetOrbitCharacter</c> (ScoutmasterGhostOrbiter.cs:50) with the local character. Spawned 4 m off rather
    /// than on the body, so following it (:22-33) shows.
    /// </summary>
    IEnumerator OrbiterStep()
    {
        var c = Character.localCharacter;
        var go = PhotonNetwork.Instantiate(
            "ScoutmasterGhostOrbiter",
            SandboxActions.FrontPoint(4f) + Vector3.up * 2f,
            Quaternion.identity,
            0
        );
        if (go == null)
        {
            Fail("instantiate failed");
            yield break;
        }
        spawned.Add(go);
        var orbiter = go.GetComponent<global::Peak.ScoutmasterGhostOrbiter>();
        if (orbiter == null)
        {
            Fail("no ScoutmasterGhostOrbiter");
            yield break;
        }
        float from = Vector3.Distance(go.transform.position, c.Center);
        orbiter.SetOrbitCharacter(c);
        yield return Wait(2f);
        float to = Vector3.Distance(go.transform.position, c.Center);
        yield return Shot("05-mob-ScoutmasterGhostOrbiter");
        if (to > 1f)
            Fail("did not follow the character");
        Note($"distance to the player {from:0.0} m, after 2 s {to:0.00} m");
    }

    /// <summary>
    /// mob.TiedBalloon: a balloon item (the one with <c>Action_Balloon</c>) used from the hand. Its action ties
    /// balloons through <c>CharacterBalloons.TieNewBalloon</c> (Action_Balloon.cs:5-19, CharacterBalloons.cs:99-101),
    /// which runs <c>TiedBalloon.Init</c> (TiedBalloon.cs:36). Each one counts in <c>currentBalloonCount</c>; then
    /// they are popped the game's way (<c>Pop</c>, :89), which unties them.
    /// </summary>
    IEnumerator BalloonStep()
    {
        var prefab = FirstItem(i => i.GetComponent<Action_Balloon>() != null);
        if (prefab == null)
        {
            stepSkip = "no item with Action_Balloon";
            yield break;
        }
        var c = Character.localCharacter;
        var before = Ids<TiedBalloon>();
        yield return HoldGiven(prefab);
        if (!stepOk)
            yield break;
        SelfTestInputPatch.HoldPrimary = true;
        yield return WaitFor(
            () => Fresh<TiedBalloon>(before) != null,
            GameTiming.Budget(GameTiming.UseSeconds(HeldItem()), 1f)
        );
        SelfTestInputPatch.HoldPrimary = false;
        yield return Wait(1f);
        var balloons = c.refs.balloons;
        int tied = balloons.currentBalloonCount - balloons.heldBalloonCount;
        var first = Fresh<TiedBalloon>(before);
        // Captured before popping: Pop destroys the balloon, after which `first == null` is true.
        bool found = first != null;
        float distance = found ? Vector3.Distance(first.transform.position, c.Center) : -1f;
        yield return Shot("05-mob-TiedBalloon");
        foreach (var b in FindObjectsByType<TiedBalloon>(FindObjectsSortMode.None))
        {
            if (!before.Contains(b.GetInstanceID()))
            {
                spawned.Add(b.gameObject);
                b.Pop();
            }
        }
        yield return Wait(0.5f);
        int left = balloons.currentBalloonCount - balloons.heldBalloonCount;
        if (!found || tied < 1)
            Fail("no balloon tied to the character");
        else if (left > 0)
            Fail("popped balloons still counted");
        Note(
            $"{prefab.name}: {tied} tied, first {distance:0.0} m from the body, {left} after popping"
        );
        yield return DropHeld();
    }

    /// <summary>Empty hands, then the item from <c>GameUtils.InstantiateAndGrab</c> in hand.</summary>
    IEnumerator HoldGiven(Item prefab)
    {
        yield return DropHeld();
        if (!stepOk)
            yield break;
        if (!SandboxActions.GiveItem(prefab))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Until(() => Holding(prefab), 5f, prefab.name + " not in hand");
        if (stepOk)
            spawned.Add(HeldItem().gameObject);
    }

    // ---------------------------------------------------------------- tornado

    /// <summary>After a segment jump: mob.Tornado once a segment with a running <c>TornadoSpawner</c> is active.
    /// Only after a jump that passed: one that threw midway (VR run 27) left the segment just switched on, its
    /// spawner's <c>Start</c> (TornadoSpawner.cs:20-23, which sets <c>view</c>) not yet run, so <c>SpawnTornado</c>
    /// threw on <c>view.ViewID</c> after instantiating a tornado with no targets (Tornado.PickTarget NRE spam).</summary>
    IEnumerator TornadoIfHere()
    {
        if (!tornadoPending || !stepOk)
            yield break;
        TornadoSpawner spawner = null;
        foreach (var s in FindObjectsByType<TornadoSpawner>(FindObjectsSortMode.None))
        {
            if (s.isActiveAndEnabled && s.GetComponent<PhotonView>() != null)
                spawner = s;
            else
                tornadoSpawnerOff = true;
        }
        if (spawner == null)
            yield break;
        tornadoPending = false;
        var home = StandUnder(Character.localCharacter.Center);
        yield return Run("mob.Tornado", 60f, TornadoStep(spawner));
        Safe("mob.despawn", Despawn);
        yield return Wait(2f);
        Recover();
        yield return Guard(WarpTo(home), Now + 8f, _ => { });
        warpedForProp = false;
    }

    /// <summary>Records mob.Tornado as SKIP when no segment had a running spawner.</summary>
    void TornadoNotFound(string where)
    {
        if (!tornadoPending)
            return;
        tornadoPending = false;
        Skip(
            "mob.Tornado",
            tornadoSpawnerOff
                ? "the TornadoSpawner is disabled (RunSettings Hazard_Tornado 0, TornadoSpawner.cs:25)"
                : $"no segment of {where} has a TornadoSpawner, the only spawn path (TornadoSpawner.cs:43-46)"
        );
    }

    /// <summary>
    /// mob.Tornado when today's map had no spawner: a Mesa level (biome id letter 'M', TodaysBiomes.cs:58) is loaded
    /// the game's way, back to the airport as after the peak (MapHandler.cs:662-664), the console map picker
    /// <c>MapDebugUI.SetMapNumber</c> (MapDebugUI.cs:145-149, read by the kiosk at AirportCheckInKiosk.cs:142) and
    /// the kiosk. Then the segments are jumped until one has a running spawner. Last in the script: the run ends
    /// on that level, so nothing needs the old map back; the level offset is restored.
    /// </summary>
    IEnumerator TornadoTrip()
    {
        if (!tornadoPending || tornadoSpawnerOff)
        {
            TornadoNotFound("this map");
            yield break;
        }
        var baker = SingletonAsset<MapBaker>.Instance;
        var service = GameHandler.GetService<NextLevelService>();
        int count = baker != null ? baker.ScenePaths.Length : 0;
        int current =
            count > 0
                ? (service.NextLevelIndexOrFallback + NextLevelService.debugLevelIndexOffset)
                    % count
                : -1;
        int level = -1;
        for (int i = 0; i < count && i < baker.BiomeIDs.Count && level < 0; i++)
        {
            if (i != current && baker.BiomeIDs[i].Contains('M', StringComparison.Ordinal))
                level = i;
        }
        if (level < 0)
        {
            TornadoNotFound("this map, and no other level has the Mesa biome (MapBaker.BiomeIDs)");
            yield break;
        }
        var where = $"Mesa level {level} {baker.GetLevel(level)} ({baker.BiomeIDs[level]})";
        int offset = NextLevelService.debugLevelIndexOffset;
        yield return Run("08-mesa-level", 480f, MesaLevel(level, where));
        NextLevelService.debugLevelIndexOffset = offset;
        if (!Ready || Singleton<MapHandler>.Instance == null)
        {
            TornadoNotFound(where + " (it did not load)");
            yield break;
        }
        SandboxActions.SetGodMode(true);
        // Mesa takes Alpine's place; try that segment first.
        foreach (var segment in new[] { Segment.Alpine, Segment.Tropics, Segment.Caldera })
        {
            if (Singleton<MapHandler>.Instance.GetCurrentSegment() != segment)
                yield return Run("08-mesa-" + segment, 120f, Jump(segment, "08-mesa-" + segment));
            yield return TornadoIfHere();
            if (!tornadoPending)
                yield break;
        }
        TornadoNotFound(where);
    }

    IEnumerator MesaLevel(int level, string where)
    {
        GameHandler.AddStatus<SceneSwitchingStatus>(new SceneSwitchingStatus());
        var loading = RetrievableResourceSingleton<LoadingScreenHandler>.Instance;
        loading.Load(
            LoadingScreen.LoadingScreenType.Basic,
            null,
            loading.LoadSceneProcess("Airport", networked: true, yieldForCharacterSpawn: true, 1f)
        );
        yield return Until(() => InScene("Airport") && Ready, 170f, "airport not ready");
        if (!stepOk)
            yield break;
        yield return Wait(4f);
        MapDebugUI.SetMapNumber(level);
        yield return Level("08-mesa-level");
        Note(where);
    }

    /// <summary>
    /// mob.Tornado: the spawner's own spawn, early (<c>SpawnTornado</c>, TornadoSpawner.cs:43-46, which its Update
    /// calls when the 30-300 s timer runs out, :30-41): Instantiate plus <c>RPCA_InitTornado</c> with the spawner's
    /// view. It must travel to its <c>TornadoPoints</c> targets (Tornado.cs:112-149) and, with the player warped
    /// beside it, catch them (<c>RPCA_CaptureCharacter</c>, :311, from AttractCharacters :279-308). In VR the
    /// capture pulses comfort.events' vignette (ComfortEventsPatches.TornadoCapture).
    /// </summary>
    IEnumerator TornadoStep(TornadoSpawner spawner)
    {
        var c = Character.localCharacter;
        var before = Ids<Tornado>();
        Exception threw = null;
        try
        {
            SpawnTornadoMethod.Invoke(spawner, null);
        }
        catch (TargetInvocationException e)
        {
            threw = e.InnerException ?? e;
        }
        if (threw != null)
        {
            // Instantiate ran before the RPC argument threw: remove the tornado it left with no targets.
            var stray = Fresh<Tornado>(before);
            if (stray != null)
                spawned.Add(stray.gameObject);
            Fail($"SpawnTornado threw {threw.GetType().Name}: {threw.Message}");
            yield break;
        }
        yield return WaitFor(() => Fresh<Tornado>(before) != null, 2f);
        var tornado = Fresh<Tornado>(before);
        if (tornado == null)
        {
            Fail("SpawnTornado made no tornado");
            yield break;
        }
        spawned.Add(tornado.gameObject);
        var p0 = tornado.transform.position;
        yield return Wait(4f);
        if (tornado == null)
        {
            Fail("gone within 4 s");
            yield break;
        }
        float moved = FlatDistance(tornado.transform.position, p0);

        bool caught = false;
        float vignette = 0f;
        float nextWarp = 0f;
        float end = Now + 20f;
        while (Now < end && !caught && tornado != null)
        {
            if (Now >= nextWarp)
            {
                nextWarp = Now + 2f;
                var t = tornado.transform.position;
                var stand = StandUnder(t + Flat(c.Center - t) * 3f + Vector3.up * 3f);
                c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
            }
            caught = TornadoCaughtRef(tornado).Contains(c);
            vignette = Mathf.Max(vignette, ComfortEventsRuntime.VignetteBoost);
            yield return null;
        }
        float y0 = c.Center.y;
        float lift = 0f;
        float hold = Now + 2f;
        bool shot = false;
        while (caught && tornado != null && Now < hold)
        {
            lift = Mathf.Max(lift, c.Center.y - y0);
            vignette = Mathf.Max(vignette, ComfortEventsRuntime.VignetteBoost);
            // The test's own warps beside the tornado black the view for a moment (ui.runflow warp fade).
            if (!shot && FadeView.Alpha < 0.01f)
            {
                shot = true;
                yield return Shot("05-mob-Tornado");
            }
            yield return null;
        }
        if (caught && !shot)
            yield return ShotWhenClear("05-mob-Tornado", 0f);
        var note =
            $"moved {moved:0.0} m in 4 s, caught {caught}, lifted {lift:0.0} m"
            + (VRSession.Active ? $", vignette peak {vignette:0.00}" : "");
        if (moved < 1f)
            Fail("the tornado did not move");
        if (!caught)
            Fail("never caught the player");
        else if (VRSession.Active && vignette <= 0f)
            Fail("no comfort vignette while caught");
        Note(note);
    }

    // ---------------------------------------------------------------- deaths

    /// <summary>
    /// The death steps, last in the script. Each kills the solo player through the game's own path and revives
    /// them with the game's revive RPC. A solo death ends the run at once (<c>CheckEndGame</c>, Character.cs:835,
    /// then <c>RPCEndGame</c> :890 deletes the quicksave and opens the end screen), so <see cref="SelfTestEndGamePatch"/>
    /// holds that back for the step. God mode is off meanwhile: its tick clears petrify.
    /// </summary>
    IEnumerator DeathSteps()
    {
        foreach (var mob in DeathMobs)
        {
            var c = Character.localCharacter;
            if (!deathPending.Contains(mob) || c == null)
                continue;
            var home = StandUnder(c.Center);
            yield return DropHeld();
            SnapshotItems();
            var skeletons = Ids<Skelleton>();
            SandboxActions.SetGodMode(false);
            SelfTestEndGamePatch.Hold = true;
            IEnumerator body = mob switch
            {
                "MushroomZombie_Player" => ZombieStep(home),
                "PetrifiedScout" => PetrifyStep(home),
                _ => GhostStep(home),
            };
            yield return Run("mob." + mob, 40f, body);
            yield return Guard(
                ReviveIfDead(home),
                Now + 8f,
                e =>
                    Plugin.Log.LogWarning($"[selftest] mob.{mob} revive: {e?.Message ?? "timeout"}")
            );
            Safe("mob.despawn", Despawn);
            Safe("death.sweep", SweepNewItems);
            Safe("death.skeletons", () => DestroyFresh<Skelleton>(skeletons));
            SelfTestEndGamePatch.Hold = false;
            SandboxActions.SetGodMode(true);
            yield return Wait(1f);
        }
    }

    /// <summary>
    /// mob.MushroomZombie_Player: the console <c>Character.Zombify</c> (Character.cs:785) sends <c>RPCA_Zombify</c>
    /// (:802), the RPC the game's spore death sends (:601-607); as master it spawns the zombie and <c>RPC_Arise</c>
    /// (:808, MushroomZombie.cs:344), which runs <c>FinishZombifying</c> (Character.cs:814). The revive must then
    /// remove the zombie (MushroomZombie.cs:417).
    /// </summary>
    IEnumerator ZombieStep(Vector3 home)
    {
        var c = Character.localCharacter;
        var before = Ids<MushroomZombie>();
        Character.Zombify();
        yield return Until(
            () => Fresh<MushroomZombie>(before) != null && c.data.dead && c.data.zombified,
            5f,
            "no zombie, or the player not zombified"
        );
        if (!stepOk)
            yield break;
        var zombie = Fresh<MushroomZombie>(before);
        spawned.Add(zombie.gameObject);
        yield return Wait(3f);
        yield return Shot("05-mob-MushroomZombie_Player");
        ReviveAt(c, home);
        yield return Until(
            () => !c.data.dead && zombie == null,
            5f,
            "the revived player's zombie was not removed"
        );
        Note($"{(zombie == null ? "zombie removed" : "zombie still there")} after the revive");
    }

    /// <summary>
    /// mob.PetrifiedScout: petrify at 100 (the console <c>PetrifyFully</c>, CharacterAfflictions.cs:1348) and
    /// <c>Character.Die</c> (Character.cs:692): <c>RPCA_Die</c> (:733) takes <c>SpawnSkeletonOnDeath</c> (:763) to
    /// <c>SpawnStatue</c> (:780-782). The game never removes a statue; the step does.
    /// </summary>
    IEnumerator PetrifyStep(Vector3 home)
    {
        var c = Character.localCharacter;
        var before = Ids<global::Peak.PetrifiedScout>();
        c.data.SetPetrify(100);
        Character.Die();
        yield return Until(
            () => Fresh<global::Peak.PetrifiedScout>(before) != null && c.data.dead,
            5f,
            "no statue, or the player not dead"
        );
        if (!stepOk)
            yield break;
        var statue = Fresh<global::Peak.PetrifiedScout>(before);
        spawned.Add(statue.gameObject);
        yield return Wait(3f);
        yield return Shot("05-mob-PetrifiedScout");
        ReviveAt(c, home);
        yield return Until(
            () => !c.data.dead && !c.data.shouldPetrify,
            5f,
            "the player not revived and un-petrified"
        );
        Note($"statue {statue.name} at {Vector3.Distance(statue.transform.position, home):0.0} m");
    }

    /// <summary>
    /// mob.PlayerGhost: <c>Character.Die</c> (Character.cs:692); while the dead player can still be spectated (5 s,
    /// CharacterData.cs:324-334), <c>MainCameraMovement.Spectate</c> spawns the ghost (MainCameraMovement.cs:146).
    /// Solo, the spectated character is the player's own body. In VR the rig takes the spectate view and fades;
    /// ui.spatial's nameplate shows only for a friend (SpatialRuntime <c>wantSpectate</c>), so solo it must stay
    /// off. The revive must remove the ghost (<c>StopSpectating</c>, :120) and clear the fade.
    /// </summary>
    IEnumerator GhostStep(Vector3 home)
    {
        var c = Character.localCharacter;
        Character.Die();
        yield return Until(() => c.Ghost != null, 5f, "no PlayerGhost while dead");
        if (!stepOk)
            yield break;
        bool spectating = MainCameraMovement.IsSpectating;
        var spec = MainCameraMovement.specCharacter;
        float fade = 0f;
        bool plate = false;
        float t0 = Now;
        while (Now - t0 < 1.5f)
        {
            fade = Mathf.Max(fade, VRFade.Target);
            plate |= SpectateView.ClaimsStatus;
            yield return null;
        }
        yield return Shot("05-mob-PlayerGhost");
        // Where the spectate view stands: near where the body fell, never out at the parked body (Character.DeathPos).
        string view = "";
        bool onBody = false;
        for (
            var t =
                GUIManager.instance != null && GUIManager.instance.spectatingNameText != null
                    ? GUIManager.instance.spectatingNameText.transform
                    : null;
            t != null && !onBody;
            t = t.parent
        )
            onBody = t.name == "PeakXR_SpectatePlate";
        if (VRSession.Active && VRRig.Instance != null && VRRig.Instance.Head != null)
        {
            var eye = VRRig.Instance.Head.position;
            float fromFall = Vector3.Distance(eye, c.VirtualCenter);
            view = $", eye {fromFall:0.0} m from where the body fell, label on the body {onBody}";
            if (fromFall > 8f)
                Fail($"spectate view {fromFall:0.0} m from where the body fell");
            if (spectating && SpatialFeature.Settings.SpectateOnPlayer.Value && !onBody)
                Fail("'spectating' label still on the HUD, not over the body");
        }
        var ghost = c.Ghost;
        ReviveAt(c, home);
        yield return Until(
            () => !c.data.dead && c.Ghost == null && ghost == null,
            5f,
            "the ghost was not removed after the revive"
        );
        string vr = "";
        if (VRSession.Active)
        {
            yield return WaitFor(() => VRFade.Target <= 0f, 10f);
            vr =
                $"; VR fade peak {fade:0.00}, after revive {VRFade.Target:0.00}, nameplate {plate}";
            if (VRFade.Target > 0f)
                Fail("VRFade.Target stuck after the revive");
            if (plate != (spec != null && spec != c))
                Fail("your own status bar taken onto your own body's nameplate");
        }
        if (!spectating)
            Fail("not spectating while dead");
        Note(
            $"spectating {spectating} {(spec == c ? "self" : spec != null ? spec.name : "nobody")}{view}{vr}"
        );
    }

    static IEnumerator ReviveIfDead(Vector3 home)
    {
        var c = Character.localCharacter;
        if (c == null || !(c.data.dead || c.data.fullyPassedOut))
            yield break;
        ReviveAt(c, home);
        yield return WaitFor(() => !c.data.dead, 3f);
    }

    /// <summary>The game's revive RPC (Character.cs:1799) as the console <c>Character.Revive</c> (:1762) sends it,
    /// at the step's start and without the revive curse.</summary>
    static void ReviveAt(Character c, Vector3 at)
    {
        c.data.SetPetrify(0);
        c.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, at, false, -1);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The step's details, set after its verdict: appended to a failure reason instead of replacing it.</summary>
    void Note(string note) => stepNote = stepOk || stepNote == null ? note : stepNote + ": " + note;

    static HashSet<int> Ids<T>()
        where T : Component
    {
        var set = new HashSet<int>();
        foreach (var it in FindObjectsByType<T>(FindObjectsSortMode.None))
            set.Add(it.GetInstanceID());
        return set;
    }

    /// <summary>An instance of <typeparamref name="T"/> that is not in <paramref name="before"/>.</summary>
    static T Fresh<T>(HashSet<int> before)
        where T : Component
    {
        foreach (var it in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            if (!before.Contains(it.GetInstanceID()))
                return it;
        }
        return null;
    }

    static void DestroyFresh<T>(HashSet<int> before)
        where T : Component
    {
        foreach (var it in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            if (!before.Contains(it.GetInstanceID()))
                Destroy(it.gameObject);
        }
    }

    static Item FirstItem(Func<Item, bool> ok)
    {
        foreach (var it in SandboxActions.Items)
        {
            if (ok(it))
                return it;
        }
        return null;
    }

    /// <summary>The ground under <paramref name="p"/>, as <c>WarpPlayerRPC</c> wants it.</summary>
    static Vector3 StandUnder(Vector3 p) =>
        Physics.Raycast(
            p + Vector3.up * 2f,
            Vector3.down,
            out var hit,
            12f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point + Vector3.up * 0.1f
            : p;
}
