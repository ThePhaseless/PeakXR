using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// Hammering a climbing spike: while the game runs a spike cast, each strike of a tracked hand into the wall at the
/// preview (a knock and a hard pulse) brings the placement forward. Holding alone still places it at the game's time;
/// all strikes place it up to a third sooner (<see cref="HammerCast"/>, CharacterItems.cs:999-1000). The game's own
/// checks (use button held,
/// preview in range) still cancel it. With no tracked hand, or the setting off, the hold timer runs as usual.
/// </summary>
internal static class SpikeHammer
{
    private static readonly StrikeGesture LeftStrike = NewStrike();
    private static readonly StrikeGesture RightStrike = NewStrike();

    private static bool casting;
    private static int strikes;

    // The timer a vanilla hold would show now: the game's start value plus every frame since.
    private static float elapsed;
    private static bool lastStrikeLeft;

    /// <summary>A spike cast is running and being hammered (self-test vr.44c).</summary>
    internal static bool Casting => casting;

    /// <summary>Strikes landed in the running cast.</summary>
    internal static int Strikes => strikes;

    /// <summary>Where the running cast's preview sits on the wall.</summary>
    internal static Vector3 Preview { get; private set; }

    private static StrikeGesture NewStrike() =>
        new()
        {
            Radius = 0.2f,
            RearmRadius = 0.35f,
            MinSpeed = 1.2f,
        };

    public static void BeforeUpdate(
        Character character,
        ref float tick,
        ItemSlot slot,
        in RaycastHit hit
    )
    {
        if (character == null || !character.IsLocal)
            return;
        var rig = VRRig.Instance;
        bool leftTracked = VRInput.Left.Tracked;
        bool rightTracked = VRInput.Right.Tracked;
        if (
            tick <= 0f
            || slot == null
            || slot.prefab == null
            || !ToolsConfig.Instance.HammerSpikes
            || !VRSession.Active
            || rig == null
            || rig.Origin == null
            || (!leftTracked && !rightTracked)
        )
        {
            casting = false;
            return;
        }
        if (!casting)
        {
            casting = true;
            strikes = 0;
            elapsed = tick;
            LeftStrike.Reset();
            RightStrike.Reset();
        }
        Preview = hit.point;
        if (leftTracked)
            Strike(LeftStrike, true, rig, in hit);
        if (rightTracked)
            Strike(RightStrike, false, rig, in hit);
        int needed = ToolsConfig.Instance.HammerStrikes;
        float castTime = slot.prefab.usingTimePrimary;
        tick = HammerCast.TickBeforeUpdate(strikes, needed, castTime, Time.deltaTime, elapsed);
        elapsed += Time.deltaTime;
        // All strikes in before the capped time (HammerCast.MaxSpeedup): keep holding, it is nearly in.
        if (strikes >= needed && tick + Time.deltaTime < castTime)
            ParityFeedback.Denied(lastStrikeLeft, hit.point, ParityFeedback.KeepHolding);
    }

    /// <summary>A strike: the hand reaches the preview point moving into the wall (against its normal).</summary>
    private static void Strike(StrikeGesture gesture, bool left, VRRig rig, in RaycastHit hit)
    {
        var hand = rig.Hand(left);
        if (hand == null)
            return;
        ref var state = ref VRInput.Hand(left);
        var velocity = rig.Origin.TransformVector(state.Velocity);
        float distance = Vector3.Distance(hand.position, hit.point);
        float speed = Vector3.Dot(velocity, -hit.normal);
        if (!gesture.Update(distance, speed))
            return;
        strikes++;
        lastStrikeLeft = left;
        Haptics.Pulse(left, 0.8f, 0.06f);
        // Heard where it lands: a dull knock at the spike (the cue set's end-stop knock).
        UI.Affordances.Affordance.Play(CueSound.EndStop, hit.point);
    }
}
