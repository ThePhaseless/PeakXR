using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.UI.Lobby;

/// <summary>Invite kiosk press: the vanilla Steam overlay call runs unchanged, then VR players get an
/// in-world note at the kiosk. A/AirportInviteFriendsKiosk.cs:39 <c>public void Interact(Character interactor)</c>.</summary>
[HarmonyPatch]
internal static class InviteKioskPatch
{
    // Resolved at install time: the Harmony analyzer cannot see members whose class references Steamworks.
    private static MethodBase TargetMethod() =>
        GameMember.Method(
            typeof(AirportInviteFriendsKiosk),
            nameof(AirportInviteFriendsKiosk.Interact),
            new[] { typeof(Character) }
        );

    private static void Postfix(AirportInviteFriendsKiosk __instance)
    {
        var note = UILobbyFeature.Note;
        if (VRSession.Active && note != null)
            note.Show(__instance);
    }
}
