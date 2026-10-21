using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Common;
using UnityEngine;

namespace PeakXR.UI.Passport;

/// <summary>
/// The closed passport the airport puts in your hand is modelled 0.38 m long (its cover mesh, scaled 0.141), sized
/// for the flat camera that holds it up between two hands. Gripped by one VR hand it hovers in front of the face, so
/// the local holder's copy shrinks about its <c>Hand_R</c> grip to palm size. Only this client's visuals and
/// collider change (its body keeps game-size mass properties); peers keep their own vanilla copy, and the item is
/// restored the moment it leaves the hand.
/// </summary>
internal static class HeldPassport
{
    /// <summary>Longest side of the held passport in VR, metres: a little over a real passport's 0.125 m,
    /// to match the game's chunky hands.</summary>
    private const float LengthMeters = 0.16f;

    private static readonly List<Transform> parts = new();
    private static readonly List<Vector3> positions = new();
    private static readonly List<Vector3> scales = new();
    private static readonly List<MeshFilter> filters = new();
    private static Item current;
    private static Rigidbody body;
    private static Vector3 centerOfMass;
    private static Vector3 inertia;
    private static Quaternion inertiaRotation;

    /// <summary>Per frame: shrink the local player's held passport in VR, restore anything else.</summary>
    public static void Update(bool active)
    {
        var c = Character.localCharacter;
        var item = active && c != null && c.data != null ? c.data.currentItem : null;
        if (ReferenceEquals(item, current))
            return;
        Restore();
        current = item;
        // Action_Passport (A/Action_Passport.cs:1) marks the passport whatever the item is called; the game finds
        // item actions in children too (Item.cs:520).
        if (item != null && item.GetComponentInChildren<Action_Passport>() != null)
            Shrink(item);
    }

    private static void Shrink(Item item)
    {
        var root = item.transform;
        var grip = root.Find("Hand_R");
        if (grip == null)
            grip = root.Find("Hand_L");
        var scale = HeldBookSize.FitScale(Longest(root), LengthMeters);
        if (scale >= 1f)
            return;
        var pivot = grip != null ? grip.localPosition.ToNumerics() : default;
        // The scaled collider would move the body's automatic centre of mass and inertia, which the hand weld
        // (CharacterItems.cs:646) and the drop (Item.cs:756 reads rig.centerOfMass) feel: pin the game-size values.
        body = item.rig;
        if (body != null)
        {
            centerOfMass = body.centerOfMass;
            inertia = body.inertiaTensor;
            inertiaRotation = body.inertiaTensorRotation;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            var part = root.GetChild(i);
            // The grips stay: they are where the avatar's hand holds the item (ItemInController).
            if (part.name is "Hand_R" or "Hand_L")
                continue;
            parts.Add(part);
            positions.Add(part.localPosition);
            scales.Add(part.localScale);
            part.localScale *= scale;
            part.localPosition = HeldBookSize
                .ScaleAbout(part.localPosition.ToNumerics(), pivot, scale)
                .ToUnity();
        }
        PinBody();
        Plugin.Log.LogDebug($"[ui.passport] held passport at {scale:0.00} of its game size");
    }

    private static void PinBody()
    {
        if (body == null)
            return;
        body.centerOfMass = centerOfMass;
        body.inertiaTensor = inertia;
        body.inertiaTensorRotation = inertiaRotation;
    }

    /// <summary>Longest side of the item's drawn meshes, metres (the grip hands are inactive, HandVisual.cs:9).</summary>
    private static float Longest(Transform root)
    {
        root.GetComponentsInChildren(false, filters);
        float longest = 0f;
        foreach (var f in filters)
        {
            if (f.sharedMesh == null)
                continue;
            var size = Vector3.Scale(f.sharedMesh.bounds.size, f.transform.lossyScale);
            longest = Mathf.Max(
                longest,
                Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)))
            );
        }
        filters.Clear();
        return longest;
    }

    private static void Restore()
    {
        for (int i = 0; i < parts.Count; i++)
        {
            // A despawned item takes its parts with it.
            if (parts[i] == null)
                continue;
            parts[i].localScale = scales[i];
            parts[i].localPosition = positions[i];
        }
        // Back at game size the pinned values equal the automatic ones again.
        PinBody();
        body = null;
        parts.Clear();
        positions.Clear();
        scales.Clear();
    }
}
