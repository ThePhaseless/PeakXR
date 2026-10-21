using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Guide;

/// <summary>
/// Draws one <see cref="GuideDemo"/> in the world: a small ghost body, two ghost controllers whose buttons light up
/// as the demo presses them, the props and the affordance cue shapes, and plays the cue sounds (with a faint tick on
/// the matching real controller). Everything is built when a demo starts and destroyed with <see cref="Destroy"/>;
/// <see cref="Tick"/> only moves what exists, so it does not allocate.
/// </summary>
internal sealed class GhostDemo
{
    /// <summary>World metres per demo metre: the 1.6 m ghost stands about half a metre tall beside the panel.</summary>
    public const float StageScale = 0.32f;

    /// <summary>Ghost controllers are drawn larger than life so their buttons read at that scale.</summary>
    private const float ControllerScale = 1.6f;

    private const int RingPoints = 24;
    private const float LineWidth = 0.003f;
    private static readonly Color Ghost = new(0.85f, 0.92f, 1f, 0.32f);
    private static readonly Color Lit = new(0.45f, 0.85f, 1f, 0.95f);
    private static readonly Color PropColor = new(0.85f, 0.78f, 0.65f, 0.6f);
    private static readonly Color FireColor = new(1f, 0.55f, 0.15f, 0.85f);
    private static readonly Vector3 Neck = new(0f, -0.15f, 0f);
    private static readonly Vector3 Hips = new(0f, -0.8f, 0f);
    private static readonly Vector3 ShoulderL = new(-0.18f, -0.22f, 0f);
    private static readonly Vector3 ShoulderR = new(0.18f, -0.22f, 0f);
    private static Mesh sphere,
        cube;

    private sealed class Pad
    {
        public Transform Body;
        public readonly Renderer[] Dots = new Renderer[(int)GuideButton.Menu + 1];
        public readonly bool[] On = new bool[(int)GuideButton.Menu + 1];
        public Vector3 StickRest;
    }

    private sealed class PropView
    {
        public GuideProp Prop;
        public Transform Mesh;
        public LineRenderer A,
            B;
    }

    private readonly Transform root;
    private readonly List<Object> owned = new();
    private readonly Material ghost,
        lit,
        prop,
        fire,
        hint,
        deny;
    private readonly Pad left,
        right;
    private readonly Transform head,
        nose;
    private readonly LineRenderer torso,
        armL,
        armR;
    private readonly List<PropView> props = new();
    private GuideDemo demo;
    private float elapsed;
    private float prevPhase;
    private float lastAlpha = -1f;
    private bool mirror;

    public GhostDemo(string name, Transform parent)
    {
        root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localScale = Vector3.one * StageScale;
        ghost = Own(UIMaterials.Create(Ghost, drawOnTop: false, renderQueue: 3100));
        lit = Own(UIMaterials.Create(Lit, drawOnTop: false, renderQueue: 3110));
        prop = Own(UIMaterials.Create(PropColor, drawOnTop: false, renderQueue: 3090));
        fire = Own(UIMaterials.Create(FireColor, drawOnTop: false, renderQueue: 3095));
        hint = Own(UIMaterials.Create(Affordance.HintColor, drawOnTop: false, renderQueue: 3120));
        deny = Own(UIMaterials.Create(Affordance.DenyColor, drawOnTop: false, renderQueue: 3120));
        head = MeshChild("Head", Sphere(), ghost, root, Vector3.one * 0.2f);
        nose = MeshChild("Nose", Sphere(), ghost, root, Vector3.one * 0.05f);
        torso = NewLine("Torso", 2, false, ghost);
        armL = NewLine("ArmL", 2, false, ghost);
        armR = NewLine("ArmR", 2, false, ghost);
        left = NewPad("PadL", true);
        right = NewPad("PadR", false);
    }

    public Transform Root => root;

    public bool Alive => root != null;

    public GuideTopic Topic => demo != null ? demo.Topic : GuideTopic.Count;

    /// <summary>Cue sounds the game's SFX player took since the demo started, for the self-test.</summary>
    public int SoundsPlayed { get; private set; }

    /// <summary>Starts <paramref name="topic"/> from its first frame, replacing the props of the last demo.</summary>
    public void Play(GuideTopic topic)
    {
        foreach (var view in props)
        {
            if (view.Mesh != null)
                Object.Destroy(view.Mesh.gameObject);
            if (view.A != null)
                Object.Destroy(view.A.gameObject);
            if (view.B != null)
                Object.Destroy(view.B.gameObject);
        }
        props.Clear();
        demo = GuideTable.Get(topic);
        elapsed = 0f;
        prevPhase = -1f;
        SoundsPlayed = 0;
        foreach (var p in demo.Props)
            props.Add(BuildProp(p));
        left.Body.gameObject.SetActive(demo.Left != null || demo.Right != null);
        right.Body.gameObject.SetActive(demo.Left != null || demo.Right != null);
    }

