using System;

namespace PeakXR.Core
{
    /// <summary>What the VR settings tab shows in 3D while a row is changed or hovered. Every kind also shows the
    /// row's info card; the others add a sample of the effect itself.</summary>
    public enum PreviewKind
    {
        /// <summary>Info card only.</summary>
        Info,
        Vignette,
        Turn,
        MoveDirection,
        Crouch,
        Fade,
        Hud,
        Wrist,
        MenuPlacement,
        StatusGlow,
        NameTag,
        Haptic,
        BeltSlots,
    }

    /// <summary>
    /// Maps config entries (section, key) to their preview and to a note when a change does not apply at once,
    /// so the settings tab needs no hooks in the features that own the entries.
    /// </summary>
    public static class SettingPreviewTable
    {
        const string Restart = "Applies after restarting the game.";

        public static PreviewKind KindFor(string section, string key)
        {
            if (key.Contains("Haptic", StringComparison.OrdinalIgnoreCase))
                return PreviewKind.Haptic;
            if (key == "FadeSeconds")
                return PreviewKind.Fade;
            switch (section)
            {
                case "Comfort":
                    return key switch
                    {
                        "Vignette" or "VignetteStrength" => PreviewKind.Vignette,
                        "TurnMode" or "SnapTurnAngle" or "SmoothTurnSpeed" => PreviewKind.Turn,
                        _ => PreviewKind.Info,
                    };
                case "Locomotion":
                    return key switch
                    {
                        "MoveRelativeTo" => PreviewKind.MoveDirection,
                        "PhysicalCrouch" => PreviewKind.Crouch,
                        _ => PreviewKind.Info,
                    };
                case "UI":
                    return key switch
                    {
                        "MenuDistance" or "MenuTilt" => PreviewKind.MenuPlacement,
                        "StatusOverlays" => PreviewKind.StatusGlow,
                        "NameTagScale" => PreviewKind.NameTag,
                        _ => PreviewKind.Info,
                    };
                case "UI.HUD":
                    return key switch
                    {
                        "WristHand" or "WristWidth" => PreviewKind.Wrist,
                        "HudMode" or "HudDistance" or "HudWidth" or "HudTilt" or "HudFollowAngle" =>
                            PreviewKind.Hud,
                        _ => PreviewKind.Info,
                    };
                case "UI.Status":
                    return key is "PeripheralIntensity" or "DyingTint"
                        ? PreviewKind.StatusGlow
                        : PreviewKind.Info;
                case "Inventory":
                    return
                        key
                            is "BodySlots"
                                or "SlotLayout"
                                or "SlotRadius"
                                or "SlotHeight"
                                or "SlotGhosts"
                        ? PreviewKind.BeltSlots
                        : PreviewKind.Info;
                default:
                    return PreviewKind.Info;
            }
        }

        /// <summary>Hover shows only passive samples: a fade, a rumble or a moving menu happen on change only.</summary>
        public static bool VisualOnHover(PreviewKind kind) =>
            kind is not (PreviewKind.Fade or PreviewKind.Haptic or PreviewKind.MenuPlacement);

        /// <summary>Text for the info card when the entry does not take effect at once; null when it does.</summary>
        public static string ApplyNote(string section, string key) =>
            (section, key) switch
            {
                ("XR", "PauseInputOnFocusLoss") => null,
                ("XR", _) => Restart,
                ("Interaction.Binoculars", "LensResolution") => Restart,
                ("UI", "TitleOnBeach") => "Applies the next time the title screen loads.",
                ("Render", "RenderScale") =>
                    "Applies once the slider rests for a moment. Higher is sharper but costs frame rate.",
                _ => null,
            };
    }

    /// <summary>
    /// Timing of the settings preview: a change shows at once; a hover only after the laser rests on the row, so
    /// sweeping across the list shows nothing. Both end on their own and fade out over the last moment.
    /// </summary>
    public sealed class PreviewClock
    {
        public const float ChangeSeconds = 1.8f;
        public const float HoverSeconds = 1.2f;
        public const float HoverDwell = 0.25f;
        public const float FadeOut = 0.25f;

        float remaining;
        float duration;
        float dwell = -1f;

        public bool Active => remaining > 0f;

        public bool FromHover { get; private set; }

        /// <summary>Seconds since the preview started, for animated samples.</summary>
        public float Elapsed => duration - remaining;

        /// <summary>1 while showing, easing to 0 over <see cref="FadeOut"/>.</summary>
        public float Alpha =>
            remaining <= 0f ? 0f
            : remaining >= FadeOut ? 1f
            : remaining / FadeOut;

        /// <summary>0 at the start, 1 after <paramref name="seconds"/>: eases a sample in instead of popping it.</summary>
        public float RampIn(float seconds) =>
            seconds <= 0f ? 1f : MathF.Min(1f, MathF.Max(0f, Elapsed / seconds));

        public void Change()
        {
            duration = remaining = ChangeSeconds;
            FromHover = false;
            dwell = -1f;
        }

        public void HoverEnter() => dwell = 0f;

        public void HoverExit() => dwell = -1f;

        public void Stop()
        {
            remaining = 0f;
            dwell = -1f;
        }

        /// <summary>Advances time; true on the frame a hover has rested long enough to start its preview.</summary>
        public bool Tick(float dt)
        {
            if (remaining > 0f)
                remaining = MathF.Max(0f, remaining - dt);
            if (dwell < 0f)
                return false;
            dwell += dt;
            if (dwell < HoverDwell)
                return false;
            dwell = -1f;
            duration = remaining = HoverSeconds;
            FromHover = true;
            return true;
        }
    }
}
