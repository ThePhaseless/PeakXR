namespace PeakXR.Core;

/// <summary>
/// The throw charge a hand throw sends. Vanilla's <c>DropItemRpc</c> (CharacterItems.cs:385-428) adds
/// <c>0.5 * multiplier * (minThrowForce + (maxThrowForce - minThrowForce) * charge)</c> along the look direction
/// for any charge above 0. A hand throw sends the charge that force would have taken to reach the hand's own speed,
/// so a hard swing is a charged throw (sticky items need one, :277-289). <see cref="Release"/> then sends the charge
/// only where a sticky item needs it, so the item leaves along the hand.
/// </summary>
public static class HandThrow
{
    /// <param name="handSpeed">Release speed relative to the body, m/s.</param>
    /// <param name="minCharge">The item's sticky <c>throwChargeRequirement</c>, or 0.</param>
    public static float Charge(
        float handSpeed,
        float minForce,
        float maxForce,
        float multiplier,
        float minCharge
    )
    {
        float range = maxForce - minForce;
        float charge = 0f;
        if (range > 1e-4f && multiplier > 1e-4f)
            charge = System.Math.Clamp(
                (handSpeed / (0.5f * multiplier) - minForce) / range,
                0f,
                1f
            );
        return System.MathF.Max(charge, System.Math.Clamp(minCharge, 0f, 1f));
    }

    /// <summary>Speed the master adds along the look for <paramref name="charge"/> (0 for no charge).</summary>
    public static float LookSpeed(float charge, float minForce, float maxForce, float multiplier) =>
        charge > 0f ? 0.5f * multiplier * (minForce + (maxForce - minForce) * charge) : 0f;

    /// <summary>
    /// Release speed of a hand throw, relative to the body. A real arm tops out far below a charged vanilla throw
    /// (a hard swing is 6 to 8 m/s, a full charge 0.5 * multiplier * maxThrowForce), so items tuned to break or bonk
    /// on a strong vanilla throw never did. From <paramref name="fromSpeed"/> (the throw threshold, the hand's own
    /// speed) the launch rises to <paramref name="fullLaunch"/> at <paramref name="fullSpeed"/>, and never above it.
    /// </summary>
    public static float LaunchSpeed(
        float handSpeed,
        float fromSpeed,
        float fullSpeed,
        float fullLaunch
    )
    {
        float t =
            fullSpeed - fromSpeed > 1e-4f
                ? System.Math.Clamp((handSpeed - fromSpeed) / (fullSpeed - fromSpeed), 0f, 1f)
                : 1f;
        return System.MathF.Min(
            fullLaunch,
            handSpeed + System.MathF.Max(0f, fullLaunch - handSpeed) * t
        );
    }

    /// <summary>The charge a throw has to keep (the floor <see cref="Release"/> takes): a sticky item's
    /// requirement, and for an item vanilla sends with no velocity (<paramref name="sentStill"/>, a backpack,
    /// CharacterItems.cs:268-272) all of it. With none it only drops, and BackpackStabilizer reads a charge under 0.15 as
    /// a drop too (#117).</summary>
    public static float ChargeKept(bool sentStill, float charge, float stickyCharge) =>
        sentStill ? charge : stickyCharge;

    /// <summary>
    /// The velocity to leave on the held item, and the charge to send, so a throw leaves at <paramref name="launch"/>.
    /// For any charge the master adds <paramref name="lookSpeed"/> along its own copy of the thrower's look
    /// (CharacterItems.cs:391-395, :414, :426), which on a client lags the head, so taking that force off here left a
    /// lean toward where the head turned (#30). With no charge it adds nothing: the whole launch rides the item's
    /// velocity, exactly, on every client. Only a sticky item (<paramref name="minCharge"/> above 0) needs its charge
    /// (:277-289) and keeps it, the look force taken off along <paramref name="look"/>.
    /// </summary>
    public static System.Numerics.Vector3 Release(
        System.Numerics.Vector3 launch,
        System.Numerics.Vector3 look,
        float lookSpeed,
        ref float charge,
        float minCharge
    )
    {
        if (minCharge <= 0f || charge <= 0f)
        {
            charge = 0f;
            return launch;
        }
        return look.LengthSquared() < 1e-8f
            ? launch
            : launch - System.Numerics.Vector3.Normalize(look) * lookSpeed;
    }
}
