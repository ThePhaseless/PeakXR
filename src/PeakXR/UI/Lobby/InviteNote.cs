using Peak.Network;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Settings;

namespace PeakXR.UI.Lobby;

/// <summary>
/// In-world note at the invite kiosk. The kiosk opens the Steam overlay invite dialog
/// (A/AirportInviteFriendsKiosk.cs:41), which draws on the desktop window and may never reach the
/// headset, so pressing it in VR would otherwise look like nothing happened. Built once, reused, and
/// shown for a few seconds facing the player; it owns no raycaster or collider, so it never blocks the
/// laser or interaction.
/// </summary>
internal sealed class InviteNote : MonoBehaviour
{
    private const float WidthMeters = 0.6f;
    private static readonly Vector2 Size = new(900f, 300f);

    private readonly LobbyDockLimits limits = new() { ScreenDrop = -0.15f };
    private UILobbySettings cfg;
    private GameObject panel;
    private TextMeshProUGUI text;
    private bool fontSet;
    private AirportInviteFriendsKiosk kiosk;
    private float hideAt;

    public static InviteNote Create(UILobbySettings cfg)
    {
        var go = new GameObject("PeakXR_InviteNote");
        DontDestroyOnLoad(go);
        var note = go.AddComponent<InviteNote>();
        note.cfg = cfg;
        note.Build();
        note.enabled = false;
        return note;
    }

    /// <summary>Called after the vanilla kiosk press. VR only; flat players see nothing new.</summary>
    public void Show(AirportInviteFriendsKiosk pressed)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || pressed == null || rig == null || rig.Head == null)
            return;

        // The cue first, for the hand nearer the kiosk: offline the invite cannot work (the Blocked refusal, the
        // kiosk turned red); online the dialog opened on the desktop, where the headset cannot show it (a notice).
        var matchmaking = NetCode.Matchmaking;
        bool offline = matchmaking == null || !matchmaking.InLobby;
        var at = pressed.transform.position;
        bool left =
            rig.LeftHand != null
            && rig.RightHand != null
            && (rig.LeftHand.position - at).sqrMagnitude
                < (rig.RightHand.position - at).sqrMagnitude;
        if (offline)
            Affordance.Deny(left, at, DenyReason.Blocked, default, pressed);
        else
            Affordance.Inform(left, at, DenyReason.NotHere, default);

        // The written note is opt-in text, like every other cue's label.
        if (!cfg.InviteNote.Value || AffordanceFeature.ActionHintText?.Value != true)
            return;

        if (!fontSet)
            fontSet = TryGameFont();
        // No panel behind the note: an outline keeps it readable against the airport.
        TextHalo.Apply(text);
        text.text = Message();

        KioskDock.Measure(pressed.transform, out var center, out var top);
        limits.MaxDistance = cfg.MaxDistance.Value;
        var position = LobbyDock
            .Pose(
                center.ToNumerics(),
                top,
                pressed.transform.forward.ToNumerics(),
                rig.Head.position.ToNumerics(),
                limits,
                out var facing
            )
            .ToUnity();
        panel.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(facing.ToUnity(), Vector3.up)
        );

        kiosk = pressed;
        hideAt = Time.unscaledTime + cfg.InviteNoteSeconds.Value;
        panel.SetActive(true);
        enabled = true;
    }

    private void Update()
    {
        // The kiosk dies with the airport scene, which also hides a note left up during a level load.
        if (!VRSession.Active || kiosk == null || Time.unscaledTime >= hideAt)
            Hide();
    }

    private void Hide()
    {
        panel.SetActive(false);
        kiosk = null;
        enabled = false;
    }

    /// <summary>Built on a press, never per frame.</summary>
    private static string Message()
    {
        var matchmaking = NetCode.Matchmaking;
        if (matchmaking == null || !matchmaking.InLobby)
            return "Not in a Steam lobby, so Steam invites are unavailable.";

        var message =
            "Steam invite dialog opened on the desktop overlay.\n"
            + "Not in your headset? Check the desktop, or have friends join from your Steam profile.";
        var code = RoomCode();
        return code == null ? message : message + "\nRoom code: " + code;
    }

    /// <summary>The room code the vanilla pause menu shows (A/PauseMenuMainPage.cs:116), honouring the
    /// game's hide-room-code setting (A/PauseMenuMainPage.cs:126).</summary>
    private static string RoomCode()
    {
        if (PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return null;
        var game = GameHandler.Instance;
        if (game == null || game.SettingsHandler == null)
            return null;
        var hide = game.SettingsHandler.GetSetting<HideRoomCodeSetting>();
        if (hide == null || hide.Value == OffOnMode.ON)
            return null;
        return PhotonNetwork.CurrentRoom.Name;
    }

    /// <summary>Use the font of a game text so the note matches the game's UI.</summary>
    private bool TryGameFont()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui.boardingPass == null || gui.boardingPass.playerName == null)
            return false;
        var font = gui.boardingPass.playerName.font;
        if (font == null)
            return false;
        text.font = font;
        return true;
    }

    private void Build()
    {
        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = PeakXR.Core.DrawOrder.WorldUi;
        var rect = (RectTransform)panel.transform;
        rect.sizeDelta = Size;
        rect.localScale = Vector3.one * PanelMath.MetersPerPixel(WidthMeters, Size.x);

        text = Child<TextMeshProUGUI>(panel.transform, "Text");
        text.fontSize = 34f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform, 30f);

        panel.SetActive(false);
    }

    private static T Child<T>(Transform parent, string name)
        where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var graphic = go.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }
}
