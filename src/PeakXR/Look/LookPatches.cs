using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Locomotion;
using PeakXR.UI.Common;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.Look;

/// <summary>Harmony patches and per-frame state of <see cref="PlayerLookFeature"/>.</summary>
internal static class LookPatches
{
    /// <summary><c>CharacterMovement.character</c> (CharacterMovement.cs:32, private).</summary>
    public static AccessTools.FieldRef<CharacterMovement, Character> Movement;

    /// <summary><c>Character.RecalculateLookDirections</c> (Character.cs:1344, internal).</summary>
    public static Action<Character> Recalculate;

    private static readonly TurnInput Turn = new();
    private static readonly LookOverride Override = new();

    /// <summary>How much a scripted look (frog tongue, rescue hook, scoutmaster) holds the heading, 0..1. Read by
    /// comfort.events as its forced-look cue.</summary>
    internal static float ScriptedLookWeight => Override.Weight;

    /// <summary>Yaw (degrees, + = right) from the headset's look to the scripted one while a script holds the
    /// heading: the side the pull comes from, for comfort.events' lateral rumble.</summary>
    internal static float ScriptedPullYaw { get; private set; }
    private static Character lastCharacter;
    private static bool faceSpawnLook;
    private static float spawnYaw;
    private static int tickFrame = -10;

    /// <summary>Smooth turning speed (deg/s) from this or the previous frame, for the comfort vignette (its update
    /// order against CharacterMovement is not fixed). Snaps report 0, and so do frames without a look update.</summary>
    public static float SmoothTurnRate => Time.frameCount - tickFrame <= 1 ? Turn.SmoothRate : 0f;

    /// <summary>
    /// CharacterMovement.CameraLook (CharacterMovement.cs:283, private): called from CharacterMovement.Update
    /// (:201) right after CharacterInput.Sample (:198) whenever no wheel is open (CanMoveCamera, :174). It adds the
    /// look stick to <c>data.lookValues</c> and recalculates the look directions that movement (Character.cs:1308),
    /// aiming, interaction, body yaw (Character.cs:1526) and the sync to peers (CharacterSyncData.cs:62) read.
    /// The prefix zeroes the stick so only the headset moves the view; the postfix applies turning and writes the
    /// headset look.
    /// </summary>
    [HarmonyPatch]
    internal static class CameraLook
    {
        private static MethodBase TargetMethod() =>
            Framework.GameMember.Method(typeof(CharacterMovement), "CameraLook");

        private static void Prefix(CharacterMovement __instance)
        {
            if (!VRSession.Active || VRRig.Instance == null)
                return;
            var c = Movement(__instance);
            if (c != null && c.IsLocal)
                c.input.lookInput = Vector2.zero;
        }

        private static void Postfix(CharacterMovement __instance)
        {
            if (!VRSession.Active)
                return;
            var c = Movement(__instance);
            if (c != null && c.IsLocal)
                Tick(c);
        }
    }

