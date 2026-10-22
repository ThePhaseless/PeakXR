using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.UI.World;
using PeakXR.VR;

namespace PeakXR.UI.Passport;

/// <summary>
/// A locked cosmetic in the passport: vanilla's click does nothing (A/PassportButton.cs:63-69 <c>Click</c>, the lock
/// test at :65), so in VR it refuses with the padlock cue on the hand that pressed, the cell's border turned red.
/// The lock icon is shown exactly when the option is locked (A/PassportButton.cs:27-29), so it stands in for the
/// private option. The vanilla click still runs and still does nothing.
/// </summary>
[HarmonyPatch]
internal static class PassportLockPatch
{
    private static MethodBase TargetMethod() => GameMember.Method(typeof(PassportButton), "Click");

    private static void Prefix(PassportButton __instance)
    {
        if (
            !VRSession.Active
            || __instance.lockedIcon == null
            || !__instance.lockedIcon.gameObject.activeSelf
        )
            return;
        var tint =
            __instance.border != null
                ? (UnityEngine.Component)__instance.border
                : __instance.lockedIcon;
        Affordance.Deny(
            LaserPointer.PressLeft,
            __instance.transform.position,
            DenyReason.Locked,
            default,
            tint
        );
    }
}