    /// <summary>Advances the loop and poses everything. <paramref name="eye"/> faces the cue rings toward the viewer.</summary>
    public void Tick(float dt, float alpha, Transform eye)
    {
        if (demo == null || root == null)
            return;
        mirror = VRInput.LeftHanded;
        elapsed += dt;
        float t = elapsed % demo.Loop;
        SetAlpha(alpha);

        demo.BodyAt(t, out var bodyN, out float yaw);
        var body = Map(bodyN.ToUnity());
        if (mirror)
            yaw = -yaw;
        var bodyRot = Quaternion.Euler(0f, yaw, 0f);
        head.localPosition = body;
        nose.localPosition = body + bodyRot * new Vector3(0f, -0.02f, 0.11f);
        SetLine(torso, World(body + bodyRot * Neck), World(body + bodyRot * Hips));

        // Authored for the right hand as dominant: mirrored, the authored right hand is the player's left.
        var shownLeft = mirror ? demo.Right : demo.Left;
        var shownRight = mirror ? demo.Left : demo.Right;
        PosePad(left, shownLeft, t, bodyRot, body + bodyRot * ShoulderL, armL);
        PosePad(right, shownRight, t, bodyRot, body + bodyRot * ShoulderR, armR);

        for (int i = 0; i < props.Count; i++)
            PoseProp(props[i], i, t, eye);

        for (int i = 0; i < demo.Sounds.Length; i++)
        {
            ref readonly var s = ref demo.Sounds[i];
            if (!GuideDemo.Crossed(prevPhase, t, s.Time))
                continue;
            var at = World(Map(demo.SoundPosition(s, t).ToUnity()));
            if (CueAudio.Play(s.Sound, at, 0.8f))
                SoundsPlayed++;
            if (s.Anchor != GuideAnchor.Free)
                UIHaptics.Tick((s.Anchor == GuideAnchor.Left) != mirror, 0.12f, 0.03f);
        }
        prevPhase = t;
    }

    public void Destroy()
    {
        if (root != null)
            Object.Destroy(root.gameObject);
        foreach (var o in owned)
            if (o != null)
                Object.Destroy(o);
        owned.Clear();
        props.Clear();
        demo = null;
    }

    // ---- posing ----

    private Vector3 Map(Vector3 v)
    {
        if (mirror)
            v.x = -v.x;
        return v;
    }

    private Vector3 World(Vector3 local) => root.TransformPoint(local);

    private void PosePad(
        Pad pad,
        GuideTrack track,
        float t,
        Quaternion bodyRot,
        Vector3 shoulder,
        LineRenderer arm
    )
    {
        bool shown = track != null;
        if (pad.Body.gameObject.activeSelf != shown)
            pad.Body.gameObject.SetActive(shown);
        if (arm.gameObject.activeSelf != shown)
            arm.gameObject.SetActive(shown);
        if (!shown)
            return;
        var k = track.Sample(t);
        demo.BodyAt(t, out var bodyN, out _);
        var local = Map(bodyN.ToUnity()) + bodyRot * Map(k.Pos.ToUnity());
        float roll = mirror ? -k.Roll : k.Roll;
        pad.Body.localPosition = local;
        pad.Body.localRotation = bodyRot * Quaternion.Euler(k.Pitch, 0f, roll);
        SetLine(arm, World(shoulder), World(local));

        Light(pad, GuideButton.Grip, k.Grip > 0.5f || k.Button == GuideButton.Grip);
        Light(pad, GuideButton.Trigger, k.Trigger > 0.5f || k.Button == GuideButton.Trigger);
        Light(pad, GuideButton.Lower, k.Button == GuideButton.Lower);
        Light(pad, GuideButton.Upper, k.Button == GuideButton.Upper);
        Light(pad, GuideButton.Menu, k.Button == GuideButton.Menu);
        Light(pad, GuideButton.Stick, k.Button is GuideButton.Stick or GuideButton.StickClick);
        var stick = pad.Dots[(int)GuideButton.Stick].transform;
        stick.localPosition =
            pad.StickRest
            + new Vector3(Map(new Vector3(k.Stick.X, 0f, 0f)).x, 0f, k.Stick.Y) * 0.008f;
    }

