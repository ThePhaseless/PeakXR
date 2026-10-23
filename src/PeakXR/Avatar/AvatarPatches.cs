using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Avatar;

/// <summary>Harmony patches of <see cref="AvatarFeature"/>. Each only acts on characters whose
/// <see cref="AvatarPoseDriver"/> has a fresh pose; everyone else keeps vanilla behaviour.</summary>
internal static class AvatarPatches
{
    private static AccessTools.FieldRef<CharacterAnimations, Character> animationsCharacter;
    private static AccessTools.FieldRef<CharacterRagdoll, Character> ragdollCharacter;
    private static AccessTools.FieldRef<CharacterItems, Character> itemsCharacter;
    private static AccessTools.FieldRef<HideTheBody, Character> hideCharacter;
    private static AccessTools.FieldRef<HideTheBody, bool> hideShowing;
    private static System.Action<HideTheBody, bool> hideToggle;

    /// <summary><c>CharacterMovement.maxAngle</c> (CharacterMovement.cs:132, private): the walkable slope limit.</summary>
    internal static AccessTools.FieldRef<CharacterMovement, float> MaxAngle;

    public static void Resolve()
    {
        MaxAngle = GameMember.Field<CharacterMovement, float>("maxAngle");
        animationsCharacter = GameMember.Field<CharacterAnimations, Character>("character");
        ragdollCharacter = GameMember.Field<CharacterRagdoll, Character>("character");
        itemsCharacter = GameMember.Field<CharacterItems, Character>("character");
        hideCharacter = GameMember.Field<HideTheBody, Character>("character");
        hideShowing = GameMember.Field<HideTheBody, bool>("isShowing");
        hideToggle = GameMember.Delegate<System.Action<HideTheBody, bool>>(
            typeof(HideTheBody),
            "Toggle",
            new[] { typeof(bool) }
        );
    }

    /// <summary>Whether <paramref name="hide"/> shows the whole body, head included (HideTheBody.isShowing,
    /// HideTheBody.cs:27). Read by the self-test (vr.33).</summary>
    internal static bool BodyShowing(HideTheBody hide) => hideShowing(hide);

    private static AvatarPoseDriver Driver(Character c) =>
        AvatarPoseDriver.Of(c) is { HasFreshPose: true } d ? d : null;

