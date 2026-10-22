using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// The game's own duration for what a self-test step waits on, read from that item's or object's data, and the
/// wait budget around it. Items differ (an apple, a bandage, a lit lantern each take their own time), so a fixed
/// budget sized for one fails a slower one.
/// </summary>
internal static class GameTiming
{
    /// <summary>Slowest terminal fall speed to plan for (m/s); the ragdoll fell at 57 m/s in vr25. A lower cap
    /// only lengthens the wait.</summary>
    private const float TerminalFallSpeed = 50f;

    /// <summary>A zombie idles this long after waking before it looks for a target (MushroomZombie.cs:590).</summary>
    private const float ZombieIdleSeconds = 2f;

    /// <summary>Seconds a wait gets for <paramref name="gameSeconds"/> of game time after
    /// <paramref name="leadIn"/> seconds of scripted setup (<see cref="SelfTestChecks.WaitBudget"/>).</summary>
    internal static float Budget(float gameSeconds, float leadIn = 0f) =>
        SelfTestChecks.WaitBudget(gameSeconds, leadIn);

    /// <summary>Primary use hold: <c>Item.usingTimePrimary</c> (Item.cs:130, cast progress Item.cs:874-876).
    /// A lantern or candle rewrites it when lit (Lantern.cs:151, Candle.cs:114), so read it at the time.</summary>
    internal static float UseSeconds(Item item) => item != null ? item.usingTimePrimary : 0f;

    /// <summary>Use on a friend, the secondary hold: <c>Item.totalSecondaryUsingTime</c>, 0.7 of the primary for
    /// <c>canUseOnFriend</c> items (Item.cs:362-371).</summary>
    internal static float FeedSeconds(Item item) =>
        item != null ? item.totalSecondaryUsingTime : 0f;

    /// <summary>Interact hold: <c>GetInteractTime</c> (IInteractibleConstant.cs:7, read at Interaction.cs:122):
    /// a campfire's <c>cookTime</c> to light or cook (Campfire.cs:441), a suitcase's <c>timeToOpen</c>
    /// (Luggage.cs:214).</summary>
    internal static float InteractSeconds(IInteractibleConstant target, Character c) =>
        target != null ? target.GetInteractTime(c) : 0f;

    /// <summary>A spawned zombie stays hidden, and cannot be woken, this long (<c>RevealZombie</c>,
    /// MushroomZombie.cs:493-501, started in <c>Start</c>, :238-239; <c>DoSleeping</c> needs <c>visible</c>, :507).</summary>
    private const float ZombieRevealSeconds = 1f;

    /// <summary>Spawn to target of a level zombie seen at once: the reveal, then <see cref="ZombieRiseSeconds"/>.</summary>
    internal static float ZombieWakeSeconds(MushroomZombie zombie) =>
        ZombieRevealSeconds + ZombieRiseSeconds(zombie);

    /// <summary>From waking to a target: <c>initialWakeUpTime</c> (MushroomZombie.cs:38, counted at :649), then the
    /// idle before it picks one (:590-596).</summary>
    internal static float ZombieRiseSeconds(MushroomZombie zombie) =>
        zombie.initialWakeUpTime + ZombieIdleSeconds;

    /// <summary>Time for a vine to grow <paramref name="meters"/> at its <c>growingSpeed</c> (MagicBeanVine.cs:15,
    /// :29).</summary>
    internal static float VineGrowSeconds(MagicBeanVine vine, float meters) =>
        meters / Mathf.Max(0.01f, vine.growingSpeed);

    /// <summary>Time to fall <paramref name="height"/> metres under the physics gravity.</summary>
    internal static float FallSeconds(float height) =>
        SelfTestChecks.FallSeconds(height, Mathf.Abs(Physics.gravity.y), TerminalFallSpeed);
}
