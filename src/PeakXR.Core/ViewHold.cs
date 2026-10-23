using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Holds the first-person view still against the standing body's idle sway. Since PEAK 3.0 the ragdoll sways about
/// 0.07 m/s and bobs about 4 cm while standing (2 mm on 2.6.b); the view rides the body, so a still player saw the
/// world move. While <see cref="ViewHoldGate"/> says the body is only idling (or holding a lean still), the view stays where it is unless the
/// body wanders past <see cref="Deadband"/> (then it is carried along, never left behind) and glides back onto the
/// body very slowly; the moment the body really moves, the held offset decays onto it in a fraction of a second.
/// Local view only: nothing about the body changes.
/// </summary>
public sealed class ViewHold
{
    /// <summary>Seconds the gate must stay open before the hold takes over, so a pause in a walk is not held.</summary>
    public const float EngageSeconds = 0.3f;

    /// <summary>How far (m) the body may sway from the held view before the view is carried along. Above the
    /// measured sway's 2.2 cm amplitude, both ways.</summary>
    public const float Deadband = 0.05f;

    /// <summary>Time constant (s) of the held view's glide back onto the body: slow enough to be unseen.</summary>
    public const float RecenterSeconds = 120f;

    /// <summary>Time constant (s) of the release onto the moving body.</summary>
    public const float ReleaseSeconds = 0.15f;

    /// <summary>A jump of the body this large (m) in one update is a warp: the view goes with it at once.</summary>
    public const float SnapDistance = 0.5f;

    private Vector3 output;

    // The view's offset from the body: while released it rides the body and shrinks.
    private Vector3 off;
    private bool have;
    private float stillFor;

    /// <summary>The hold is keeping the view off the body this update.</summary>
    public bool Holding => stillFor >= EngageSeconds;

    /// <summary>The view's offset from the body (m), for tracing.</summary>
    public float Offset { get; private set; }

    /// <summary>
    /// The eye anchor moved under a still body (a snap or smooth turn swings it round the torso, and the turn shift
    /// cancels that swing): carry the held point with it, keeping the offset, so the turn pivots on the eye. Call
    /// before <see cref="Update"/> on every frame the turn shift is being set.
    /// </summary>
    public void Rebase(Vector3 body)
    {
        if (have)
            output = body + off;
    }

    /// <summary>Forget the held point: the next update starts on the body (recenter, new view, warp).</summary>
    public void Reset()
    {
        have = false;
        stillFor = 0f;
        Offset = 0f;
    }

    /// <param name="body">Where the view would be without the hold (the body's eye anchor).</param>
    /// <param name="still"><see cref="ViewHoldGate.Why"/> is null: nothing but idling moves the body.</param>
    /// <returns>Where the view goes.</returns>
    public Vector3 Update(Vector3 body, bool still, float dt)
    {
        if (!have || (body - output).LengthSquared() > SnapDistance * SnapDistance)
        {
            output = body;
            off = Vector3.Zero;
            have = true;
            stillFor = 0f;
            Offset = 0f;
            return output;
        }
        stillFor = still ? stillFor + dt : 0f;
        if (Holding)
        {
            // The held view stays where it was in the world while the body sways under it.
            off = output - body;
            float len = off.Length();
            if (len > Deadband)
                off *= Deadband / len;
            off *= MathF.Exp(-dt / RecenterSeconds);
        }
        else
        {
            off *= MathF.Exp(-dt / ReleaseSeconds);
        }
        output = body + off;
        Offset = off.Length();
        return output;
    }
}

/// <summary>What the body is doing this frame, for <see cref="ViewHoldGate"/>.</summary>
public struct ViewHoldInputs
{
    /// <summary><see cref="HoldGate.Free"/>: grounded, upright, no stick input, not carried, swimming, warping,
    /// sliding, bouncing or on a slope it cannot stand on.</summary>
    public bool BodyFree;

    /// <summary>How fast the lean moves the eyes (m/s, <see cref="LeanMotion"/>).</summary>
    public float LeanSpeed;

    /// <summary>The ragdoll's mean part speed (m/s, <c>CharacterData.avarageVelocity</c>).</summary>
    public float Speed;
    public bool Jumping;

    /// <summary>Room-scale is walking the body under the head.</summary>
    public bool RoomWalking;
}

/// <summary>When <see cref="ViewHold"/> may hold: the body is only idling or holding a lean still. Anything that
/// moves it (a walk, a jump, a fall, a climb, a knockback, a ride, a lean bending or straightening) releases the
/// view onto it.</summary>
public static class ViewHoldGate
{
    /// <summary>A held lean's eyes drift about 1 cm/s (run 43: 0.152-0.153 m over 0.27 s samples); bending or
    /// straightening moves them 0.3-0.6 m/s, and 5 degrees a second is already 0.05 m/s.</summary>
    public const float MaxLeanSpeed = 0.05f;

    /// <summary>Faster than idle sway (0.07 m/s), slower than any walk, push or ride.</summary>
    public const float MaxSpeed = 0.3f;

    /// <summary>The first closed gate, or null when the view may hold.</summary>
    public static string Why(in ViewHoldInputs i)
    {
        if (!i.BodyFree)
            return "body";
        if (i.Jumping)
            return "jump";
        if (i.RoomWalking)
            return "room walk";
        if (!(i.LeanSpeed < MaxLeanSpeed))
            return "lean";
        if (!(i.Speed < MaxSpeed))
            return "moving";
        return null;
    }
}

/// <summary>How fast the lean moves the eyes, for <see cref="ViewHoldGate"/>. A held lean bends the body but moves
/// nothing, so only a lean in motion releases the view. The shift is computed on the 50 Hz animation tick and read
/// each rendered frame, so the per-frame velocity is smoothed as a vector: the ragdoll's tick jitter cancels out,
/// a real bend does not.</summary>
public sealed class LeanMotion
{
    /// <summary>Time constant (s) of the velocity smoothing: a fast lean closes the gate within its first centimetre.</summary>
    public const float SmoothSeconds = 0.05f;

    private Vector3 last;
    private Vector3 velocity;
    private bool have;

    /// <param name="eyeShift">How far the lean moves the eyes (m, world).</param>
    /// <returns>The smoothed speed (m/s).</returns>
    public float Update(Vector3 eyeShift, float dt)
    {
        if (!have)
        {
            last = eyeShift;
            have = true;
        }
        if (dt > 0f)
        {
            velocity += ((eyeShift - last) / dt - velocity) * (1f - MathF.Exp(-dt / SmoothSeconds));
            last = eyeShift;
        }
        return velocity.Length();
    }
}