    private void Light(Pad pad, GuideButton button, bool on)
    {
        int i = (int)button;
        if (pad.On[i] == on)
            return;
        pad.On[i] = on;
        var dot = pad.Dots[i];
        dot.sharedMaterial = on ? lit : ghost;
        dot.transform.localScale = Vector3.one * (on ? 0.02f : 0.013f);
    }

    private void PoseProp(PropView view, int index, float t, Transform eye)
    {
        var p = view.Prop;
        bool visible = p.VisibleAt(t);
        SetActive(view, visible);
        if (!visible)
            return;
        var local = Map(demo.PropPosition(index, t).ToUnity());
        var world = World(local);
        var anchor = p.Track.Sample(t).Anchor;
        Quaternion rotation = root.rotation;
        if (anchor != GuideAnchor.Free)
        {
            bool authoredLeft = anchor == GuideAnchor.Left;
            rotation = (authoredLeft != mirror ? left : right).Body.rotation;
        }
        float scale = StageScale;
        switch (p.Shape)
        {
            case GuideShape.Fire:
                view.Mesh.position = world;
                view.Mesh.localScale =
                    Vector3.one * (p.Size * (1f + 0.12f * Mathf.Sin(elapsed * 9f)));
                break;
            case GuideShape.Rod:
            case GuideShape.Ball:
            case GuideShape.Box:
            case GuideShape.Wall:
            case GuideShape.Rope:
            case GuideShape.Panel:
                view.Mesh.SetPositionAndRotation(world, rotation);
                break;
            case GuideShape.Disc:
                Ring(view.A, world, root.up, p.Size * 0.5f * scale);
                break;
            case GuideShape.Wheel:
                Ring(view.A, world, root.forward, p.Size * 0.5f * scale);
                break;
            case GuideShape.Laser:
                SetLine(view.A, world, world + rotation * Vector3.forward * (p.Size * scale));
                break;
            default:
                PoseCue(view, world, eye, p.Size * 0.5f * scale);
                break;
        }
    }

    /// <summary>The affordance shapes: double ring breathing (works), ring and slash (refused), ring and pin (notice).</summary>
    private void PoseCue(PropView view, Vector3 center, Transform eye, float radius)
    {
        var toEye = eye != null ? eye.position - center : -root.forward;
        if (toEye.sqrMagnitude < 1e-6f)
            toEye = -root.forward;
        var normal = toEye.normalized;
        var up = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (up.sqrMagnitude < 1e-4f)
            up = Vector3.ProjectOnPlane(root.forward, normal);
        up.Normalize();
        switch (view.Prop.Shape)
        {
            case GuideShape.Target:
                float breathe = CueMotion.Breathe(elapsed);
                Ring(view.A, center, normal, radius * breathe);
                Ring(view.B, center, normal, radius * 1.4f * breathe);
                break;
            case GuideShape.Refused:
                Ring(view.A, center, normal, radius);
                var across = Vector3.Cross(normal, up) * radius * 0.7f;
                SetLine(
                    view.B,
                    center - across - up * radius * 0.7f,
                    center + across + up * radius * 0.7f
                );
                break;
            default:
                Ring(view.A, center, normal, radius);
                SetLine(view.B, center + up * radius, center + up * radius * 1.8f);
                break;
        }
    }

