using System.Collections.Generic;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// How a controller's usages feed a hand, decided once per connect from its usage names. Profiles differ:
    /// the Vive wand has no face buttons and its trackpad is its stick, WMR has no face buttons but a separate
    /// touchpad, the Khronos simple profile has no trigger or grip axis.
    /// </summary>
    public struct ControllerLayout
    {
        /// <summary>An analog trigger; without one the trigger reads its button.</summary>
        public bool HasTrigger;

        /// <summary>An analog grip; without one the grip reads its button.</summary>
        public bool HasGrip;

        /// <summary>No face buttons: they come from trackpad clicks.</summary>
        public bool EmulateFaceButtons;

        /// <summary>No separate clickable trackpad (Secondary2DAxis with a click), so the stick is the pad.</summary>
        public bool PadIsStick;

        /// <summary>A clickable trackpad next to real face buttons: its click is the menu button.</summary>
        public bool PadMenuClick;

        /// <summary>A firm trackpad squeeze next to real face buttons is the menu button.</summary>
        public bool PadMenuForce;

        public static ControllerLayout From(IReadOnlyList<string> usages)
        {
            bool Has(string name) => XRUsageNames.Find(usages, name) != null;

            var l = default(ControllerLayout);
            l.HasTrigger = Has("Trigger");
            l.HasGrip = Has("Grip");
            l.EmulateFaceButtons = !Has("PrimaryButton") && !Has("SecondaryButton");
            l.PadIsStick = !(Has("Secondary2DAxis") && Has("Secondary2DAxisClick"));
            // SteamVR keeps the Index system button (the profile's MenuButton) for its dashboard, so an Index
            // player would have no pause or recenter. A trackpad next to real face buttons is otherwise unbound.
            l.PadMenuClick = !l.EmulateFaceButtons && !l.PadIsStick;
            l.PadMenuForce = !l.EmulateFaceButtons && Has("Secondary2DAxisForce");
            return l;
        }
    }

    /// <summary>One frame of raw controller usages; a usage the device lacks reads zero/false. Pad is the
    /// separate trackpad (Secondary2DAxis), not the stick.</summary>
    public struct ControllerSample
    {
        public float Trigger;
        public float Grip;
        public float PadForce;
        public bool TriggerButton;
        public bool GripButton;
        public bool TriggerTouch;
        public bool Primary;
        public bool PrimaryTouch;
        public bool Secondary;
        public bool SecondaryTouch;
        public bool StickClick;
        public bool StickTouch;
        public bool Menu;
        public bool PadClick;
        public Vector2 Stick;
        public Vector2 Pad;
    }

    /// <summary>The button and axis part of a hand's state, the same on every profile.</summary>
    public struct ControllerButtons
    {
        public float Trigger;
        public float Grip;
        public Vector2 Stick;
        public bool TriggerTouch;
        public bool Primary;
        public bool Secondary;
        public bool PrimaryTouch;
        public bool SecondaryTouch;
        public bool StickClick;
        public bool StickTouch;
        public bool Menu;
    }

    /// <summary>Maps raw usages to hand buttons by <see cref="ControllerLayout"/>.</summary>
    public static class ControllerMap
    {
        /// <summary>A firm trackpad squeeze, well above a resting thumb.</summary>
        public const float PadMenuForce = 0.6f;

        /// <summary>Trigger pull that counts as a touch on controllers without a trigger touch sensor.</summary>
        public const float TriggerTouchPull = 0.05f;

        /// <param name="moveHand">This hand walks (the off hand); only the Vive wand pad uses it.</param>
        public static ControllerButtons Map(
            in ControllerLayout l,
            in ControllerSample s,
            bool moveHand
        )
        {
            var b = default(ControllerButtons);
            b.Trigger = l.HasTrigger ? s.Trigger : (s.TriggerButton ? 1f : 0f);
            b.Grip = l.HasGrip ? s.Grip : (s.GripButton ? 1f : 0f);
            b.TriggerTouch = s.TriggerTouch || b.Trigger > TriggerTouchPull;
            b.Stick = s.Stick;
            b.StickTouch = s.StickTouch;
            b.Menu =
                s.Menu
                || (l.PadMenuClick && s.PadClick)
                || (l.PadMenuForce && s.PadForce > PadMenuForce);
            b.PrimaryTouch = s.PrimaryTouch;
            b.SecondaryTouch = s.SecondaryTouch;
            b.StickClick = s.StickClick;

            if (l.EmulateFaceButtons && l.PadIsStick)
            {
                // Vive wand: the pad is the only stick and it reports a resting thumb, so everything takes a click.
                var wand = WandPad.Map(s.Stick, s.StickClick, moveHand);
                b.Stick = wand.Stick;
                b.Primary = wand.Primary;
                b.Secondary = wand.Secondary;
                b.StickClick = wand.StickClick;
            }
            else if (l.EmulateFaceButtons)
            {
                var zone = TrackpadButtons.Zone(s.Pad);
                b.Secondary = s.PadClick && zone == PadZone.Up;
                b.Primary = s.PadClick && zone == PadZone.Down;
            }
            else
            {
                b.Primary = s.Primary;
                b.Secondary = s.Secondary;
            }
            return b;
        }
    }
}
