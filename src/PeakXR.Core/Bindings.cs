using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>Game actions the controller layout can press. Movement and turning are sticks, not actions.</summary>
    public enum VRAction
    {
        Jump,
        Sprint,
        Crouch,
        Interact,
        UsePrimary,
        UseSecondary,
        Drop,
        HotbarNext,
        HotbarPrevious,
        ScrollForward,
        ScrollBackward,
        PutAway,
        Backpack,
        Ping,
        Emote,
        Pause,

        /// <summary>Voice push-to-talk. Only the game's Push To Talk and Push To Mute microphone modes read it.</summary>
        PushToTalk,

        /// <summary>Spectate the next or previous teammate while dead or passed out.</summary>
        SpectateNext,
        SpectatePrevious,
    }

    /// <summary>
    /// Which controller a binding lives on. Roles other than Left/Right resolve per frame, so a left-handed
    /// player gets a mirrored layout and item use follows whichever hand holds the item.
    /// </summary>
    public enum BindingHand
    {
        Left,
        Right,
        Dominant,
        OffHand,
        ItemHand,
        OtherHand,
    }

    public enum VRControl
    {
        Trigger,
        Grip,
        Primary,
        Secondary,
        StickClick,

        /// <summary>Stick flicked up past the activate threshold (and more up than sideways).</summary>
        StickUp,
        StickDown,
        Menu,
    }

    public enum PressKind
    {
        /// <summary>Down while the control is down.</summary>
        Hold,

        /// <summary>One-frame press when the control is released before the long-press time.</summary>
        Tap,

        /// <summary>Down from the long-press time until the control is released.</summary>
        LongPress,
    }

    public readonly struct Binding
    {
        public readonly VRAction Action;
        public readonly BindingHand Hand;
        public readonly VRControl Control;
        public readonly PressKind Press;

        public Binding(VRAction action, BindingHand hand, VRControl control, PressKind press)
        {
            Action = action;
            Hand = hand;
            Control = control;
            Press = press;
        }
    }

    /// <summary>One controller's raw values for <see cref="ActionMapper"/>.</summary>
    public struct ControllerInput
    {
        public float Trigger;
        public float Grip;
        public bool Primary;
        public bool Secondary;
        public bool StickClick;
        public bool Menu;
        public Vector2 Stick;
    }

    /// <summary>
    /// The default controller layout. docs/controls.md documents it for players; keep both in sync. Menu long-press
    /// is not here: rig.camera owns it (recenter).
    /// </summary>
    public static class BindingTable
    {
        public static readonly Binding[] Default =
        {
            new Binding(VRAction.Jump, BindingHand.Dominant, VRControl.Primary, PressKind.Hold),
            new Binding(VRAction.Sprint, BindingHand.OffHand, VRControl.StickClick, PressKind.Hold),
            new Binding(
                VRAction.Crouch,
                BindingHand.Dominant,
                VRControl.StickClick,
                PressKind.Hold
            ),
            new Binding(VRAction.Interact, BindingHand.Left, VRControl.Grip, PressKind.Hold),
            new Binding(VRAction.Interact, BindingHand.Right, VRControl.Grip, PressKind.Hold),
            new Binding(
                VRAction.UsePrimary,
                BindingHand.ItemHand,
                VRControl.Trigger,
                PressKind.Hold
            ),
            new Binding(
                VRAction.UseSecondary,
                BindingHand.OtherHand,
                VRControl.Trigger,
                PressKind.Hold
            ),
            new Binding(VRAction.Drop, BindingHand.Dominant, VRControl.Secondary, PressKind.Hold),
            new Binding(
                VRAction.HotbarNext,
                BindingHand.Dominant,
                VRControl.StickUp,
                PressKind.Hold
            ),
            new Binding(
                VRAction.HotbarPrevious,
                BindingHand.Dominant,
                VRControl.StickDown,
                PressKind.Hold
            ),
            new Binding(
                VRAction.ScrollForward,
                BindingHand.Dominant,
                VRControl.StickUp,
                PressKind.Hold
            ),
            new Binding(
                VRAction.ScrollBackward,
                BindingHand.Dominant,
                VRControl.StickDown,
                PressKind.Hold
            ),
            new Binding(VRAction.PutAway, BindingHand.OffHand, VRControl.Primary, PressKind.Tap),
            new Binding(
                VRAction.Backpack,
                BindingHand.OffHand,
                VRControl.Primary,
                PressKind.LongPress
            ),
            new Binding(VRAction.Ping, BindingHand.OffHand, VRControl.Secondary, PressKind.Tap),
            new Binding(
                VRAction.Emote,
                BindingHand.OffHand,
                VRControl.Secondary,
                PressKind.LongPress
            ),
            new Binding(
                VRAction.SpectateNext,
                BindingHand.Dominant,
                VRControl.StickUp,
                PressKind.Hold
            ),
            new Binding(
                VRAction.SpectatePrevious,
                BindingHand.Dominant,
                VRControl.StickDown,
                PressKind.Hold
            ),
            new Binding(VRAction.Pause, BindingHand.Left, VRControl.Menu, PressKind.Tap),
            new Binding(VRAction.Pause, BindingHand.Right, VRControl.Menu, PressKind.Tap),
            // Every control already has a press; a long press of the move-stick click is the one gap. Its
            // first frame still toggles sprint, which does nothing while standing still.
            new Binding(
                VRAction.PushToTalk,
                BindingHand.OffHand,
                VRControl.StickClick,
                PressKind.LongPress
            ),
        };

        /// <summary>True when <paramref name="role"/> means the left controller.</summary>
        public static bool ResolveLeft(BindingHand role, bool dominantLeft, bool itemInLeft)
        {
            switch (role)
            {
                case BindingHand.Left:
                    return true;
                case BindingHand.Right:
                    return false;
                case BindingHand.Dominant:
                    return dominantLeft;
                case BindingHand.OffHand:
                    return !dominantLeft;
                case BindingHand.ItemHand:
                    return itemInLeft;
                case BindingHand.OtherHand:
                    return !itemInLeft;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }

    /// <summary>
    /// Parses button setting names such as <c>DominantSecondary</c>, <c>OffHandPrimary</c> or <c>RightStickClick</c>.
    /// Hand-role names follow <c>Input.DominantHand</c>, so a left-handed player's drop button moves with the layout.
    /// </summary>
    public static class ButtonName
    {
        /// <summary>Hand-role names first (they follow the dominant hand), then fixed controllers.</summary>
        public static readonly string[] All =
        {
            "None",
            "DominantPrimary",
            "DominantSecondary",
            "DominantStickClick",
            "OffHandPrimary",
            "OffHandSecondary",
            "OffHandStickClick",
            "LeftPrimary",
            "LeftSecondary",
            "LeftStickClick",
            "RightPrimary",
            "RightSecondary",
            "RightStickClick",
        };

        public static bool TryParse(
            string name,
            bool dominantLeft,
            out bool left,
            out VRControl control
        )
        {
            left = false;
            control = default;
            if (string.IsNullOrEmpty(name))
                return false;
            BindingHand hand;
            int skip;
            if (name.StartsWith("Dominant", StringComparison.Ordinal))
                (hand, skip) = (BindingHand.Dominant, 8);
            else if (name.StartsWith("OffHand", StringComparison.Ordinal))
                (hand, skip) = (BindingHand.OffHand, 7);
            else if (name.StartsWith("Left", StringComparison.Ordinal))
                (hand, skip) = (BindingHand.Left, 4);
            else if (name.StartsWith("Right", StringComparison.Ordinal))
                (hand, skip) = (BindingHand.Right, 5);
            else
                return false;
            if (Rest(name, skip, "Primary"))
                control = VRControl.Primary;
            else if (Rest(name, skip, "Secondary"))
                control = VRControl.Secondary;
            else if (Rest(name, skip, "StickClick"))
                control = VRControl.StickClick;
            else
                return false;
            left = BindingTable.ResolveLeft(hand, dominantLeft, dominantLeft);
            return true;
        }

        /// <summary>The name continues with exactly <paramref name="tail"/> (no allocation: parsed per frame).</summary>
        static bool Rest(string name, int skip, string tail) =>
            name.Length == skip + tail.Length
            && string.CompareOrdinal(name, skip, tail, 0, tail.Length) == 0;
    }

    /// <summary>Stick helpers shared by turning and the action layout.</summary>
    public static class StickAxes
    {
        /// <summary>Stick X for turning, or 0 when the push is more up/down than sideways (a hotbar flick).</summary>
        public static float TurnX(Vector2 stick) =>
            MathF.Abs(stick.X) >= MathF.Abs(stick.Y) ? stick.X : 0f;
    }

    /// <summary>
    /// Turns two controllers into per-action press/hold/release edges using a binding table. Call
    /// <see cref="Update"/> once per frame. Actions can be disabled (owned by another feature or out of
    /// context) and single controls reserved (bound elsewhere); a disabled action that was held reports its
    /// release on the next update, so the game never sees a stuck button.
    /// </summary>
    public sealed class ActionMapper
    {
        const int ControlCount = 8;
        const int ActionCount = (int)VRAction.SpectatePrevious + 1;

        public float FlickActivate = 0.7f;
        public float FlickRelease = 0.4f;

        /// <summary>Seconds a control must stay down to count as a long press.</summary>
        public float LongPressSeconds = 0.5f;

        /// <summary>
        /// Tap limit for the menu button. rig.camera recenters on a menu hold of this length, so a release before
        /// it pauses and no hold length does nothing. Set it from the recenter setting (one threshold).
        /// </summary>
        public float MenuHoldSeconds = 1f;

        public bool DominantLeft;

        /// <summary>Hand that holds the item (or the dominant hand when empty-handed).</summary>
        public bool ItemInLeft;

        readonly Binding[] _bindings;
        readonly AnalogButton[] _analog = new AnalogButton[4];
        readonly ButtonEdge[] _controls = new ButtonEdge[2 * ControlCount];
        readonly bool[] _reserved = new bool[2 * ControlCount];
        readonly ButtonEdge[] _actions = new ButtonEdge[ActionCount];
        readonly bool[] _disabled = new bool[ActionCount];
        readonly bool[] _down = new bool[ActionCount];

        public ActionMapper()
            : this(BindingTable.Default) { }

        public ActionMapper(Binding[] bindings)
        {
            _bindings = bindings;
            for (int i = 0; i < _analog.Length; i++)
                _analog[i] = new AnalogButton();
            for (int i = 0; i < _controls.Length; i++)
                _controls[i] = new ButtonEdge();
            for (int i = 0; i < _actions.Length; i++)
                _actions[i] = new ButtonEdge();
        }

        public bool Pressed(VRAction action) => _actions[(int)action].Pressed;

        public bool Held(VRAction action) => _actions[(int)action].Held;

        public bool Released(VRAction action) => _actions[(int)action].Released;

        public void SetDisabled(VRAction action, bool disabled) =>
            _disabled[(int)action] = disabled;

        public void SetReserved(bool left, VRControl control, bool reserved) =>
            _reserved[Index(left, control)] = reserved;

        public void Update(in ControllerInput left, in ControllerInput right, float dt)
        {
            UpdateControls(true, left, dt);
            UpdateControls(false, right, dt);

            Array.Clear(_down, 0, _down.Length);
            foreach (var b in _bindings)
            {
                int a = (int)b.Action;
                if (_disabled[a] || _down[a])
                    continue;
                int c = Index(
                    BindingTable.ResolveLeft(b.Hand, DominantLeft, ItemInLeft),
                    b.Control
                );
                if (!_reserved[c])
                    _down[a] = IsDown(_controls[c], b.Press, b.Control);
            }
            for (int a = 0; a < ActionCount; a++)
                _actions[a].Update(_down[a], dt);
        }

        public void Reset()
        {
            foreach (var a in _analog)
                a.Reset();
            foreach (var c in _controls)
                c.Reset();
            foreach (var a in _actions)
                a.Reset();
        }

        bool IsDown(ButtonEdge control, PressKind press, VRControl which)
        {
            switch (press)
            {
                case PressKind.Tap:
                    return control.Released
                        && control.HeldTime
                            < (which == VRControl.Menu ? MenuHoldSeconds : LongPressSeconds);
                case PressKind.LongPress:
                    return control.Held && control.HeldTime >= LongPressSeconds;
                default:
                    return control.Held;
            }
        }

        void UpdateControls(bool left, in ControllerInput c, float dt)
        {
            int side = left ? 0 : 2;
            _analog[side].Update(c.Trigger, dt);
            _analog[side + 1].Update(c.Grip, dt);
            Set(left, VRControl.Trigger, _analog[side].Held, dt);
            Set(left, VRControl.Grip, _analog[side + 1].Held, dt);
            Set(left, VRControl.Primary, c.Primary, dt);
            Set(left, VRControl.Secondary, c.Secondary, dt);
            Set(left, VRControl.StickClick, c.StickClick, dt);
            Set(left, VRControl.Menu, c.Menu, dt);
            Set(left, VRControl.StickUp, Flick(left, VRControl.StickUp, c.Stick, 1f), dt);
            Set(left, VRControl.StickDown, Flick(left, VRControl.StickDown, c.Stick, -1f), dt);
        }

        bool Flick(bool left, VRControl control, Vector2 stick, float sign)
        {
            float y = stick.Y * sign;
            if (_controls[Index(left, control)].Held)
                return y > FlickRelease;
            return y >= FlickActivate && y > MathF.Abs(stick.X);
        }

        void Set(bool left, VRControl control, bool down, float dt) =>
            _controls[Index(left, control)].Update(down, dt);

        static int Index(bool left, VRControl control) => (left ? 0 : ControlCount) + (int)control;
    }
}
