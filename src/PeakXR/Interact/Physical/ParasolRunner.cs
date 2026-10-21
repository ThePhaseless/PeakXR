using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Parasol runner (G12): the free hand grips a held parasol's shaft and slides up to open it or down to close it.
/// Each stroke presses the parasol's own primary use once (<c>Action_Parasol</c>, Action_Parasol.cs:7 calls
/// <c>Parasol.ToggleOpen</c>, Parasol.cs:23, which sends <c>ToggleOpenRPC</c>, Parasol.cs:56), so every client sees
/// the vanilla toggle. The parasol is one-handed, so the grip arrives as an <see cref="IGripExtension"/> claim
/// instead of a two-hand hold. Slide height is the free hand relative to the holding hand, so walking or raising
/// both arms does not count.
/// </summary>
internal sealed class ParasolRunner : IGripExtension
{
    public static readonly ParasolRunner Instance = new();

    private readonly RunnerSlide slide = new();
    private readonly InjectedPress press = new();
    private int hand = -1;
    private bool wantOpen;

    public void Reset()
    {
        hand = -1;
        slide.End();
        press.Reset();
    }

    public bool ClaimPress(in GripHookContext ctx)
    {
        var parasol = ItemGestures.Parasol;
        if (
            !VRSession.Active
            || !ItemGesturesConfig.Instance.ParasolRunner
            || ctx.ThisHandHolds
            || parasol == null
            || ctx.HeldItem == null
            || !ReferenceEquals(ctx.HeldItem, ItemGestures.Held)
        )
            return false;
        return NearShaft(ctx.HeldItem, ctx.HandPosition);
    }

    public void BeginPress(in GripHookContext ctx)
    {
        hand = ctx.Left ? 0 : 1;
        slide.Travel = 0.15f;
        slide.Begin(Along(ctx.Left));
        Haptics.Pulse(ctx.Left, 0.4f, 0.04f);
    }

    public bool EndPress(in GripHookContext ctx)
    {
        if (hand == (ctx.Left ? 0 : 1))
        {
            hand = -1;
            slide.End();
        }
        return true;
    }

    public bool TryHeldRelease(in GripHookContext ctx) => false;

    public void Tick(Character c, CharacterInput input, ItemGesturesConfig cfg, float dt)
    {
        var parasol = ItemGestures.Parasol;
        if (ItemGestures.ItemChanged || parasol == null || !cfg.ParasolRunner)
        {
            Reset();
            return;
        }
        if (hand >= 0 && !press.Active)
        {
            bool left = hand == 0;
            int stroke = slide.Update(Along(left));
            if ((stroke > 0 && !parasol.isOpen) || (stroke < 0 && parasol.isOpen))
            {
                wantOpen = stroke > 0;
                // Hold through the item's own use time, in case the prefab toggles on cast finish.
                press.Begin(Mathf.Max(0f, ItemGestures.Held.usingTimePrimary) + 0.5f);
                Haptics.Pulse(left, 0.5f, 0.06f);
                Haptics.Pulse(ItemGestures.HoldLeft, 0.3f, 0.06f);
            }
        }
        if (!press.Active)
            return;
        var f = press.Update(input.usePrimaryIsPressed, parasol.isOpen == wantOpen, dt);
        input.usePrimaryWasPressed |= f.Pressed;
        input.usePrimaryIsPressed |= f.Held;
        input.usePrimaryWasReleased |= f.Released;
    }

    /// <summary>Free hand height above the holding hand, tracking space.</summary>
    private static float Along(bool freeLeft) =>
        VRInput.Hand(freeLeft).Position.y - VRInput.Hand(!freeLeft).Position.y;

    private static bool NearShaft(Item item, Vector3 hand)
    {
        const float radius = 0.12f;
        foreach (var col in item.colliders)
        {
            if (
                col != null
                && col.enabled
                && (col.bounds.ClosestPoint(hand) - hand).sqrMagnitude <= radius * radius
            )
                return true;
        }
        return (item.Center() - hand).sqrMagnitude <= 0.35f * 0.35f;
    }
}
