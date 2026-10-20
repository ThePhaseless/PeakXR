using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Voice;

/// <summary>
/// A speaking icon above every remote player who talks, for VR. The game only shows speech on the
/// name tag (UIPlayerNames.cs:46), and that tag needs a look at the player (IsLookedAt.cs:45); with a
/// headset you want to see who is talking wherever they stand. Icons are world-space sprites, billboarded
/// to the HMD and sized by visual angle, so they read the same in both eyes at any distance. They use
/// the name tag's own sprites and its amplitude mapping, and step aside while the name tag is showing.
/// </summary>
internal sealed class SpeakingIndicators
{
    private const float HeightAboveHead = 0.45f;
    private const float TagVisibleAlpha = 0.5f;

    private sealed class Slot
    {
        public Character Character;
        public AnimatedMouth Mouth;
        public GameObject Go;
        public Transform Transform;
        public SpriteRenderer Renderer;
        public readonly VoiceIndicatorFade Fade = new();
        public Sprite Sprite;
        public bool Seen;
    }

    private readonly List<Slot> slots = new();
    private readonly Transform root;
    private Material material;

    public SpeakingIndicators(Transform root) => this.root = root;

    public void Tick(VoiceSettings cfg, VRRig rig, float dt)
    {
        var names = GUIManager.instance != null ? GUIManager.instance.playerNames : null;
        if (!cfg.SpeakingIcons.Value || rig == null || rig.Head == null || names == null)
        {
            HideAll();
            return;
        }

        foreach (var slot in slots)
            slot.Seen = false;

        var head = rig.Head.position;
        var range = cfg.SpeakingIconRange.Value;
        var degrees = cfg.SpeakingIconSize.Value;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var character = all[i];
            if (character == null || character.IsLocal || character.isBot)
                continue;
            var slot = Get(character);
            slot.Seen = true;
            UpdateSlot(slot, names, head, range, degrees, dt);
        }

        // Swap-remove slots of characters that are gone; hide the ones only skipped this frame.
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (slot.Seen)
                continue;
            if (slot.Character == null)
            {
                Object.Destroy(slot.Go);
                slots[i] = slots[slots.Count - 1];
                slots.RemoveAt(slots.Count - 1);
            }
            else
                Hide(slot);
        }
    }

    public void HideAll()
    {
        foreach (var slot in slots)
            Hide(slot);
    }

    private static void UpdateSlot(
        Slot slot,
        UIPlayerNames names,
        Vector3 head,
        float range,
        float degrees,
        float dt
    )
    {
        var character = slot.Character;
        var voice = character.refs.voice;
        // Hidden while the voice is stolen by a raven (VoiceThief.cs:182): an icon on the victim would give
        // the trick away and point at the wrong place.
        var eligible =
            slot.Mouth != null
            && !character.data.dead
            && !character.data.passedOut
            && (voice == null || !voice.IsStolen);
        if (!eligible || TagShowing(names, character))
        {
            Hide(slot);
            return;
        }

        var position = character.Head + Vector3.up * HeightAboveHead;
        var toIcon = position - head;
        var distance = toIcon.magnitude;
        var speaking = slot.Mouth.isSpeaking && distance <= range;
        if (speaking)
        {
            var sprite = PickSprite(names, character, slot.Mouth.amplitudeIndex);
            if (sprite != null)
                slot.Sprite = sprite;
        }

        var alpha = slot.Fade.Update(speaking, dt);
        if (alpha <= 0f || slot.Sprite == null || distance < 0.05f)
        {
            if (slot.Renderer.enabled)
                slot.Renderer.enabled = false;
            return;
        }

        if (slot.Renderer.sprite != slot.Sprite)
            slot.Renderer.sprite = slot.Sprite;
        slot.Renderer.color = new Color(1f, 1f, 1f, alpha);
        slot.Transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(toIcon, Vector3.up)
        );
        var height = slot.Sprite.bounds.size.y;
        var scale = VoiceIndicator.WorldSize(distance, degrees) / (height > 1e-4f ? height : 1f);
        slot.Transform.localScale = new Vector3(scale, scale, scale);
        if (!slot.Renderer.enabled)
            slot.Renderer.enabled = true;
    }

    /// <summary>Same choice as the name tag: muted sprite when the player's volume slider is at 0
    /// (UIPlayerNames.cs:66), else a level sprite (UIPlayerNames.cs:81).</summary>
    private static Sprite PickSprite(UIPlayerNames names, Character character, int amplitude)
    {
        var owner = character.photonView != null ? character.photonView.Owner : null;
        if (owner != null && AudioLevels.GetPlayerLevel(owner.UserId) == 0f)
            return names.mutedAudioSprite;
        var sprites = names.audioSprites;
        var index = VoiceIndicator.SpriteIndex(amplitude, sprites != null ? sprites.Length : 0);
        return index < 0 ? null : sprites[index];
    }

    /// <summary>The vanilla name tag (placed in the world by ui.world) already shows speech while it is up.</summary>
    private static bool TagShowing(UIPlayerNames names, Character character)
    {
        var tags = names.playerNameText;
        if (tags == null)
            return false;
        for (int i = 0; i < tags.Length; i++)
        {
            var tag = tags[i];
            if (
                tag != null
                && tag.characterInteractable != null
                && tag.characterInteractable.character == character
            )
                return tag.gameObject.activeSelf && tag.group.alpha > TagVisibleAlpha;
        }
        return false;
    }

    private static void Hide(Slot slot)
    {
        slot.Fade.Reset();
        if (slot.Renderer.enabled)
            slot.Renderer.enabled = false;
    }

    private Slot Get(Character character)
    {
        foreach (var slot in slots)
        {
            if (slot.Character == character)
                return slot;
        }

        var go = new GameObject("PeakXR_SpeakingIcon");
        go.transform.SetParent(root, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        var shared = SharedMaterial();
        if (shared != null)
            renderer.sharedMaterial = shared;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        var created = new Slot
        {
            Character = character,
            Mouth = character.GetComponent<AnimatedMouth>(),
            Go = go,
            Transform = go.transform,
            Renderer = renderer,
        };
        slots.Add(created);
        return created;
    }

    private Material SharedMaterial()
    {
        if (material == null)
        {
            // Built-in and always included; URP draws it through its unlit fallback pass.
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                material = new Material(shader);
        }
        return material;
    }
}