    private static void Ring(LineRenderer line, Vector3 center, Vector3 normal, float radius)
    {
        var a = Vector3
            .Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward)
            .normalized;
        var b = Vector3.Cross(normal, a);
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = i * Mathf.PI * 2f / RingPoints;
            line.SetPosition(i, center + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
        }
    }

    private static void SetLine(LineRenderer line, Vector3 from, Vector3 to)
    {
        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    private static void SetActive(PropView view, bool on)
    {
        if (view.Mesh != null && view.Mesh.gameObject.activeSelf != on)
            view.Mesh.gameObject.SetActive(on);
        if (view.A != null && view.A.gameObject.activeSelf != on)
            view.A.gameObject.SetActive(on);
        if (view.B != null && view.B.gameObject.activeSelf != on)
            view.B.gameObject.SetActive(on);
    }

    private void SetAlpha(float alpha)
    {
        if (Mathf.Abs(alpha - lastAlpha) < 0.01f)
            return;
        lastAlpha = alpha;
        Tint(ghost, Ghost, alpha);
        Tint(lit, Lit, alpha);
        Tint(prop, PropColor, alpha);
        Tint(fire, FireColor, alpha);
        Tint(hint, Affordance.HintColor, alpha);
        Tint(deny, Affordance.DenyColor, alpha);
    }

    private static void Tint(Material m, Color c, float alpha)
    {
        c.a *= alpha;
        m.color = c;
    }

    // ---- building (once per demo) ----

    private PropView BuildProp(GuideProp p)
    {
        var view = new PropView { Prop = p };
        var s = p.Size;
        switch (p.Shape)
        {
            case GuideShape.Ball:
                view.Mesh = MeshChild("Ball", Sphere(), prop, root, Vector3.one * s);
                break;
            case GuideShape.Fire:
                view.Mesh = MeshChild("Fire", Sphere(), fire, root, Vector3.one * s);
                break;
            case GuideShape.Rod:
                view.Mesh = MeshChild("Rod", Sphere(), prop, root, new Vector3(0.035f, 0.035f, s));
                break;
            case GuideShape.Box:
                view.Mesh = MeshChild("Box", Cube(), prop, root, Vector3.one * s);
                break;
            case GuideShape.Wall:
                view.Mesh = MeshChild("Wall", Cube(), prop, root, new Vector3(s, s, 0.03f));
                break;
            case GuideShape.Rope:
                view.Mesh = MeshChild("Rope", Cube(), prop, root, new Vector3(0.025f, s, 0.025f));
                break;
            case GuideShape.Panel:
                view.Mesh = MeshChild(
                    "Panel",
                    Cube(),
                    ghost,
                    root,
                    new Vector3(s, s * 0.6f, 0.01f)
                );
                break;
            case GuideShape.Disc:
            case GuideShape.Wheel:
                view.A = NewLine("Ring", RingPoints, true, ghost);
                break;
            case GuideShape.Laser:
                view.A = NewLine("Laser", 2, false, lit);
                break;
            case GuideShape.Target:
                view.A = NewLine("Inner", RingPoints, true, hint);
                view.B = NewLine("Outer", RingPoints, true, hint);
                break;
            case GuideShape.Refused:
                view.A = NewLine("Ring", RingPoints, true, deny);
                view.B = NewLine("Slash", 2, false, deny);
                break;
            default:
                view.A = NewLine("Ring", RingPoints, true, hint);
                view.B = NewLine("Pin", 2, false, hint);
                break;
        }
        SetActive(view, false);
        return view;
    }

    private Pad NewPad(string name, bool isLeft)
    {
        var pad = new Pad();
        // The pad's own scale undoes the stage scale, so the controller and its buttons stay readable.
        pad.Body = MeshChild(
            name,
            Sphere(),
            ghost,
            root,
            Vector3.one * (ControllerScale / StageScale)
        );
        var shell = MeshChild("Shell", Sphere(), ghost, pad.Body, new Vector3(0.04f, 0.035f, 0.1f));
        shell.localPosition = Vector3.zero;
        // Grip on the inside of the hand, buttons on top, trigger under the front.
        float inner = isLeft ? 1f : -1f;
        Dot(pad, GuideButton.Grip, new Vector3(0.022f * inner, -0.008f, -0.005f));
        Dot(pad, GuideButton.Trigger, new Vector3(0f, -0.016f, 0.035f));
        Dot(pad, GuideButton.Stick, new Vector3(0.008f * inner, 0.02f, 0.01f));
        Dot(pad, GuideButton.Upper, new Vector3(-0.01f * inner, 0.02f, 0.015f));
        Dot(pad, GuideButton.Lower, new Vector3(-0.01f * inner, 0.02f, -0.002f));
        Dot(pad, GuideButton.Menu, new Vector3(0f, 0.019f, -0.03f));
        pad.StickRest = pad.Dots[(int)GuideButton.Stick].transform.localPosition;
        // The shell sphere is the controller; the parent's own sphere would double it.
        pad.Body.GetComponent<MeshRenderer>().enabled = false;
        return pad;
    }

    private void Dot(Pad pad, GuideButton button, Vector3 at)
    {
        var t = MeshChild(button.ToString(), Sphere(), ghost, pad.Body, Vector3.one * 0.013f);
        t.localPosition = at;
        pad.Dots[(int)button] = t.GetComponent<MeshRenderer>();
    }

    private LineRenderer NewLine(string name, int points, bool loop, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points;
        line.loop = loop;
        line.startWidth = line.endWidth = LineWidth;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        return line;
    }

    private static Transform MeshChild(
        string name,
        Mesh mesh,
        Material material,
        Transform parent,
        Vector3 scale
    )
    {
        var t = UIMaterials.MeshObject(name, mesh, material, parent).transform;
        t.localScale = scale;
        return t;
    }

    private Material Own(Material m)
    {
        owned.Add(m);
        return m;
    }

    private static Mesh Sphere() => sphere != null ? sphere : sphere = UIMaterials.SphereMesh();

    private static Mesh Cube()
    {
        if (cube != null)
            return cube;
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = primitive.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(primitive);
        return cube;
    }
}
