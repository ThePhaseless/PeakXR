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

    // Debug mode's aim log, at most 4 lines a second.
    private static float nextAimLog;

    public static CameraSwap Begin(Vector3 position, Quaternion rotation)
    {
        var mc = MainCamera.instance;
        if (mc == null)
            return default;
        var mainT = mc.transform;
        var cam = Camera.main;
        var otherT = cam != null && cam.transform != mainT ? cam.transform : null;
        var swap = new CameraSwap(mainT, otherT);
        if (Diagnostics.DebugLog.Verbose && Time.unscaledTime >= nextAimLog)
        {
            nextAimLog = Time.unscaledTime + 0.25f;
            var f = rotation * Vector3.forward;
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[interact.aim] game call aimed from ({position.x:0.00}, {position.y:0.00}, {position.z:0.00}) along ({f.x:0.00}, {f.y:0.00}, {f.z:0.00}), {Vector3.Distance(mainT.position, position):0.00} m from the eye"
                )
            );
        }
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
