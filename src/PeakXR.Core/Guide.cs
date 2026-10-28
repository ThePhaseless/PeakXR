using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>One row of the VR Guide: a VR mechanic PeakXR adds or changes. PEAK's own gameplay is not taught.</summary>
public enum GuideTopic : byte
{
    Grab,
    TwoHand,
    Throw,
    Eat,
    Holster,
    Backpack,
    Climb,
    Rope,
    Piton,
    Cook,
    Light,
    Lid,
    Bugle,
    Bandage,
    Parasol,
    Turn,
    Move,
    Crouch,
    Emote,
    PauseRecenter,
    Laser,
    Wrist,
    Refused,

    /// <summary>Finger-gun ping: point, then squeeze the trigger.</summary>
    Ping,

    /// <summary>Talk: a free hand at the mouth like a walkie-talkie.</summary>
    Mic,

    /// <summary>Mute: a flat palm over the mouth.</summary>
    Mute,

    /// <summary>Spectate: flick the stick to watch the next or previous teammate.</summary>
    Spectate,

    /// <summary>An item into a taker such as an altar socket: held up under it.</summary>
    Socket,

    /// <summary>A slow grip release drops the held item.</summary>
    ReleaseDrop,

    /// <summary>A refusal out of sight shows its ring at the edge of the view.</summary>
    EdgeRing,

    /// <summary>The first refusal of each kind carries a short label; later ones the ring only.</summary>
    FirstLabel,

    /// <summary>Rope swing: lean along the swing to pump it.</summary>
    RopePump,

    /// <summary>Respawn chest: one hand on the lid is refused, both hands lift it.</summary>
    Chest,

    /// <summary>Stoop to pick an item up from the ground.</summary>
    GroundPickup,

    /// <summary>Grip a hanging friend's hand and pull them up.</summary>
    HelpUp,

    /// <summary>Grip a passed-out friend with both hands to carry them.</summary>
    Carry,

    /// <summary>Hold food at a friend's mouth to feed them.</summary>
    Feed,

    /// <summary>Shake both hands to break out of a web.</summary>
    Struggle,

    /// <summary>Strike a piton with the hammer to set it.</summary>
    Hammer,

    /// <summary>Steer the glider by banking the spread arms.</summary>
    Glider,

    /// <summary>Passed out: both grips and both triggers give up.</summary>
    GiveUp,

    /// <summary>Number of topics; not a topic.</summary>
    Count,
}

/// <summary>The controller part a demo key lights up on the ghost controller, besides grip and trigger travel.</summary>
public enum GuideButton : byte
{
    None,
    Grip,
    Trigger,

    /// <summary>A / X.</summary>
    Lower,

    /// <summary>B / Y.</summary>
    Upper,
    Stick,
    StickClick,
    Menu,
}

/// <summary>What a demo prop looks like. The last three are the affordance cue shapes (docs/research/vr-affordance-audit.md):
/// a breathing double ring where an action works, a ring with a slash where it is refused, a ring with a pin for a notice.</summary>
public enum GuideShape : byte
{
    Ball,
    Rod,
    Wall,
    Rope,
    Fire,
    Box,
    Panel,
    Disc,
    Wheel,
    Laser,
    Target,
    Refused,
    Notice,
}

/// <summary>Where a prop key's position is measured from: the stage, or a hand (the key's position is then an offset).</summary>
public enum GuideAnchor : byte
{
    Free,
    Left,
    Right,
}

/// <summary>One keyframe. Hands and the body read every field; props read <see cref="Pos"/> and <see cref="Anchor"/>.</summary>
public readonly struct GuideKey
{
    public readonly float Time;
    public readonly Vector3 Pos;
    public readonly float Grip;
    public readonly float Trigger;
    public readonly GuideButton Button;
    public readonly Vector2 Stick;
    public readonly float Yaw;
    public readonly float Pitch;
    public readonly float Roll;
    public readonly GuideAnchor Anchor;

    public GuideKey(
        float time,
        Vector3 pos,
        float grip,
        float trigger,
        GuideButton button,
        Vector2 stick,
        float yaw,
        float pitch,
        float roll,
        GuideAnchor anchor
    )
    {
        Time = time;
        Pos = pos;
        Grip = grip;
        Trigger = trigger;
        Button = button;
        Stick = stick;
        Yaw = yaw;
        Pitch = pitch;
        Roll = roll;
        Anchor = anchor;
    }
}

/// <summary>Keyframes sampled with a smoothstep between neighbours; discrete fields (button, anchor) step.</summary>
public sealed class GuideTrack
{
    public readonly GuideKey[] Keys;

    public GuideTrack(params GuideKey[] keys) => Keys = keys;

    /// <summary>The keys around <paramref name="t"/> and the eased fraction between them. Equal times jump to the later key.</summary>
    public void Span(float t, out int a, out int b, out float f)
    {
        var keys = Keys;
        if (t <= keys[0].Time)
        {
            a = b = 0;
            f = 0f;
            return;
        }
        int last = keys.Length - 1;
        if (t >= keys[last].Time)
        {
            a = b = last;
            f = 0f;
            return;
        }
        int i = 0;
        while (i < last && keys[i + 1].Time <= t)
            i++;
        a = i;
        b = i + 1;
        float span = keys[b].Time - keys[a].Time;
        float x = span > 1e-6f ? (t - keys[a].Time) / span : 1f;
        f = x * x * (3f - 2f * x);
    }

    public GuideKey Sample(float t)
    {
        Span(t, out int a, out int b, out float f);
        var k0 = Keys[a];
        var k1 = Keys[b];
        return new GuideKey(
            t,
            Vector3.Lerp(k0.Pos, k1.Pos, f),
            Lerp(k0.Grip, k1.Grip, f),
            Lerp(k0.Trigger, k1.Trigger, f),
            k0.Button,
            Vector2.Lerp(k0.Stick, k1.Stick, f),
            Lerp(k0.Yaw, k1.Yaw, f),
            Lerp(k0.Pitch, k1.Pitch, f),
            Lerp(k0.Roll, k1.Roll, f),
            k0.Anchor
        );
    }

    static float Lerp(float a, float b, float f) => a + (b - a) * f;
}

/// <summary>A shape on the demo stage, shown from <see cref="From"/> to <see cref="To"/> (always when To is 0).</summary>
public sealed class GuideProp
{
    public readonly GuideShape Shape;
    public readonly float Size;
    public readonly GuideTrack Track;
    public readonly float From;
    public readonly float To;

    public GuideProp(GuideShape shape, float size, GuideTrack track, float from = 0f, float to = 0f)
    {
        Shape = shape;
        Size = size;
        Track = track;
        From = from;
        To = to;
    }

    public bool VisibleAt(float t) => t >= From && (To <= 0f || t < To);
}

/// <summary>A cue sound at a moment of the loop, at a hand (plus offset) or a stage point.</summary>
public readonly struct GuideSound
{
    public readonly float Time;
    public readonly CueSound Sound;
    public readonly GuideAnchor Anchor;
    public readonly Vector3 Pos;

    public GuideSound(float time, CueSound sound, GuideAnchor anchor, Vector3 pos)
    {
        Time = time;
        Sound = sound;
        Anchor = anchor;
        Pos = pos;
    }
}

