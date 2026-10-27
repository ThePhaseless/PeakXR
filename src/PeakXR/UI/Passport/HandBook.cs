using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.UI;

namespace PeakXR.UI.Passport;

/// <summary>
/// The book the local VR player is reading: the passport while its window is open, else an open
/// guidebook (or single guidebook page) in hand. Owns the hand-held reading pose and the page-flip
/// entry point, so the swipe gesture and controller bindings turn pages through the game's own calls.
/// </summary>
internal static class HandBook
{
    // Rough half extents of an open guidebook spread, metres; only bound the swipe zone.
    private const float GuidebookHalfWidth = 0.25f;
    private const float GuidebookHalfHeight = 0.17f;

    private static Action<Guidebook> flipRight;
    private static Action<Guidebook> flipLeft;

    /// <summary>The passport canvas, set once ui.world places it through <see cref="PassportPlacement"/>.</summary>
    internal static Canvas PassportCanvas;

    internal static void Init(Action<Guidebook> right, Action<Guidebook> left)
    {
        flipRight = right;
        flipLeft = left;
    }

    /// <summary>Whether a book is open in VR, so a binding can give page flips priority over other uses.</summary>
    public static bool IsOpen => VRSession.Active && (PassportOpen || OpenGuidebook() != null);

    /// <summary>
    /// Turn a page of the open book: <paramref name="dir"/> &gt; 0 forward, &lt; 0 back. The passport turns
    /// its outfit pages (A/PassportManager.cs:316 PageNext, :326 PagePrev) and moves to the neighbouring
    /// tab past the last page (:160 OpenTab); the guidebook flips its spread (A/Guidebook.cs:197, :206).
    /// Returns false when no book is open or there is nothing to turn to.
    /// </summary>
    public static bool FlipPage(int dir)
    {
        if (dir == 0 || !VRSession.Active)
            return false;
        var passport = PassportManager.instance;
        if (passport != null && passport.isOpen)
            return FlipPassport(passport, dir);
        var guidebook = OpenGuidebook();
        if (guidebook == null)
            return false;
        var flip = dir > 0 ? flipRight : flipLeft;
        if (flip == null)
            return false;
        // The flip does nothing past the first or last spread (A/Guidebook.cs:199, :208).
        int before = guidebook.currentPageSet;
        flip(guidebook);
        return guidebook.currentPageSet != before;
    }

    internal static bool PassportOpen
    {
        get
        {
            var passport = PassportManager.instance;
            return passport != null && passport.isOpen;
        }
    }

    /// <summary>The local player's held guidebook while it is open (A/Guidebook.cs:58 <c>isOpen</c>).</summary>
    internal static Guidebook OpenGuidebook()
    {
        var c = Character.localCharacter;
        if (c == null || c.data == null)
            return null;
        return c.data.currentItem is Guidebook book && book.isOpen ? book : null;
    }

    /// <summary>Hand holding the book: the held item's hand, or the left hand when nothing is held.</summary>
    internal static bool BookInLeftHand()
    {
        var c = Character.localCharacter;
        var holding = c != null && c.data != null && c.data.currentItem != null;
        return !holding || LocalHold.ItemInLeftHand;
    }

    /// <summary>Reading pose above the book hand, facing the eyes, its lower edge clear of the hand
    /// (<see cref="BookPose"/> with the book's half height).</summary>
    internal static bool TryPose(
        VRRig rig,
        bool left,
        bool passport,
        out Vector3 position,
        out Quaternion rotation
    )
    {
        position = default;
        rotation = Quaternion.identity;
        var hand = rig != null ? rig.Hand(left) : null;
        if (hand == null || rig.Head == null)
            return false;
        var cfg = PassportFeature.Settings;
        var head = rig.Head;
        BookPose.Compute(
            hand.position.ToNumerics(),
            head.position.ToNumerics(),
            head.forward.ToNumerics(),
            left,
            cfg.BookLift.Value,
            cfg.BookSide.Value,
            out var pos,
            out var rot,
            HalfExtents(passport).y
        );
        position = pos.ToUnity();
        rotation = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
        return true;
    }

    /// <summary>
    /// Where the passport canvas's pivot goes so its centre lands on <paramref name="centre"/>. PassportUI/Canvas keeps
    /// its pivot off centre (vr.129 logged a middle cell at canvas (646, 404) of 1280x720), so posing the pivot put
    /// the page's corner on the book pose: the page rose half its height and slid half its width away (vr.70, 0.38 m
    /// from the hand instead of 0.24 m).
    /// </summary>
    internal static Vector3 PivotFor(
        RectTransform canvas,
        Vector3 centre,
        Quaternion rotation,
        float widthMeters
    )
    {
        var rect = canvas.rect;
        return rect.width > 1f
            ? centre - rotation * (Vector3)(rect.center * (widthMeters / rect.width))
            : centre;
    }

