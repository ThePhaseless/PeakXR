using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Lights;

/// <summary>Which hand-held light the local player holds.</summary>
internal enum HeldLightKind
{
    None,
    Flare,
    Candle,
    Lantern,
    Torch,
}

/// <summary>
/// Per-frame logic of <see cref="HandLightsFeature"/> for the local VR player's held flare, candle, lantern or
/// torch. Every light of these items is a child of the item (Lantern.cs:18, Action_Torch.cs:7, candle particles
/// Candle.cs:25, flame moved between item children Candle.cs:128-136) or copies the item's transform each
/// LateUpdate (flare VFX, TrackNetworkedObject.cs:70-79). Nothing is placed from the camera or a body bone, and the
/// avatar feature drives the item to the tracked hand, so the lights already follow the hand. The torch simulates
/// its particles in a hip-following space (Action_Torch.cs:22, CharacterAnimations.cs:271), so hand swings still
/// leave a trail. The game never changes a held item's layer or any camera's or light's culling mask (only mirror
/// cameras set cullingMask, MirrorReflection.cs:53), so there is no culling cause to undo; this class only turns off
/// the held light's shadows and turns a free-hand strike into the game's own primary-use input.
/// Does nothing unless such an item is held.
/// </summary>
internal static class HandLights
{
    private const float StrikeRadius = 0.15f;
    private const float StrikeRearm = 0.3f;

    private static readonly InjectedPress Press = new();
    private static readonly StrikeGesture Strike = new()
    {
        Radius = StrikeRadius,
        RearmRadius = StrikeRearm,
    };
    private static readonly List<Light> Found = new(8);
    private static readonly List<Light> Tuned = new(8);
    private static readonly List<LightShadows> SavedShadows = new(8);

    private static Item held;
    private static HeldLightKind kind;
    private static Transform wick;
    private static TrackableNetworkObject trackable;
    private static TrackNetworkedObject tracker;
    private static bool lightsDirty;
    private static bool tunedNoShadows;
    private static bool wasLit;
    private static float pressStart;

    /// <summary>Called once per frame from the local character's input sampling, after interact.physical.</summary>
    public static void Tick(Character c, CharacterInput input)
    {
        // A destroyed item (stashed, consumed) compares equal to null: treat it as no item.
        var item = c.data.currentItem;
        var current = VRSession.Active && item != null ? item : null;
        bool userHeld = input.usePrimaryIsPressed;
        if (!ReferenceEquals(current, held))
        {
            // The press belonged to the previous item: release it before switching.
            if (Press.Active)
                Apply(input, Press.Update(userHeld, true, Time.deltaTime));
            Bind(current);
        }
        if (kind == HeldLightKind.None)
            return;

        var cfg = LightsConfig.Instance;
        UpdateLights(cfg);

        bool lit = IsLit(held);
        if (lit && !wasLit)
            Haptics.Pulse(LocalHold.ItemInLeftHand, 0.4f, 0.08f);
        wasLit = lit;

        UpdateStrike(cfg, lit);
        if (Press.Active)
        {
            bool done = held.lastFinishedCast >= pressStart;
            Apply(input, Press.Update(userHeld, done, Time.deltaTime));
        }
    }

    private static void Bind(Item item)
    {
        RestoreLights();
        Press.Reset();
        Strike.Reset();
        held = item;
        kind = HeldLightKind.None;
        wick = null;
        trackable = null;
        tracker = null;
        lightsDirty = true;
        if (item == null)
            return;

        if (item.TryGetComponent(out Flare _))
        {
            kind = HeldLightKind.Flare;
            trackable = item.GetComponent<TrackableNetworkObject>();
        }
        else if (item.TryGetComponent(out Candle candle))
        {
            kind = HeldLightKind.Candle;
            if (candle.fireParticle != null)
                wick = candle.fireParticle.transform;
        }
        else if (item.TryGetComponent(out Lantern _))
        {
            kind = HeldLightKind.Lantern;
        }
        else if (item.TryGetComponent(out Action_Torch _))
        {
            kind = HeldLightKind.Torch;
        }
        // Lit on pickup is not a new lighting: no pulse.
        wasLit = kind != HeldLightKind.None && IsLit(item);
    }