/// <summary>
/// A looping demonstration of one mechanic: a ghost body (head, torso) that can move and turn, two ghost controllers
/// in the body frame, props in the stage frame and cue sounds. Authored for a right-handed player; the runtime mirrors
/// it for <c>Input.DominantHand = Left</c>. Units are metres at real size; the runtime scales the stage down.
/// </summary>
public sealed class GuideDemo
{
    public readonly GuideTopic Topic;
    public readonly string Name;
    public readonly string Caption;
    public readonly float Loop;
    public readonly GuideTrack Body;
    public readonly GuideTrack Left;
    public readonly GuideTrack Right;
    public readonly GuideProp[] Props;
    public readonly GuideSound[] Sounds;

    public GuideDemo(
        GuideTopic topic,
        string name,
        string caption,
        float loop,
        GuideTrack body,
        GuideTrack left,
        GuideTrack right,
        GuideProp[] props,
        GuideSound[] sounds
    )
    {
        Topic = topic;
        Name = name;
        Caption = caption;
        Loop = loop;
        Body = body;
        Left = left;
        Right = right;
        Props = props;
        Sounds = sounds;
    }

    /// <summary>Body offset and yaw at <paramref name="t"/>; zero when the body does not move.</summary>
    public void BodyAt(float t, out Vector3 offset, out float yaw)
    {
        if (Body == null)
        {
            offset = Vector3.Zero;
            yaw = 0f;
            return;
        }
        var k = Body.Sample(t);
        offset = k.Pos;
        yaw = k.Yaw;
    }

    /// <summary>A body-frame point in the stage frame at <paramref name="t"/>.</summary>
    public Vector3 FromBody(Vector3 local, float t)
    {
        BodyAt(t, out var offset, out float yaw);
        return offset + Turn(local, yaw);
    }

    /// <summary>Hand position in the stage frame; zero for a hand the demo does not show.</summary>
    public Vector3 HandPosition(bool left, float t)
    {
        var track = left ? Left : Right;
        return track == null ? Vector3.Zero : FromBody(track.Sample(t).Pos, t);
    }

    /// <summary>Prop position in the stage frame. An anchored key resolves to its hand at <paramref name="t"/> (plus its
    /// offset turned with the body), so a carried prop follows the hand and leaves it smoothly when the next key is free.</summary>
    public Vector3 PropPosition(int index, float t)
    {
        var track = Props[index].Track;
        track.Span(t, out int a, out int b, out float f);
        return Vector3.Lerp(Resolve(track.Keys[a], t), Resolve(track.Keys[b], t), f);
    }

    Vector3 Resolve(in GuideKey key, float t)
    {
        if (key.Anchor == GuideAnchor.Free)
            return key.Pos;
        BodyAt(t, out _, out float yaw);
        return HandPosition(key.Anchor == GuideAnchor.Left, t) + Turn(key.Pos, yaw);
    }

    /// <summary>A sound position in the stage frame.</summary>
    public Vector3 SoundPosition(in GuideSound sound, float t)
    {
        if (sound.Anchor == GuideAnchor.Free)
            return sound.Pos;
        BodyAt(t, out _, out float yaw);
        return HandPosition(sound.Anchor == GuideAnchor.Left, t) + Turn(sound.Pos, yaw);
    }

    /// <summary>True when loop time moved past <paramref name="at"/> between two frames, the loop wrap included.</summary>
    public static bool Crossed(float prev, float now, float at) =>
        now >= prev ? prev < at && at <= now : at > prev || at <= now;

    /// <summary>Unity's yaw convention: positive turns forward (+Z) toward right (+X).</summary>
    public static Vector3 Turn(Vector3 v, float yawDegrees)
    {
        if (yawDegrees == 0f)
            return v;
        float r = yawDegrees * MathF.PI / 180f;
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }
}

/// <summary>
/// Every guide demo. Body frame: origin at the eyes, +X right, +Y up, +Z forward; the floor is 1.6 m down. Captions
/// are optional text (the runtime shows them only with <c>UI.ActionHintText</c>); the motion, the lit button, the cue
/// shapes and sounds carry each demo without them.
/// </summary>
public static class GuideTable
{
    static readonly Vector3 RestL = new(-0.2f, -0.45f, 0.3f);
    static readonly Vector3 RestR = new(0.2f, -0.45f, 0.3f);
    static readonly Vector3 Mouth = new(0f, -0.1f, 0.09f);
    static readonly Vector3 MouthHand = new(0.04f, -0.13f, 0.12f);
    static readonly Vector3 BeltR = new(0.17f, -0.72f, 0.17f);

    static readonly GuideDemo[] all = Build();

    public static GuideDemo Get(GuideTopic topic) => all[(int)topic];

    /// <summary>A key with only the fields a demo needs named.</summary>
    public static GuideKey K(
        float t,
        Vector3 pos,
        float grip = 0f,
        float trigger = 0f,
        GuideButton button = GuideButton.None,
        float stickX = 0f,
        float stickY = 0f,
        float yaw = 0f,
        float pitch = 0f,
        float roll = 0f,
        GuideAnchor anchor = GuideAnchor.Free
    ) => new(t, pos, grip, trigger, button, new Vector2(stickX, stickY), yaw, pitch, roll, anchor);

    static GuideTrack T(params GuideKey[] keys) => new(keys);

    static GuideProp P(GuideShape shape, float size, Vector3 at, float from = 0f, float to = 0f) =>
        new(shape, size, T(K(0f, at)), from, to);

    static GuideProp Held(
        GuideShape shape,
        float size,
        GuideAnchor hand,
        Vector3 offset,
        float to = 0f
    ) => new(shape, size, T(K(0f, offset, anchor: hand)), 0f, to);

    static GuideSound S(float t, CueSound sound, GuideAnchor at, Vector3 pos = default) =>
        new(t, sound, at, pos);

    static GuideSound S(float t, CueSound sound, Vector3 pos) =>
        new(t, sound, GuideAnchor.Free, pos);

    static Vector3 V(float x, float y, float z) => new(x, y, z);

    const GuideAnchor L = GuideAnchor.Left;
    const GuideAnchor R = GuideAnchor.Right;

    static GuideDemo[] Build()
    {
        var d = new GuideDemo[(int)GuideTopic.Count];
        void Add(GuideDemo demo) => d[(int)demo.Topic] = demo;
        var quiet = Array.Empty<GuideSound>();
        var restLeft = T(K(0f, RestL));
        var restRight = T(K(0f, RestR));
        var crate = V(0.2f, -0.96f, 0.45f);

        Add(
            new GuideDemo(
                GuideTopic.Grab,
                "Grab and drop",
                "Grip to pick up. The upper button on that hand drops it.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.7f, crate + V(0f, 0.03f, -0.02f)),
                    K(0.9f, crate + V(0f, 0.03f, -0.02f), grip: 1f),
                    K(1.6f, RestR, grip: 1f),
                    K(2.1f, RestR, grip: 1f),
                    K(2.2f, RestR, grip: 1f, button: GuideButton.Upper),
                    K(2.4f, RestR),
                    K(3f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.5f, V(0.2f, -1.25f, 0.45f)),
                    new GuideProp(
                        GuideShape.Ball,
                        0.08f,
                        T(
                            K(0f, crate),
                            K(0.9f, crate),
                            K(0.9f, Vector3.Zero, anchor: R),
                            K(2.2f, Vector3.Zero, anchor: R),
                            K(2.6f, crate)
                        )
                    ),
                    P(GuideShape.Target, 0.14f, crate, 0f, 0.9f),
                },
                new[] { S(0.9f, CueSound.WouldWork, R) }
            )
        );

