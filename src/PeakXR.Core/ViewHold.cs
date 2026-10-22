using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Holds the first-person view still against the standing body's idle sway. Since PEAK 3.0 the ragdoll sways about
/// 0.07 m/s and bobs about 4 cm while standing (2 mm on 2.6.b); the view rides the body, so a still player saw the
/// world move. While <see cref="ViewHoldGate"/> says the body is only idling, the view stays where it is unless the
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

    /// <summary>The lean's bend (degrees).</summary>
    public float LeanDegrees;

    /// <summary>The ragdoll's mean part speed (m/s, <c>CharacterData.avarageVelocity</c>).</summary>
    public float Speed;
    public bool Jumping;

    /// <summary>Room-scale is walking the body under the head.</summary>
    public bool RoomWalking;
}

/// <summary>When <see cref="ViewHold"/> may hold: the body is only idling. Anything that moves it (a walk, a jump,
/// a fall, a climb, a knockback, a ride, a lean past its deadband) releases the view onto it.</summary>
public static class ViewHoldGate
{
    /// <summary>Above the lean's own deadband (<see cref="BodyHold"/> starts at 1 degree), the body is leaning.</summary>
    public const float MaxLeanDegrees = 1f;

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
        if (!(i.LeanDegrees < MaxLeanDegrees))
            return "lean";
        if (!(i.Speed < MaxSpeed))
            return "moving";
        return null;
    }
}
