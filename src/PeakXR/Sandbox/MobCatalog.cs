using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

public enum EntityKind
{
    /// <summary>Has the game's <c>Mob</c> component (Beetle, Scorpion, frog tongue...).</summary>
    Mob,

    /// <summary>Has a <c>Character</c> component but is not a player (Scoutmaster, zombies).</summary>
    Character,

    /// <summary>Any other networked prefab: hazards, effects, props.</summary>
    Entity,
}

/// <summary>How a prefab has to be created and initialised to behave.</summary>
public enum SpawnStyle
{
    /// <summary><c>PhotonNetwork.Instantiate</c> and nothing else.</summary>
    Plain,

    /// <summary>Room-owned object, created by the master client (<c>InstantiateRoomObject</c>).</summary>
    Room,

    /// <summary>Needs the <c>RPCA_InitTornado</c> call with a <c>TornadoSpawner</c> view id.</summary>
    Tornado,

    /// <summary>Attaches to the local player through <c>Bugfix.AttachBug</c>.</summary>
    AttachToPlayer,

    /// <summary>A mob that the game ships as an item prefab (<c>MobItem</c> under <c>0_Items/</c>: Beetle, Scorpion,
    /// Frog...). Created like an item, with <c>InstantiateItemRoom</c>.</summary>
    ItemMob,

    /// <summary>Needs state from a specific player or death; it cannot be created standalone.</summary>
    Unsupported,
}

public sealed class MobEntry
{
    public readonly string Name;
    public readonly EntityKind Kind;
    public readonly SpawnStyle Style;
    public readonly string Note;

    /// <summary>True when the name comes from the game's code rather than runtime discovery only.</summary>
    public readonly bool Known;

    /// <summary>Text for lists, built once so drawing a list does not allocate.</summary>
    public readonly string Label;

    public MobEntry(string name, EntityKind kind, SpawnStyle style, string note, bool known)
    {
        Name = name;
        Kind = kind;
        Style = style;
        Note = note;
        Known = known;
        Label = style == SpawnStyle.Unsupported ? $"{name}  [{kind}, needs setup]" : $"{name}  [{kind}]";
    }
}

/// <summary>
/// Finds the networked prefabs that can be spawned by name. PEAK has no mob database, and Photon loads
/// prefabs by Resources path, so the catalogue merges three sources: names the game's own code uses
/// (verified in the decompile), prefabs already cached by Photon's <see cref="DefaultPool"/>, and, on
/// request, every GameObject in a Resources root folder that carries a PhotonView.
/// </summary>
public static class MobCatalog
{
    const string ItemFolder = "0_Items/";

    // name, kind, style, note (decompile reference)
    static readonly (string, EntityKind, SpawnStyle, string)[] Known =
    {
        ("Character_Scoutmaster", EntityKind.Character, SpawnStyle.Room, "ScoutmasterSpawner.cs:24, master only"),
        ("Tornado", EntityKind.Entity, SpawnStyle.Tornado, "TornadoSpawner.cs:45, needs a TornadoSpawner in the scene"),
        ("BugfixOnYou", EntityKind.Entity, SpawnStyle.AttachToPlayer, "Bugfixer.cs:19, attaches to you"),
        ("MushroomZombie", EntityKind.Character, SpawnStyle.Plain, "MushroomZombieSpawner.cs:65; the spawner link is left empty (assumed fine)"),
        ("BeeSwarm", EntityKind.Entity, SpawnStyle.Plain, "Beehive.cs:51; spawned without a beehive (assumed fine)"),
        ("RavenVoiceThief", EntityKind.Entity, SpawnStyle.Plain, "RavenPerch.cs:144; spawned without a perch (assumed fine)"),
        ("tumbleweed", EntityKind.Entity, SpawnStyle.Plain, "name only seen in Resources; behaviour unchecked"),
        ("ScoutmasterGhostOrbiter", EntityKind.Entity, SpawnStyle.Unsupported, "ScoutmasterSoulPillar.cs:178, needs SetOrbitCharacter"),
        ("MushroomZombie_Player", EntityKind.Character, SpawnStyle.Unsupported, "Character.cs:811, needs a dying player (RPC_Arise)"),
        ("PetrifiedScout", EntityKind.Entity, SpawnStyle.Unsupported, "Character.cs:785, needs a killed player"),
        ("PlayerGhost", EntityKind.Entity, SpawnStyle.Unsupported, "MainCameraMovement.cs:146, spectate ghost"),
        ("TiedBalloon", EntityKind.Entity, SpawnStyle.Unsupported, "CharacterBalloons.cs:101, needs Init from a balloon item"),
    };

