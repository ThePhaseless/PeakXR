using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// How long a hand's grip outlives its tracking. Lost tracking keeps the last grip (opening the hand there dropped
/// or threw items, and dropped climbers), and normal play loses it for seconds: a Lighthouse controller hidden behind
/// the body, a WMR or Reverb G2 controller behind the back or out of the headset cameras' view. Only a controller
/// that is really gone opens: one the runtime reports disconnected for <see cref="DisconnectSeconds"/> in one go
/// (switched off, flat; a radio hiccup or a profile reload drops the device for less), and one untracked for
/// <see cref="MaxSeconds"/>, which covers a runtime that keeps a dead controller as an untracked device. While the
/// runtime holds input focus (its dashboard is open) the clock stops: the player is not playing then, multiplayer
/// does not pause, and SteamVR may report the controllers untracked or gone, so opening the grip would drop a climber
/// or an item the player never let go of. The clock resumes where it stopped when focus comes back.
/// </summary>
public sealed class BlinkHold
{
    /// <summary>Far longer than any occlusion in play (a look over the shoulder, a reach behind the back), short
    /// enough that a dead controller does not hold an item or a rope for good.</summary>
    public const float MaxSeconds = 8f;

    /// <summary>Longer than a radio hiccup or a controller profile reload (under a second), short enough that a
    /// switched-off controller lets go about as soon as the player looks for it.</summary>
    public const float DisconnectSeconds = 2f;

    private float lost;
    private float off;
    private bool gone;

    /// <summary>The hand's controller has been disconnected for <see cref="DisconnectSeconds"/> or untracked past
    /// <see cref="MaxSeconds"/>: treat its grip as opened, until it is tracked again.</summary>
    public bool Expired => gone || lost >= MaxSeconds;

    /// <summary>Why the hold expired, for the caller's log line.</summary>
    public static string Why(bool disconnected) =>
        disconnected
            ? $"disconnected for {DisconnectSeconds:0.##} s"
            : $"untracked for {MaxSeconds:0.##} s";

    /// <param name="tracked">The controller's pose is tracked this frame.</param>
    /// <param name="disconnected">The runtime has no device for this hand (switched off, flat, unpaired).</param>
    /// <param name="paused">The runtime holds input focus (dashboard open): the hold neither runs down nor opens.</param>
    /// <param name="dt">Seconds since the last update.</param>
    /// <returns>True on the frame the hold expires (the caller's one log line or cue).</returns>
    public bool Update(bool tracked, bool disconnected, bool paused, float dt)
    {
        bool was = Expired;
        if (tracked)
        {
            lost = off = 0f;
            gone = false;
            return false;
        }
        if (paused)
            return false;
        dt = Math.Max(dt, 0f);
        lost += dt;
        off = disconnected ? off + dt : 0f;
        gone |= off >= DisconnectSeconds;
        return Expired && !was;
    }
}

/// <summary>
/// Turns arm-pulled body motion into the stick input the game's wall climbing reads, so physical climbing moves
/// through the game's own climb code (speed limits, stamina cost, slides) instead of moving the body directly.
/// Axes match <c>CharacterClimbing.GetRequestedPostition</c>: Y along the wall's up, X along its right.
/// </summary>
public static class ClimbInput
{
    static readonly Vector3 WorldUp = new Vector3(0f, 1f, 0f);

    /// <summary>
    /// A lost controller freezes a rope, vine or handle hang (no release, no let-go), so a tracking blink never drops
    /// the climber. Only a hand in use counts: one still anchored, or any controller in two-handed play. One-handed
    /// play leaves the other controller untracked for good, and it must not freeze the one hand forever.
    /// </summary>
    /// <param name="theOneHand">This is the controller one-handed play reads.</param>
    public static bool FreezesHang(bool tracked, bool anchored, bool oneHanded, bool theOneHand) =>
        !tracked && (anchored || !oneHanded || theOneHand);

    /// <summary>
    /// Wall speed at full stick, m/s, to pass as <c>climbSpeed</c> to <see cref="FromBodyVelocity"/>. The game asks
    /// for <c>climbSpeed * multiplier</c> per second, but <c>SampleWall</c> casts from 0.4 m off the wall to 1 m
    /// behind the requested point, so on a flat wall only 0.4/1.4 of each step lands (CharacterClimbing.cs:327, :412).
    /// </summary>
    /// <param name="multiplier">climbSpeedMod x staminaMod x surface speedMultiplier (CharacterClimbing.cs:309).</param>
    public static float WallSpeed(float climbSpeed, float multiplier) =>
        climbSpeed * multiplier * (0.4f / 1.4f);