    /// <summary>Flare, candle and lantern all keep their on state in the FlareActive item data
    /// (Flare.cs:57, Candle.cs:192, Lantern.cs:208); the torch has no on state.</summary>
    internal static bool IsLit(Item item) =>
        item.HasData(DataEntryKey.FlareActive)
        && item.GetData<BoolItemData>(DataEntryKey.FlareActive).Value;

    private static void UpdateStrike(LightsConfig cfg, bool lit)
    {
        // Flare: strike to light, once (Action_Flare.cs:7). Candle: the same use toggles it (Action_LightCandle.cs:12).
        bool target = kind == HeldLightKind.Flare ? !lit : kind == HeldLightKind.Candle;
        var rig = VRRig.Instance;
        bool freeLeft = !LocalHold.ItemInLeftHand;
        if (!cfg.StrikeToLight || !target || Press.Active || LocalHold.TwoHand || rig == null)
            return;
        ref var hand = ref VRInput.Hand(freeLeft);
        var handTransform = rig.Hand(freeLeft);
        if (!hand.Tracked || handTransform == null)
            return;

        var point = wick != null ? wick.position : held.Center();
        float distance = Vector3.Distance(handTransform.position, point);
        var velocity =
            rig.Origin != null ? rig.Origin.TransformVector(hand.Velocity) : hand.Velocity;
        Strike.MinSpeed = cfg.StrikeSpeed;
        if (!Strike.Update(distance, velocity.magnitude))
            return;

        // Hold primary use through the item's own hold time (Candle.cs:114 sets it per frame), then release.
        pressStart = Time.time;
        Press.Begin(Mathf.Max(0f, held.usingTimePrimary) + 0.5f);
        Haptics.Pulse(freeLeft, 0.5f, 0.04f);
    }

    private static void Apply(CharacterInput input, PressFrame f)
    {
        input.usePrimaryWasPressed |= f.Pressed;
        input.usePrimaryIsPressed |= f.Held;
        input.usePrimaryWasReleased |= f.Released;
    }

    private static void UpdateLights(LightsConfig cfg)
    {
        // The flare's light lives on its VFX object, spawned once the flare is lit (Flare.cs:32-34, 60-63).
        if (trackable != null && !ReferenceEquals(trackable.currentTracker, tracker))
        {
            tracker = trackable.currentTracker;
            lightsDirty = true;
        }
        // The local body always casts shadows, in flat through HideTheBody's shadow-only copy (HideTheBody.cs:60)
        // and in VR through the visible body (AvatarPatches.HideBody). In VR the arm, hand and fingers sit on the
        // tracked hand right next to the held light, so they would throw large moving shadows over the scene.
        bool noShadows = !cfg.HeldLightShadows;
        if (!lightsDirty && noShadows == tunedNoShadows)
            return;

        RestoreLights();
        lightsDirty = false;
        tunedNoShadows = noShadows;
        if (!noShadows)
            return;
        DisableShadows(held.transform);
        if (tracker != null)
            DisableShadows(tracker.transform);
    }

    private static void DisableShadows(Transform root)
    {
        root.GetComponentsInChildren(true, Found);
        foreach (var light in Found)
        {
            if (light.shadows == LightShadows.None)
                continue;
            Tuned.Add(light);
            SavedShadows.Add(light.shadows);
            light.shadows = LightShadows.None;
        }
        Found.Clear();
    }

    private static void RestoreLights()
    {
        for (int i = 0; i < Tuned.Count; i++)
        {
            var light = Tuned[i];
            if (light != null)
                light.shadows = SavedShadows[i];
        }
        Tuned.Clear();
        SavedShadows.Clear();
    }
}
