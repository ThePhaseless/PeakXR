using PeakXR.Core;
using PeakXR.UI.Affordances;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Voice;

/// <summary>
/// Your own mic, seen without text: the game's speaking icon (the name tag's level sprites, UIPlayerNames.cs:81) above
/// the hand at your mouth and above the wrist while your voice goes out, and its muted sprite (:66) in red while the
/// palm mutes. Billboarded to the head and sized by visual angle like <see cref="SpeakingIndicators"/>. Local only.
/// </summary>
internal sealed class MicIcon
{
    private const float HandDegrees = 2.5f;
    private const float WristDegrees = 2f;
    private const float AboveHand = 0.08f;
    private const float AboveWrist = 0.05f;

    private static readonly Color OpenColor = new(1f, 1f, 1f, 0.9f);

    private readonly SpriteRenderer hand;
    private readonly SpriteRenderer wrist;

    public MicIcon(Transform root)
    {
        var shader = Shader.Find("Sprites/Default");
        var material = shader != null ? new Material(shader) : null;
        hand = Make("PeakXR_MicIconHand", root, material);
        wrist = Make("PeakXR_MicIconWrist", root, material);
    }

    /// <summary>The glyph drawn now (Off when hidden), for the self-test.</summary>
    public MicGlyph Shown { get; private set; }

    /// <param name="at">The hand at the mouth, or null (a button, or Voice Activation picking up your voice).</param>
    public void Tick(MicGlyph glyph, Transform at, Transform wristHand, Vector3 eye, int amplitude)
    {
        var names = GUIManager.instance != null ? GUIManager.instance.playerNames : null;
        Sprite sprite = null;
        if (glyph != MicGlyph.Off && names != null)
        {
            if (glyph == MicGlyph.Muted)
                sprite = names.mutedAudioSprite;
            else
            {
                var sprites = names.audioSprites;
                int i = VoiceIndicator.SpriteIndex(amplitude, sprites != null ? sprites.Length : 0);
                sprite = i >= 0 ? sprites[i] : null;
            }
        }
        if (sprite == null)
        {
            Hide();
            return;
        }
        var color = glyph == MicGlyph.Muted ? Affordance.DenyColor : OpenColor;
        Place(hand, at, AboveHand, HandDegrees, sprite, color, eye);
        Place(
            wrist,
            wristHand != at ? wristHand : null,
            AboveWrist,
            WristDegrees,
            sprite,
            color,
            eye
        );
        Shown = hand.enabled || wrist.enabled ? glyph : MicGlyph.Off;
    }

    public void Hide()
    {
        hand.enabled = false;
        wrist.enabled = false;
        Shown = MicGlyph.Off;
    }

    private static void Place(
        SpriteRenderer r,
        Transform anchor,
        float above,
        float degrees,
        Sprite sprite,
        Color color,
        Vector3 eye
    )
    {
        if (anchor == null)
        {
            r.enabled = false;
            return;
        }
        var position = anchor.position + Vector3.up * above;
        var toIcon = position - eye;
        float distance = toIcon.magnitude;
        if (distance < 0.05f)
        {
            r.enabled = false;
            return;
        }
        if (r.sprite != sprite)
            r.sprite = sprite;
        r.color = color;
        r.transform.SetPositionAndRotation(position, Quaternion.LookRotation(toIcon, Vector3.up));
        float height = sprite.bounds.size.y;
        float scale = VoiceIndicator.WorldSize(distance, degrees) / (height > 1e-4f ? height : 1f);
        r.transform.localScale = new Vector3(scale, scale, scale);
        r.enabled = true;
    }

    private static SpriteRenderer Make(string name, Transform root, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var r = go.AddComponent<SpriteRenderer>();
        if (material != null)
            r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return r;
    }
}
