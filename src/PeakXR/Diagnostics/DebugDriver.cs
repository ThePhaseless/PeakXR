using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Render;
using PeakXR.Rig;
using PeakXR.UI.Common;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Diagnostics;

/// <summary>
/// The per-frame part of debug mode: the 10 Hz pose recorder (last 30 s, <see cref="Poses"/>), the wrist frame
/// readout (<c>Debug.ShowFrameStats</c>) and the bug report's completion cue. With debug mode off it only polls the
/// report. Allocation-free per frame: samples go into a preallocated ring, the held item's name is read only when the
/// item changes and the readout text changes twice a second.
/// </summary>
internal sealed class DebugDriver : MonoBehaviour
{
    public const float SampleSeconds = 0.1f;
    public const int SampleCount = 300;

    /// <summary>Main thread only.</summary>
    public static readonly Ring<PoseSample> Poses = new(SampleCount);

    /// <summary>How often the trace's privacy names are re-read: players join and leave.</summary>
    private const float TraceNamesSeconds = 2f;

    private float nextSample;
    private float nextTraceNames;
    private Item heldItem;
    private string heldName;

    private GameObject statsRoot;
    private TextMeshProUGUI statsText;
    private GUIManager fontFrom;
    private Material fontClone;
    private float statsSince;
    private int statsFrames;
    private float statsMax;

    private void Update()
    {
        BugReport.Poll();
        if (DebugLog.Verbose && Time.unscaledTime >= nextTraceNames)
        {
            nextTraceNames = Time.unscaledTime + TraceNamesSeconds;
            DebugLog.TraceNames = BugReport.Names();
        }
        bool vr = VRSession.Active;
        if (DebugLog.Verbose && vr && Time.unscaledTime >= nextSample)
        {
            nextSample = Time.unscaledTime + SampleSeconds;
            Sample();
        }
        UpdateStats(DebugLog.Verbose && vr && DebugFeature.ShowFrameStats.Value);
    }

    private void Sample()
    {
        var s = new PoseSample
        {
            Wall = System.DateTime.Now.Ticks,
            T = Time.unscaledTime,
            HeadTracked = VRInput.HeadTracked,
            Head = VRInput.HeadPosition.N(),
            HeadRotation = VRInput.HeadRotation.N(),
            Left = Hand(true),
            Right = Hand(false),
        };
        var c = Character.localCharacter;
        if (c != null)
        {
            var d = c.data;
            s.Grounded = d.isGrounded;
            s.Climbing = d.isClimbingAnything;
            s.Stamina = d.currentStamina;
            if (d.currentItem != heldItem)
            {
                heldItem = d.currentItem;
                heldName = heldItem != null ? heldItem.name : null;
            }
            s.Held = heldName;
        }
        Poses.Add(s);
    }

    private static PoseHand Hand(bool left)
    {
        ref var h = ref VRInput.Hand(left);
        return new PoseHand
        {
            Tracked = h.Tracked,
            Position = h.Position.N(),
            Rotation = h.Rotation.N(),
            Trigger = h.Trigger,
            Grip = h.Grip,
            Stick = new System.Numerics.Vector2(h.Stick.x, h.Stick.y),
            Primary = h.Primary,
            Secondary = h.Secondary,
            StickClick = h.StickClick,
            Menu = h.Menu,
            TriggerTouch = h.TriggerTouch,
            Curls = LocalAvatar.ControllerCurls(left),
        };
    }

    // ---------------------------------------------------------------- wrist frame readout

    private void UpdateStats(bool on)
    {
        var rig = VRRig.Instance;
        var hand = on && rig != null && rig.Head != null ? rig.Hand(VRInput.OffHandLeft) : null;
        if (hand == null)
        {
            if (statsRoot != null && statsRoot.activeSelf)
                statsRoot.SetActive(false);
            statsFrames = 0;
            statsSince = Time.unscaledTime;
            statsMax = 0f;
            return;
        }
        if (statsRoot == null)
            Build();
        EnsureFont();
        if (!statsRoot.activeSelf)
            statsRoot.SetActive(true);
        // On the back of the wrist, a little toward the elbow, turned to the eye.
        var t = statsRoot.transform;
        t.position = hand.TransformPoint(GripAxes.FromHand(new Vector3(0f, 0.045f, -0.09f)));
        var toEye = t.position - rig.Head.position;
        if (toEye.sqrMagnitude > 1e-6f)
            t.rotation = Quaternion.LookRotation(toEye, rig.Head.up);

        float dt = Time.unscaledDeltaTime;
        statsFrames++;
        if (dt > statsMax)
            statsMax = dt;
        float span = Time.unscaledTime - statsSince;
        if (span < 0.5f)
            return;
        float avg = span / Mathf.Max(1, statsFrames) * 1000f;
        statsText.text = System.FormattableString.Invariant(
            $"{statsFrames / span:0} fps  {avg:0.0} ms\nmax {statsMax * 1000f:0.0} ms / {FrameClock.RefreshHz:0} Hz"
        );
        statsFrames = 0;
        statsSince = Time.unscaledTime;
        statsMax = 0f;
    }

    private void Build()
    {
        statsRoot = new GameObject("PeakXR_FrameStats", typeof(RectTransform), typeof(Canvas));
        statsRoot.transform.SetParent(transform, false);
        var canvas = statsRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DrawOrder.HandUi;
        ((RectTransform)statsRoot.transform).sizeDelta = new Vector2(240f, 70f);
        // 0.5 mm per pixel: 24 px text is 12 mm tall, about 1.7 degrees at a raised wrist.
        statsRoot.transform.localScale = Vector3.one * 0.0005f;
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(statsRoot.transform, false);
        ((RectTransform)textGo.transform).sizeDelta = new Vector2(240f, 70f);
        statsText = textGo.AddComponent<TextMeshProUGUI>();
        statsText.fontSize = 24f;
        statsText.alignment = TextAlignmentOptions.Center;
        statsText.textWrappingMode = TextWrappingModes.NoWrap;
        statsText.raycastTarget = false;
        statsText.text = "";
    }

    /// <summary>The game's prompt font, drawn over the hand and forearm like the other hand UI.</summary>
    private void EnsureFont()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui == fontFrom)
            return;
        var source =
            gui.interactName != null
                ? gui.interactName.GetComponentInChildren<TMP_Text>(true)
                : null;
        if (source == null || source.font == null)
            return;
        fontFrom = gui;
        statsText.font = source.font;
        if (fontClone != null)
            Destroy(fontClone);
        var mat = statsText.fontSharedMaterial;
        fontClone = mat != null ? new Material(mat) { name = "PeakXR_FrameStats (on top)" } : null;
        if (fontClone != null)
        {
            fontClone.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            TextHalo.Halo(fontClone, light: false, tight: true);
            statsText.fontSharedMaterial = fontClone;
        }
    }
}