    /// <param name="bodyVelocity">Wanted body velocity, world space, m/s.</param>
    /// <param name="wallNormal">Climb surface normal (points away from the wall).</param>
    /// <param name="climbSpeed">Game climb speed in m/s at full stick.</param>
    /// <param name="deadzone">Stick magnitude below which the result is zero.</param>
    public static Vector2 FromBodyVelocity(
        Vector3 bodyVelocity,
        Vector3 wallNormal,
        float climbSpeed,
        float deadzone
    )
    {
        if (climbSpeed <= 1e-5f || wallNormal.LengthSquared() < 1e-8f)
            return Vector2.Zero;
        var n = Vector3.Normalize(wallNormal);
        var up = WorldUp - n * Vector3.Dot(WorldUp, n);
        if (up.LengthSquared() < 1e-8f)
            return Vector2.Zero;
        up = Vector3.Normalize(up);
        var right = -Vector3.Normalize(Vector3.Cross(up, n));

        var input =
            new Vector2(Vector3.Dot(bodyVelocity, right), Vector3.Dot(bodyVelocity, up))
            / climbSpeed;
        float len = input.Length();
        if (len < deadzone)
            return Vector2.Zero;
        return len > 1f ? input / len : input;
    }

    /// <summary>Exponential smoothing toward <paramref name="target"/>; rate &lt;= 0 snaps.</summary>
    public static Vector2 Smooth(Vector2 current, Vector2 target, float rate, float dt)
    {
        return current + (target - current) * ExpSmooth.Factor(rate, dt);
    }

    /// <summary>
    /// Cleans the smoothed arm input before the game reads it. Smoothing never decays exactly to zero, and the
    /// game climbs down at full speed for any negative stick y (CharacterClimbing.cs:303), so a residue would
    /// slide the climber down with the hands still. Tiny components become zero, and y goes negative only while
    /// the arms clearly pull down (<paramref name="target"/> below -0.3).
    /// </summary>
    public static Vector2 Settle(Vector2 smoothed, Vector2 target)
    {
        const float deadzone = 0.05f;
        float x = MathF.Abs(smoothed.X) < deadzone ? 0f : smoothed.X;
        float y = MathF.Abs(smoothed.Y) < deadzone ? 0f : smoothed.Y;
        if (y < 0f && target.Y > -0.3f)
            y = 0f;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Climb lunge from a hand thrown upward as it lets go of the wall: the stick direction (wall right, wall
    /// up; unit length) for the game's climb jump, or zero when the release was not fast enough upward.
    /// </summary>
    /// <param name="releaseVelocity">Hand velocity relative to the body at release, world space, m/s.</param>
    /// <param name="wallNormal">Climb surface normal.</param>
    /// <param name="minUpSpeed">Speed along the wall's up needed to count as a lunge.</param>
    public static Vector2 Lunge(Vector3 releaseVelocity, Vector3 wallNormal, float minUpSpeed)
    {
        var stick = FromBodyVelocity(releaseVelocity, wallNormal, 1f, 0f);
        float len = stick.Length();
        if (len < 1e-5f)
            return Vector2.Zero;
        // FromBodyVelocity clamps to unit length; recover the raw up speed from the unclamped projection.
        var n = Vector3.Normalize(wallNormal);
        var up = WorldUp - n * Vector3.Dot(WorldUp, n);
        if (up.LengthSquared() < 1e-8f)
            return Vector2.Zero;
        float upSpeed = Vector3.Dot(releaseVelocity, Vector3.Normalize(up));
        if (upSpeed < minUpSpeed)
            return Vector2.Zero;
        return stick / len;
    }

    /// <summary>
    /// Walking stick input that moves the character along <paramref name="worldDir"/> (flattened): the
    /// inverse of <c>Character.CalculateWorldMovementDir</c> (Character.cs:1311), which walks along the flat
    /// look direction times stick Y plus look-right times stick X. Zero for a vertical direction.
    /// </summary>
    public static Vector2 WalkStick(Vector3 worldDir, Vector3 lookForward, Vector3 lookRight)
    {
        var dir = new Vector3(worldDir.X, 0f, worldDir.Z);
        var fwd = new Vector3(lookForward.X, 0f, lookForward.Z);
        var right = new Vector3(lookRight.X, 0f, lookRight.Z);
        if (
            dir.LengthSquared() < 1e-8f
            || fwd.LengthSquared() < 1e-8f
            || right.LengthSquared() < 1e-8f
        )
            return Vector2.Zero;
        dir = Vector3.Normalize(dir);
        var stick = new Vector2(
            Vector3.Dot(dir, Vector3.Normalize(right)),
            Vector3.Dot(dir, Vector3.Normalize(fwd))
        );
        float len = stick.Length();
        return len > 1f ? stick / len : stick;
    }
}
