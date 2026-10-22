using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;

namespace PeakXR.UI.Menu;

/// <summary>
/// MainCameraMovement.HandleSpecSelection (A/MainCameraMovement.cs:175, private): while you spectate, a next or
/// previous press does nothing when the view is on yourself (:189-191 returns before reading the presses) or when
/// nobody else can be watched (SwapSpecPlayer keeps the only entry, :220-247, which skips bots :225). Such a flick
/// gets the NeedsFriend cue at the dominant hand, whose stick flicks (player.locomotion's SpectateNext/Previous).
/// Read-only: the presses still reach the game.
/// </summary>
[HarmonyPatch]
internal static class SpectateRefusalPatch
{
    private static MethodBase TargetMethod() =>
        GameMember.Method(typeof(MainCameraMovement), "HandleSpecSelection");

    private static void Prefix()
    {
        var local = Character.localCharacter;
        if (
            !VRSession.Active
            || local == null
            || !(local.input.spectateLeftWasPressed || local.input.spectateRightWasPressed)
        )
            return;
        var spec = MainCameraMovement.specCharacter;
        if (spec != null && spec != local && OthersToWatch(spec) > 0)
            return;
        Affordance.DenyAtHand(VRInput.LeftHanded, DenyReason.NeedsFriend);
    }

    /// <summary>Spectatable players other than <paramref name="current"/>, as SwapSpecPlayer counts them.</summary>
    private static int OthersToWatch(Character current)
    {
        int n = 0;
        // Runs only on a flick frame, so the game's list allocation is not per frame.
        foreach (var c in PlayerHandler.GetAllPlayerCharacters())
            if (c != current && c.data.canBeSpectated && !c.isBot)
                n++;
        return n;
    }
}