    /// <summary>Half size of the open book for the swipe zone, metres.</summary>
    internal static Vector2 HalfExtents(bool passport)
    {
        if (!passport)
            return new Vector2(GuidebookHalfWidth, GuidebookHalfHeight);
        var halfWidth = 0.5f * PassportFeature.Settings.PassportWidth.Value;
        var aspect = 9f / 16f;
        if (PassportCanvas != null)
        {
            var size = ((RectTransform)PassportCanvas.transform).rect.size;
            if (size.x > 1f)
                aspect = size.y / size.x;
        }
        return new Vector2(halfWidth, halfWidth * aspect);
    }

    private static bool FlipPassport(PassportManager passport, int dir)
    {
        var tabs = passport.tabs;
        var tabCount = tabs != null ? tabs.Length : 0;
        var tab = -1;
        for (int i = 0; i < tabCount; i++)
        {
            if (tabs[i] != null && tabs[i].type == passport.activeType)
            {
                tab = i;
                break;
            }
        }
        // The arrows are shown exactly when a page exists that way (A/PassportManager.cs:219-220).
        var hasNext = passport.buttonNext != null && passport.buttonNext.activeSelf;
        var hasPrev = passport.buttonPrev != null && passport.buttonPrev.activeSelf;
        switch (PassportPaging.Decide(dir, hasNext, hasPrev, tab, tabCount, out var target))
        {
            case PassportFlip.NextPage:
                passport.PageNext();
                return true;
            case PassportFlip.PrevPage:
                passport.PagePrev();
                return true;
            case PassportFlip.Tab:
                if (tabs[target] == null)
                    return false;
                passport.OpenTab(tabs[target].type);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Places the passport canvas (GAME/PassportManager/PassportUI/Canvas, A/PassportManager.cs:10) as a book
/// in the hand that holds the passport item, so it opens where the item is (IT-29) and moves with the
/// hand rather than the head. Interactive: the other hand's laser works its tabs and buttons.
/// </summary>
internal sealed class PassportPlacement : ICanvasPlacement
{
    private RectMask2D crop;

    public bool Interactive => true;

    public PassportPlacement() => VRSession.Stopped += Uncrop;

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        HandBook.PassportCanvas = canvas;
        var cfg = PassportFeature.Settings;
        widthMeters = cfg.PassportWidth.Value;
        if (
            !cfg.PassportAtHand.Value
            || !VRSession.Active
            || !HandBook.TryPose(
                VRRig.Instance,
                HandBook.BookInLeftHand(),
                true,
                out position,
                out rotation
            )
        )
        {
            Uncrop();
            position = default;
            rotation = Quaternion.identity;
            return PlacementResult.Default;
        }
        position = HandBook.PivotFor(
            (RectTransform)canvas.transform,
            position,
            rotation,
            widthMeters
        );
        Crop(canvas);
        return PlacementResult.Placed;
    }

    /// <summary>
    /// The booklet is 1225 units tall on a 720-unit canvas (PassportUI/Canvas/Panel/BG (3)): on a monitor the screen
    /// edge crops its blank upper page, at the hand all of it showed. Crop to the canvas as the screen did. At rest
    /// (Panel y 25, from the Airport scene) the lower page spans y -203..265, tabs, options, close and portrait all
    /// inside -640..640 x -360..360; the PassportOpen slide-in (Panel y 824 to 25 over 0.25 s) is cropped as on a
    /// monitor.
    /// </summary>
    private void Crop(Canvas canvas)
    {
        if (crop != null && crop.gameObject == canvas.gameObject)
            return;
        Uncrop();
        if (!canvas.TryGetComponent(out RectMask2D _))
            crop = canvas.gameObject.AddComponent<RectMask2D>();
    }

    private void Uncrop()
    {
        if (crop != null)
            UnityEngine.Object.Destroy(crop);
        crop = null;
    }
}

/// <summary>
/// Per-frame driver for hand-held books: poses the open guidebook at the hand after its own
/// camera-locked pose (A/Guidebook.cs:176 LateUpdate, :180-181), re-poses books just before rendering so
/// they do not trail the tracked hand, and turns pages when the free hand swipes across the book.
/// Runs after ui.world (31000) has placed the passport canvas for the frame.
/// </summary>
[DefaultExecutionOrder(31010)]
internal sealed class HandBookRuntime : MonoBehaviour
{
    private const float TriggerBlock = 0.5f;
    private const float FlipHaptic = 0.35f;
    private const float FlipHapticSeconds = 0.03f;

    private readonly PageSwipe swipe = new();
    private readonly AnalogButton closeTrigger = new(0.6f, 0.4f);
    private UnityEngine.Object lastBook;
    private bool passportWasOpen;

    public static void Create()
    {
        var go = new GameObject("PeakXR_HandBook");
        DontDestroyOnLoad(go);
        go.AddComponent<HandBookRuntime>();
    }

    private void OnEnable() => Application.onBeforeRender += OnBeforeRender;

    private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

    private void LateUpdate()
    {
        var rig = VRRig.Instance;
        HeldPassport.Update(VRSession.Active);
        if (!VRSession.Active || rig == null || rig.Head == null)
        {
            passportWasOpen = false;
            Forget();
            PassportPoke.Reset();
            return;
        }

        var cfg = PassportFeature.Settings;
        var left = HandBook.BookInLeftHand();
        CloseOnTrigger(left);
        var guidebook = cfg.GuidebookAtHand.Value ? HandBook.OpenGuidebook() : null;
        if (guidebook != null)
            PoseGuidebook(guidebook, rig, left);

        // The passport window wins; it blocks player input, so no guidebook is being read meanwhile.
        var passport =
            HandBook.PassportOpen
            && cfg.PassportAtHand.Value
            && UIWorldRuntime.Ready
            && HandBook.PassportCanvas != null;
        UnityEngine.Object book = passport ? PassportManager.instance : guidebook;
        // Try-on by touch: the free hand pokes a cosmetic cell (swipe setting or not).
        if (passport)
            PassportPoke.Tick(
                rig,
                PassportManager.instance,
                HandBook.PassportCanvas.transform,
                !left
            );
        else
            PassportPoke.Reset();
        if (book == null || !cfg.SwipeToFlip.Value)
        {
            Forget();
            return;
        }
        if (book != lastBook)
        {
            swipe.Reset();
            lastBook = book;
        }

        Vector3 position;
        Quaternion rotation;
        if (passport)
        {
            var rect = (RectTransform)HandBook.PassportCanvas.transform;
            position = rect.TransformPoint(rect.rect.center);
            rotation = rect.rotation;
        }
        else if (!HandBook.TryPose(rig, left, false, out position, out rotation))
            return;

        var half = HandBook.HalfExtents(passport);
        swipe.HalfWidth = half.x;
        swipe.HalfHeight = half.y;

        var freeLeft = !left;
        var free = rig.Hand(freeLeft);
        if (free == null)
            return;
        var local = Quaternion.Inverse(rotation) * (free.position - position);
        // A pointing index presses a cell (PassportPoke), it does not turn the page.
        var blocked =
            VRInput.Hand(freeLeft).Trigger > TriggerBlock
            || (passport && PassportPoke.Pointing(freeLeft));
        var dir = swipe.Update(local.ToNumerics(), blocked, Time.unscaledDeltaTime);
        if (dir == 0)
            return;
        if (HandBook.FlipPage(dir))
            UIHaptics.Tick(freeLeft, FlipHaptic, FlipHapticSeconds);
        // Past the cover: a dull bump instead of the flip tick, like a book that has no more pages that way.
        else
            Affordance.EndStop(freeLeft);
    }

    /// <summary>
    /// The trigger that opened the passport closes it again. It opened through the item's use
    /// (A/Action_Passport.cs:5 <c>ToggleOpen</c>), but the open window blocks item use, so vanilla never sees the
    /// second press. Only a fresh press on the book hand while it was already open, and not one the laser spends on
    /// the passport's own controls.
    /// </summary>
    private void CloseOnTrigger(bool left)
    {
        closeTrigger.Update(VRInput.Hand(left).Trigger, Time.unscaledDeltaTime);
        bool open = HandBook.PassportOpen;
        if (
            open
            && passportWasOpen
            && closeTrigger.Pressed
            && !(LaserPointer.Over != null && LaserPointer.OverLeft == left)
        )
        {
            PassportManager.instance.ToggleOpen();
            UIHaptics.Tick(left, FlipHaptic, FlipHapticSeconds);
        }
        passportWasOpen = open;
    }

    private static void OnBeforeRender()
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || rig.Head == null)
            return;
        var cfg = PassportFeature.Settings;
        var left = HandBook.BookInLeftHand();

        if (cfg.GuidebookAtHand.Value)
        {
            var guidebook = HandBook.OpenGuidebook();
            if (guidebook != null)
                PoseGuidebook(guidebook, rig, left);
        }

        var canvas = HandBook.PassportCanvas;
        if (
            cfg.PassportAtHand.Value
            && UIWorldRuntime.Ready
            && canvas != null
            && HandBook.PassportOpen
            && WorldCanvases.IsConverted(canvas)
            && HandBook.TryPose(rig, left, true, out var position, out var rotation)
        )
            canvas.transform.SetPositionAndRotation(
                HandBook.PivotFor(
                    (RectTransform)canvas.transform,
                    position,
                    rotation,
                    cfg.PassportWidth.Value
                ),
                rotation
            );
    }

    /// <summary>Only the local holder's book is moved (vanilla checks the same, A/Guidebook.cs:178); the
    /// page keeps rendering through its own camera into the render texture.</summary>
    private static void PoseGuidebook(Guidebook guidebook, VRRig rig, bool left)
    {
        var book = guidebook.bookTransform;
        if (book != null && HandBook.TryPose(rig, left, false, out var position, out var rotation))
            book.SetPositionAndRotation(position, rotation);
    }

    private void Forget()
    {
        swipe.Reset();
        lastBook = null;
    }
}
