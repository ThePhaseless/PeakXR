using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using PeakXR.XRInput;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Edge logger for live-input tests (<c>-peakxr-selftest</c> only): another session drives virtual controllers
/// through SteamVR and these <c>[input]</c> lines prove what the real path (<see cref="XRInputSampler"/> ->
/// <see cref="VRInput"/> -> the bindings) received. Logs changes only and keeps all state in value fields, so a
/// frame with no change allocates nothing.
/// </summary>
internal static class InputEdgeLog
{
    const float PosStep = 0.02f;
    const float PosInterval = 1f;

    struct Watch
    {
        public bool Init;
        public int Flags;
        public int Trigger;
        public int Grip;
        public int Stick;
        public Vector3 LoggedPos;
        public float NextPos;
    }

    static readonly string[] FlagNames =
    {
        "tracked",
        "primary",
        "secondary",
        "menu",
        "stickclick",
        "triggertouch",
        "primarytouch",
    };
    static readonly string[] StickNames = { "stick-right", "stick-left", "stick-up", "stick-down" };
    static readonly string[] CharNames =
    {
        "jumpWasPressed",
        "sprintIsPressed",
        "usePrimaryIsPressed",
        "useSecondaryIsPressed",
        "interactIsPressed",
        "dropIsPressed",
        "emoteIsPressed",
        "pingWasPressed",
        "move-right",
        "move-left",
        "move-forward",
        "move-back",
    };

    static Watch left,
        right;
    static int charFlags;

    internal static void Install() => XRInputSampler.AfterSample += Sample;

    static void Sample()
    {
        Update("L", ref left, ref VRInput.Left);
        Update("R", ref right, ref VRInput.Right);
    }

    static void Update(string hand, ref Watch w, ref HandState s)
    {
        int flags =
            (s.Tracked ? 1 : 0)
            | (s.Primary ? 2 : 0)
            | (s.Secondary ? 4 : 0)
            | (s.Menu ? 8 : 0)
            | (s.StickClick ? 16 : 0)
            | (s.TriggerTouch ? 32 : 0)
            | (s.PrimaryTouch ? 64 : 0);
        int trigger = Level(s.Trigger);
        int grip = Level(s.Grip);
        int stick =
            (s.Stick.x > 0.5f ? 1 : 0)
            | (s.Stick.x < -0.5f ? 2 : 0)
            | (s.Stick.y > 0.5f ? 4 : 0)
            | (s.Stick.y < -0.5f ? 8 : 0);

        if (!w.Init)
        {
            // The first sample is the baseline; only changes from it are edges.
            w.Init = true;
            w.Flags = flags;
            w.Trigger = trigger;
            w.Grip = grip;
            w.Stick = stick;
            w.LoggedPos = s.Position;
            Plugin.Log.LogInfo($"[input] {hand} baseline tracked={s.Tracked}");
            return;
        }

        if (flags != w.Flags)
        {
            int diff = flags ^ w.Flags;
            for (int i = 0; i < FlagNames.Length; i++)
            {
                if ((diff & (1 << i)) != 0)
                    Plugin.Log.LogInfo(
                        $"[input] {hand} {FlagNames[i]} {((flags & (1 << i)) != 0 ? "on" : "off")}"
                    );
            }
            w.Flags = flags;
        }
        if (trigger != w.Trigger)
        {
            Plugin.Log.LogInfo($"[input] {hand} trigger level {trigger} ({s.Trigger:0.00})");
            w.Trigger = trigger;
        }
        if (grip != w.Grip)
        {
            Plugin.Log.LogInfo($"[input] {hand} grip level {grip} ({s.Grip:0.00})");
            w.Grip = grip;
        }
        if (stick != w.Stick)
        {
            int diff = stick ^ w.Stick;
            for (int i = 0; i < StickNames.Length; i++)
            {
                if ((diff & (1 << i)) != 0)
                    Plugin.Log.LogInfo(
                        $"[input] {hand} {StickNames[i]} {((stick & (1 << i)) != 0 ? "on" : "off")} ({s.Stick.x:0.00},{s.Stick.y:0.00})"
                    );
            }
            w.Stick = stick;
        }

        float now = Time.unscaledTime;
        if (
            s.Tracked
            && now >= w.NextPos
            && (s.Position - w.LoggedPos).sqrMagnitude > PosStep * PosStep
        )
        {
            w.NextPos = now + PosInterval;
            w.LoggedPos = s.Position;
            Plugin.Log.LogInfo(
                $"[input] {hand} pos {s.Position.x:0.000} {s.Position.y:0.000} {s.Position.z:0.000}"
            );
        }
    }

    /// <summary>0 below 0.1, 1 below 0.5, 2 below 0.9, 3 from 0.9.</summary>
    static int Level(float v) =>
        v >= 0.9f ? 3
        : v >= 0.5f ? 2
        : v >= 0.1f ? 1
        : 0;

    internal static void CharacterSampled(CharacterInput c)
    {
        var m = c.movementInput;
        int flags =
            (c.jumpWasPressed ? 1 : 0)
            | (c.sprintIsPressed ? 2 : 0)
            | (c.usePrimaryIsPressed ? 4 : 0)
            | (c.useSecondaryIsPressed ? 8 : 0)
            | (c.interactIsPressed ? 16 : 0)
            | (c.dropIsPressed ? 32 : 0)
            | (c.emoteIsPressed ? 64 : 0)
            | (c.pingWasPressed ? 128 : 0)
            | (m.x > 0.1f ? 256 : 0)
            | (m.x < -0.1f ? 512 : 0)
            | (m.y > 0.1f ? 1024 : 0)
            | (m.y < -0.1f ? 2048 : 0);
        if (flags == charFlags)
            return;
        int diff = flags ^ charFlags;
        charFlags = flags;
        for (int i = 0; i < CharNames.Length; i++)
        {
            if ((diff & (1 << i)) != 0)
                Plugin.Log.LogInfo(
                    $"[input] char {CharNames[i]} {((flags & (1 << i)) != 0 ? "on" : "off")}"
                );
        }
    }
}

/// <summary>
/// Reads the fields the bindings wrote, after every other <c>CharacterInput.Sample(bool)</c> postfix
/// (decomp Assembly-CSharp/CharacterInput.cs:212) via <c>Priority.Last</c>.
/// </summary>
[HarmonyPatch]
static class InputEdgeCharacterPatch
{
    static MethodBase TargetMethod() =>
        GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

    [HarmonyPriority(Priority.Last)]
    static void Postfix(CharacterInput __instance)
    {
        var c = Character.localCharacter;
        if (c != null && c.input == __instance)
            InputEdgeLog.CharacterSampled(__instance);
    }
}
