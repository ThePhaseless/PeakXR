using System.Collections;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Sandbox;
using PeakXR.UI.Affordances;
using PeakXR.UI.Settings;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using Zorro.UI;

namespace PeakXR.UI.Guide;

/// <summary>
/// Plays VR Guide demos beside the settings panel, with the settings previews' timing (<see cref="PreviewClock"/>:
/// a rest of the laser on a row starts it, a click restarts it) and placement (beside the row's right edge, see
/// <see cref="PreviewStage.RowRightEdge"/>). The ghost and its caption are built when a demo starts and destroyed
/// when it ends or the tab closes. Also offers the practice once, beside the title panel on the first VR visit.
/// </summary>
internal sealed class GuideStage : MonoBehaviour
{
    private const float OfferCheckSeconds = 1f;
    private const float OfferSettleSeconds = 3f;

    public static GuideStage Instance { get; private set; }

    private readonly PreviewClock clock = new();
    private readonly Vector3[] corners = new Vector3[4];
    private GhostDemo demo;
    private GuideRow row;
    private GuideRow hoverRow;

    /// <summary>Seconds the laser has rested on <see cref="hoverRow"/>; below 0 when not counting. Kept apart from the
    /// demo clock: a rest that ends without starting a demo must not shorten the one on show.</summary>
    private float hoverDwell = -1f;
    private GameObject caption;
    private TextMeshProUGUI captionText;
    private float nextOfferCheck;
    private float titleSince = -1f;
    private bool offerTried;
    private GhostDemo offerDemo;
    private TouchOrb offerYes,
        offerNo;
    private GameObject offerCaption;
    private MainMenuPageHandler offerHandler;

    /// <summary>The demo on show, for the self-test; null when none.</summary>
    public GhostDemo Demo => demo;

    /// <summary>Why the last demo ended or did not start, for the self-test note; null while none did.</summary>
    public string LastStop { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        End();
        HideOffer();
        if (Instance == this)
            Instance = null;
    }

    private void OnDisable() => End();

    public void Hover(GuideRow target, bool enter)
    {
        if (enter)
        {
            hoverRow = target;
            hoverDwell = 0f;
        }
        else if (hoverRow == target)
        {
            hoverRow = null;
            hoverDwell = -1f;
        }
    }

    /// <summary>Starts <paramref name="target"/>'s demo for <paramref name="loops"/> loops beside its row.</summary>
    public void Play(GuideRow target, int loops)
    {
        if (!VRSession.Active || target.Cell == null)
        {
            LastStop =
                $"{target.Topic} not started (VR {VRSession.Active}, row shown {target.Cell != null})";
            Plugin.Log.LogDebug("[ui.guide] demo " + LastStop);
            return;
        }
        hoverDwell = -1f;
        row = target;
        if (demo == null || !demo.Alive)
            demo = new GhostDemo("PeakXR_GuideDemo", transform);
        demo.Play(target.Topic);
        float seconds = GuideTable.Get(target.Topic).Loop * loops;
        clock.Play(seconds);
        FillCaption(target);
        Plugin.Log.LogDebug($"[ui.guide] demo {target.Topic} started for {seconds:0.0} s");
    }

    private void Update()
    {
        clock.Tick(Time.unscaledDeltaTime);
        if (hoverDwell >= 0f && (hoverDwell += Time.unscaledDeltaTime) >= PreviewClock.HoverDwell)
        {
            hoverDwell = -1f;
            // Resting on the row already playing leaves it running.
            if (hoverRow != null && (hoverRow != row || demo == null))
                Play(hoverRow, GuideRows.DemoLoops);
        }
        CheckOffer();
        TickOffer(Time.unscaledDeltaTime);
        // The comfort-first view lasts while its menu is open; closing the settings returns the tab to normal.
        if (
            GuideRows.ComfortFirst && (GuideRows.Menu == null || !GuideRows.Menu.isActiveAndEnabled)
        )
            GuideRows.ComfortFirst = false;

        var rig = VRRig.Instance;
        var cell = row != null ? row.Cell : null;
        if (demo == null)
            return;
        string stop =
            !clock.Active ? "its loops ran out"
            : !VRSession.Active ? "VR stopped"
            : rig == null || rig.Head == null ? "no rig"
            : cell == null ? "its row was removed"
            : !cell.gameObject.activeInHierarchy ? "its row was hidden"
            : null;
        if (stop != null)
        {
            LastStop = $"{demo.Topic} ended: {stop}";
            Plugin.Log.LogDebug("[ui.guide] demo " + LastStop);
            End();
            return;
        }
        Place((RectTransform)cell.transform);
        demo.Tick(Time.unscaledDeltaTime, clock.Alpha, rig.Head);
        if (captionText != null)
            captionText.alpha = clock.Alpha;
    }

