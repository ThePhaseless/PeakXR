using System;

namespace PeakXR.Core;

/// <summary>
/// How big the local VR player's own hands are drawn (<c>Avatar.HandScale</c>, 1 = the game's size). The scout's
/// cartoon hand is about twice an adult's width and three times its thickness, and in VR it sits right in front of
/// the eyes, so it made everything read as gigantic (tester: "the scale problem comes mainly from the hands"). The
/// hand is drawn smaller about the controller grip, the forearm at the same thickness and the upper arm halfway, so
/// the arm tapers without a step.
/// </summary>
public static class HandScale
{
    /// <summary>Smallest and largest settable hand scale.</summary>
    public const float Min = 0.4f;

    public const float Max = 1f;

    /// <summary>The scout's palm across its fingers, game units (Character/Scout/MainMesh skinned to its rest pose,
    /// Scout transform x0.3308).</summary>
    public const float ScoutPalmWidth = 0.178f;

    /// <summary>
    /// The scout's right hand from the game's data (game units, its rest pose) against an adult hand (metres). Its
    /// length is near real; its width and thickness are 2-3.5x, which is what reads as chunky. The wrist (0.175
    /// across) and forearm (0.205) are a near-uniform tube, so the forearm takes the hand's scale.
    /// </summary>
    public static readonly (float Game, float Real)[] References =
    {
        (ScoutPalmWidth, 0.085f), // palm width across the fingers
        (0.106f, 0.030f), // palm thickness, back to palm
        (0.045f, 0.019f), // finger thickness, tip segment
        (0.205f, 0.190f), // hand length, wrist joint to fingertip
    };

    /// <summary>The default (0.5): the hand at an adult's size at 1:1, the world scale that follows the player's real
    /// eye height lands near. Seen at 1:1: 8.9 cm wide, a 5.3 cm palm, 2.3 cm fingers, 10.3 cm long.</summary>
    public static readonly float Default = LifeSize(References, 1f);

    /// <summary>
    /// The scale that draws the hand life-size for a rig <paramref name="rigScale"/> world units per real metre: the
    /// geometric mean of the real/game ratios of <paramref name="refs"/> (width, thickness and length weigh alike,
    /// a hand too thick by as much as it is too short cancels out) times the rig's growth, rounded up to 0.05 (the
    /// bigger hand reads better) and kept in range.
    /// </summary>
    public static float LifeSize(ReadOnlySpan<(float Game, float Real)> refs, float rigScale)
    {
        if (refs.Length == 0)
            return Max;
        float log = 0f;
        foreach (var (game, real) in refs)
            log += MathF.Log(real / game);
        float mean = MathF.Exp(log / refs.Length);
        return Clamp(MathF.Ceiling(mean * rigScale * 20f - 1e-3f) / 20f);
    }

    /// <summary>A setting kept in [<see cref="Min"/>, <see cref="Max"/>]; NaN reads as the game's size.</summary>
    public static float Clamp(float scale) =>
        float.IsNaN(scale) ? Max : Math.Clamp(scale, Min, Max);

    /// <summary>The upper arm's drawn thickness: halfway from the body's to the hand's, so the arm narrows in two
    /// gentle steps (elbow and shoulder) instead of one at the wrist.</summary>
    public static float UpperArm(float hand) => 0.5f * (1f + hand);

    /// <summary>Size of a ghost hand modelled on a real <paramref name="ghostPalm"/>-wide palm that matches the
    /// drawn hand.</summary>
    public static float Ghost(float hand, float ghostPalm) => ScoutPalmWidth * hand / ghostPalm;

    /// <summary>Curl at which the drawn fingers meet a held item of drawn <paramref name="radius"/>: smaller fingers
    /// bend less round the same item (<see cref="HandPose.WrapCurl"/>).</summary>
    public static float Wrap(float radius, float hand) =>
        HandPose.WrapCurl(hand > 0f ? radius / hand : radius);
}
