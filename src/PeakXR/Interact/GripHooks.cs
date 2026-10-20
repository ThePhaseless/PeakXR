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

    public GripHookContext(
        Character character,
        bool left,
        Vector3 handPosition,
        Item heldItem,
        bool thisHandHolds
    )
    {
        Character = character;
        Left = left;
        HandPosition = handPosition;
        HeldItem = heldItem;
        ThisHandHolds = thisHandHolds;
    }
}

/// <summary>
/// A feature that takes grips before <c>interact.physical</c>'s own grab, climb and interact logic. Priority follows
/// <see cref="Core.GripIntent"/>: a free hand joining the held item comes first, then extensions (in registration
/// order), then grab, climb and the interact fallback. Methods run on grip edges of the local VR player only and
/// must not allocate.
/// </summary>
internal interface IGripExtension
{
    /// <summary>Grip closed: true when this extension wants it. Must not act yet (the second hand may still win).</summary>
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

    public static void Register(IGripExtension extension)
    {
        if (!Extensions.Contains(extension))
            Extensions.Add(extension);
    }

    /// <summary>First extension claiming the press, or null.</summary>
    public static IGripExtension Claim(in GripHookContext ctx)
    {
        for (int i = 0; i < Extensions.Count; i++)
        {
            if (Extensions[i].ClaimPress(ctx))
                return Extensions[i];
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
        return false;
    }
}
