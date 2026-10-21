using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Inputs that <c>interact.items</c> gestures ask <see cref="PhysicalInteraction"/> to hold this frame: the game's
/// interact on a chosen target (cook, light) and primary use (bugle, bandage). They merge into the same button edges
/// as the grips and triggers, so a gesture and a button never fight. Each request lasts only for the frame it was
/// made in, so a feature that stops ticking cannot leave a button stuck.
/// </summary>
internal static class GestureRequests
{
    private static IInteractible interactTarget;
    private static int interactFrame = -10;
    private static int primaryFrame = -10;

    /// <summary>Hold interact on <paramref name="target"/> this frame (it becomes the interaction raycast result).</summary>
    public static void Interact(IInteractible target)
    {
        interactTarget = target;
        interactFrame = Time.frameCount;
    }

    /// <summary>Hold the held item's primary use this frame.</summary>
    public static void Primary() => primaryFrame = Time.frameCount;

    public static bool TryInteract(out IInteractible target)
    {
        target = interactFrame == Time.frameCount ? interactTarget : null;
        if (target is Object unityObject && unityObject == null)
            target = null;
        return target != null;
    }

    public static bool PrimaryHeld => primaryFrame == Time.frameCount;
}
