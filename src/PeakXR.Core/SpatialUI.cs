using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Placement and sizing for game UI that lives in the world instead of on a screen (Unity convention: Y up,
    /// yaw 0 = +Z). Text keeps a constant angular size, so it reads the same at the hand and 30 m away.
    /// </summary>
    public static class SpatialMath
    {
        /// <summary>Comfortable focus range for a label at an interact target, in metres from the eye (a target
        /// nearer than <see cref="TargetNear"/> keeps its label in front of it).</summary>
        public const float TargetNear = 0.4f;
        public const float TargetFar = 2f;

        /// <summary>Metres per canvas pixel that make one pixel subtend <paramref name="degreesPerPixel"/>.</summary>
        public static float MetersPerPixel(float distance, float degreesPerPixel) =>
            MathF.Max(distance, 0f) * MathF.Tan(degreesPerPixel * UnityQuat.Deg2Rad);

        /// <summary>Width in metres of something that spans <paramref name="degrees"/> at <paramref name="distance"/>.</summary>
        public static float SpanMeters(float distance, float degrees) =>
            2f * MathF.Max(distance, 0f) * MathF.Tan(degrees * 0.5f * UnityQuat.Deg2Rad);

        /// <summary>Angle of one canvas pixel at <paramref name="metersPerPixel"/> seen from <paramref name="distance"/>.</summary>
        public static float DegreesPerPixel(float metersPerPixel, float distance) =>
            distance > 0f ? MathF.Atan(metersPerPixel / distance) / UnityQuat.Deg2Rad : 0f;

        /// <summary>
        /// Where the label of an interact target goes: on the line from the eye to the target's centre, pulled toward
        /// the eye past the object's <paramref name="radius"/> (capped at 0.6 m) so it floats in front of it, kept
        /// within <see cref="TargetNear"/>..<see cref="TargetFar"/>, and lifted above the sight line.
        /// </summary>
        public static Vector3 TargetLabel(Vector3 center, Vector3 eye, float radius)
        {
            float r = Math.Clamp(radius, 0.05f, 0.6f);
            var to = center - eye;
            float dist = to.Length();
            if (dist < 1e-3f)
                return center + Vector3.UnitY * (r + 0.05f);
            // Never behind the target: for something closer than TargetNear the label stays just in front of it.
            float d = MathF.Min(Math.Clamp(dist - r - 0.05f, TargetNear, TargetFar), dist * 0.9f);
            float lift = (r * 0.5f + 0.08f) * d / dist;
            return eye + to / dist * d + Vector3.UnitY * lift;
        }

        /// <summary>A distant sign <paramref name="distance"/> metres along <paramref name="yawDeg"/>, raised
        /// <paramref name="elevationDeg"/> above eye level.</summary>
        public static Vector3 Banner(
            Vector3 eye,
            float yawDeg,
            float distance,
            float elevationDeg
        ) => eye + PanelMath.InFront(yawDeg, distance, -elevationDeg);

        /// <summary>
        /// Offset of slot <paramref name="index"/> of <paramref name="count"/> on an arc of <paramref name="radius"/>
        /// centred on <paramref name="yawDeg"/> (the direction toward the viewer). Slots run left to right as the
        /// viewer sees them.
        /// </summary>
        public static Vector3 ArcSlot(
            int index,
            int count,
            float yawDeg,
            float radius,
            float spacingDeg
        )
        {
            float a = (yawDeg - (index - (count - 1) * 0.5f) * spacingDeg) * UnityQuat.Deg2Rad;
            return new Vector3(MathF.Sin(a) * radius, 0f, MathF.Cos(a) * radius);
        }
    }

    /// <summary>
    /// When button labels show: for a while after they change (an item was drawn, a prompt swapped) and while the
    /// player looks at the controller, with hysteresis so a glance at the edge does not flicker them.
    /// </summary>
    public sealed class LabelReveal
    {
        public float HoldSeconds = 3f;
        public float EnterDegrees = 25f;
        public float ExitDegrees = 35f;

        private float timer;
        private bool looking;

        public bool Visible { get; private set; }

        /// <param name="changed">The labels changed this frame.</param>
        /// <param name="gazeDegrees">Angle between the view direction and the direction to the controller.</param>
        public bool Update(bool changed, float gazeDegrees, float dt)
        {
            timer = changed ? HoldSeconds : MathF.Max(0f, timer - dt);
            looking = gazeDegrees < (looking ? ExitDegrees : EnterDegrees);
            Visible = timer > 0f || looking;
            return Visible;
        }

        public void Reset()
        {
            timer = 0f;
            looking = false;
            Visible = false;
        }
    }

    /// <summary>
    /// Fires once when stamina drops low or a new affliction appears, so the wrist can pulse and the controller tick
    /// while the player's eyes are on the wall. A growing affliction does not fire again; low stamina re-arms only
    /// after a real recovery.
    /// </summary>
    public sealed class StatusAlert
    {
        public float LowStamina = 0.2f;
        public float Rearm = 0.4f;

        /// <summary>A status counts as present above this (statuses run 0..1 per segment).</summary>
        public float Present = 0.02f;
        public float Cooldown = 1f;
        public float PulseSeconds = 0.6f;

        /// <summary>Index of a status that never alerts (one that comes and goes with routine actions), or -1.</summary>
        public int Ignore = -1;

        private float[] last = Array.Empty<float>();
        private bool primed,
            low,
            pending;
        private float sinceAlert = float.MaxValue,
            pulseTime = float.MaxValue;

        /// <summary>0..1 bump that follows each alert for <see cref="PulseSeconds"/>.</summary>
        public float Pulse { get; private set; }

        /// <summary>True on the frame an alert fires. <paramref name="statuses"/> is read, never kept.</summary>
        public bool Update(float stamina, float[] statuses, float dt)
        {
            int n = statuses?.Length ?? 0;
            if (last.Length != n)
            {
                last = new float[n];
                primed = false;
            }

            bool fire = false;
            if (primed)
            {
                if (!low && stamina < LowStamina)
                    fire = true;
                for (int i = 0; i < n; i++)
                {
                    if (i != Ignore && last[i] <= Present && statuses[i] > Present)
                        fire = true;
                }
            }
            if (stamina < LowStamina)
                low = true;
            else if (stamina > Rearm)
                low = false;
            for (int i = 0; i < n; i++)
                last[i] = statuses[i];
            primed = true;

            sinceAlert += dt;
            pulseTime += dt;
            // An alert inside the cooldown waits for it to end instead of being lost.
            pending |= fire;
            fire = pending && sinceAlert >= Cooldown;
            if (fire)
            {
                pending = false;
                sinceAlert = 0f;
                pulseTime = 0f;
            }

            float t = PulseSeconds > 0f ? pulseTime / PulseSeconds : 1f;
            Pulse = t < 1f ? MathF.Sin(t * MathF.PI) : 0f;
            return fire;
        }

        public void Reset()
        {
            primed = false;
            low = false;
            pending = false;
            sinceAlert = pulseTime = float.MaxValue;
            Pulse = 0f;
        }
    }

    /// <summary>The game's item prompts (GUIManager itemPrompt*).</summary>
    public enum ItemPrompt
    {
        Main,
        Secondary,
        Scroll,
        Drop,
        Throw,
    }

    /// <summary>
    /// Where a label for a control sits on a controller, in grip-pose space (x right, y up, z forward along the
    /// controller), and which control each game prompt maps to in the PeakXR layout (docs/controls.md).
    /// Positions are approximate for common controllers and keep the label off the outer side, clear of the item.
    /// </summary>
    public static class ControllerLabels
    {
        /// <summary>Label anchor beside <paramref name="control"/>, mirrored for the left controller.</summary>
        public static Vector3 Offset(VRControl control, bool left)
        {
            Vector3 v;
            switch (control)
            {
                case VRControl.Trigger:
                    v = new Vector3(0.05f, -0.025f, 0.05f);
                    break;
                case VRControl.Grip:
                    v = new Vector3(0.055f, -0.045f, -0.01f);
                    break;
                case VRControl.Primary:
                    v = new Vector3(0.055f, 0.02f, 0f);
                    break;
                case VRControl.Secondary:
                    v = new Vector3(0.055f, 0.035f, 0.025f);
                    break;
                case VRControl.StickUp:
                    v = new Vector3(0f, 0.04f, 0.065f);
                    break;
                case VRControl.StickDown:
                    v = new Vector3(0f, 0.04f, 0.005f);
                    break;
                case VRControl.Menu:
                    v = new Vector3(0.04f, 0.02f, -0.03f);
                    break;
                default:
                    // Stick and stick click: beside the stick.
                    v = new Vector3(0.06f, 0.04f, 0.035f);
                    break;
            }
            if (left)
                v.X = -v.X;
            return v;
        }

        /// <summary>
        /// Control for a game item prompt: use on the item hand's trigger, secondary use on the other trigger, scroll
        /// beside the stick that scrolls, drop on the configured drop button, throw at the item hand's grip (release
        /// while swinging). False when the prompt has no control (drop button set to None).
        /// </summary>
        public static bool ForPrompt(
            ItemPrompt prompt,
            bool itemInLeft,
            bool dominantLeft,
            string dropButton,
            out bool left,
            out VRControl control
        )
        {
            switch (prompt)
            {
                case ItemPrompt.Main:
                    (left, control) = (itemInLeft, VRControl.Trigger);
                    return true;
                case ItemPrompt.Secondary:
                    (left, control) = (!itemInLeft, VRControl.Trigger);
                    return true;
                case ItemPrompt.Throw:
                    (left, control) = (itemInLeft, VRControl.Grip);
                    return true;
                case ItemPrompt.Drop:
                    return ButtonName.TryParse(dropButton, dominantLeft, out left, out control);
                default:
                    bool found = ForAction(
                        VRAction.ScrollForward,
                        dominantLeft,
                        itemInLeft,
                        out left,
                        out _
                    );
                    control = VRControl.StickClick;
                    return found;
            }
        }

        /// <summary>The first binding of <paramref name="action"/> in <see cref="BindingTable.Default"/>.</summary>
        public static bool ForAction(
            VRAction action,
            bool dominantLeft,
            bool itemInLeft,
            out bool left,
            out VRControl control
        )
        {
            foreach (var b in BindingTable.Default)
            {
                if (b.Action != action)
                    continue;
                left = BindingTable.ResolveLeft(b.Hand, dominantLeft, itemInLeft);
                control = b.Control;
                return true;
            }
            left = false;
            control = default;
            return false;
        }
    }
}
