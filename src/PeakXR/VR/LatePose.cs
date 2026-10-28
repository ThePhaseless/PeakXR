using System;
using UnityEngine;

namespace PeakXR.VR;

/// <summary>
/// One ordered <c>Application.onBeforeRender</c> pass. Unity calls that event's handlers in no set order, so
/// head-locked UI posed there could read <see cref="VRRig.Head"/> before rig.camera re-read the HMD for the frame
/// and lag a frame behind the view. The rig's late pose runs first (<see cref="SetRig"/>), then every
/// <see cref="Posed"/> handler.
/// </summary>
public static class LatePose
{
    private static Action rig;
    private static Action posed;
    private static bool hooked;

    /// <summary>Runs after the rig's late pose, before rendering, once per frame.</summary>
    public static event Action Posed
    {
        add
        {
            Hook();
            posed += value;
        }
        remove => posed -= value;
    }

    /// <summary>The rig's late pose (one at a time); null clears it.</summary>
    public static void SetRig(Action pose)
    {
        Hook();
        rig = pose;
    }

    private static void Hook()
    {
        if (hooked)
            return;
        hooked = true;
        Application.onBeforeRender += Run;
    }

    private static void Run()
    {
        rig?.Invoke();
        posed?.Invoke();
    }
}
