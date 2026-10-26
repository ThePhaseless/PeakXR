using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Reading pose of a hand-held book (passport, guidebook) in Unity's convention: the book rides above
/// the holding hand, shifted toward the free side as if gripped by its edge, and faces the eyes upright.
/// Following the hand instead of the head keeps the page still when the head turns and lets the player
/// bring it closer to read.
/// </summary>
public static class BookPose
{
    static readonly Vector3 Up = new(0f, 1f, 0f);

    /// <summary>The game's chunky hand (thumb up) reaches about this far above the grip pose, metres: a book's lower
    /// edge stays above it, so the holding hand never covers the page (tester report, guidebook).</summary>
    public const float HandClearance = 0.1f;

    /// <summary>Nearest a book comes to the eyes, horizontally, metres: a hand pulled in under the chin still leaves
    /// the page at reading distance.</summary>
    public const float MinReach = 0.3f;

    /// <summary>Furthest below the line of the eyes a book's centre sits, degrees: a hand resting low lifts the page
    /// into a comfortable downward gaze instead of out of view.</summary>
    public const float MaxBelowDegrees = 35f;

    /// <param name="hand">Grip position of the holding hand.</param>
    /// <param name="head">Eye position.</param>
    /// <param name="headForward">Head forward, used when the hand is straight below or at the eyes.</param>
    /// <param name="leftHand">Whether the left hand holds the book (the book then extends to the right).</param>
    /// <param name="lift">Metres above the hand.</param>
    /// <param name="side">Metres toward the free hand, as seen by the reader.</param>
    /// <param name="position">Book centre.</param>
    /// <param name="rotation">Book rotation; +Z points away from the eyes, so a world canvas reads right.</param>
    /// <param name="halfHeight">Half the open book's height, metres. Above 0 the book also stays readable: its
    /// lower edge clears the hand by <see cref="HandClearance"/>, and it keeps <see cref="MinReach"/> and
    /// <see cref="MaxBelowDegrees"/>. Each of these only moves the book up or away from the eyes, never onto the hand.</param>
    public static void Compute(
        Vector3 hand,
        Vector3 head,
        Vector3 headForward,
        bool leftHand,
        float lift,
        float side,
        out Vector3 position,
        out Quaternion rotation,
        float halfHeight = 0f
    )
    {
        if (halfHeight > 0f)
            lift = MathF.Max(lift, halfHeight + HandClearance);
        var flat = new Vector3(hand.X - head.X, 0f, hand.Z - head.Z);
        if (flat.LengthSquared() < 1e-4f)
            flat = new Vector3(headForward.X, 0f, headForward.Z);
        var right =
            flat.LengthSquared() > 1e-8f
                ? Vector3.Normalize(Vector3.Cross(Up, flat))
                : new Vector3(1f, 0f, 0f);

        position = hand + Up * lift + right * (leftHand ? side : -side);
        if (halfHeight > 0f)
            position = Readable(position, head, flat);

        var toBook = position - head;
        rotation =
            toBook.LengthSquared() > 1e-6f
                ? UnityQuat.LookRotation(toBook, Up)
                : UnityQuat.LookRotation(headForward, Up);
    }

    /// <summary><paramref name="position"/> pushed out to <see cref="MinReach"/> and raised to
    /// <see cref="MaxBelowDegrees"/> below the eyes, as needed; <paramref name="away"/> is the flat direction to use
    /// when the book is straight below the eyes.</summary>
    static Vector3 Readable(Vector3 position, Vector3 head, Vector3 away)
    {
        var flat = new Vector3(position.X - head.X, 0f, position.Z - head.Z);
        float reach = flat.Length();
        var dir =
            reach > 1e-4f ? flat / reach
            : away.LengthSquared() > 1e-8f ? Vector3.Normalize(away)
            : new Vector3(0f, 0f, 1f);
        reach = MathF.Max(reach, MinReach);
        float lowest = head.Y - MathF.Tan(MaxBelowDegrees * MathF.PI / 180f) * reach;
        return new Vector3(
            head.X + dir.X * reach,
            MathF.Max(position.Y, lowest),
            head.Z + dir.Z * reach
        );
    }
}

/// <summary>What a page flip does in the passport.</summary>
public enum PassportFlip
{
    None,
    NextPage,
    PrevPage,

    /// <summary>Open the neighbouring tab (the current tab has no more pages that way).</summary>
    Tab,
}

/// <summary>
/// Turns a page flip into the passport's own controls: page arrows first, then the neighbouring tab, so
/// flipping forward walks through every outfit page of every tab like a real booklet.
/// </summary>
public static class PassportPaging
{
    /// <param name="dir">+1 forward, -1 back.</param>
    /// <param name="hasNextPage">The game's next-page arrow is shown.</param>
    /// <param name="hasPrevPage">The game's previous-page arrow is shown.</param>
    /// <param name="tab">Index of the open tab, -1 when unknown.</param>
    /// <param name="tabCount">Number of tabs.</param>
    /// <param name="targetTab">Tab to open when the result is <see cref="PassportFlip.Tab"/>.</param>
    public static PassportFlip Decide(
        int dir,
        bool hasNextPage,
        bool hasPrevPage,
        int tab,
        int tabCount,
        out int targetTab
    )
    {
        targetTab = -1;
        if (dir > 0)
        {
            if (hasNextPage)
                return PassportFlip.NextPage;
            if (tab >= 0 && tab < tabCount - 1)
            {
                targetTab = tab + 1;
                return PassportFlip.Tab;
            }
        }
        else if (dir < 0)
        {
            if (hasPrevPage)
                return PassportFlip.PrevPage;
            if (tab > 0 && tab < tabCount)
            {
                targetTab = tab - 1;
                return PassportFlip.Tab;
            }
        }
        return PassportFlip.None;
    }
}

/// <summary>
/// Hand size for a held book whose game model is drawn oversized for the flat camera (PEAK's passport cover is
/// 0.38 m long). In VR it sits in a real hand, so it shrinks about its grip to a size that fits the palm.
/// </summary>
public static class HeldBookSize
{
    /// <summary>Scale that brings a model <paramref name="longest"/> metres long down to
    /// <paramref name="target"/>; 1 when it already fits or could not be measured.</summary>
    public static float FitScale(float longest, float target) =>
        longest > target && target > 0f ? target / longest : 1f;

    /// <summary>Where <paramref name="point"/> lands when scaled by <paramref name="scale"/> about
    /// <paramref name="pivot"/>, so the grip stays in the hand.</summary>
    public static Vector3 ScaleAbout(Vector3 point, Vector3 pivot, float scale) =>
        pivot + (point - pivot) * scale;
}
