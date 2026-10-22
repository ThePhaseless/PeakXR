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

        /// <summary>Gap between the target's silhouette and the label's lower edge, in degrees.</summary>
        public const float LabelGapDegrees = 1.5f;

        /// <summary>Most a silhouette lifts a label, in degrees: a target you are up against fills half the view, and
        /// a label above all of it would leave the view; past this it overlaps the target's top instead.</summary>
        public const float MaxSilhouetteDegrees = 15f;

        /// <summary>Most a held item in the way lifts a target label above the target, in degrees.</summary>
        public const float MaxLiftDegrees = 40f;

        /// <summary>Most a hand pushes its button labels aside, in degrees (a hand at the face).</summary>
        public const float MaxHandClearDegrees = 30f;

        private const float LiftStepDegrees = 0.5f;

        /// <inheritdoc cref="TargetLabel(Vector3, Vector3, float, float, Vector3, float)"/>
        public static Vector3 TargetLabel(
            Vector3 center,
            Vector3 eye,
            float radius,
            float halfHeightDegrees
        ) => TargetLabel(center, eye, radius, halfHeightDegrees, default, 0f);

        /// <summary>
        /// Where the centre of an interact target's label goes: just above the target's silhouette as the eye sees it
        /// (its bounding sphere of <paramref name="radius"/>, 0.05..1 m, counted up to
        /// <see cref="MaxSilhouetteDegrees"/>), so the text never crosses the object or the hand reaching into it, at
        /// the depth of the object's front (never further than the target), kept within
        /// <see cref="TargetNear"/>..<see cref="TargetFar"/>. <paramref name="halfHeightDegrees"/> is the angle from
        /// the label's centre down to its lower edge, so the edge, not the centre, clears the silhouette. A sphere at
        /// <paramref name="avoid"/> (the item in your hand: food over a fire) lifts the label further until it clears
        /// that too, up to <see cref="MaxLiftDegrees"/>.
        /// </summary>
        public static Vector3 TargetLabel(
            Vector3 center,
            Vector3 eye,
            float radius,
            float halfHeightDegrees,
            Vector3 avoid,
            float avoidRadius
        )
        {
            float r = Math.Clamp(radius, 0.05f, 1f);
            var to = center - eye;
            float dist = to.Length();
            if (dist < 1e-3f)
                return center + Vector3.UnitY * (r + 0.05f);
            var dir = to / dist;
            // "Up" as the eye sees it: world up square to the sight line; looking straight down, away from you.
            var up = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, dir) * dir;
            if (up.LengthSquared() < 1e-6f)
                up = Vector3.UnitZ - Vector3.Dot(Vector3.UnitZ, dir) * dir;
            up = Vector3.Normalize(up);
            float clear = (LabelGapDegrees + MathF.Max(0f, halfHeightDegrees)) * UnityQuat.Deg2Rad;
            float angle = Silhouette(r, dist) + clear;
            if (avoidRadius > 0f)
            {
                var toAvoid = avoid - eye;
                float avoidDist = toAvoid.Length();
                if (avoidDist > 1e-3f)
                {
                    var avoidDir = toAvoid / avoidDist;
                    float need = Silhouette(avoidRadius, avoidDist) + clear;
                    float limit = MaxLiftDegrees * UnityQuat.Deg2Rad;
                    float step = LiftStepDegrees * UnityQuat.Deg2Rad;
                    while (angle < limit && Between(Tilt(dir, up, angle), avoidDir) < need)
                        angle += step;
                }
            }
            float d = MathF.Min(Math.Clamp(dist - r, TargetNear, TargetFar), dist);
            return eye + Tilt(dir, up, angle) * d;
        }

        /// <summary>
        /// A button label in a column beside a hand, as the eye sees it: pushed to the hand's outer side (the
        /// viewer's right of a right hand) until its near edge clears the hand's silhouette, so it never lands on the
        /// hand mesh, then raised <paramref name="rowDegrees"/> for its row, at the hand's depth.
        /// <paramref name="halfWidthDegrees"/> is half the label's angular width.
        /// </summary>
        public static Vector3 BesideHand(
            Vector3 eye,
            Vector3 hand,
            float handRadius,
            bool right,
            float halfWidthDegrees,
            float rowDegrees
        )
        {
            var to = hand - eye;
            float dist = to.Length();
            if (dist < 1e-3f)
                return hand + (right ? Vector3.UnitX : -Vector3.UnitX) * (handRadius + 0.05f);
            var dir = to / dist;
            var side = Vector3.Cross(Vector3.UnitY, dir);
            // Hand straight below the eye: any side will do.
            side = side.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(side);
            var up = Vector3.Cross(dir, side);
            float h =
                MathF.Min(
                    MathF.Asin(MathF.Min(1f, MathF.Max(0f, handRadius) / dist)),
                    MaxHandClearDegrees * UnityQuat.Deg2Rad
                )
                + (LabelGapDegrees + MathF.Max(0f, halfWidthDegrees)) * UnityQuat.Deg2Rad;
            var flat = Tilt(dir, right ? side : -side, h);
            return eye + Tilt(flat, up, rowDegrees * UnityQuat.Deg2Rad) * dist;
        }

        /// <summary>
        /// Centres of <paramref name="count"/> rows of <paramref name="heights"/> stacked top to bottom with
        /// <paramref name="gap"/> between them, the whole stack centred on 0 (positive is up). Writes into
        /// <paramref name="centres"/>, so a caller per frame allocates nothing.
        /// </summary>
        public static void StackRows(float[] heights, int count, float gap, float[] centres)
        {
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += heights[i] + (i > 0 ? gap : 0f);
            float y = total * 0.5f;
            for (int i = 0; i < count; i++)
            {
                centres[i] = y - heights[i] * 0.5f;
                y -= heights[i] + gap;
            }
        }

        /// <summary>Angular radius of a sphere seen from <paramref name="dist"/>, capped at
        /// <see cref="MaxSilhouetteDegrees"/>.</summary>
        private static float Silhouette(float radius, float dist) =>
            MathF.Min(
                MathF.Asin(MathF.Min(1f, radius / dist)),
                MaxSilhouetteDegrees * UnityQuat.Deg2Rad
            );

        /// <summary><paramref name="dir"/> turned <paramref name="radians"/> toward the perpendicular
        /// <paramref name="toward"/>.</summary>
        private static Vector3 Tilt(Vector3 dir, Vector3 toward, float radians) =>
            dir * MathF.Cos(radians) + toward * MathF.Sin(radians);

        private static float Between(Vector3 a, Vector3 b) =>
            MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));

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

        /// <summary>A status becomes present above this (statuses run 0..1 per segment)...</summary>
        public float Present = 0.02f;

        /// <summary>...and absent again only below this, so one hovering at the edge alerts once.</summary>
        public float Absent = 0.005f;
        public float Cooldown = 1f;
        public float PulseSeconds = 0.6f;

        /// <summary>Index of a status that never alerts (one that comes and goes with routine actions), or -1.</summary>
        public int Ignore = -1;

        private bool[] present = Array.Empty<bool>();
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
            if (present.Length != n)
            {
                present = new bool[n];
                primed = false;
            }

            bool fire = false;
            if (primed)
            {
                if (!low && stamina < LowStamina)
                    fire = true;
                for (int i = 0; i < n; i++)
                {
                    if (i != Ignore && !present[i] && statuses[i] > Present)
                        fire = true;
                }
            }
            if (stamina < LowStamina)
                low = true;
            else if (stamina > Rearm)
                low = false;
            for (int i = 0; i < n; i++)
            {
                if (statuses[i] > Present)
                    present[i] = true;
                else if (statuses[i] < Absent)
                    present[i] = false;
            }
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

        /// <summary>Alert at the next <see cref="Update"/> (cooldown still applies): a refusal about stamina raises
        /// the wrist the same way a low-stamina alert does.</summary>
        public void Kick() => pending = true;

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

    /// <summary>One controller's capacitive touch sensors this frame (Index: trigger, A, B, stick).</summary>
    public struct ControlTouch
    {
        public bool Trigger;
        public bool Primary;
        public bool Secondary;
        public bool Stick;
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
        /// Where <paramref name="control"/> itself is on the controller (a Valve Index, roughly right for others), in
        /// grip-pose space, mirrored for the left controller: the end of a label's leader line.
        /// </summary>
        public static Vector3 Spot(VRControl control, bool left)
        {
            Vector3 v;
            switch (control)
            {
                case VRControl.Trigger:
                    v = new Vector3(0f, -0.015f, 0.05f);
                    break;
                case VRControl.Grip:
                    v = new Vector3(0f, -0.05f, 0.02f);
                    break;
                case VRControl.Primary:
                    v = new Vector3(0.01f, 0.035f, 0.005f);
                    break;
                case VRControl.Secondary:
                    v = new Vector3(0.01f, 0.04f, 0.02f);
                    break;
                case VRControl.StickUp:
                    v = new Vector3(-0.005f, 0.045f, 0.04f);
                    break;
                case VRControl.StickDown:
                    v = new Vector3(-0.005f, 0.045f, 0.025f);
                    break;
                case VRControl.Menu:
                    v = new Vector3(0f, 0.03f, -0.01f);
                    break;
                default:
                    v = new Vector3(-0.005f, 0.045f, 0.032f);
                    break;
            }
            if (left)
                v.X = -v.X;
            return v;
        }

        /// <summary>Short button name shown beside a label, as printed on an Index controller.</summary>
        public static string Glyph(VRControl control) =>
            control switch
            {
                VRControl.Trigger => "TRIGGER",
                VRControl.Grip => "GRIP",
                VRControl.Primary => "A",
                VRControl.Secondary => "B",
                VRControl.StickUp => "STICK UP",
                VRControl.StickDown => "STICK DN",
                VRControl.Menu => "MENU",
                _ => "STICK",
            };

        /// <summary>The finger rests on <paramref name="control"/> (capacitive touch). Never the grip: the fingers wrap
        /// it whenever an item is held.</summary>
        public static bool Touched(VRControl control, ControlTouch touch) =>
            control switch
            {
                VRControl.Trigger => touch.Trigger,
                VRControl.Primary => touch.Primary,
                VRControl.Secondary => touch.Secondary,
                VRControl.StickClick or VRControl.StickUp or VRControl.StickDown => touch.Stick,
                _ => false,
            };

        /// <summary>Opacity of a label: full while its button is touched, dimmed while another button on the same hand
        /// is, a little under full otherwise.</summary>
        public static float Emphasis(bool touched, bool anyOnHand) =>
            touched ? 1f
            : anyOnHand ? 0.4f
            : 0.85f;

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
                    return ForAction(
                        VRAction.UsePrimary,
                        dominantLeft,
                        itemInLeft,
                        out left,
                        out control
                    );
                case ItemPrompt.Secondary:
                    return ForAction(
                        VRAction.UseSecondary,
                        dominantLeft,
                        itemInLeft,
                        out left,
                        out control
                    );
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
