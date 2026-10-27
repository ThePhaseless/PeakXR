using System;

namespace PeakXR.Core;

/// <summary>How <c>Camera.WorldScale</c> is chosen. Auto sizes the world to the player's calibrated height
/// (<see cref="WorldScale.Auto"/>), so head-to-floor is the same in the room and in the game.</summary>
public enum WorldScaleMode
{
    Auto,
    Manual,
}

/// <summary>
/// How big the world looks in VR (<c>Camera.WorldScale</c>, 1 = the game's own units as metres) and the conversions
/// that go with it. The player grows instead of the world shrinking: the tracking space is scaled by
/// <see cref="RigScale"/>, so head motion, hand reach and (rig.camera's stereo override) the eye separation all grow
/// together and nothing reads inconsistent. Tracking-space numbers stay real metres.
/// </summary>
public static class WorldScale
{
    /// <summary>Smallest and largest settable world scale.</summary>
    public const float Min = 0.6f;

    public const float Max = 1.25f;

    /// <summary>The band <see cref="Auto"/> stays in: a height taken kneeling never shrinks the world below
    /// <see cref="AutoMin"/>, a very tall player never sees it grow past <see cref="AutoMax"/>.</summary>
    public const float AutoMin = 0.75f;

    public const float AutoMax = 1.25f;

    /// <summary>The derived default never goes below this: a bigger player than this reaches past the scout's 0.6 m
    /// arm at every stretch and sees the floor too close under the scout's eyes.</summary>
    public const float ComfortMin = 0.8f;

    /// <summary>
    /// Fixed fittings and architecture measured in the game's data (Airport scene, world-space mesh bounds and
    /// surface heights; game units) against typical real sizes (metres). The scout's own eye height (1.64) and arm
    /// (0.605) match a real adult 1:1, and so do standard-height fittings (counters, rails, stanchions); doors,
    /// openings, seat backs and steps are built 15-65% bigger for the 2 m, big-headed scouts. Props (2-3x, an apple
    /// 0.42 across) are left out: no uniform scale makes them real without shrinking the player's own body.
    /// </summary>
    public static readonly (float Game, float Real)[] References =
    {
        (2.51f, 2.05f), // door leaf height
        (0.99f, 0.95f), // door push bar
        (2.93f, 2.10f), // elevator door leaf height
        (2.80f, 2.35f), // elevator cabin floor to ceiling
        (4.03f, 2.20f), // sliding door panel height
        (0.515f, 0.175f), // photobooth stair rise
        (0.56f, 0.45f), // seat height
        (1.41f, 0.85f), // seat back top
        (0.50f, 0.45f), // bench top
        (0.545f, 0.45f), // curved bench seat
        (1.10f, 1.05f), // check-in counter top
        (0.965f, 1.05f), // gate desk top
        (0.94f, 0.95f), // kiosk ledge
        (1.96f, 1.65f), // kiosk total height
        (0.977f, 0.97f), // stanchion
        (0.936f, 0.95f), // handrail top
        (0.885f, 0.95f), // trash bin
        (0.91f, 1.0f), // luggage cart rail
    };

    /// <summary>The <c>Camera.WorldScaleMode</c> Auto scale: the player's calibrated <paramref name="realEye"/> over the
    /// avatar's standing <paramref name="avatarEye"/> above its floor (both in metres/units). With the eyes on the
    /// avatar's, this puts the real floor on the game floor (<see cref="FloorReach"/> 0). Clamped to
    /// [<see cref="AutoMin"/>, <see cref="AutoMax"/>] and rounded to 0.01 so small sample noise lands on the same
    /// value; a height outside the plausible range reads as <see cref="HeightCalibration.DefaultStandingEyeHeight"/>.</summary>
    public static float Auto(float realEye, float avatarEye)
    {
        realEye = Plausible(realEye);
        avatarEye = Plausible(avatarEye);
        return MathF.Round(Math.Clamp(realEye / avatarEye, AutoMin, AutoMax) * 100f) / 100f;
    }

    private static float Plausible(float eye) =>
        eye >= HeightCalibration.MinPlausibleEyeHeight
        && eye <= HeightCalibration.MaxPlausibleEyeHeight
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;

    /// <summary>The <c>Camera.WorldScaleMode</c> Manual default: the median real-over-game ratio of <see cref="References"/> (0.87), kept in the comfort
    /// band.</summary>
    public static readonly float Default = Comfortable(FromReferences(References));

    /// <summary>World units per real metre of tracking space for a world that looks <paramref name="worldScale"/>
    /// times its size (clamped; NaN reads as 1).</summary>
    public static float RigScale(float worldScale)
    {
        if (float.IsNaN(worldScale))
            return 1f;
        return 1f / Math.Clamp(worldScale, Min, Max);
    }

    public static float ToWorld(float metres, float rigScale) => metres * rigScale;

    public static float ToReal(float units, float rigScale) => units / rigScale;

    /// <summary>Real hand height above the real floor that reaches the game floor, with the eyes on the avatar's
    /// (<paramref name="avatarEye"/> units above its feet) and the player's calibrated <paramref name="realEye"/>:
    /// positive means the game floor is reached before the real one.</summary>
    public static float FloorReach(float avatarEye, float realEye, float rigScale) =>
        realEye - avatarEye / rigScale;

    /// <summary>Median of real / game over <paramref name="refs"/>.</summary>
    public static float FromReferences(ReadOnlySpan<(float Game, float Real)> refs)
    {
        if (refs.Length == 0)
            return 1f;
        var r = new float[refs.Length];
        for (int i = 0; i < refs.Length; i++)
            r[i] = refs[i].Real / refs[i].Game;
        Array.Sort(r);
        int m = r.Length / 2;
        return r.Length % 2 == 1 ? r[m] : 0.5f * (r[m - 1] + r[m]);
    }

    /// <summary>A measured ratio kept within [<see cref="ComfortMin"/>, 1], rounded to 0.01.</summary>
    public static float Comfortable(float measured) =>
        MathF.Round(Math.Clamp(measured, ComfortMin, 1f) * 100f) / 100f;
}
