using Photon.Pun;
using UnityEngine;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakXR.Sandbox;

/// <summary>
/// Sandbox controls that put the local character into a state a test needs, through the game's own APIs. Offline
/// only. Stable API (console commands in <see cref="SandboxCommands"/>):
/// <list type="bullet">
/// <item><c>SetStamina(current, extra)</c>, <c>SetMaxStamina(max)</c></item>
/// <item><c>AddStatus(type, amount)</c>, <c>ClearStatus(type)</c></item>
/// <item><c>PassOut()</c>; wake with <c>Revive()</c></item>
/// <item><c>SetRunSetting(type, value)</c>, e.g. HelpingHand 1 for kick mode</item>
/// <item><c>GiveItem(item, left)</c>: into one VR hand</item>
/// <item><c>PlaceWall()</c>: a climbable test wall ahead of the player</item>
/// </list>
/// God mode undoes body changes (infinite stamina, locked statuses, its tick clears them), so the body actions turn
/// it off first and say so; the self-test's step runner turns it back on after the step.
/// </summary>
public static partial class SandboxActions
{
    static GameObject testWall;

    static bool OfflineGuard()
    {
        if (!Guard())
            return false;
        if (PhotonNetwork.OfflineMode)
            return true;
        Say("Only offline (Play Solo).");
        return false;
    }

    static void LiftGodMode()
    {
        if (GodMode)
            SetGodMode(false);
    }

    /// <summary>Current stamina (clamped to the max) and the bonus bar; god mode goes off.</summary>
    public static bool SetStamina(float current, float extra)
    {
        if (!OfflineGuard())
            return false;
        LiftGodMode();
        var c = Character.localCharacter;
        // AddStamina clamps to the max and redraws the bar (Character.cs:1598); SetExtraStamina too (:1618).
        c.AddStamina(current - c.data.currentStamina);
        c.SetExtraStamina(extra);
        Say(
            $"Stamina {c.data.currentStamina:0.00} of {c.GetMaxStamina():0.00}, extra {c.data.extraStamina:0.00}."
        );
        return true;
    }

    /// <summary>Max stamina is 1 minus the status sum (Character.cs:1613): set through Hunger, so the bar shows
    /// it as the game would. Rounded to the game's 1/40 status step.</summary>
    public static bool SetMaxStamina(float max)
    {
        if (!OfflineGuard())
            return false;
        LiftGodMode();
        var c = Character.localCharacter;
        var a = c.refs.afflictions;
        a.SetStatus(
            STATUS.Hunger,
            Mathf.Clamp01(a.GetCurrentStatus(STATUS.Hunger) + c.GetMaxStamina() - max)
        );
        Say(
            $"Max stamina {c.GetMaxStamina():0.00} (Hunger {a.GetCurrentStatus(STATUS.Hunger):0.00})."
        );
        return true;
    }

    /// <summary>Adds a status the way the game does (<c>AddStatus</c>, CharacterAfflictions.cs:712), which god mode's
    /// <c>statusesLocked</c> would block; the game refuses it in the airport.</summary>
    public static bool AddStatus(STATUS type, float amount)
    {
        if (!OfflineGuard())
            return false;
        LiftGodMode();
        var a = Character.localCharacter.refs.afflictions;
        bool added = a.AddStatus(type, amount);
        Say(
            added
                ? $"{type} now {a.GetCurrentStatus(type):0.00}."
                : $"The game refused {type} (in the airport, or immune)."
        );
        return added;
    }

    /// <summary>Sets one status to 0 (<c>SetStatus</c>, CharacterAfflictions.cs:632), the others stay.</summary>
    public static bool ClearStatus(STATUS type)
    {
        if (!OfflineGuard())
            return false;
        LiftGodMode();
        Character.localCharacter.refs.afflictions.SetStatus(type, 0f);
        Say($"{type} cleared.");
        return true;
    }