    /// <summary>The ghost stands beside the row's outer edge (<see cref="VRTabGrid.RowOuterEdge"/>), a little toward the
    /// viewer, facing the way you face.</summary>
    private void Place(RectTransform cell)
    {
        VRTabGrid.RowOuterEdge(cell, corners, out var edge, out var outward);
        var facing = Vector3.ProjectOnPlane(cell.forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f)
            facing = Vector3.forward;
        var eye = edge + outward * 0.3f + Vector3.up * 0.15f - cell.forward * 0.08f;
        demo.Root.SetPositionAndRotation(eye, Quaternion.LookRotation(facing, Vector3.up));
        if (caption != null)
            caption.transform.SetPositionAndRotation(eye + Vector3.down * 0.6f, cell.rotation);
    }

    private void FillCaption(GuideRow target)
    {
        // Text is optional: shown only with UI.ActionHintText, like the refusal labels.
        if (AffordanceFeature.ActionHintText == null || !AffordanceFeature.ActionHintText.Value)
            return;
        if (caption == null)
        {
            caption = new GameObject("PeakXR_GuideCaption", typeof(RectTransform));
            caption.transform.SetParent(transform, false);
            WorldSign(caption);
            ((RectTransform)caption.transform).sizeDelta = new Vector2(520f, 90f);
            caption.transform.localScale = Vector3.one * 0.001f;
            captionText = caption.AddComponent<TextMeshProUGUI>();
            captionText.fontSize = 30f;
            captionText.alignment = TextAlignmentOptions.Center;
            captionText.raycastTarget = false;
            var label = target.Cell != null ? target.Cell.m_text : null;
            if (label != null)
                captionText.font = label.font;
        }
        captionText.text = GuideTable.Get(target.Topic).Caption;
    }

    private void End()
    {
        clock.Stop();
        demo?.Destroy();
        demo = null;
        if (caption != null)
            Destroy(caption);
        caption = null;
        captionText = null;
        row = null;
    }

    // ---- the once-only offer ----

    /// <summary>Once a second while not yet offered: the first time VR reaches the title's main page and it has stayed
    /// a moment, show the offer. It is marked offered only when answered (accept or dismiss), so leaving the title
    /// without answering offers it again next launch; <c>offerTried</c> keeps it to once per launch.</summary>
    private void CheckOffer()
    {
        if (offerTried || Time.unscaledTime < nextOfferCheck)
            return;
        nextOfferCheck = Time.unscaledTime + OfferCheckSeconds;
        var offered = GuideFeature.Offered;
        if (offered == null || offered.Value)
        {
            offerTried = true;
            return;
        }
        var handler = VRSession.Active ? MainPage() : null;
        bool ready = PracticeStart.ShouldOffer(
            VRSession.Active,
            offered.Value,
            FeatureManager.IsActive<SelfTestFeature>(),
            handler != null
        );
        if (!ready)
        {
            titleSince = -1f;
            return;
        }
        if (titleSince < 0f)
            titleSince = Time.unscaledTime;
        if (Time.unscaledTime - titleSince < OfferSettleSeconds)
            return;
        offerTried = true;
        ShowOffer(handler);
    }

    private static MainMenuPageHandler MainPage()
    {
        if (!GuideScene.Title || LoadingScreenHandler.loading)
            return null;
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        return IsMainPage(handler) ? handler : null;
    }

    private static bool IsMainPage(MainMenuPageHandler handler) =>
        handler != null
        && handler.currentPage is MainMenuMainPage page
        && page.gameObject.activeInHierarchy;

