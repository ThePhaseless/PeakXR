using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Peak;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Comfort;

/// <summary>
/// Hooks of <see cref="ComfortEventsFeature"/> at the sources of view-moving events. Every patch only reports the
/// event (or swaps a mono render effect for a VR-safe one); gameplay forces, damage and RPCs run as vanilla.
/// Paths are relative to decomp/Assembly-CSharp/.
/// </summary>
internal static class ComfortEventsPatches
{
    /// <summary>CharacterMovement.character (CharacterMovement.cs:32, private).</summary>
    internal static AccessTools.FieldRef<CharacterMovement, Character> MovementCharacter;

    /// <summary>ItemActionBase.item (ItemActionBase.cs:5, protected).</summary>
    internal static AccessTools.FieldRef<ItemActionBase, Item> ActionItem;

    /// <summary>Set while a push zone applies its force, so the impulse it sends is not also a knockback.</summary>
    private static bool inPushZone;

    public static void Resolve()
    {
        TornadoPull.Ignored = GameMember.Field<Tornado, List<Character>>("ignoredCharacters");
        TornadoPull.Caught = GameMember.Field<Tornado, List<Character>>("caughtCharacters");
        TornadoPull.Height = GameMember.Delegate<Func<Tornado, Character, float>>(
            typeof(Tornado),
            "Height",
            [typeof(Character)]
        );
        LavaPush.OutsideBounds = GameMember.Delegate<Func<Lava, Vector3, bool>>(
            typeof(Lava),
            "OutsideBounds",
            [typeof(Vector3)]
        );
        MovementCharacter = GameMember.Field<CharacterMovement, Character>("character");
        ActionItem = GameMember.Field<ItemActionBase, Item>("item");
        BlindWash.Material = GameMember.Field<IllegalScreenEffect, Material>("mat");
        BlindWash.Renderer = GameMember.Field<IllegalScreenEffect, MeshRenderer>("rend");
    }

    private static bool IsLocal(Character c) => c != null && c.IsLocal;