    private static void Tick(Character c)
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Origin == null)
            return;
        if (c != lastCharacter)
        {
            lastCharacter = c;
            Override.Reset();
            Turn.Reset();
            faceSpawnLook = true;
            // Kept from the first tick: the writes below overwrite lookValues every frame, also while the turn
            // waits out an OriginOverride.
            spawnYaw = c.data.lookValues.x;
        }
        float dt = Time.deltaTime;
        tickFrame = Time.frameCount;

        Turn.Mode = ComfortConfig.Turn;
        Turn.Snap.Angle = ComfortConfig.SnapAngle;
        Turn.SmoothSpeed = ComfortConfig.SmoothTurnSpeed;
        if (CanTurn())
        {
            var stick = VRInput.Hand(VRInput.LeftHanded).Stick;
            rig.RotateYaw(Turn.Update(new NVector2(stick.x, stick.y), dt));
        }
        else
        {
            Turn.Reset();
        }

        if (!VRInput.HeadTracked)
            return;
        var current = c.data.lookValues;
        if (faceSpawnLook && !rig.OriginOverride)
        {
            // A new body starts with the game's spawn look: nothing sets lookValues at spawn
            // (CharacterSpawner.cs:245), so flat players face +Z. The first write below would otherwise hand it
            // whatever yaw the tracking space kept from the title stage or loading room, and rig.camera's
            // recenter then keeps that yaw (it reads the look this write produced).
            faceSpawnLook = false;
            var hmdYaw = (VRInput.HeadRotation * Vector3.forward).HeadingDegrees();
            rig.Origin.rotation = Quaternion.Euler(
                0f,
                RigMath.RecenterYaw(OpenSpawnYaw(c, spawnYaw), hmdYaw),
                0f
            );
        }
        var head = (rig.Origin.rotation * VRInput.HeadRotation).N();
        var hmdLook = LookMath.ToLookValues(head);
        var look = Override.Update(new NVector2(current.x, current.y), hmdLook, dt);
        ScriptedPullYaw =
            Override.Weight > 0f ? LookMath.WrapDegrees(Override.Scripted.X - hmdLook.X) : 0f;
        c.data.lookValues = new Vector2(
            LookMath.ContinueYaw(current.x, look.X, Unobserved(c)),
            look.Y
        );
        Recalculate(c);
    }

    /// <summary>A wall nearer than this (m) along the spawn look turns the new body toward open space.</summary>
    private const float SpawnOpenDepth = 2.5f;

    /// <summary>How far (m) each spawn heading is probed.</summary>
    private const float SpawnProbeLength = 10f;

    private static readonly float[] spawnDepths = new float[LookMath.SpawnYawSteps.Length];

    /// <summary>
    /// The heading a new body faces: the game's spawn look (lookValues, never set at spawn, so +Z for every seat:
    /// in the Airport that is a wall or a door half a metre away), else the nearest heading with
    /// <see cref="SpawnOpenDepth"/> of room, else the most open one. The same wall layers and back-face rays as
    /// ui.world's menu placement (<see cref="UI.World.WorldCanvases.WallAlong"/>). Runs once per body.
    /// </summary>
    private static float OpenSpawnYaw(Character c, float lookYaw)
    {
        var eye = c.Head;
        var backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            for (int i = 0; i < spawnDepths.Length; i++)
            {
                var dir =
                    Quaternion.Euler(0f, lookYaw + LookMath.SpawnYawSteps[i], 0f) * Vector3.forward;
                spawnDepths[i] = UI.World.WorldCanvases.WallAlong(
                    eye,
                    dir * SpawnProbeLength,
                    out _
                );
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
        int pick = LookMath.PickOpen(spawnDepths, SpawnOpenDepth);
        float step = LookMath.SpawnYawSteps[pick];
        Plugin.Log.LogInfo(
            FormattableString.Invariant(
                $"[player.look] spawn facing: game look {lookYaw:0} deg has {spawnDepths[0]:0.0} m of room; facing {lookYaw + step:0} deg ({step:+0;-0;0} deg) with {spawnDepths[pick]:0.0} m"
            )
        );
        return lookYaw + step;
    }

    /// <summary>No peer sees this avatar's look: offline or alone in the room, or dead, which parks the body at
    /// <c>Character.DeathPos</c> (Character.cs:558, :758). Only then may the synced yaw jump by whole turns.</summary>
    private static bool Unobserved(Character c)
    {
        var room = PhotonNetwork.CurrentRoom;
        return PhotonNetwork.OfflineMode || room == null || room.PlayerCount <= 1 || c.data.dead;
    }

    /// <summary>No turning while a menu or wheel takes the sticks (the game's own gate, Character.cs:1711).</summary>
    private static bool CanTurn()
    {
        var gui = GUIManager.instance;
        return gui == null || !(gui.windowBlockingInput || gui.wheelActive);
    }
}
