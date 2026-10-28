using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Emotes;

/// <summary>
/// <c>avatar.emotes</c>: emote playback on tracked avatars. A tracked avatar's arms normally follow its
/// controllers; during an emote they keep doing so unless the emote is full-body (configurable), in which case the
/// animation owns the arms until it ends. Applies to every character with a pose driver (the local VR player and
/// remote PeakXR VR players), so it also runs for flat players. The emote wheel itself belongs to the UI features.
/// </summary>
public sealed class EmotesFeature : Feature
{
    public override string Id => "avatar.emotes";

    public override bool VROnly => false;

    internal static ConfigEntry<string> Mode;
    internal static ConfigEntry<string> FullBody;
    internal static ConfigEntry<bool> FadeBackflip;
    internal static ConfigEntry<bool> Gestures;

    protected internal override void Install()
    {
        const string s = "Emotes";
        Mode = Plugin.Settings.Bind(
            s,
            "TrackedArms",
            nameof(EmoteArmsMode.TrackedUnlessFullBody),
            new ConfigDescription(
                "Who moves a VR player's arms during an emote. TrackedUnlessFullBody: the controllers, except for "
                    + "the full-body emotes below. AlwaysTracked: always the controllers. AlwaysAnimated: always the "
                    + "emote animation.",
                new AcceptableValueList<string>(EmoteArms.ModeNames)
            )
        );
        FullBody = Plugin.Settings.Bind(
            s,
            "FullBodyEmotes",
            EmoteArms.DefaultFullBody,
            "Comma-separated emote animation names that animate the whole body, arms included."
        );
        FadeBackflip = Plugin.Settings.Bind(
            s,
            "FadeDuringBackflip",
            true,
            "Fade the view to black while your own backflip emote flips the camera."
        );
        Gestures = Plugin.Settings.Bind(
            s,
            "GestureEmotes",
            true,
            "In VR, wave a raised open hand side to side to wave, hold both open hands over your head to cheer (one-handed: one "
                + "open hand held still over your head), or hold a fist out in front with the thumb up for a thumbs-up. "
                + "They are the game's own emotes, so every player sees them, modded or not."
        );
        Patch(typeof(PlayEmote));
        GestureEmotes.Create();
    }

    /// <summary>CharacterAnimations.RPCA_PlayRemove(string, bool) (CharacterAnimations.cs:417, private RPC): the
    /// emote starts on every client. PlayDead only ragdolls (:419) and needs no arm handling.</summary>
    [HarmonyPatch]
    internal static class PlayEmote
    {
        private const string PlayDead = "A_Scout_Emote_PlayDead";
        internal const string BackFlip = "A_Scout_Emote_BackFlip";

        private static MethodBase TargetMethod() =>
            GameMember.Method(
                typeof(CharacterAnimations),
                "RPCA_PlayRemove",
                new[] { typeof(string), typeof(bool) }
            );

        private static void Postfix(CharacterAnimations __instance, string emoteName)
        {
            // Runs on a peer's RPC, for flat clients too: a throw would reach Photon's RPC dispatch.
            try
            {
                Apply(__instance, emoteName);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<EmotesFeature>(e);
            }
        }

        private static void Apply(CharacterAnimations __instance, string emoteName)
        {
            if (emoteName == PlayDead)
                return;
            var character = __instance.GetComponent<Character>();
            if (character == null)
                return;
            var driver = AvatarPoseDriver.Of(character);
            bool suspend =
                driver != null
                && EmoteArms.SuspendArms(
                    EmoteArms.ParseMode(Mode.Value),
                    emoteName,
                    FullBody.Value
                );
            bool fade =
                emoteName == BackFlip
                && FadeBackflip.Value
                && VRSession.Active
                && character == Character.localCharacter;
            // A new emote replaces the previous one's arm state, so update whenever there is a driver.
            if (driver == null && !fade)
                return;
            var state = character.GetComponent<EmoteState>();
            if (state == null)
                state = character.gameObject.AddComponent<EmoteState>();
            state.Begin(character, driver, suspend, fade);
        }
    }
}

/// <summary>Tracks one character's running emote: hands its arms to the animation and clears the backflip fade.</summary>
internal sealed class EmoteState : MonoBehaviour
{
    private static readonly int EmoteBool = Animator.StringToHash("Emote");

    /// <summary>Vanilla ends emotes after 2 s (CharacterAnimations.cs:277); this is a safety net.</summary>
    private const float MaxSeconds = 2.5f;

    /// <summary>The backflip launch comes 0.35 s in and lands about a second later (CharacterAnimations.cs:435).</summary>
    private const float FadeSeconds = 1.4f;

    private Character character;
    private AvatarPoseDriver driver;
    private float startTime;
    private bool fading;

    public void Begin(Character owner, AvatarPoseDriver poseDriver, bool suspend, bool fade)
    {
        character = owner;
        driver = poseDriver;
        startTime = Time.time;
        if (driver != null)
            driver.ArmsSuspended = suspend;
        fading = fade;
        if (fade)
            VRFade.Set(this, 1f);
        enabled = true;
    }

    private void LateUpdate()
    {
        float t = Time.time - startTime;
        if (fading && (t > FadeSeconds || !VRSession.Active))
            StopFade();

        var animator = character != null ? character.refs.animator : null;
        bool playing = t < 0.1f || (animator != null && animator.GetBool(EmoteBool));
        if (playing && t < MaxSeconds)
            return;
        if (driver != null)
            driver.ArmsSuspended = false;
        StopFade();
        enabled = false;
    }

    private void StopFade()
    {
        if (!fading)
            return;
        fading = false;
        VRFade.Clear(this);
    }

    private void OnDisable() => StopFade();
}
