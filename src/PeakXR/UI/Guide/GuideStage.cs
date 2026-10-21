using System.Collections;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Sandbox;
using PeakXR.UI.Affordances;
using PeakXR.UI.Settings;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.UI;

namespace PeakXR.UI.Guide;

/// <summary>
/// Plays VR Guide demos beside the settings panel, with the settings previews' timing (<see cref="PreviewClock"/>:
/// a rest of the laser on a row starts it, a click restarts it) and placement (beside the row's right edge, see
/// <see cref="PreviewStage.RowRightEdge"/>). The ghost and its caption are built when a demo starts and destroyed
/// when it ends or the tab closes. Also offers the practice once, on the first VR visit to the title.
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
    private GameObject caption;
    private TextMeshProUGUI captionText;
    private float nextOfferCheck;
    private float titleSince = -1f;
    private bool offerTried;

    /// <summary>The demo on show, for the self-test; null when none.</summary>
    public GhostDemo Demo => demo;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        End();
        if (Instance == this)
            Instance = null;
    }

    private void OnDisable() => End();

    public void Hover(GuideRow target, bool enter)
    {
        if (enter)
        {
            hoverRow = target;
            clock.HoverEnter();
        }
        else if (hoverRow == target)
        {
            hoverRow = null;
            clock.HoverExit();
        }
    }

    /// <summary>Starts <paramref name="target"/>'s demo for <paramref name="loops"/> loops beside its row.</summary>
    public void Play(GuideRow target, int loops)
    {
        if (!VRSession.Active || target.Cell == null)
            return;
        row = target;
        if (demo == null || !demo.Alive)
            demo = new GhostDemo("PeakXR_GuideDemo", transform);
        demo.Play(target.Topic);
        clock.Play(GuideTable.Get(target.Topic).Loop * loops);
        FillCaption(target);
    }

    private void Update()
    {
        if (clock.Tick(Time.unscaledDeltaTime) && hoverRow != null)
            Play(hoverRow, GuideRows.DemoLoops);
        CheckOffer();
        // The comfort-first view lasts while its menu is open; closing the settings returns the tab to normal.
        if (
            GuideRows.ComfortFirst && (GuideRows.Menu == null || !GuideRows.Menu.isActiveAndEnabled)
        )
            GuideRows.ComfortFirst = false;

        var rig = VRRig.Instance;
        var cell = row != null ? row.Cell : null;
        if (
            demo == null
            || !clock.Active
            || !VRSession.Active
            || rig == null
            || rig.Head == null
            || cell == null
            || !cell.gameObject.activeInHierarchy
        )
        {
            if (demo != null)
                End();
            return;
        }
        Place((RectTransform)cell.transform);
        demo.Tick(Time.unscaledDeltaTime, clock.Alpha, rig.Head);
        if (captionText != null)
            captionText.alpha = clock.Alpha;
    }

    /// <summary>The ghost stands beside the row's right edge, a little toward the viewer, facing the way you face.</summary>
    private void Place(RectTransform cell)
    {
        PreviewStage.RowRightEdge(cell, corners, out var edge, out var right);
        var facing = Vector3.ProjectOnPlane(cell.forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f)
            facing = Vector3.forward;
        var eye = edge + right * 0.3f + Vector3.up * 0.15f - cell.forward * 0.08f;
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
            caption.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
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

    /// <summary>Once a second while not yet offered: the first time VR reaches the title's main page and it has been
    /// up a moment, open the settings on the comfort-first practice view. "Not now" there leaves the guide's Practice
    /// row for later.</summary>
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
        StartCoroutine(Offer(handler));
    }

    private static MainMenuPageHandler MainPage()
    {
        if (
            SceneManager.GetActiveScene().name != MainMenu.SceneName
            || LoadingScreenHandler.loading
        )
            return null;
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        return
            handler != null
            && handler.currentPage is MainMenuMainPage page
            && page.gameObject.activeInHierarchy
            ? handler
            : null;
    }

    private static IEnumerator Offer(MainMenuPageHandler handler)
    {
        // MainMenuMainPage.SettingsClicked (MainMenuMainPage.cs:215) does the same transition.
        var page = handler.TransistionToPage<MainMenuSettingsPage>(new SetActivePageTransistion());
        yield return null;
        GuideRows.ComfortFirst = true;
        if (!OpenVRTab(page != null ? page.SharedSettingsMenu : null))
        {
            GuideRows.ComfortFirst = false;
            Plugin.Log.LogWarning("[ui.guide] could not open the VR tab to offer the practice");
            yield break;
        }
        GuideFeature.Offered.Value = true;
        Plugin.Log.LogInfo("[ui.guide] offered the VR practice (first VR launch)");
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
}
