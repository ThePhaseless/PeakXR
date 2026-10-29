using PeakXR.UI.Common;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// A few seconds of text in front of the eyes, easing after the head: the item room's refusal and where it was set
/// up. A cue at the hand or at an orb is out of view a moment later, and the room may already be gone. Drawn over
/// level geometry, since the spawn elevator's wall is closer than the text.
/// </summary>
internal sealed class RoomNotice : MonoBehaviour
{
    private const float Seconds = 6f;
    private const float Distance = 1.1f;
    private const float Drop = 0.12f;

    /// <summary>How fast it eases after the head, per second.</summary>
    private const float Follow = 4f;

    private static RoomNotice current;
    private TextMeshProUGUI label;
    private Material onTop;
    private float until;

    /// <summary>Shows <paramref name="text"/> in VR (replacing one on show); nothing in flat play.</summary>
    public static void Show(string text)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || rig.Head == null)
            return;
        if (current == null)
        {
            var go = new GameObject("PeakXR_ItemRoomNotice", typeof(RectTransform));
            DontDestroyOnLoad(go);
            current = go.AddComponent<RoomNotice>();
            current.Build();
        }
        current.label.text = text;
        current.until = Time.unscaledTime + Seconds;
        current.Place(rig.Head, 1f);
    }

    private void Build()
    {
        gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)transform).sizeDelta = new Vector2(700f, 240f);
        transform.localScale = Vector3.one * 0.0012f;
        label = gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 34f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        // The game's prompt font: TMP's default font may not ship with the game.
        var source =
            GUIManager.instance != null && GUIManager.instance.interactName != null
                ? GUIManager.instance.interactName.GetComponentInChildren<TMP_Text>(true)
                : null;
        if (source != null && source.font != null)
            label.font = source.font;
        var mat = label.fontSharedMaterial;
        if (mat != null)
        {
            onTop = new Material(mat) { name = "PeakXR_ItemRoomNotice (on top)" };
            onTop.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            TextHalo.Halo(onTop, light: false, tight: true);
            label.fontSharedMaterial = onTop;
        }
    }

    private void LateUpdate()
    {
        var rig = VRRig.Instance;
        if (Time.unscaledTime > until || rig == null || rig.Head == null)
        {
            Destroy(gameObject);
            return;
        }
        Place(rig.Head, 1f - Mathf.Exp(-Follow * Time.unscaledDeltaTime));
    }

    private void Place(Transform head, float t)
    {
        var target = head.position + head.forward * Distance - Vector3.up * Drop;
        transform.position = Vector3.Lerp(transform.position, target, t);
        transform.rotation = Quaternion.LookRotation(transform.position - head.position);
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;
        if (onTop != null)
            Destroy(onTop);
    }
}
