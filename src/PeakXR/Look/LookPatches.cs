using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Locomotion;
using PeakXR.VR;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.Look;

/// <summary>Harmony patches and per-frame state of <see cref="PlayerLookFeature"/>.</summary>
internal static class LookPatches
{
    /// <summary><c>CharacterMovement.character</c> (CharacterMovement.cs:32, private).</summary>
    public static AccessTools.FieldRef<CharacterMovement, Character> Movement;

    /// <summary><c>Character.RecalculateLookDirections</c> (Character.cs:1347, internal).</summary>
    public static Action<Character> Recalculate;

    private static readonly TurnInput Turn = new();
    private static readonly LookOverride Override = new();
    private static Character lastCharacter;
    private static int tickFrame = -10;

    /// <summary>Smooth turning speed (deg/s) from this or the previous frame, for the comfort vignette (its update
    /// order against CharacterMovement is not fixed). Snaps report 0, and so do frames without a look update.</summary>
    public static float SmoothTurnRate => Time.frameCount - tickFrame <= 1 ? Turn.SmoothRate : 0f;

    /// <summary>
    /// CharacterMovement.CameraLook (CharacterMovement.cs:283, private): called from CharacterMovement.Update
    /// (:201) right after CharacterInput.Sample (:198) whenever no wheel is open (CanMoveCamera, :174). It adds the
    /// look stick to <c>data.lookValues</c> and recalculates the look directions that movement (Character.cs:1311),
    /// aiming, interaction, body yaw (Character.cs:1529) and the sync to peers (CharacterSyncData.cs:62) read.
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
            if (!VRSession.Active)
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
        var head = (rig.Origin.rotation * VRInput.HeadRotation).N();
        var current = c.data.lookValues;
        var look = Override.Update(
            new NVector2(current.x, current.y),
            LookMath.ToLookValues(head),
            dt
        );
        c.data.lookValues = new Vector2(LookMath.ContinueYaw(current.x, look.X), look.Y);
        Recalculate(c);
    }

    /// <summary>No turning while a menu or wheel takes the sticks (the game's own gate, Character.cs:1714).</summary>
    private static bool CanTurn()
    {
        var gui = GUIManager.instance;
        return gui == null || !(gui.windowBlockingInput || gui.wheelActive);
    }
}
