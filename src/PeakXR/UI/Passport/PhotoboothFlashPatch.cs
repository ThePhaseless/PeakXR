using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine.UI;

namespace PeakXR.UI.Passport;

/// <summary>
/// The photobooth strobes a flash image four times per session (A/AirportPhotoboothKiosk.cs:104
/// PhotoboothRoutine). The game picks the harsh <c>flashImage</c> or the soft
/// <c>photosensitiveFlashImage</c> once in Start (:76-77) from its photosensitivity setting; in VR the
/// choice is forced every frame from <see cref="PassportSettings.PhotoboothFlash"/>, and the game's own
/// choice comes back when VR stops or the setting returns to Vanilla.
/// Target: A/AirportPhotoboothKiosk.cs:82 <c>private void Update()</c>.
/// </summary>
[HarmonyPatch]
internal static class PhotoboothFlashPatch
{
    private static bool overridden;

    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(AirportPhotoboothKiosk), "Update");

    private static void Postfix(AirportPhotoboothKiosk __instance)
    {
        var mode = PassportFeature.Settings.PhotoboothFlash.Value;
        if (!VRSession.Active || mode == PhotoboothFlashMode.Vanilla)
        {
            if (overridden)
            {
                overridden = false;
                var gui = GUIManager.instance;
                var soft = gui != null && gui.photosensitivity;
                Set(__instance.flashImage, !soft);
                Set(__instance.photosensitiveFlashImage, soft);
            }
            return;
        }
        overridden = true;
        Set(__instance.flashImage, false);
        Set(__instance.photosensitiveFlashImage, mode == PhotoboothFlashMode.Dimmed);
    }

    private static void Set(Image image, bool on)
    {
        if (image != null && image.enabled != on)
            image.enabled = on;
    }
}
