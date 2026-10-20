using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Moves the game camera to another pose for the duration of one game call and puts it back afterwards. Game
/// code reads <c>MainCamera.instance.transform</c> (and sometimes <c>Camera.main</c>) as its aim source; swapping
/// the pose around that one call aims it from a hand without touching anything else. Used as a Harmony
/// <c>__state</c>: <see cref="Begin"/> in the prefix, <see cref="End"/> in a finalizer so an exception in the game
/// method cannot leave the camera at the hand.
/// </summary>
internal readonly struct CameraSwap
{
    private readonly Transform main;
    private readonly Vector3 mainPos;
    private readonly Quaternion mainRot;
    private readonly Transform other;
    private readonly Vector3 otherPos;
    private readonly Quaternion otherRot;

    private CameraSwap(Transform main, Transform other)
    {
        this.main = main;
        mainPos = main.localPosition;
        mainRot = main.localRotation;
        this.other = other;
        if (other != null)
        {
            otherPos = other.localPosition;
            otherRot = other.localRotation;
        }
        else
        {
            otherPos = default;
            otherRot = Quaternion.identity;
        }
    }

    public bool Active => main != null;

    public static CameraSwap Begin(Vector3 position, Quaternion rotation)
    {
        var mc = MainCamera.instance;
        if (mc == null)
            return default;
        var mainT = mc.transform;
        var cam = Camera.main;
        var otherT = cam != null && cam.transform != mainT ? cam.transform : null;
        var swap = new CameraSwap(mainT, otherT);
        mainT.SetPositionAndRotation(position, rotation);
        otherT?.SetPositionAndRotation(position, rotation);
        return swap;
    }

    /// <summary>Harmony finalizer body: restores the camera and passes any exception on. Static, because Harmony
    /// flags a call on a non-ref <c>__state</c> as a lost write.</summary>
    public static System.Exception Restore(System.Exception exception, CameraSwap swap)
    {
        swap.End();
        return exception;
    }

    public void End()
    {
        // Child-first restore of local poses works whether or not one camera is parented under the other.
        if (other != null)
            other.SetLocalPositionAndRotation(otherPos, otherRot);
        if (main != null)
            main.SetLocalPositionAndRotation(mainPos, mainRot);
    }
}
