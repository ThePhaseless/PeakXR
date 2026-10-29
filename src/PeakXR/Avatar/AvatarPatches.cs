using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

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
        AvatarPoseDriver.VanillaAttach = GameMember.Method(typeof(CharacterItems), "AttachItem");
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
    /// the animated pose is evaluated (CharacterRagdoll.cs:170), for local and remote characters. Every driver, fresh
    /// or stale: a stale pose hands the lean, legs and fingers back to vanilla there.</summary>
    [HarmonyPatch]
    internal static class ConfigureIK
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterAnimations), "ConfigureIK");

        private static void Postfix(CharacterAnimations __instance)
        {
            try
            {
                var d = AvatarPoseDriver.Of(animationsCharacter(__instance));
                if (d != null)
                    d.OnConfigureIK();
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
            }
        }
    }

    /// <summary>CharacterAnimations.HandleIK (CharacterAnimations.cs:339, private): sets rig and arm IK weights
    /// from Update and LateUpdate.</summary>
    [HarmonyPatch]
    internal static class HandleIK
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterAnimations), "HandleIK");

        private static void Postfix(CharacterAnimations __instance)
        {
            try
            {
                var d = Driver(animationsCharacter(__instance));
                if (d != null)
                    d.OnHandleIK();
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
            }
        }
    }

    /// <summary>CharacterRagdoll.SaveAdditionalTransformPositions (CharacterRagdoll.cs:212, private): writes
    /// animationItemTransform, the frame that GetItemHoldPos and GetItemPosLeft/Right read.</summary>
    [HarmonyPatch]
    internal static class SaveAdditional
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterRagdoll), "SaveAdditionalTransformPositions");

        private static void Postfix(CharacterRagdoll __instance)
        {
            try
            {
                var d = Driver(ragdollCharacter(__instance));
                if (d != null)
                    d.OnSaveAdditional();
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
            }
        }
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

    /// <summary>CharacterItems.GetItemHoldUp (CharacterItems.cs:696, private): hold torque target.</summary>
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
            try
            {
                var d = Driver(itemsCharacter(__instance));
                return d == null || !d.AttachToHoldingHand(item);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
                return true;
            }
        }
    }

    /// <summary>CharacterItems.HoldItem (CharacterItems.cs:170, private): the held item's spring toward its hold
    /// point, every physics tick. A local held item the ragdoll arm cannot bring to the controller follows it free of
    /// the hand instead (<see cref="AvatarPoseDriver.FollowHeld"/>). Every driver, fresh or stale: once the pose goes
    /// stale FollowHeld re-welds a free item.</summary>
    [HarmonyPatch]
    internal static class HoldItem
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "HoldItem");

        private static bool Prefix(CharacterItems __instance, Item item)
        {
            try
            {
                var d = AvatarPoseDriver.Of(itemsCharacter(__instance));
                return d == null || !d.FollowHeld(item);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
                return true;
            }
        }
    }

    /// <summary>The held item rides a tracked hand this tick (welded or carried free by <see cref="AvatarPoseDriver.FollowHeld"/>):
    /// set by the ragdoll's pass (CharacterRagdoll.cs:155-176, execution order -99) before CharacterMovement's.</summary>
    private static bool HandCarried(CharacterItems items, out Character c)
    {
        c = itemsCharacter(items);
        return Driver(c) is { ItemFrameOverridden: true } && c.data.currentItem != null;
    }

    /// <summary>CharacterItems.AddMovementForce (CharacterItems.cs:835, internal), from CharacterMovement.FixedUpdate
    /// (CharacterMovement.cs:311): the walk pushes the held item on its own, 5 kg at about 27 m/s^2. Welded, that push
    /// lands on the hand whenever the body cannot keep pace (a wall, the walk's ramp): it held the hand 0.3 m off its
    /// controller and let the weld go each time the player walked into a wall (vr run of e7b3cd9). A hand-carried item
    /// moves with the hand instead; local physics only, held items are not synced (ItemPhysicsSyncer.cs:55).</summary>
    [HarmonyPatch]
    internal static class ItemWalkForce
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "AddMovementForce");

        private static bool Prefix(CharacterItems __instance)
        {
            try
            {
                return !HandCarried(__instance, out _);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
                return true;
            }
        }
    }

    /// <summary>CharacterItems.AddDrag (CharacterItems.cs:840, internal), from CharacterMovement.cs:312 and :354:
    /// without the walk's push a hand-carried item's own drag would hold it back on the hand at walking speed, so it
    /// damps only the item's motion against the body (<see cref="ItemFollow.BodyDrag"/>).</summary>
    [HarmonyPatch]
    internal static class ItemDrag
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "AddDrag");

        private static bool Prefix(CharacterItems __instance, float drag, float factor)
        {
            try
            {
                if (!HandCarried(__instance, out var c) || c.refs.hip == null)
                    return true;
                float d = factor != 1f ? Mathf.Lerp(1f, drag, factor) : drag;
                var item = c.data.currentItem.rig;
                var hip = c.refs.hip.Rig;
                item.linearVelocity = ItemFollow
                    .BodyDrag(item.linearVelocity.N(), hip.linearVelocity.N(), d)
                    .U();
                item.angularVelocity = ItemFollow
                    .BodyDrag(item.angularVelocity.N(), hip.angularVelocity.N(), d)
                    .U();
                return false;
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
                return true;
            }
        }
    }

    /// <summary>CharacterItems.UnAttachItem (CharacterItems.cs:665, private): remove our single-hand weld too.</summary>
    [HarmonyPatch]
    internal static class UnAttachItem
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterItems), "UnAttachItem");

        private static void Postfix(CharacterItems __instance)
        {
            try
            {
                var d = AvatarPoseDriver.Of(itemsCharacter(__instance));
                if (d != null)
                {
                    d.DestroyItemJoint();
                    d.EndPassBody();
                }
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<AvatarFeature>(e);
            }
        }
    }

    /// <summary>HideTheBody.Toggle (HideTheBody.cs:57, private): first-person body hiding via the shader's
    /// <c>_VertexGhost</c>. In VR, show the body (arms) and keep the head, face and hats out of the eyes.</summary>
    [HarmonyPatch]
    internal static class HideBody
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(HideTheBody), "Toggle");

        private static void Postfix(HideTheBody __instance, bool show)
        {
            var c = hideCharacter(__instance);
            if (c == null || !c.IsLocal)
                return;
            var mode = AvatarConfig.Instance.OwnBody;
            bool vanilla = string.Equals(
                mode,
                AvatarConfig.ShowVanilla,
                System.StringComparison.Ordinal
            );
            bool all = string.Equals(mode, AvatarConfig.ShowAll, System.StringComparison.Ordinal);
            // The ghost hides only red-painted vertices within 1 m of the camera measured flat (BodyGhost), so the
            // eyes, face and hat brims stayed drawn whenever the head was not exactly round the eye (looking down,
            // a lean): the head, face and hats cast their shadow only while the view is in the head.
            ShadowOnlyHead(__instance, !show && VRSession.Active && !vanilla && !all);
            if (show || !VRSession.Active || vanilla)
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
            // The hats cast their own shadow as well (ShadowOnlyHead), so their shadow-only copy would double it too.
            if (__instance.shadowCasterHat != null)
                __instance.shadowCasterHat.SetActive(false);

            if (!all)
                return;
            __instance.SetShowing(__instance.headRend, 0f);
            foreach (var r in __instance.face.GetComponentsInChildren<Renderer>())
                __instance.SetShowing(r, 0f);
            foreach (var hat in __instance.refs.playerHats)
                __instance.SetShowing(hat, 0f);
        }

        /// <summary>Head, face and hat renderers (HideTheBody.cs:10, :14, CustomizationRefs.playerHats) drawn only
        /// into shadow maps, or back to drawn. Only renderers in the mode we set are switched back, so a renderer the
        /// game keeps shadowless stays so. Runs on a toggle, never per frame.</summary>
        private static void ShadowOnlyHead(HideTheBody hide, bool shadowOnly)
        {
            Shadow(hide.headRend, shadowOnly);
            if (hide.face != null)
                foreach (var r in hide.face.GetComponentsInChildren<Renderer>(true))
                    Shadow(r, shadowOnly);
            if (hide.refs != null && hide.refs.playerHats != null)
                foreach (var hat in hide.refs.playerHats)
                    Shadow(hat, shadowOnly);
        }

        private static void Shadow(Renderer r, bool shadowOnly)
        {
            if (r == null)
                return;
            var from = shadowOnly ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            if (r.shadowCastingMode == from)
                r.shadowCastingMode = shadowOnly
                    ? ShadowCastingMode.ShadowsOnly
                    : ShadowCastingMode.On;
        }
    }

    /// <summary>
    /// CharacterAnimations.Update (CharacterAnimations.cs:160, private) feeds the animator's <c>Look Y</c> from the
    /// look pitch (:200-206), which bows the head and upper body down and forward when looking down. In VR the
    /// headset pitches the view itself: the bow only moved the local avatar's head and chest in front of and under the
    /// eyes (your own head and hat in view, the body ahead of you). The local VR body stays level on foot and leans
    /// only with the real head (<see cref="AvatarPoseDriver"/>'s lean); climbing, ragdolled and other characters keep
    /// the game's pose.
    /// </summary>
    [HarmonyPatch]
    internal static class LevelLook
    {
        private static readonly int LookY = Animator.StringToHash("Look Y");

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterAnimations), "Update");

        private static void Postfix(CharacterAnimations __instance)
        {
            if (!VRSession.Active || !Rig.RigDriver.ViewOnBody)
                return;
            var c = animationsCharacter(__instance);
            if (c == null || !c.IsLocal || Driver(c) == null || !Rig.RigDriver.Upright(c))
                return;
            var animator = c.refs.animator;
            if (animator != null)
                animator.SetFloat(LookY, 0f);
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
            {
                __instance.gameObject.AddComponent<LocalAvatar>();
                __instance.gameObject.AddComponent<DrawnHands>();
            }
        }
    }
}
