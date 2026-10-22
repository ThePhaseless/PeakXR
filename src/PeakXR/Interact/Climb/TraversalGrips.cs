using System;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using UnityEngine;

namespace PeakXR.Interact.Climb;

/// <summary>
/// A grip that closes on a rope, vine or piton handle the game would not let the player take. The hand-picked grab
/// only offers what <c>IsInteractible</c> accepts, so without this the grip fell through to a wall climb or a
/// head-aimed interact with no cue. This extension takes the grip and says why (<see cref="TraversalRefusal"/>):
/// <list type="bullet">
/// <item>rope (RopeSegment.cs:49-58): not anchored, segment too far from vertical, out of stamina;</item>
/// <item>capsule vine (JungleVine.cs:96-108): out of stamina;</item>
/// <item>handle (ClimbHandle.cs:99-102): someone else hangs there.</item>
/// </list>
/// The rope's and vine's 0.2 s regrab timeout (CharacterRopeHandling.cs:31, CharacterVineClimbing.cs:44) takes the
/// grip quietly. Geometry decides "on the line" (<see cref="ClimbHands.NearRope"/>), so a grip on the wall beside a
/// rope still climbs the wall.
/// </summary>
internal sealed class TraversalGrips : IGripExtension
{
    public static readonly TraversalGrips Instance = new();

    /// <summary>Overlap radius that finds a line's colliders: vine triggers sit <c>colliderOffset</c> above the drawn
    /// vine (JungleVine.cs:286-297), so the search is wider than the grip radius.</summary>
    private const float SearchRadius = 0.5f;

    private const float VineRise = 1.5f;

    // Jungle and camp scenes put many trigger and prop colliders around the hand: room for all of them, so the line is
    // not cut off the end of the list.
    private static readonly Collider[] Hits = new Collider[128];

    /// <summary>What the last grip check found, for self-test notes (vr.57-59).</summary>
    internal static string LastNote = "";

    private DenyReason reason;
    private Vector3 spot;
    private Component tint;
    private DenyHint hint;

    public bool ClaimPress(in GripHookContext ctx)
    {
        tint = null;
        hint = default;
        reason = DenyReason.None;
        if (ctx.ThisHandHolds || !InteractConfig.Instance.PhysicalGrab)
            return false;
        try
        {
            return Find(ctx.Character, ctx.HandPosition);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[interact.climb] traversal grip check failed: {e.Message}");
            return false;
        }
    }

    public void BeginPress(in GripHookContext ctx)
    {
        switch (reason)
        {
            case DenyReason.None:
                // The regrab timeout: the game will take the next grip.
                break;
            case DenyReason.NoStamina:
                Affordance.DenyStamina(ctx.Left, spot);
                break;
            default:
                Affordance.Deny(ctx.Left, spot, reason, hint, tint);
                break;
        }
        tint = null;
    }

    public bool EndPress(in GripHookContext ctx) => true;

    public bool TryHeldRelease(in GripHookContext ctx) => false;

    /// <summary>A refused (or timed-out) line or handle under the hand; fills <see cref="reason"/>.</summary>
    private bool Find(Character c, Vector3 pos)
    {
        // Up to VineRise above the hand: a vine's trigger capsules sit colliderOffset above the drawn vine the hand
        // holds, more than SearchRadius on some vines (run 34's vr.59 found none).
        int n = Physics.OverlapCapsuleNonAlloc(
            pos,
            pos + Vector3.up * VineRise,
            SearchRadius,
            Hits,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide
        );
        bool found = false;
        bool outOfStamina = PhysicalInteraction.OutOfStamina(c);
        int ropes = 0,
            vines = 0,
            handles = 0;
        for (int i = 0; i < n && !found; i++)
        {
            var col = Hits[i];
            Hits[i] = null;
            if (col == null)
                continue;
            if (col.TryGetComponent<RopeSegment>(out var segment))
            {
                ropes++;
                found = Rope(c, segment, pos, outOfStamina);
            }
            else if (col.GetComponentInParent<ClimbHandle>() is { } handle)
            {
                handles++;
                found = Handle(c, handle, pos);
            }
            else if (col.GetComponentInParent<JungleVine>() is { } vine)
            {
                vines++;
                found = Vine(c, vine, pos, outOfStamina);
            }
        }
        for (int i = 0; i < n; i++)
            Hits[i] = null;
        LastNote = System.FormattableString.Invariant(
            $"grip check: {n} colliders, rope {ropes}, vine {vines}, handle {handles}; out of stamina {outOfStamina}; claimed {found} ({reason})"
        );
        return found;
    }

    private bool Rope(Character c, RopeSegment segment, Vector3 pos, bool outOfStamina)
    {
        var rope = segment.rope;
        var d = c.data;
        if (
            rope == null
            || (d.isRopeClimbing && d.heldRope == rope)
            || !ClimbHands.NearRope(rope, pos, ClimbHands.LineRadius)
        )
            return false;
        var handling = c.refs.ropeHandling;
        reason = TraversalRefusal.Rope(
            segment.GetAngle(),
            handling.maxRopeAngle,
            rope.isClimbable,
            outOfStamina
        );
        spot = segment.transform.position;
        tint = rope;
        // Would work once fixed: the rope's first segment, the end that anchors it.
        if (reason == DenyReason.Unanchored && Affordance.Hints)
        {
            var segments = rope.GetRopeSegments();
            if (segments != null && segments.Count > 0 && segments[0] != null)
                hint = DenyHint.On(segments[0]);
        }
        return reason != DenyReason.None || !handling.canInteractWithRope;
    }

    private bool Vine(Character c, JungleVine vine, Vector3 pos, bool outOfStamina)
    {
        var d = c.data;
        if (
            vine.colliderType != JungleVine.ColliderType.Capsule
            || (d.isVineClimbing && d.heldVine == vine)
            || !ClimbHands.NearVine(vine, pos, ClimbHands.LineRadius)
        )
            return false;
        reason = TraversalRefusal.Vine(outOfStamina);
        spot = pos;
        tint = vine;
        return reason != DenyReason.None || !c.refs.vineClimbing.canInteractVine;
    }

    private bool Handle(Character c, ClimbHandle handle, Vector3 pos)
    {
        float r = ClimbHands.HandleRadius * 1.5f;
        if ((handle.transform.position - pos).sqrMagnitude > r * r)
            return false;
        reason = TraversalRefusal.Handle(handle.hanger != null, handle.hanger == c);
        spot = handle.transform.position;
        tint = handle;
        return reason != DenyReason.None;
    }
}