    /// <summary>CharacterAnimations.ConfigureIK (CharacterAnimations.cs:390): runs in the physics tick right after
    /// the animated pose is evaluated (CharacterRagdoll.cs:170), for local and remote characters.</summary>
    [HarmonyPatch]
    internal static class ConfigureIK
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterAnimations), "ConfigureIK");

        private static void Postfix(CharacterAnimations __instance) =>
            Driver(animationsCharacter(__instance))?.OnConfigureIK();
    }

    /// <summary>CharacterAnimations.HandleIK (CharacterAnimations.cs:339, private): sets rig and arm IK weights
    /// from Update and LateUpdate.</summary>
    [HarmonyPatch]
    internal static class HandleIK
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterAnimations), "HandleIK");

        private static void Postfix(CharacterAnimations __instance) =>
            Driver(animationsCharacter(__instance))?.OnHandleIK();
    }

    /// <summary>CharacterRagdoll.SaveAdditionalTransformPositions (CharacterRagdoll.cs:212, private): writes
    /// animationItemTransform, the frame that GetItemHoldPos and GetItemPosLeft/Right read.</summary>
    [HarmonyPatch]
    internal static class SaveAdditional
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterRagdoll), "SaveAdditionalTransformPositions");

        private static void Postfix(CharacterRagdoll __instance) =>
            Driver(ragdollCharacter(__instance))?.OnSaveAdditional();
    }

    /// <summary>CharacterItems.GetItemHoldForward (CharacterItems.cs:701, private): hold torque target.</summary>
    [HarmonyPatch]
    internal static class HoldForward
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "GetItemHoldForward");

        private static void Postfix(CharacterItems __instance, ref Vector3 __result)
        {
            var c = itemsCharacter(__instance);
            var d = Driver(c);
            if (d != null && d.ItemFrameOverridden)
                __result = c.refs.animationItemTransform.forward;
        }
    }

    /// <summary>CharacterItems.GetItemHoldUp (CharacterItems.cs:704, private): hold torque target.</summary>
    [HarmonyPatch]
    internal static class HoldUp
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "GetItemHoldUp");

        private static void Postfix(CharacterItems __instance, ref Vector3 __result)
        {
            var c = itemsCharacter(__instance);
            var d = Driver(c);
            if (d != null && d.ItemFrameOverridden)
                __result = c.refs.animationItemTransform.up;
        }
    }

    /// <summary>CharacterItems.AttachItem (CharacterItems.cs:641, private): vanilla welds both hands to the item.
    /// Tracked characters weld only the holding hand so the other hand stays free.</summary>
    [HarmonyPatch]
    internal static class AttachItem
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "AttachItem");

        private static bool Prefix(CharacterItems __instance, Item item)
        {
            var d = Driver(itemsCharacter(__instance));
            return d == null || !d.AttachToHoldingHand(item);
        }
    }

    /// <summary>CharacterItems.HoldItem (CharacterItems.cs:170, private): the held item's spring toward its hold
    /// point, every physics tick. A local held item the ragdoll arm cannot bring to the controller follows it free of
    /// the hand instead (<see cref="AvatarPoseDriver.FollowHeld"/>).</summary>
    [HarmonyPatch]
    internal static class HoldItem
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "HoldItem");

        private static bool Prefix(CharacterItems __instance, Item item)
        {
            var d = Driver(itemsCharacter(__instance));
            return d == null || !d.FollowHeld(item);
        }
    }

    /// <summary>CharacterItems.UnAttachItem (CharacterItems.cs:~722, private): remove our single-hand weld too.</summary>
    [HarmonyPatch]
    internal static class UnAttachItem
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "UnAttachItem");

        private static void Postfix(CharacterItems __instance)
        {
            var c = itemsCharacter(__instance);
            AvatarPoseDriver.Of(c)?.DestroyItemJoint();
        }
    }

    /// <summary>HideTheBody.Toggle (HideTheBody.cs:57, private): first-person body hiding via the shader's
    /// <c>_VertexGhost</c>. In VR, show the body (arms) and keep the head, face and hats hidden.</summary>
    [HarmonyPatch]
    internal static class HideBody
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(HideTheBody), "Toggle");

        private static void Postfix(HideTheBody __instance, bool show)
        {
            if (show || !VRSession.Active)
                return;
            var c = hideCharacter(__instance);
            if (c == null || !c.IsLocal)
                return;
            var mode = AvatarConfig.Instance.OwnBody;
            if (string.Equals(mode, AvatarConfig.ShowVanilla, System.StringComparison.Ordinal))
                return;

            __instance.SetShowing(__instance.body, 0f);
            for (int i = 0; i < __instance.costumes.Length; i++)
                __instance.SetShowing(__instance.costumes[i], 0f);
            if (__instance.sash != null)
                __instance.SetShowing(__instance.sash, 0f);
            if (__instance.medal != null)
                __instance.SetShowing(__instance.medal, 0f);
            // The visible body casts its own shadow now; the shadow-only copy would double it.
            if (__instance.shadowCaster != null)
                __instance.shadowCaster.SetActive(false);

            if (!string.Equals(mode, AvatarConfig.ShowAll, System.StringComparison.Ordinal))
                return;
            __instance.SetShowing(__instance.headRend, 0f);
            foreach (var r in __instance.face.GetComponentsInChildren<Renderer>())
                __instance.SetShowing(r, 0f);
            foreach (var hat in __instance.refs.playerHats)
                __instance.SetShowing(hat, 0f);
            if (__instance.shadowCasterHat != null)
                __instance.shadowCasterHat.SetActive(false);
        }
    }

    /// <summary>HideTheBody.Update (HideTheBody.cs:39-50, private) shows a body only for remote, passed-out or dead
    /// characters. While rig.camera shows the local body from outside (a detached takeover view: tumbling, rocket),
    /// show all of it, head included, as vanilla does for a body seen from outside; when the view returns, vanilla's
    /// Update hides it again through <see cref="HideBody"/>.</summary>
    [HarmonyPatch]
    internal static class ShowBodyWhenDetached
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(HideTheBody), "Update");

        private static bool Prefix(HideTheBody __instance)
        {
            if (!VRSession.Active || !Rig.RigDriver.ViewDetached)
                return true;
            var c = hideCharacter(__instance);
            if (c == null || !c.IsLocal)
                return true;
            if (!hideShowing(__instance))
                hideToggle(__instance, true);
            return false;
        }
    }

    /// <summary>Character.Start (Character.cs:494): attach the local pose builder to the local character.</summary>
    [HarmonyPatch]
    internal static class CharacterStart
    {
        private static MethodBase TargetMethod() => GameMember.Method(typeof(Character), "Start");

        private static void Postfix(Character __instance)
        {
            if (
                VRSession.Requested != VRRuntime.Disabled
                && __instance.IsLocal
                && __instance.GetComponent<LocalAvatar>() == null
            )
                __instance.gameObject.AddComponent<LocalAvatar>();
        }
    }
}