    // Player prefabs: spawning these would add fake players.
    static readonly string[] Excluded = { "Character", "Player" };

    static readonly List<MobEntry> entries = new();
    static readonly HashSet<string> seen = new(StringComparer.Ordinal);
    static bool built;

    /// <summary>Bumped whenever the list changes, so a UI can rebuild its filtered view.</summary>
    public static int Version { get; private set; }

    public static IReadOnlyList<MobEntry> Entries
    {
        get
        {
            if (!built)
                Refresh(deep: false);
            return entries;
        }
    }

    public static MobEntry Find(string name)
    {
        var list = Entries;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return list[i];
        }
        return null;
    }

    /// <summary>
    /// Rebuild the list. The quick pass is cheap (known names plus Photon's cache). The deep pass loads
    /// every GameObject in the Resources root once, which can hitch for a moment.
    /// </summary>
    public static void Refresh(bool deep)
    {
        built = true;
        entries.Clear();
        seen.Clear();

        foreach (var (name, kind, style, note) in Known)
        {
            // Only list names that really exist in this game version.
            if (Resources.Load<GameObject>(name) != null)
                Add(new MobEntry(name, kind, style, note, known: true));
        }

        // Beetle, Scorpion and Frog are item prefabs (MobItem : Item) with a Mob component.
        var db = SingletonAsset<ItemDatabase>.Instance;
        if (db != null)
        {
            foreach (var item in db.Objects)
            {
                if (item != null && item.GetComponent<Mob>() != null)
                    Add(new MobEntry(item.name, EntityKind.Mob, SpawnStyle.ItemMob, "0_Items prefab with a Mob component (MobItem)", known: false));
            }
        }

        if (PhotonNetwork.PrefabPool is DefaultPool pool)
        {
            foreach (var pair in pool.ResourceCache)
                AddPrefab(pair.Key, pair.Value);
        }

        if (deep)
        {
            foreach (var go in Resources.LoadAll<GameObject>(""))
            {
                // A prefab in a Resources subfolder cannot be loaded by bare name, so Photon could not spawn it.
                if (go != null && !seen.Contains(go.name) && Resources.Load<GameObject>(go.name) == go)
                    AddPrefab(go.name, go);
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Version++;
    }

    static void AddPrefab(string path, GameObject prefab)
    {
        if (prefab == null || path.StartsWith(ItemFolder, StringComparison.Ordinal) || seen.Contains(path))
            return;
        if (Array.IndexOf(Excluded, path) >= 0 || prefab.GetComponent<PhotonView>() == null)
            return;
        if (prefab.GetComponent<Item>() != null || prefab.GetComponent<Player>() != null)
            return;

        EntityKind kind = prefab.GetComponent<Mob>() != null ? EntityKind.Mob
            : prefab.GetComponent<Character>() != null ? EntityKind.Character
            : EntityKind.Entity;
        Add(new MobEntry(path, kind, kind == EntityKind.Character ? SpawnStyle.Room : SpawnStyle.Plain, null, known: false));
    }

    static void Add(MobEntry entry)
    {
        if (seen.Add(entry.Name))
            entries.Add(entry);
    }
}
