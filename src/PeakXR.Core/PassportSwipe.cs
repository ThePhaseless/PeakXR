using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Detects a page-turn swipe of the free hand across an open book, from the hand position in the book's
/// own frame (+X the reader's right, +Y up, +Z away from the reader, metres from the page centre). Using
/// the book frame makes walking, turning and moving the holding hand irrelevant; only a quick sideways
/// stroke close in front of the page counts. A right-to-left stroke turns forward, like a real page.
/// Allocation-free: samples live in a fixed ring buffer.
/// </summary>
public sealed class PageSwipe
{
    const int Capacity = 128;

    /// <summary>Half the page width and height, metres.</summary>
    public float HalfWidth = 0.2f;
    public float HalfHeight = 0.15f;

    /// <summary>Extra room around the page edges, metres.</summary>
    public float Margin = 0.05f;

    /// <summary>How far in front of the page (toward the reader) the hand may be, metres.</summary>
    public float Reach = 0.2f;

    /// <summary>How far behind the page the hand may be, metres.</summary>
    public float Behind = 0.05f;

    /// <summary>Sideways travel that makes a swipe, metres.</summary>
    public float MinTravel = 0.12f;

    /// <summary>Time the travel must happen within, seconds.</summary>
    public float Window = 0.35f;

    /// <summary>Dead time after a flip, so the return stroke does not flip back.</summary>
    public float Cooldown = 0.5f;

    readonly float[] xs = new float[Capacity];
    readonly float[] ts = new float[Capacity];
    int head;
    int count;
    float clock;
    float cooldownLeft;

    /// <param name="local">Free hand in the book frame.</param>
    /// <param name="blocked">The hand is busy (trigger held for the laser): no swipe.</param>
    /// <param name="dt">Frame time, seconds.</param>
    /// <returns>+1 forward, -1 back, 0 nothing.</returns>
    public int Update(Vector3 local, bool blocked, float dt)
    {
        clock += dt;
        if (cooldownLeft > 0f)
        {
            cooldownLeft -= dt;
            count = 0;
            return 0;
        }
        if (blocked || !InZone(local))
        {
            count = 0;
            return 0;
        }

        Push(local.X);
        float oldest = clock - Window;
        float best = 0f;
        for (int i = 0; i < count; i++)
        {
            int k = (head - count + i + Capacity) % Capacity;
            if (ts[k] < oldest)
                continue;
            float dx = local.X - xs[k];
            if (MathF.Abs(dx) > MathF.Abs(best))
                best = dx;
        }
        if (MathF.Abs(best) < MinTravel)
            return 0;

        count = 0;
        cooldownLeft = Cooldown;
        return best < 0f ? 1 : -1;
    }

    /// <summary>Forget the current stroke and any cooldown (book closed or swapped).</summary>
    public void Reset()
    {
        count = 0;
        cooldownLeft = 0f;
    }

    bool InZone(Vector3 p) =>
        MathF.Abs(p.X) <= HalfWidth + Margin
        && MathF.Abs(p.Y) <= HalfHeight + Margin
        && p.Z >= -Reach
        && p.Z <= Behind;

    void Push(float x)
    {
        xs[head] = x;
        ts[head] = clock;
        head = (head + 1) % Capacity;
        if (count < Capacity)
            count++;
    }
}