    /// <summary>
    /// PerlinShake.AddShake(float, float, float) (PerlinShake.cs:28): every camera shake ends here
    /// (GamefeelHandler.AddPerlinShake :46 and AddPerlinShakeProximity :56, the Vector3 overload :38; rows IC-13,
    /// IC-35, CH-16, CH-21, ST-07, ST-09, ST-12, EN-07, EN-17, EN-23, EN-25, EN-43, EN-49, EN-52, EN-56). Its
    /// rotation never reaches the headset (rig.camera puts back the pose MainCameraMovement.LateUpdate writes, shake
    /// included); here it becomes a comfort cue.
    /// </summary>
    [HarmonyPatch]
    internal static class Shake
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(PerlinShake),
                nameof(PerlinShake.AddShake),
                [typeof(float), typeof(float), typeof(float)]
            );

        private static void Prefix(float amount, float duration, float scale)
        {
            if (VRSession.Active)
                ComfortEventsRuntime.RaiseShake(amount, duration, scale);
        }
    }

    /// <summary>
    /// Character.RPCA_Fall(float, float) (Character.cs:1749, [PunRPC] on every client; Character.Fall :1732 feeds it):
    /// the body starts tumbling (CH-30, CH-31, IT-05, IT-47, EN-03, EN-11, EN-14, EN-15, EN-30, EN-42, EN-44).
    /// </summary>
    [HarmonyPatch]
    internal static class Fall
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Character), "RPCA_Fall", [typeof(float), typeof(float)]);

        private static void Postfix(Character __instance, float seconds)
        {
            if (VRSession.Active && seconds > 0f && IsLocal(__instance))
                ComfortEventsRuntime.Raise(ComfortEvent.Ragdoll, seconds / 5f);
        }
    }

    /// <summary>
    /// Character.RPCA_AddForceAtPosition(Vector3, Vector3, float) (Character.cs:458, [PunRPC] on every client):
    /// impulse knockbacks from mobs, bonks, arrows, axes, rollers, explosions (CH-32, IT-47, EN-02, EN-03, EN-30,
    /// EN-44, EN-46, EN-48, EN-56). Vanilla impulses run 200 to 500+.
    /// </summary>
    [HarmonyPatch]
    internal static class ForceAtPosition
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(Character),
                nameof(Character.RPCA_AddForceAtPosition),
                [typeof(Vector3), typeof(Vector3), typeof(float)]
            );

        private static void Postfix(Character __instance, Vector3 force)
        {
            if (!VRSession.Active || !IsLocal(__instance))
                return;
            float size = Vector3.Magnitude(force);
            // A wind zone's own push (see WindZone): sized by the force it really applies, which shelter, the
            // intensity curve or low gravity can scale to 0. Its nominal 15 (WindChillZone.cs:52) gives 0.5.
            // Felt more on the side the push came from (against the force): windward, or facing the hit.
            if (inPushZone)
            {
                if (size > 0.5f)
                {
                    float strength = Mathf.Min(size / 30f, 1f);
                    ComfortEventsRuntime.RaiseFrom(ComfortEvent.WindPush, strength, -force);
                    // Shelter scales the force to 0 (WindChillZone.cs:202, :250), so the pressure fades behind rock.
                    ComfortEventsRuntime.Pressure(-force, BodyCues.PullAmplitude(strength));
                }
            }
            else if (size >= 50f)
                ComfortEventsRuntime.RaiseFrom(ComfortEvent.Knockback, size / 800f, -force);
        }
    }

    /// <summary>
    /// Character.RPCA_AddForceToBodyPart(BodypartType, Vector3, Vector3) (Character.cs:2082, [PunRPC]): drags by
    /// tongue, web, flytrap and rescue hook every frame, breakable pushes and the bad-mushroom launch (IN-18, EN-04,
    /// EN-07, EN-14, IC-53).
    /// </summary>
    [HarmonyPatch]
    internal static class ForceToBodyPart
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(Character),
                nameof(Character.RPCA_AddForceToBodyPart),
                [typeof(BodypartType), typeof(Vector3), typeof(Vector3)]
            );

        private static void Postfix(Character __instance, Vector3 wholeBodyForce)
        {
            if (!VRSession.Active || !IsLocal(__instance))
                return;
            float size = Vector3.Magnitude(wholeBodyForce);
            if (size >= 20f)
                ComfortEventsRuntime.RaiseFrom(
                    ComfortEvent.Knockback,
                    size / 1000f,
                    -wholeBodyForce
                );
        }
    }

    /// <summary>ScoutCannon.RPCA_LaunchTarget(int) (ScoutCannon.cs:268, [PunRPC] on every client) (IT-34).</summary>
    [HarmonyPatch]
    internal static class CannonLaunch
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(ScoutCannon), "RPCA_LaunchTarget", [typeof(int)]);

        private static void Postfix(int targetID)
        {
            var c = Character.localCharacter;
            if (
                VRSession.Active
                && c != null
                && c.refs.view != null
                && c.refs.view.ViewID == targetID
            )
                ComfortEventsRuntime.RaiseLaunch(1f);
        }
    }

    /// <summary>Action_LaunchPlayer.RunAction() (Action_LaunchPlayer.cs:5): an item flings its holder (IC-63, IT-39).</summary>
    [HarmonyPatch]
    internal static class ItemLaunch
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Action_LaunchPlayer), nameof(Action_LaunchPlayer.RunAction));

        private static void Postfix(Action_LaunchPlayer __instance)
        {
            var item = ActionItem(__instance);
            if (VRSession.Active && item != null && IsLocal(item.holderCharacter))
                ComfortEventsRuntime.RaiseLaunch(0.8f);
        }
    }

    /// <summary>CharacterMovement.DoSuperJump(float, float, int, float) (CharacterMovement.cs:487): super jump
    /// amulet and mass super jump (IT-52, ST-39).</summary>
    [HarmonyPatch]
    internal static class SuperJump
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterMovement),
                nameof(CharacterMovement.DoSuperJump),
                [typeof(float), typeof(float), typeof(int), typeof(float)]
            );

        private static void Postfix(CharacterMovement __instance)
        {
            if (VRSession.Active && IsLocal(MovementCharacter(__instance)))
                ComfortEventsRuntime.RaiseLaunch(0.7f);
        }
    }

    /// <summary>Tornado.RPCA_CaptureCharacter(int) (Tornado.cs:311, [PunRPC]): the tornado lifts a player (EN-29).</summary>
    [HarmonyPatch]
    internal static class TornadoCapture
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Tornado), "RPCA_CaptureCharacter", [typeof(int)]);

        private static void Postfix(int targetViewID)
        {
            var c = Character.localCharacter;
            if (
                VRSession.Active
                && c != null
                && c.refs.view != null
                && c.refs.view.ViewID == targetViewID
            )
                ComfortEventsRuntime.RaiseLaunch(1f);
        }
    }

    /// <summary>
    /// Steady pushes, each a comfort WindPush while it acts on the local body: WindChillZone.AddWindForceToCharacter
    /// (WindChillZone.cs:215, IsMine only; its force goes through RPCA_AddForceAtPosition, kept out of knockback by
    /// the scope flag) (EN-27), PlayerMoveZone.AddForceToCharacter (PlayerMoveZone.cs:42, local only) (EN-36),
    /// Tornado.AttractCharacters (Tornado.cs:279) (EN-29), Lava.DoEffects (Lava.cs:89, local only) (EN-21).
    /// </summary>
    [HarmonyPatch]
    internal static class WindZone
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(WindChillZone),
                "AddWindForceToCharacter",
                [typeof(Character), typeof(float)]
            );

        // The cue itself is raised by ForceAtPosition from the force this call really sends.
        private static void Prefix() => inPushZone = true;

        private static void Finalizer() => inPushZone = false;
    }

    /// <inheritdoc cref="WindZone"/>
    [HarmonyPatch]
    internal static class MoveZone
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(PlayerMoveZone), "AddForceToCharacter", Type.EmptyTypes);

        private static void Prefix()
        {
            if (VRSession.Active)
                ComfortEventsRuntime.Raise(ComfortEvent.WindPush, 0.5f);
        }
    }

    /// <inheritdoc cref="WindZone"/>
    [HarmonyPatch]
    internal static class TornadoPull
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Tornado), "AttractCharacters", Type.EmptyTypes);

        /// <summary>Tornado.ignoredCharacters / caughtCharacters (Tornado.cs:42, :44, private).</summary>
        internal static AccessTools.FieldRef<Tornado, List<Character>> Ignored,
            Caught;

        /// <summary>Tornado.Height(Character) (Tornado.cs:237, private).</summary>
        internal static Func<Tornado, Character, float> Height;

        private static void Prefix(Tornado __instance)
        {
            var c = Character.localCharacter;
            if (!VRSession.Active || c == null)
                return;
            // Same tests as AttractCharacters (Tornado.cs:283-290): not ignored or caught, in reach and height.
            if (Ignored(__instance).Contains(c) || Caught(__instance).Contains(c))
                return;
            float reach = __instance.transform.localScale.x * __instance.range;
            var d = __instance.transform.position - c.Center;
            float h = Height(__instance, c);
            float flat = d.x * d.x + d.z * d.z;
            if (flat <= reach * reach && h >= -10f && h <= 50f)
            {
                ComfortEventsRuntime.Raise(ComfortEvent.WindPush, 0.7f);
                // G2: the pull grows toward the funnel (Tornado.cs:293-301); crouched it is a quarter (:296-300).
                float strength = reach > 0f ? 1f - Mathf.Sqrt(flat) / reach : 1f;
                ComfortEventsRuntime.Pressure(
                    d,
                    BodyCues.PullAmplitude(0.5f + 0.5f * strength, c.data.isCrouching)
                );
            }
        }
    }

    /// <inheritdoc cref="WindZone"/>
    [HarmonyPatch]
    internal static class LavaPush
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Lava), "DoEffects", Type.EmptyTypes);

        /// <summary>Lava.OutsideBounds(Vector3) (Lava.cs:68, protected virtual).</summary>
        internal static Func<Lava, Vector3, bool> OutsideBounds;

        private static void Prefix(Lava __instance)
        {
            var c = Character.localCharacter;
            // Same tests as DoEffects (Lava.cs:92): inside the lava's bounds and not above its surface.
            if (
                VRSession.Active
                && c != null
                && c.Center.y - 0.5f <= __instance.transform.position.y
                && !OutsideBounds(__instance, c.Center)
            )
                ComfortEventsRuntime.Raise(ComfortEvent.WindPush, 0.6f);
        }
    }

    /// <summary>
    /// GUIManager.AddStatusFX(STATUSTYPE, float) (GUIManager.cs:836), called only for the local, observed character
    /// (CharacterAfflictions.cs:841-844): every status flash (IC-34, IC-50, ST-07, ST-10, ST-12, ST-13, ST-15,
    /// ST-16, ST-17, ST-19, ST-23, ST-24, ST-27, ST-34, ST-58).
    /// </summary>
    [HarmonyPatch]
    internal static class StatusFlash
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(GUIManager),
                nameof(GUIManager.AddStatusFX),
                [typeof(CharacterAfflictions.STATUSTYPE), typeof(float)]
            );

        private static void Prefix(float amount)
        {
            if (VRSession.Active)
                ComfortEventsRuntime.Raise(ComfortEvent.StatusFlash, amount * 4f);
        }
    }

    /// <summary>
    /// IllegalScreenEffect.Update (IllegalScreenEffect.cs:25): the BLIND status (Affliction_Blind.OnApplied,
    /// Peak.Afflictions/Affliction_Blind.cs:21, applied by explosion and flash AOEs, AOE.cs:118) shows Misc/BlindPost,
    /// a quad whose BlindPost shader (Forward pass: Blend SrcAlpha OneMinusSrcAlpha, ZTest LEqual, no ZWrite) lays a
    /// depth fog of HDR cyan (_Color1 (0, 3.1, 12), _Color2 (0, 6.1, 12)) over the view at <c>_Alpha</c>, which stays
    /// near 1 until the last 3 s (IllegalScreenEffect.cs:44-53). Capping <c>_Alpha</c> cannot help: 0.4 of a colour
    /// twelve times white still blows out after tonemapping. So in VR the game's quad is not drawn
    /// (<c>forceRenderingOff</c>; the game keeps toggling <c>enabled</c>, :42, :45, :60) and its <c>_Alpha</c> ramp
    /// becomes <see cref="Strength"/>, which drives a capped white haze (ComfortEventsRuntime, Core
    /// ComfortPolicy.BlindHaze) and a white edge glow (ui.status). The status itself (isBlind) is untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class BlindWash
    {
        /// <summary>IllegalScreenEffect.mat (IllegalScreenEffect.cs:16, private), its own material instance.</summary>
        internal static AccessTools.FieldRef<IllegalScreenEffect, Material> Material;

        /// <summary>IllegalScreenEffect.rend (IllegalScreenEffect.cs:14, private).</summary>
        internal static AccessTools.FieldRef<IllegalScreenEffect, MeshRenderer> Renderer;

        private static float strength;
        private static int frame = -10;

        /// <summary>The game's wash strength 0..1 (its <c>_Alpha</c> while the overlay is on), 0 when not blind or
        /// when no overlay updated in the last frame (scene change).</summary>
        internal static float Strength => Time.frameCount - frame <= 1 ? strength : 0f;

        /// <summary>The renderer this patch stopped drawing, given back when VR stops so flat play sees BLIND again.</summary>
        private static MeshRenderer hidden;

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(IllegalScreenEffect), "Update", Type.EmptyTypes);

        private static void Postfix(IllegalScreenEffect __instance)
        {
            var rend = Renderer(__instance);
            if (!VRSession.Active)
            {
                if (hidden != null && ReferenceEquals(rend, hidden))
                {
                    hidden.forceRenderingOff = false;
                    hidden = null;
                }
                return;
            }
            var mat = Material(__instance);
            if (rend == null || mat == null)
                return;
            if (!rend.forceRenderingOff)
            {
                rend.forceRenderingOff = true;
                hidden = rend;
            }
            strength = rend.enabled ? mat.GetFloat(__instance.shaderVarName) : 0f;
            frame = Time.frameCount;
        }
    }

    /// <summary>
    /// Post-volume pulses on the local HUD: GUIManager.StartSugarRush (GUIManager.cs:962), StartEnergyDrink (:985),
    /// StartHeat (:1001), PlayPertrifyFX(float) (:919) (IC-49, IC-52, ST-20, ST-26, ST-29, ST-31).
    /// </summary>
    [HarmonyPatch]
    internal static class PostEffect
    {
        private static MethodBase[] TargetMethods() =>
            [
                GameMember.Method(
                    typeof(GUIManager),
                    nameof(GUIManager.StartSugarRush),
                    Type.EmptyTypes
                ),
                GameMember.Method(
                    typeof(GUIManager),
                    nameof(GUIManager.StartEnergyDrink),
                    Type.EmptyTypes
                ),
                GameMember.Method(
                    typeof(GUIManager),
                    nameof(GUIManager.StartHeat),
                    Type.EmptyTypes
                ),
                GameMember.Method(
                    typeof(GUIManager),
                    nameof(GUIManager.PlayPertrifyFX),
                    [typeof(float)]
                ),
            ];

        private static void Prefix()
        {
            if (VRSession.Active)
                ComfortEventsRuntime.Raise(ComfortEvent.PostEffect, 1f);
        }
    }

    /// <summary>
    /// Cutscene camera jitter: CutsceneShake.Update (CutsceneShake.cs:11) and CinematicScreenShake.Update
    /// (Peak/CinematicScreenShake.cs:13, fed by AddCinematicScreenShake.FixedUpdate) shake the cutscene camera's
    /// position (ST-64, MF-51). In VR the camera keeps its smooth path without the jitter, and CinematicScreenShake's
    /// jitter becomes a shake cue (rumble by default). CutsceneShake's is a constant prefab wobble (nothing writes
    /// <c>shake</c>), so it gets no cue: it would rumble for the whole cutscene. Presentation only: these components
    /// move nothing else. The game's own Update still runs, with only the shake amount neutralised around it, so a
    /// change to the camera path in a game update carries over.
    /// </summary>
    [HarmonyPatch]
    internal static class CutsceneJitter
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CutsceneShake), "Update");

        // Zero for the game's Update, then the prefab value back, so flat play after VR keeps its wobble. The
        // finalizer restores it even when the game's Update throws.
        private static void Prefix(CutsceneShake __instance, out float __state)
        {
            __state = __instance.shake;
            if (VRSession.Active)
                __instance.shake = 0f;
        }

        private static void Finalizer(CutsceneShake __instance, float __state)
        {
            if (__instance != null)
                __instance.shake = __state;
        }
    }

    /// <inheritdoc cref="CutsceneJitter"/>
    [HarmonyPatch]
    internal static class CinematicJitter
    {
        /// <summary>Stands in for the real shake during the game's Update: its jitter is micrometres, and the decay
        /// the game applies to it (shake / Probe) is replayed on the real amount, so the decay stays the game's.</summary>
        private const float Probe = 1e-4f;

        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CinematicScreenShake), "Update");

        private static void Prefix(CinematicScreenShake __instance, out float __state)
        {
            __state = float.NaN;
            if (!VRSession.Active)
                return;
            __state = __instance.shake;
            __instance.shake = Probe;
            try
            {
                ComfortEventsRuntime.RaiseShake(__state * 0.1f, 0.1f);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<ComfortEventsFeature>(e);
            }
        }

        // A finalizer, so the real amount comes back even when the game's Update throws: left at Probe, the shake
        // would be lost for flat play.
        private static void Finalizer(
            CinematicScreenShake __instance,
            Exception __exception,
            float __state
        )
        {
            if (float.IsNaN(__state) || __instance == null)
                return;
            __instance.shake = __exception == null ? __state * (__instance.shake / Probe) : __state;
        }
    }

    /// <summary>
    /// Mirror.LateUpdate (Mirror.cs:55, [DefaultExecutionOrder(99999)]) renders one reflection from Camera.main's
    /// pose (Mirror.cs:77-91), the point between the eyes in VR (IT-30). With ComfortEvents.Mirrors off the mirror
    /// camera stops and the glass keeps no reflection.
    /// </summary>
    [HarmonyPatch]
    internal static class MirrorRender
    {
        // Only cameras this patch stopped are turned back on; one that ships disabled stays off.
        private static readonly HashSet<Camera> stopped = new();

        private static MethodBase TargetMethod() => GameMember.Method(typeof(Mirror), "LateUpdate");

        private static bool Prefix(Mirror __instance)
        {
            var cam = __instance.mirrorCamera;
            if (!VRSession.Active || ComfortEventsConfig.Mirrors)
            {
                // Mirrors switched back on: the camera this patch stopped renders again.
                if (stopped.Count > 0 && cam != null && stopped.Remove(cam))
                    cam.enabled = true;
                return true;
            }
            if (cam != null && cam.enabled)
            {
                cam.enabled = false;
                stopped.Add(cam);
            }
            return false;
        }
    }

    /// <summary>
    /// Mirage.fadeMirage (Mirage.cs:27) dissolves a desert mirage into particles that sample a mono screen-space
    /// render (MirageManager.sampleCamera MirageManager.cs:13, UVs from WorldToScreenPoint Mirage.cs:62), wrong
    /// in both eyes (ST-67, EN-58). In VR the mirage just vanishes at the same distance: its objects hide and the
    /// particles are cleared in the same frame, before they draw. The game's fadeMirage still runs (its one
    /// offscreen sample render, Play, the delayed hide), so a change to it in a game update carries over.
    /// </summary>
    [HarmonyPatch]
    internal static class MirageFade
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(Mirage), nameof(Mirage.fadeMirage));

        private static void Postfix(Mirage __instance)
        {
            if (!VRSession.Active)
                return;
            try
            {
                if (__instance.ps != null)
                    __instance.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (__instance.hideObjects && __instance.objectsToHide != null)
                    foreach (var go in __instance.objectsToHide)
                        if (go != null)
                            go.SetActive(false);
            }
            catch (Exception e)
            {
                PatchGuard.Fail<ComfortEventsFeature>(e);
            }
        }
    }

    /// <summary>
    /// Hard cuts to Title (MF-19): NetworkConnector.LeaveRoom (NetworkConnector.cs:407, the quit path from
    /// PauseMenuMainPage.Quit :206) starts a fade while the room is left; NetworkConnector.OnLeftRoom (:220) and
    /// KickedState.Enter (KickedState.cs:22) call SceneManager.LoadScene at once, so the view goes black right
    /// before. ui.menu fades back in after the new scene (UIMenuRuntime.FadeInAfterCut).
    /// </summary>
    [HarmonyPatch]
    internal static class LeaveRoom
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(NetworkConnector),
                nameof(NetworkConnector.LeaveRoom),
                Type.EmptyTypes
            );

        private static void Prefix()
        {
            if (VRSession.Active)
                ComfortEventsRuntime.FadeForSceneCut(snap: false);
        }
    }

    /// <inheritdoc cref="LeaveRoom"/>
    [HarmonyPatch]
    internal static class SceneCut
    {
        private static MethodBase[] TargetMethods() =>
            [
                GameMember.Method(
                    typeof(NetworkConnector),
                    nameof(NetworkConnector.OnLeftRoom),
                    Type.EmptyTypes
                ),
                GameMember.Method(typeof(KickedState), nameof(KickedState.Enter), Type.EmptyTypes),
            ];

        private static void Prefix()
        {
            if (VRSession.Active)
                ComfortEventsRuntime.FadeForSceneCut(snap: true);
        }
    }
}