    /// <summary>
    /// A small spatial offer beside the title panel, never taking over the menu: a ghost reaching for an apple, and
    /// two orbs within reach on the same side, a chevron to practise and a cross to dismiss. A soft bell announces it.
    /// The caption shows only with <c>UI.ActionHintText</c>.
    /// </summary>
    private void ShowOffer(MainMenuPageHandler handler)
    {
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
        {
            offerTried = false;
            return;
        }
        offerHandler = handler;
        var canvas = handler.GetComponentInParent<Canvas>();
        var panel = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
        Vector3 edge,
            right;
        if (panel != null)
            PreviewStage.RowRightEdge(panel, corners, out edge, out right);
        else
        {
            edge = handler.transform.position;
            right = handler.transform.right;
        }
        right = Vector3.ProjectOnPlane(right, Vector3.up).normalized;
        var facing = Vector3.Cross(right, Vector3.up);
        var head = rig.Head;
        var toPanel = Vector3.ProjectOnPlane(edge - head.position, Vector3.up);
        if (Vector3.Dot(facing, toPanel) < 0f)
            facing = -facing;
        var rotation = Quaternion.LookRotation(facing, Vector3.up);

        offerDemo = new GhostDemo("PeakXR_GuideOffer", transform);
        offerDemo.Play(GuideTopic.Grab);
        offerDemo.Root.SetPositionAndRotation(edge + right * 0.3f + Vector3.up * 0.15f, rotation);
        var orbs = head.position + facing * 0.4f + Vector3.down * 0.12f;
        offerYes = new TouchOrb("PeakXR_OfferPractice", cross: false, seconds: 0.6f);
        offerNo = new TouchOrb("PeakXR_OfferDismiss", cross: true, seconds: 0.6f);
        offerYes.Place(orbs + right * 0.28f, rotation);
        offerNo.Place(orbs + right * 0.42f, rotation);
        if (AffordanceFeature.ActionHintText != null && AffordanceFeature.ActionHintText.Value)
        {
            offerCaption = new GameObject("PeakXR_OfferCaption", typeof(RectTransform));
            offerCaption.transform.SetParent(transform, false);
            WorldSign(offerCaption);
            ((RectTransform)offerCaption.transform).sizeDelta = new Vector2(420f, 60f);
            offerCaption.transform.SetPositionAndRotation(
                orbs + right * 0.35f + Vector3.up * 0.08f,
                rotation
            );
            offerCaption.transform.localScale = Vector3.one * 0.0008f;
            var text = offerCaption.AddComponent<TextMeshProUGUI>();
            text.text = "Practise the VR controls?";
            text.fontSize = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }
        CueAudio.Play(CueSound.Notice, offerDemo.Root.position, 1f);
        Plugin.Log.LogInfo("[ui.guide] offered the VR practice (first VR launch)");
    }

    private void TickOffer(float dt)
    {
        if (offerDemo == null)
            return;
        var rig = VRRig.Instance;
        // Gone as soon as the player moves on: another page, a load, or leaving the title.
        if (
            rig == null
            || rig.Head == null
            || !VRSession.Active
            || !GuideScene.Title
            || LoadingScreenHandler.loading
            || !IsMainPage(offerHandler)
        )
        {
            HideOffer();
            return;
        }
        offerDemo.Tick(dt, 1f, rig.Head);
        if (offerNo.Tick(rig, dt))
        {
            GuideFeature.Offered.Value = true;
            CueAudio.Play(CueSound.Notice, offerNo.Position, 0.6f);
            Plugin.Log.LogInfo("[ui.guide] practice offer dismissed");
            HideOffer();
            return;
        }
        if (!offerYes.Tick(rig, dt))
            return;
        GuideFeature.Offered.Value = true;
        CueAudio.Play(CueSound.WouldWork, offerYes.Position, 1f);
        var handler = offerHandler;
        HideOffer();
        StartCoroutine(OpenPractice(handler));
    }

    private void HideOffer()
    {
        offerDemo?.Destroy();
        offerDemo = null;
        offerYes?.Destroy();
        offerYes = null;
        offerNo?.Destroy();
        offerNo = null;
        if (offerCaption != null)
            Destroy(offerCaption);
        offerCaption = null;
        offerHandler = null;
    }

    /// <summary>Accepted: the title settings on the VR tab's comfort-first practice view, as the Practice row opens it.</summary>
    private static IEnumerator OpenPractice(MainMenuPageHandler handler)
    {
        if (handler == null)
            yield break;
        // MainMenuMainPage.SettingsClicked (MainMenuMainPage.cs:218) does the same transition.
        var page = handler.TransistionToPage<MainMenuSettingsPage>(new SetActivePageTransistion());
        yield return null;
        GuideRows.ComfortFirst = true;
        if (!OpenVRTab(page != null ? page.SharedSettingsMenu : null))
        {
            GuideRows.ComfortFirst = false;
            Plugin.Log.LogWarning("[ui.guide] could not open the VR tab for the practice");
        }
    }

    /// <summary>Selects the VR tab of <paramref name="menu"/> and fills it again in the current view.</summary>
    public static bool OpenVRTab(SharedSettingsMenu menu)
    {
        if (menu == null || UISettingsFeature.TabsRef == null)
            return false;
        var tabs = UISettingsFeature.TabsRef(menu);
        if (tabs == null)
            return false;
        foreach (var b in tabs.buttons)
        {
            if (b == null || b.category != VRTab.Category)
                continue;
            tabs.Select(b);
            // Select does nothing when the tab is already selected; refill it for the new view either way.
            menu.ShowSettings(VRTab.Category);
            return true;
        }
        return false;
    }

    /// <summary>A caption sign in the world, sorted over the world's transparents like converted menus
    /// (<see cref="DrawOrder.WorldUi"/>) so smoke or glass nearer by centre never blends over its text.</summary>
    internal static void WorldSign(GameObject go)
    {
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DrawOrder.WorldUi;
    }
}
