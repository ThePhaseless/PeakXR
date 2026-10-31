using System.Collections.Generic;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>One grip edge of the local VR player, handed to <see cref="IGripExtension"/>s.</summary>
internal readonly struct GripHookContext
{
    public readonly Character Character;
    public readonly bool Left;

    /// <summary>World position of the hand's grip pose.</summary>
    public readonly Vector3 HandPosition;

    /// <summary>The held item, or null.</summary>
    public readonly Item HeldItem;

    /// <summary>This hand is the one holding <see cref="HeldItem"/>.</summary>
    public readonly bool ThisHandHolds;

    /// <summary>Asked every frame for the open hand's glow, not for a press: no notes or logs.</summary>
    public readonly bool Preview;

    /// <summary>A grab target, climbable rock or a free fall is at this hand: a near target wins over anything
    /// that reaches past it (the fallback extensions).</summary>
    public readonly bool HandBusy;

    public GripHookContext(
        Character character,
        bool left,
        Vector3 handPosition,
        Item heldItem,
        bool thisHandHolds,
        bool preview = false,
        bool handBusy = false
    )
    {
        Character = character;
        Left = left;
        HandPosition = handPosition;
        HeldItem = heldItem;
        ThisHandHolds = thisHandHolds;
        Preview = preview;
        HandBusy = handBusy;
    }
}

/// <summary>
/// A feature that takes grips before <c>interact.physical</c>'s own grab, climb and interact logic. Priority follows
/// <see cref="Core.GripIntent"/>: a free hand joining the held item comes first, then extensions (in registration
/// order), then fallback extensions, then grab, climb and the interact fallback. Methods run on grip edges of the local VR player only and
/// must not allocate.
/// </summary>
internal interface IGripExtension
{
    /// <summary>Grip closed, or the open hand's glow asking what a grip would do (<see cref="GripHookContext.Preview"/>,
    /// every frame): true when this extension wants it. Must not act yet (the second hand may still win), and must not
    /// allocate.</summary>
    bool ClaimPress(in GripHookContext ctx);

    /// <summary>The claimed grip is now owned by this extension until it opens; run the action.</summary>
    void BeginPress(in GripHookContext ctx);

    /// <summary>
    /// The owned grip opened. True when the release was consumed; false lets a hand that now holds an item (one
    /// drawn from a slot) release it like its own grip: throw, drop or keep.
    /// </summary>
    bool EndPress(in GripHookContext ctx);

    /// <summary>The holding hand opened a grip no extension owned. True consumes it (e.g. holstering over a slot).</summary>
    bool TryHeldRelease(in GripHookContext ctx);
}

/// <summary>
/// Registry of <see cref="IGripExtension"/>s. Features register as the last step of <c>Install</c>, so a feature
/// that fails to install never receives grips. When <c>interact.physical</c> is off no grip reaches them and they
/// fall back to their button paths.
/// </summary>
internal static class GripHooks
{
    private static readonly List<IGripExtension> Extensions = new();

    /// <summary>Asked after every other extension, whatever the feature order: a reach past the hand (interact.social's
    /// reach-out) must not outrank a body slot at the hand (inventory.vr installs after interact.social).</summary>
    private static readonly List<IGripExtension> Fallbacks = new();

    public static void Register(IGripExtension extension, bool last = false)
    {
        var list = last ? Fallbacks : Extensions;
        if (!list.Contains(extension))
            list.Add(extension);
    }

    /// <summary>First extension claiming the press, or null; the fallbacks only when <paramref name="fallbacks"/>.</summary>
    public static IGripExtension Claim(in GripHookContext ctx, bool fallbacks = true)
    {
        var hit = First(Extensions, ctx);
        return hit == null && fallbacks ? First(Fallbacks, ctx) : hit;
    }

    private static IGripExtension First(List<IGripExtension> list, in GripHookContext ctx)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].ClaimPress(ctx))
                return list[i];
        }
        return null;
    }

    /// <summary>True when an extension consumed the holding hand's release.</summary>
    public static bool HeldRelease(in GripHookContext ctx)
    {
        for (int i = 0; i < Extensions.Count; i++)
        {
            if (Extensions[i].TryHeldRelease(ctx))
                return true;
        }
        for (int i = 0; i < Fallbacks.Count; i++)
        {
            if (Fallbacks[i].TryHeldRelease(ctx))
                return true;
        }
        return false;
    }
}