        var aimR = V(0.2f, -0.3f, 0.3f);
        var frontGrip = V(0.17f, -0.42f, 0.52f);
        Add(
            new GuideDemo(
                GuideTopic.TwoHand,
                "Two-hand hold",
                "Grip the held item's front with your free hand to aim it.",
                3.2f,
                null,
                T(
                    K(0f, RestL),
                    K(0.9f, frontGrip),
                    K(1f, frontGrip, grip: 1f),
                    K(1.8f, frontGrip + V(0f, 0.15f, 0f), grip: 1f),
                    K(2.5f, frontGrip + V(0f, 0.15f, 0f), grip: 1f),
                    K(2.7f, frontGrip + V(0f, 0.15f, 0f)),
                    K(3.2f, RestL)
                ),
                T(
                    K(0f, RestR, grip: 1f),
                    K(1f, RestR, grip: 1f),
                    K(1.8f, aimR, grip: 1f),
                    K(2.1f, aimR, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(2.3f, aimR, grip: 1f, trigger: 1f),
                    K(2.4f, aimR, grip: 1f),
                    K(3f, RestR, grip: 1f),
                    K(3.2f, RestR, grip: 1f)
                ),
                new[]
                {
                    Held(GuideShape.Rod, 0.42f, R, V(0f, 0f, 0.12f)),
                    new GuideProp(
                        GuideShape.Target,
                        0.1f,
                        T(K(0f, frontGrip - RestR, anchor: R)),
                        0f,
                        1f
                    ),
                },
                new[] { S(1f, CueSound.WouldWork, L) }
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Throw,
                "Throw",
                "Swing your arm and open your hand to throw.",
                2.6f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.6f, V(0.28f, -0.32f, -0.05f), grip: 1f),
                    K(0.9f, V(0.22f, -0.15f, 0.55f), grip: 1f),
                    K(0.95f, V(0.22f, -0.12f, 0.6f)),
                    K(1.6f, RestR),
                    K(2.6f, RestR)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Ball,
                        0.08f,
                        T(
                            K(0f, Vector3.Zero, anchor: R),
                            K(0.92f, Vector3.Zero, anchor: R),
                            K(1.6f, V(0.15f, -0.3f, 1.8f))
                        ),
                        0f,
                        1.9f
                    ),
                },
                new[] { S(0.92f, CueSound.WouldWork, R) }
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Eat,
                "Eat",
                "The trigger won't eat: hold food at your mouth.",
                3.6f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.4f, RestR, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(0.6f, RestR, grip: 1f),
                    K(1.5f, MouthHand, grip: 1f),
                    K(2.5f, MouthHand, grip: 1f),
                    K(3.1f, RestR, grip: 1f),
                    K(3.6f, RestR, grip: 1f)
                ),
                new[]
                {
                    Held(GuideShape.Ball, 0.08f, R, Vector3.Zero, 2.4f),
                    P(GuideShape.Refused, 0.12f, Mouth, 0.4f, 1f),
                    P(GuideShape.Target, 0.12f, Mouth, 1f, 1.5f),
                },
                new[] { S(0.4f, CueSound.Refused, Mouth), S(2.4f, CueSound.WouldWork, Mouth) }
            )
        );

        var slot = BeltR + V(0f, 0.03f, 0.02f);
        Add(
            new GuideDemo(
                GuideTopic.Holster,
                "Belt holster",
                "Open your hand over a belt slot to put the item away.",
                3.4f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.9f, slot, grip: 1f),
                    K(1.1f, slot),
                    K(1.7f, RestR),
                    K(2.2f, slot),
                    K(2.4f, slot, grip: 1f),
                    K(3.1f, RestR, grip: 1f),
                    K(3.4f, RestR, grip: 1f)
                ),
                new[]
                {
                    P(GuideShape.Disc, 0.12f, BeltR),
                    new GuideProp(
                        GuideShape.Ball,
                        0.08f,
                        T(
                            K(0f, Vector3.Zero, anchor: R),
                            K(1.05f, Vector3.Zero, anchor: R),
                            K(1.1f, BeltR),
                            K(2.4f, BeltR),
                            K(2.4f, Vector3.Zero, anchor: R)
                        )
                    ),
                    P(GuideShape.Target, 0.12f, BeltR, 0f, 1.1f),
                },
                new[] { S(1.1f, CueSound.WouldWork, BeltR), S(2.4f, CueSound.WouldWork, BeltR) }
            )
        );

        var shoulder = V(0.22f, -0.05f, -0.15f);
        var wheelAt = V(0.15f, -0.4f, 0.38f);
        var pocket = V(0.27f, -0.33f, 0.38f);
        Add(
            new GuideDemo(
                GuideTopic.Backpack,
                "Backpack",
                "Grip over your shoulder, pull the pack round, pick a pocket.",
                3.4f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.8f, shoulder),
                    K(1f, shoulder, grip: 1f),
                    K(1.6f, wheelAt, grip: 1f),
                    K(2.2f, pocket, grip: 1f),
                    K(2.4f, pocket),
                    K(3.4f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.3f, V(0f, -0.35f, -0.25f)),
                    P(GuideShape.Target, 0.12f, shoulder, 0f, 1f),
                    P(GuideShape.Wheel, 0.25f, wheelAt, 1.6f, 2.4f),
                    P(GuideShape.Notice, 0.08f, pocket, 1.9f, 2.4f),
                },
                new[] { S(1.6f, CueSound.Notice, wheelAt), S(2.4f, CueSound.WouldWork, pocket) }
            )
        );

        Add(
            Climb(
                GuideTopic.Climb,
                "Wall climb",
                "Grip the wall and pull down to climb. Open to let go.",
                0.18f,
                0.4f,
                GuideShape.Wall
            )
        );
        Add(
            Climb(
                GuideTopic.Rope,
                "Rope and vine",
                "Grip a rope or vine and pull hand over hand.",
                0.03f,
                0.35f,
                GuideShape.Rope
            )
        );

        var piton = V(0.15f, -0.15f, 0.42f);
        Add(
            new GuideDemo(
                GuideTopic.Piton,
                "Piton",
                "Grip a piton's handle to hang from it. Open to let go.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.7f, piton - V(0f, 0f, 0.04f)),
                    K(0.8f, piton - V(0f, 0f, 0.04f), grip: 1f),
                    K(2f, piton - V(0f, 0f, 0.04f), grip: 1f),
                    K(2.1f, piton - V(0f, 0f, 0.04f)),
                    K(2.8f, RestR),
                    K(3f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Wall, 2f, V(0f, -0.6f, 0.5f)),
                    P(GuideShape.Rod, 0.14f, piton),
                    P(GuideShape.Target, 0.1f, piton, 0f, 0.8f),
                },
                new[] { S(0.8f, CueSound.WouldWork, R) }
            )
        );

        var crouchBody = T(
            K(0f, Vector3.Zero),
            K(0.6f, V(0f, -0.4f, 0f)),
            K(2.6f, V(0f, -0.4f, 0f)),
            K(3.2f, Vector3.Zero)
        );
        var fire = V(0.05f, -1.45f, 0.6f);
        var overFire = V(0.06f, -0.5f, 0.55f);
        Add(
            new GuideDemo(
                GuideTopic.Cook,
                "Cook",
                "Hold food over a lit campfire until it cooks.",
                3.2f,
                crouchBody,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.8f, overFire, grip: 1f),
                    K(2.5f, overFire, grip: 1f),
                    K(3.2f, RestR, grip: 1f)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.2f, fire - V(0f, 0.1f, 0f)),
                    P(GuideShape.Fire, 0.25f, fire),
                    Held(GuideShape.Ball, 0.07f, R, Vector3.Zero),
                    P(GuideShape.Target, 0.14f, fire + V(0f, 0.55f, 0f), 0f, 0.8f),
                },
                new[] { S(2.2f, CueSound.WouldWork, R) }
            )
        );

        var touch = V(0.05f, -0.95f, 0.42f);
        Add(
            new GuideDemo(
                GuideTopic.Light,
                "Light a fire",
                "Touch a lit torch or flare to a fuse or campfire and hold.",
                3.2f,
                crouchBody,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.9f, touch, grip: 1f),
                    K(2.4f, touch, grip: 1f),
                    K(3.2f, RestR, grip: 1f)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.2f, fire - V(0f, 0.1f, 0f)),
                    Held(GuideShape.Rod, 0.3f, R, V(0f, 0.03f, 0.08f)),
                    Held(GuideShape.Fire, 0.07f, R, V(0f, 0.06f, 0.2f)),
                    P(GuideShape.Target, 0.14f, fire, 0f, 1f),
                    P(GuideShape.Fire, 0.22f, fire, 2.2f),
                },
                new[] { S(2.2f, CueSound.WouldWork, fire) }
            )
        );

        var lidRest = V(0.1f, -1.1f, 0.5f);
        var lidHand = V(0.1f, -0.8f, 0.38f);
        Add(
            new GuideDemo(
                GuideTopic.Lid,
                "Open a suitcase",
                "Grip the lid and lift it slowly to open the case.",
                3.4f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.6f, V(0f, -0.3f, 0f)),
                    K(2.8f, V(0f, -0.3f, 0f)),
                    K(3.4f, Vector3.Zero)
                ),
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.9f, lidHand),
                    K(1f, lidHand, grip: 1f),
                    K(2.2f, lidHand + V(0f, 0.25f, 0f), grip: 1f),
                    K(2.4f, lidHand + V(0f, 0.25f, 0f)),
                    K(3.4f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.35f, V(0.1f, -1.3f, 0.5f)),
                    new GuideProp(
                        GuideShape.Rod,
                        0.35f,
                        T(
                            K(0f, lidRest),
                            K(1f, lidRest),
                            K(1f, V(0f, 0f, 0.12f), anchor: R),
                            K(2.4f, V(0f, 0f, 0.12f), anchor: R),
                            K(2.8f, lidRest)
                        )
                    ),
                    P(GuideShape.Target, 0.12f, lidRest, 0f, 1f),
                },
                new[] { S(2.2f, CueSound.WouldWork, R) }
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Bugle,
                "Bugle",
                "Bugle at your mouth plays. Tilt the bell to change the note.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.8f, MouthHand, grip: 1f),
                    K(1.4f, MouthHand, grip: 1f, pitch: -25f),
                    K(2f, MouthHand, grip: 1f, pitch: 20f),
                    K(2.4f, MouthHand, grip: 1f),
                    K(3f, RestR, grip: 1f)
                ),
                new[]
                {
                    Held(GuideShape.Rod, 0.3f, R, V(0f, 0f, 0.12f)),
                    P(GuideShape.Target, 0.12f, Mouth, 0f, 0.8f),
                },
                new[] { S(1f, CueSound.Notice, Mouth), S(1.8f, CueSound.Notice, Mouth) }
            )
        );

        var forearm = V(-0.12f, -0.4f, 0.3f);
        Add(
            new GuideDemo(
                GuideTopic.Bandage,
                "Bandage",
                "Press a healing item to your chest, forearm or a friend.",
                3f,
                null,
                T(
                    K(0f, RestL),
                    K(0.6f, forearm, roll: 90f),
                    K(2.4f, forearm, roll: 90f),
                    K(3f, RestL)
                ),
                T(
                    K(0f, RestR, grip: 1f),
                    K(1.1f, forearm + V(0.04f, 0.03f, -0.04f), grip: 1f),
                    K(2.2f, forearm + V(0.04f, 0.03f, -0.04f), grip: 1f),
                    K(2.8f, RestR, grip: 1f),
                    K(3f, RestR, grip: 1f)
                ),
                new[]
                {
                    Held(GuideShape.Box, 0.06f, R, Vector3.Zero, 2f),
                    new GuideProp(
                        GuideShape.Target,
                        0.1f,
                        T(K(0f, V(0.04f, 0.03f, -0.04f), anchor: L)),
                        0.6f,
                        1.1f
                    ),
                },
                new[] { S(2f, CueSound.WouldWork, L) }
            )
        );

        var shaft = V(0.19f, -0.6f, 0.3f);
        Add(
            new GuideDemo(
                GuideTopic.Parasol,
                "Parasol",
                "Grip the shaft with your free hand and push up to open.",
                3.2f,
                null,
                T(
                    K(0f, RestL),
                    K(0.8f, shaft),
                    K(0.9f, shaft, grip: 1f),
                    K(1.5f, shaft + V(0f, 0.25f, 0f), grip: 1f),
                    K(1.7f, shaft + V(0f, 0.25f, 0f)),
                    K(2.4f, RestL)
                ),
                T(K(0f, RestR, grip: 1f, pitch: -80f)),
                new[]
                {
                    Held(GuideShape.Rod, 0.7f, R, V(0f, 0.1f, 0f)),
                    new GuideProp(
                        GuideShape.Disc,
                        0.6f,
                        T(K(0f, V(0f, 0.45f, 0.02f), anchor: R)),
                        1.5f
                    ),
                    P(GuideShape.Target, 0.1f, shaft, 0f, 0.9f),
                },
                new[] { S(1.5f, CueSound.Notice, R, V(0f, 0.45f, 0f)) }
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Turn,
                "Turning",
                "Flick the turn stick. Comfort picks snap or smooth turning.",
                3.4f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.5f, Vector3.Zero),
                    K(0.55f, Vector3.Zero, yaw: 30f),
                    K(1.4f, Vector3.Zero, yaw: 30f),
                    K(1.45f, Vector3.Zero, yaw: 60f),
                    K(2.3f, Vector3.Zero, yaw: 60f),
                    K(2.35f, Vector3.Zero, yaw: 30f),
                    K(2.9f, Vector3.Zero, yaw: 30f),
                    K(2.95f, Vector3.Zero),
                    K(3.4f, Vector3.Zero)
                ),
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.4f, RestR, button: GuideButton.Stick, stickX: 1f),
                    K(0.65f, RestR),
                    K(1.3f, RestR, button: GuideButton.Stick, stickX: 1f),
                    K(1.55f, RestR),
                    K(2.2f, RestR, button: GuideButton.Stick, stickX: -1f),
                    K(2.45f, RestR),
                    K(2.8f, RestR, button: GuideButton.Stick, stickX: -1f),
                    K(3.05f, RestR)
                ),
                new[] { P(GuideShape.Disc, 0.6f, V(0f, -1.6f, 0f)) },
                quiet
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Move,
                "Walk and sprint",
                "Push the move stick to walk where you look. Click to sprint.",
                2.8f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.3f, Vector3.Zero),
                    K(2.2f, V(0f, 0f, 0.9f)),
                    K(2.8f, V(0f, 0f, 0.9f))
                ),
                T(
                    K(0f, RestL),
                    K(0.2f, RestL, button: GuideButton.Stick, stickY: 1f),
                    K(1f, RestL, button: GuideButton.StickClick, stickY: 1f),
                    K(1.2f, RestL, button: GuideButton.Stick, stickY: 1f),
                    K(2.2f, RestL, button: GuideButton.Stick, stickY: 1f),
                    K(2.3f, RestL)
                ),
                restRight,
                new[] { P(GuideShape.Disc, 0.5f, V(0f, -1.6f, 0.9f)) },
                quiet
            )
        );

        Add(
            new GuideDemo(
                GuideTopic.Crouch,
                "Crouch",
                "Crouch for real to crouch. The stick click works too.",
                3f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.4f, Vector3.Zero),
                    K(1.1f, V(0f, -0.5f, 0f)),
                    K(1.9f, V(0f, -0.5f, 0f)),
                    K(2.6f, Vector3.Zero),
                    K(3f, Vector3.Zero)
                ),
                restLeft,
                restRight,
                new[] { P(GuideShape.Disc, 0.6f, V(0f, -0.55f, 0f)) },
                quiet
            )
        );

        var wheel = V(-0.2f, -0.45f, 0.32f);
        var slice = wheel + V(0f, 0.15f, 0f);
        Add(
            new GuideDemo(
                GuideTopic.Emote,
                "Emote wheel",
                "Hold the upper button, move your hand to an emote, let go.",
                3f,
                null,
                T(
                    K(0f, RestL),
                    K(0.3f, RestL, button: GuideButton.Upper),
                    K(0.8f, RestL, button: GuideButton.Upper),
                    K(1.2f, RestL + V(0f, 0.15f, 0f), button: GuideButton.Upper),
                    K(2f, RestL + V(0f, 0.15f, 0f), button: GuideButton.Upper),
                    K(2.1f, RestL + V(0f, 0.15f, 0f)),
                    K(3f, RestL)
                ),
                restRight,
                new[]
                {
                    P(GuideShape.Wheel, 0.3f, wheel, 0.4f, 2.1f),
                    P(GuideShape.Notice, 0.07f, slice, 1.2f, 2.1f),
                },
                new[] { S(0.4f, CueSound.Notice, wheel), S(2.1f, CueSound.WouldWork, L) }
            )
        );

        var holdRing = V(0f, -0.25f, 0.45f);
        Add(
            new GuideDemo(
                GuideTopic.PauseRecenter,
                "Pause and recenter",
                "Tap Menu to pause. Hold both sticks in to recenter.",
                3.4f,
                null,
                T(
                    K(0f, RestL),
                    K(0.3f, RestL, button: GuideButton.Menu),
                    K(0.5f, RestL),
                    K(1.6f, RestL, button: GuideButton.StickClick),
                    K(2.7f, RestL),
                    K(3.4f, RestL)
                ),
                T(
                    K(0f, RestR),
                    K(1.6f, RestR, button: GuideButton.StickClick),
                    K(2.7f, RestR),
                    K(3.4f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Panel, 0.5f, V(0f, -0.15f, 0.7f), 0.5f, 1.4f),
                    P(GuideShape.Notice, 0.08f, holdRing, 1.7f, 2.7f),
                },
                new[]
                {
                    S(0.5f, CueSound.Notice, V(0f, -0.15f, 0.7f)),
                    S(2.7f, CueSound.WouldWork, holdRing),
                }
            )
        );

        var button = V(0.08f, -0.2f, 0.79f);
        Add(
            new GuideDemo(
                GuideTopic.Laser,
                "Menus and laser",
                "Point at a menu and pull the trigger to click.",
                2.8f,
                null,
                restLeft,
                T(
                    K(0f, RestR, pitch: -10f),
                    K(1f, RestR, pitch: -10f),
                    K(1.1f, RestR, trigger: 1f, button: GuideButton.Trigger, pitch: -10f),
                    K(1.3f, RestR, pitch: -10f),
                    K(2.8f, RestR, pitch: -10f)
                ),
                new[]
                {
                    P(GuideShape.Panel, 0.6f, V(0f, -0.1f, 0.8f)),
                    Held(GuideShape.Laser, 0.5f, R, Vector3.Zero),
                    P(GuideShape.Notice, 0.06f, button, 0.6f, 1.3f),
                },
                new[] { S(1.1f, CueSound.WouldWork, button) }
            )
        );

        var wrist = V(-0.08f, -0.3f, 0.35f);
        Add(
            new GuideDemo(
                GuideTopic.Wrist,
                "Wrist status",
                "Raise your off-hand wrist to read stamina and status.",
                2.6f,
                null,
                T(K(0f, RestL), K(0.8f, wrist, roll: 90f), K(2f, wrist, roll: 90f), K(2.6f, RestL)),
                restRight,
                new[]
                {
                    new GuideProp(
                        GuideShape.Panel,
                        0.12f,
                        T(K(0f, V(0f, 0.04f, -0.08f), anchor: L)),
                        0.8f,
                        2f
                    ),
                },
                new[] { S(0.8f, CueSound.Notice, L) }
            )
        );

        var far = V(0.2f, -0.25f, 0.98f);
        var reachFar = V(0.2f, -0.25f, 0.6f);
        Add(
            new GuideDemo(
                GuideTopic.Refused,
                "Refused actions",
                "Falling note, slashed ring: no. Rising note, two rings: yes.",
                3.4f,
                T(
                    K(0f, Vector3.Zero),
                    K(1.4f, Vector3.Zero),
                    K(2.2f, V(0f, 0f, 0.4f)),
                    K(3.4f, V(0f, 0f, 0.4f))
                ),
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.7f, reachFar),
                    K(0.8f, reachFar, grip: 1f),
                    K(1.1f, reachFar),
                    K(1.4f, RestR),
                    K(2.6f, V(0.2f, -0.25f, 0.55f)),
                    K(2.7f, V(0.2f, -0.25f, 0.55f), grip: 1f),
                    K(3.2f, V(0.2f, -0.25f, 0.55f), grip: 1f),
                    K(3.4f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Wall, 2f, V(0f, -0.6f, 1f)),
                    P(GuideShape.Refused, 0.12f, far, 0.8f, 1.4f),
                    P(GuideShape.Target, 0.12f, far, 1.4f, 2.7f),
                },
                new[] { S(0.8f, CueSound.Refused, far), S(2.7f, CueSound.WouldWork, R) }
            )
        );

        AddComms(Add, restLeft);
        AddNew(Add, restLeft);

        for (int i = 0; i < d.Length; i++)
            if (d[i] == null)
                throw new InvalidOperationException($"no guide demo for {(GuideTopic)i}");
        return d;
    }

    /// <summary>Ping, talk, mute and spectate: the communication mechanics, with the cues the game gives them.</summary>
    static void AddComms(Action<GuideDemo> add, GuideTrack restLeft)
    {
        // Finger gun pitched down at the floor ahead: the aim line ends where the dot lands, then the squeeze pings.
        var aim = RestR + V(0f, 0f, 0.02f);
        var land = RestR + V(0f, -1.15f, 1.64f);
        add(
            new GuideDemo(
                GuideTopic.Ping,
                "Ping",
                "Point, then squeeze the trigger to ping.",
                2.8f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.4f, aim, grip: 1f, pitch: 35f),
                    K(1.3f, aim, grip: 1f, pitch: 35f),
                    K(1.4f, aim, grip: 1f, trigger: 1f, button: GuideButton.Trigger, pitch: 35f),
                    K(1.7f, aim, grip: 1f, pitch: 35f),
                    K(2.3f, RestR),
                    K(2.8f, RestR)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Laser,
                        2f,
                        T(K(0f, Vector3.Zero, anchor: R)),
                        0.5f,
                        1.7f
                    ),
                    P(GuideShape.Target, 0.1f, land, 0.5f, 1.4f),
                    P(GuideShape.Notice, 0.14f, land, 1.4f, 2.6f),
                },
                new[] { S(1.4f, CueSound.Notice, land) }
            )
        );

        // Walkie-talkie: a closed hand held at the mouth opens the mic with a click; taking it away closes it.
        add(
            new GuideDemo(
                GuideTopic.Mic,
                "Talk",
                "Hold a hand at your mouth to talk.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.9f, MouthHand, grip: 1f),
                    K(2.2f, MouthHand, grip: 1f),
                    K(2.8f, RestR),
                    K(3f, RestR)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Notice,
                        0.06f,
                        T(K(0f, V(0f, 0.08f, 0f), anchor: R)),
                        1.15f,
                        2.3f
                    ),
                },
                new[] { S(1.15f, CueSound.Transmit, R), S(2.3f, CueSound.Transmit, R) }
            )
        );

        // A flat open palm over the mouth mutes while it stays there: a muffled click and a red mic ring.
        var cover = Mouth + V(0.01f, 0f, 0.05f);
        add(
            new GuideDemo(
                GuideTopic.Mute,
                "Mute",
                "Cover your mouth with a flat palm to mute.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.9f, cover, roll: 90f),
                    K(2.2f, cover, roll: 90f),
                    K(2.8f, RestR),
                    K(3f, RestR)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Refused,
                        0.06f,
                        T(K(0f, V(0f, 0.09f, 0f), anchor: R)),
                        1.15f,
                        2.3f
                    ),
                },
                new[] { S(1.15f, CueSound.Cancel, R) }
            )
        );

        // Two teammates ahead; each flick of the stick moves the watched ring to the other one.
        var mateA = V(-0.45f, -0.9f, 1.4f);
        var mateB = V(0.45f, -0.9f, 1.4f);
        var over = V(0f, 0.55f, 0f);
        add(
            new GuideDemo(
                GuideTopic.Spectate,
                "Spectate",
                "Flick the stick up or down to watch another friend.",
                3.2f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.6f, RestR, button: GuideButton.Stick, stickY: 1f),
                    K(0.8f, RestR),
                    K(2.2f, RestR, button: GuideButton.Stick, stickY: -1f),
                    K(2.4f, RestR),
                    K(3.2f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Ball, 0.3f, mateA),
                    P(GuideShape.Ball, 0.3f, mateB),
                    P(GuideShape.Target, 0.12f, mateA + over, 0f, 0.8f),
                    P(GuideShape.Target, 0.12f, mateB + over, 0.8f, 2.4f),
                    P(GuideShape.Target, 0.12f, mateA + over, 2.4f, 3.2f),
                },
                new[]
                {
                    S(0.8f, CueSound.Notice, mateB + over),
                    S(2.4f, CueSound.Notice, mateA + over),
                }
            )
        );
    }

    /// <summary>
    /// The mechanics added after the first guide (review row 12): item takers, release-to-drop, refusals out of view and
    /// their first label, rope pump, the respawn chest, ground pickups, the co-op verbs (help up, carry, feed, struggle,
    /// give up) and the tool gestures (hammer, glider). Each shows the motion and the cue the game gives it.
    /// </summary>
    static void AddNew(Action<GuideDemo> add, GuideTrack restLeft)
    {
        var floor = V(0.2f, -1.56f, 0.4f);

        var socket = V(0.15f, 0.45f, 0.42f);
        var under = V(0.15f, 0.3f, 0.38f);
        add(
            new GuideDemo(
                GuideTopic.Socket,
                "Altar socket",
                "Hold the item up under its socket to place it.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(1.1f, under, grip: 1f),
                    K(1.4f, under, grip: 1f),
                    K(1.5f, under),
                    K(2.4f, RestR),
                    K(3f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.6f, V(0.15f, -0.2f, 0.7f)),
                    new GuideProp(
                        GuideShape.Ball,
                        0.07f,
                        T(
                            K(0f, Vector3.Zero, anchor: R),
                            K(1.4f, Vector3.Zero, anchor: R),
                            K(1.6f, socket)
                        )
                    ),
                    P(GuideShape.Target, 0.12f, socket, 0f, 1.4f),
                },
                new[] { S(1.5f, CueSound.WouldWork, socket) }
            )
        );

        add(
            new GuideDemo(
                GuideTopic.ReleaseDrop,
                "Release to drop",
                "Open your hand slowly to set the item down.",
                2.8f,
                null,
                restLeft,
                T(K(0f, RestR, grip: 1f), K(0.6f, RestR, grip: 1f), K(1.3f, RestR), K(2.8f, RestR)),
                new[]
                {
                    new GuideProp(
                        GuideShape.Ball,
                        0.08f,
                        T(
                            K(0f, Vector3.Zero, anchor: R),
                            K(1.2f, Vector3.Zero, anchor: R),
                            K(1.7f, floor)
                        )
                    ),
                    P(GuideShape.Notice, 0.12f, floor, 1.7f, 2.6f),
                },
                new[] { S(1.7f, CueSound.Notice, floor) }
            )
        );

        var behind = V(0.38f, -0.7f, -0.15f);
        var edge = V(0.3f, -0.12f, 0.45f);
        add(
            new GuideDemo(
                GuideTopic.EdgeRing,
                "Refused out of view",
                "A refusal you can't see rings at the edge of view.",
                2.8f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.8f, behind),
                    K(0.9f, behind, grip: 1f),
                    K(1.3f, behind),
                    K(2f, RestR),
                    K(2.8f, RestR)
                ),
                new[] { P(GuideShape.Refused, 0.08f, edge, 0.9f, 2f) },
                new[] { S(0.9f, CueSound.Refused, R) }
            )
        );

        var far = V(0.2f, -0.25f, 0.98f);
        var reach = V(0.2f, -0.25f, 0.6f);
        add(
            new GuideDemo(
                GuideTopic.FirstLabel,
                "First-time labels",
                "The first refusal of each kind says why, once.",
                3.2f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.6f, reach),
                    K(0.7f, reach, grip: 1f),
                    K(0.9f, reach),
                    K(1.3f, RestR),
                    K(1.9f, reach),
                    K(2f, reach, grip: 1f),
                    K(2.2f, reach),
                    K(2.7f, RestR),
                    K(3.2f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Wall, 2f, V(0f, -0.6f, 1f)),
                    P(GuideShape.Refused, 0.12f, far, 0.7f, 1.3f),
                    P(GuideShape.Panel, 0.14f, far + V(0f, 0.14f, -0.02f), 0.7f, 1.3f),
                    P(GuideShape.Refused, 0.12f, far, 2f, 2.6f),
                },
                new[] { S(0.7f, CueSound.Refused, far), S(2f, CueSound.Refused, far) }
            )
        );

        var ropeL = V(-0.04f, 0.12f, 0.3f);
        var ropeR = V(0.04f, 0.2f, 0.3f);
        add(
            new GuideDemo(
                GuideTopic.RopePump,
                "Rope swing pump",
                "Lean along the swing, in time, to swing higher.",
                3f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.5f, V(0f, 0f, 0.15f)),
                    K(1f, V(0f, 0f, -0.15f)),
                    K(1.5f, V(0f, 0f, 0.2f)),
                    K(2f, V(0f, 0f, -0.2f)),
                    K(2.5f, Vector3.Zero),
                    K(3f, Vector3.Zero)
                ),
                T(K(0f, ropeL, grip: 1f)),
                T(K(0f, ropeR, grip: 1f)),
                new[]
                {
                    new GuideProp(
                        GuideShape.Rope,
                        2.6f,
                        T(
                            K(0f, V(0f, -0.2f, 0.3f)),
                            K(0.5f, V(0f, -0.2f, 0.45f)),
                            K(1f, V(0f, -0.2f, 0.15f)),
                            K(1.5f, V(0f, -0.2f, 0.5f)),
                            K(2f, V(0f, -0.2f, 0.1f)),
                            K(2.5f, V(0f, -0.2f, 0.3f))
                        )
                    ),
                },
                new[] { S(2f, CueSound.WouldWork, R) }
            )
        );

        var lid = V(0f, -1.1f, 0.5f);
        var lidL = V(-0.12f, -0.8f, 0.38f);
        var lidR = V(0.12f, -0.8f, 0.38f);
        var lift = V(0f, 0.25f, 0f);
        add(
            new GuideDemo(
                GuideTopic.Chest,
                "Respawn chest",
                "One hand can't lift it: grip the lid with both.",
                3.6f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.5f, V(0f, -0.3f, 0f)),
                    K(3f, V(0f, -0.3f, 0f)),
                    K(3.6f, Vector3.Zero)
                ),
                T(
                    K(0f, RestL),
                    K(1.2f, RestL),
                    K(1.7f, lidL),
                    K(1.8f, lidL, grip: 1f),
                    K(2.6f, lidL + lift, grip: 1f),
                    K(2.8f, lidL + lift),
                    K(3.6f, RestL)
                ),
                T(
                    K(0f, RestR),
                    K(0.8f, lidR),
                    K(0.9f, lidR, grip: 1f),
                    K(1.8f, lidR, grip: 1f),
                    K(2.6f, lidR + lift, grip: 1f),
                    K(2.8f, lidR + lift),
                    K(3.6f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Box, 0.4f, V(0f, -1.3f, 0.5f)),
                    new GuideProp(
                        GuideShape.Rod,
                        0.4f,
                        T(K(0f, lid), K(1.8f, lid), K(2.6f, lid + lift), K(3.2f, lid))
                    ),
                    P(GuideShape.Refused, 0.12f, lid, 0.9f, 1.6f),
                },
                new[] { S(0.9f, CueSound.Refused, lid), S(2.6f, CueSound.WouldWork, lid) }
            )
        );

        add(
            new GuideDemo(
                GuideTopic.GroundPickup,
                "Pick up from the ground",
                "Stoop and grip an item on the ground.",
                3.2f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.8f, V(0f, -0.6f, 0f)),
                    K(1.8f, V(0f, -0.6f, 0f)),
                    K(2.6f, Vector3.Zero),
                    K(3.2f, Vector3.Zero)
                ),
                restLeft,
                T(
                    K(0f, RestR),
                    K(1.1f, floor + V(0f, 0.6f, 0f)),
                    K(1.2f, floor + V(0f, 0.6f, 0f), grip: 1f),
                    K(2.6f, RestR, grip: 1f),
                    K(3.2f, RestR, grip: 1f)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Ball,
                        0.08f,
                        T(K(0f, floor), K(1.2f, floor), K(1.2f, Vector3.Zero, anchor: R))
                    ),
                    P(GuideShape.Target, 0.12f, floor, 0f, 1.2f),
                },
                new[] { S(1.2f, CueSound.WouldWork, R) }
            )
        );

        var friendHand = V(0.15f, -0.95f, 0.62f);
        add(
            new GuideDemo(
                GuideTopic.HelpUp,
                "Help a friend up",
                "Grip a hanging friend's hand and pull them up.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR),
                    K(0.9f, friendHand),
                    K(1f, friendHand, grip: 1f),
                    K(2f, RestR + V(0f, 0f, 0.1f), grip: 1f),
                    K(2.2f, RestR + V(0f, 0f, 0.1f)),
                    K(3f, RestR)
                ),
                new[]
                {
                    P(GuideShape.Wall, 2f, V(0f, -1.9f, 0.9f)),
                    new GuideProp(
                        GuideShape.Ball,
                        0.1f,
                        T(
                            K(0f, friendHand),
                            K(1f, friendHand),
                            K(1f, Vector3.Zero, anchor: R),
                            K(2.1f, Vector3.Zero, anchor: R),
                            K(2.6f, friendHand)
                        )
                    ),
                    P(GuideShape.Target, 0.12f, friendHand, 0f, 1f),
                },
                new[] { S(1f, CueSound.WouldWork, R) }
            )
        );

        var body = V(0f, -1.45f, 0.6f);
        add(
            new GuideDemo(
                GuideTopic.Carry,
                "Carry a friend",
                "Grip a passed-out friend with both hands to lift.",
                3.4f,
                T(
                    K(0f, Vector3.Zero),
                    K(0.8f, V(0f, -0.6f, 0f)),
                    K(1.6f, V(0f, -0.6f, 0f)),
                    K(2.6f, Vector3.Zero),
                    K(3.4f, Vector3.Zero)
                ),
                T(
                    K(0f, RestL),
                    K(1.1f, body + V(-0.15f, 0.65f, -0.1f)),
                    K(1.2f, body + V(-0.15f, 0.65f, -0.1f), grip: 1f),
                    K(2.6f, RestL, grip: 1f),
                    K(3f, RestL),
                    K(3.4f, RestL)
                ),
                T(
                    K(0f, RestR),
                    K(1.1f, body + V(0.15f, 0.65f, -0.1f)),
                    K(1.2f, body + V(0.15f, 0.65f, -0.1f), grip: 1f),
                    K(2.6f, RestR, grip: 1f),
                    K(3f, RestR),
                    K(3.4f, RestR)
                ),
                new[]
                {
                    new GuideProp(
                        GuideShape.Rod,
                        0.6f,
                        T(
                            K(0f, body),
                            K(1.2f, body),
                            K(1.2f, V(-0.2f, 0f, 0.05f), anchor: R),
                            K(3f, V(-0.2f, 0f, 0.05f), anchor: R),
                            K(3.4f, body)
                        )
                    ),
                    P(GuideShape.Target, 0.12f, body + V(0f, 0.1f, 0f), 0f, 1.2f),
                },
                new[] { S(1.2f, CueSound.WouldWork, R) }
            )
        );

        var friendMouth = V(0.05f, -0.15f, 0.62f);
        add(
            new GuideDemo(
                GuideTopic.Feed,
                "Feed a friend",
                "Hold food at a friend's mouth to feed them.",
                3f,
                null,
                restLeft,
                T(
                    K(0f, RestR, grip: 1f),
                    K(0.9f, friendMouth + V(0f, -0.03f, -0.05f), grip: 1f),
                    K(1.9f, friendMouth + V(0f, -0.03f, -0.05f), grip: 1f),
                    K(2.5f, RestR, grip: 1f),
                    K(3f, RestR, grip: 1f)
                ),
                new[]
                {
                    P(GuideShape.Ball, 0.22f, friendMouth + V(0f, 0.12f, 0.1f)),
                    Held(GuideShape.Ball, 0.08f, R, Vector3.Zero, 1.8f),
                    P(GuideShape.Target, 0.12f, friendMouth, 0f, 0.9f),
                },
                new[] { S(1.8f, CueSound.WouldWork, friendMouth) }
            )
        );

        var shakeL = RestL + V(0f, 0.15f, 0f);
        var shakeR = RestR + V(0f, 0.15f, 0f);
        var side = V(0.14f, 0f, 0f);
        add(
            new GuideDemo(
                GuideTopic.Struggle,
                "Shake off a web",
                "Shake both hands hard to break free.",
                2.6f,
                null,
                T(
                    K(0f, RestL),
                    K(0.3f, shakeL),
                    K(0.5f, shakeL - side),
                    K(0.7f, shakeL + side),
                    K(0.9f, shakeL - side),
                    K(1.1f, shakeL + side),
                    K(1.3f, shakeL - side),
                    K(1.5f, shakeL),
                    K(2.2f, RestL),
                    K(2.6f, RestL)
                ),
                T(
                    K(0f, RestR),
                    K(0.3f, shakeR),
                    K(0.5f, shakeR + side),
                    K(0.7f, shakeR - side),
                    K(0.9f, shakeR + side),
                    K(1.1f, shakeR - side),
                    K(1.3f, shakeR + side),
                    K(1.5f, shakeR),
                    K(2.2f, RestR),
                    K(2.6f, RestR)
                ),
                new[] { P(GuideShape.Disc, 0.7f, V(0f, -0.6f, 0f), 0f, 1.5f) },
                new[] { S(1.5f, CueSound.WouldWork, V(0f, -0.3f, 0.3f)) }
            )
        );

        var spike = V(-0.05f, -0.2f, 0.45f);
        var back = V(0.2f, -0.05f, 0.3f);
        var hit = spike + V(0.06f, 0f, -0.04f);
        add(
            new GuideDemo(
                GuideTopic.Hammer,
                "Hammer a piton",
                "Strike the piton with the hammer to set it.",
                3f,
                null,
                T(K(0f, spike + V(0f, -0.05f, -0.05f), grip: 1f)),
                T(
                    K(0f, back, grip: 1f),
                    K(0.4f, hit, grip: 1f),
                    K(0.8f, back, grip: 1f),
                    K(1.2f, hit, grip: 1f),
                    K(1.6f, back, grip: 1f),
                    K(2f, hit, grip: 1f),
                    K(2.5f, back, grip: 1f),
                    K(3f, back, grip: 1f)
                ),
                new[]
                {
                    P(GuideShape.Wall, 2f, V(0f, -0.6f, 0.5f)),
                    P(GuideShape.Rod, 0.12f, spike),
                    Held(GuideShape.Rod, 0.3f, R, V(0f, 0.08f, 0.05f)),
                },
                new[]
                {
                    S(0.4f, CueSound.EndStop, spike),
                    S(1.2f, CueSound.EndStop, spike),
                    S(2f, CueSound.WouldWork, spike),
                }
            )
        );

        var wingL = V(-0.45f, -0.2f, 0.25f);
        var wingR = V(0.45f, -0.2f, 0.25f);
        var bank = V(0f, 0.15f, 0f);
        add(
            new GuideDemo(
                GuideTopic.Glider,
                "Steer the glider",
                "Grip the wing with both hands; bank your arms to turn.",
                3.6f,
                T(
                    K(0f, Vector3.Zero),
                    K(1f, Vector3.Zero),
                    K(2.2f, Vector3.Zero, yaw: 30f),
                    K(3.6f, Vector3.Zero)
                ),
                T(
                    K(0f, wingL, grip: 1f),
                    K(1f, wingL, grip: 1f),
                    K(1.6f, wingL + bank, grip: 1f),
                    K(2.6f, wingL + bank, grip: 1f),
                    K(3.2f, wingL, grip: 1f),
                    K(3.6f, wingL, grip: 1f)
                ),
                T(
                    K(0f, wingR, grip: 1f),
                    K(1f, wingR, grip: 1f),
                    K(1.6f, wingR - bank, grip: 1f),
                    K(2.6f, wingR - bank, grip: 1f),
                    K(3.2f, wingR, grip: 1f),
                    K(3.6f, wingR, grip: 1f)
                ),
                new[] { Held(GuideShape.Rod, 0.9f, R, V(-0.45f, 0f, 0f)) },
                new[] { S(0.2f, CueSound.WouldWork, V(0f, -0.2f, 0.3f)) }
            )
        );

        add(
            new GuideDemo(
                GuideTopic.GiveUp,
                "Give up",
                "Passed out: hold both grips and triggers to give up.",
                3f,
                T(K(0f, V(0f, -1.1f, 0f))),
                T(
                    K(0f, RestL),
                    K(0.5f, RestL, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(2.2f, RestL, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(2.4f, RestL),
                    K(3f, RestL)
                ),
                T(
                    K(0f, RestR),
                    K(0.5f, RestR, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(2.2f, RestR, grip: 1f, trigger: 1f, button: GuideButton.Trigger),
                    K(2.4f, RestR),
                    K(3f, RestR)
                ),
                new[] { P(GuideShape.Notice, 0.12f, V(0f, -0.15f, 0.5f), 2.2f, 2.9f) },
                new[] { S(2.2f, CueSound.Notice, V(0f, -0.15f, 0.5f)) }
            )
        );
    }

    /// <summary>Hand over hand: each hand grips high and pulls down while the body rises 0.35 m.</summary>
    static GuideDemo Climb(
        GuideTopic topic,
        string name,
        string caption,
        float x,
        float z,
        GuideShape surface
    )
    {
        const float high = 0.1f;
        float rise = 0.35f;
        var r = V(x, high, z);
        var l = V(-x, high, z);
        var down = V(0f, -rise, 0f);
        return new GuideDemo(
            topic,
            name,
            caption,
            2.8f,
            T(
                K(0f, Vector3.Zero),
                K(0.5f, Vector3.Zero),
                K(1.3f, -down),
                K(1.7f, -down),
                K(2.5f, -down * 2f),
                K(2.8f, -down * 2f)
            ),
            T(
                K(0f, RestL),
                K(1.3f, l),
                K(1.4f, l, grip: 1f),
                K(1.7f, l, grip: 1f),
                K(2.5f, l + down, grip: 1f),
                K(2.7f, l + down),
                K(2.8f, RestL)
            ),
            T(
                K(0f, RestR),
                K(0.4f, r),
                K(0.5f, r, grip: 1f),
                K(1.3f, r + down, grip: 1f),
                K(1.7f, r + down, grip: 1f),
                K(1.8f, r + down),
                K(2.5f, RestR),
                K(2.8f, RestR)
            ),
            new[]
            {
                surface == GuideShape.Wall
                    ? P(GuideShape.Wall, 2.4f, V(0f, -0.2f, z + 0.04f))
                    : P(GuideShape.Rope, 2.6f, V(0f, -0.2f, z)),
                P(GuideShape.Target, 0.1f, V(x, high, z), 0f, 0.5f),
                P(GuideShape.Target, 0.1f, V(-x, high + rise, z), 1f, 1.4f),
            },
            new[] { S(0.5f, CueSound.WouldWork, R), S(1.4f, CueSound.WouldWork, L) }
        );
    }
}
