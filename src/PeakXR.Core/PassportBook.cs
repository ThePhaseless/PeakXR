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

    /// <param name="hand">Grip position of the holding hand.</param>
    /// <param name="head">Eye position.</param>
    /// <param name="headForward">Head forward, used when the hand is straight below or at the eyes.</param>
    /// <param name="leftHand">Whether the left hand holds the book (the book then extends to the right).</param>
    /// <param name="lift">Metres above the hand.</param>
    /// <param name="side">Metres toward the free hand, as seen by the reader.</param>
    /// <param name="position">Book centre.</param>
    /// <param name="rotation">Book rotation; +Z points away from the eyes, so a world canvas reads right.</param>
    public static void Compute(
        Vector3 hand,
        Vector3 head,
        Vector3 headForward,
        bool leftHand,
        float lift,
        float side,
        out Vector3 position,
        out Quaternion rotation
    )
    {
        var flat = new Vector3(hand.X - head.X, 0f, hand.Z - head.Z);
        if (flat.LengthSquared() < 1e-4f)
            flat = new Vector3(headForward.X, 0f, headForward.Z);
        var right =
            flat.LengthSquared() > 1e-8f
                ? Vector3.Normalize(Vector3.Cross(Up, flat))
                : new Vector3(1f, 0f, 0f);

        position = hand + Up * lift + right * (leftHand ? side : -side);

        var toBook = position - head;
        rotation =
            toBook.LengthSquared() > 1e-6f
                ? UnityQuat.LookRotation(toBook, Up)
                : UnityQuat.LookRotation(headForward, Up);
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