    /// <summary>The game's <c>Character.PassOut</c> command (Character.cs:983): full Hunger, so the body stays out,
    /// then <c>RPCA_PassOut</c>. Outside the airport only (no statuses there, so it wakes again in 3 s).</summary>
    public static bool PassOut()
    {
        if (!OfflineGuard())
            return false;
        var c = Character.localCharacter;
        if (c.data.dead || c.data.passedOut)
        {
            Say("Already passed out or dead.");
            return false;
        }
        LiftGodMode();
        Character.PassOut();
        Say("Passed out.");
        return true;
    }

    /// <summary>
    /// A run setting (<c>RunSettings.SetValue</c>, RunSettings.cs:426), not saved to disk. The game reads custom
    /// values only in a custom run outside the airport (:348), so this marks the run custom, which also blocks
    /// achievements for it (:136). HelpingHand 1 is kick mode (CharacterGrabbing.cs:33).
    /// </summary>
    public static bool SetRunSetting(RunSettings.SETTINGTYPE type, int value)
    {
        if (!OfflineGuard())
            return false;
        RunSettings.Init();
        if (!RunSettings.SetValue(type, value))
        {
            Say($"No run setting {type}.");
            return false;
        }
        RunSettings.IsCustomRun = true;
        bool airport = Character.localCharacter.inAirport;
        Say(
            $"Run setting {type} = {RunSettings.GetValue(type, forceGetCustomValue: true)}{(airport ? " (applies after the airport)" : "")}."
        );
        return true;
    }

    /// <summary><see cref="GiveItem(Item)"/> into one VR hand: interact.physical puts the next held item in the hand
    /// it was told to expect (<see cref="Interact.PhysicalInteraction.ExpectItemIn"/>, within 3 s).</summary>
    public static bool GiveItem(Item item, bool left)
    {
        if (!OfflineGuard())
            return false;
        Interact.PhysicalInteraction.ExpectItemIn(left);
        if (!GiveItem(item))
            return false;
        Say($"{item.name} goes to the {(left ? "left" : "right")} hand.");
        return true;
    }

    /// <summary>Removes the test wall, if one stands; true when there was one.</summary>
    public static bool RemoveWall()
    {
        if (testWall == null)
            return false;
        testWall.SetActive(false);
        Object.Destroy(testWall);
        testWall = null;
        return true;
    }

    /// <summary>
    /// A climbable test wall 3 m ahead of the player, 4 m wide and 7 m tall, on the Map layer the climb casts use
    /// (<c>HelperFunctions.terrainMapMask</c>, CharacterClimbing.cs:418; any collider there climbs). For a map with no
    /// wall near the player. Local, one at a time (a new one replaces the last); the scene load removes it.
    /// </summary>
    public static GameObject PlaceWall()
    {
        if (!OfflineGuard())
            return null;
        var c = Character.localCharacter;
        var forward = c.data.lookDirection_Flat;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        return PlaceWall(c.Center + forward * 3.25f, forward);
    }

    /// <summary>The test wall centred over <paramref name="at"/>, its face toward -<paramref name="forward"/>.</summary>
    public static GameObject PlaceWall(Vector3 at, Vector3 forward)
    {
        if (!OfflineGuard())
            return null;
        var c = Character.localCharacter;
        float ground = Physics.Raycast(
            at + Vector3.up * 2f,
            Vector3.down,
            out var hit,
            10f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point.y
            : c.Center.y - 1f;
        if (testWall != null)
        {
            // Out of the casts at once: Destroy waits for the frame's end, and the self-test searches right after.
            testWall.SetActive(false);
            Object.Destroy(testWall);
        }
        testWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testWall.name = "PeakXR_SandboxWall";
        testWall.layer = LayerMask.NameToLayer("Map");
        // Sunk 0.5 m so a slope leaves no gap under it.
        testWall.transform.SetPositionAndRotation(
            new Vector3(at.x, ground + 3f, at.z),
            Quaternion.LookRotation(forward)
        );
        testWall.transform.localScale = new Vector3(4f, 7f, 0.5f);
        Physics.SyncTransforms();
        Say($"Placed a test wall at {testWall.transform.position}.");
        return testWall;
    }
}
