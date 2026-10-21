namespace PeakXR.Core;

/// <summary>
/// The throw charge a hand throw sends. Vanilla's <c>DropItemRpc</c> (CharacterItems.cs:385-428) adds
/// <c>0.5 * multiplier * (minThrowForce + (maxThrowForce - minThrowForce) * charge)</c> along the look direction
/// for any charge above 0. A hand throw sends the charge that force would have taken to reach the hand's own speed,
/// so a hard swing is a charged throw (sticky items need one, :277-289), and the held item's velocity is reduced by
/// the look force the master will add, so the item still leaves along the hand.
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
}
