using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Your emote button. Tapping it opens the emote picker popup through
/// PopupManager (PopupType.EmotePicker); tapping it again, or anywhere
/// outside the popup, closes it. Picking one shows it over your avatar at once
/// (through EmoteView) and sends SEND_EMOTE so everyone else sees it too. A
/// short cooldown stops spam; the server keeps its own, so this one is only
/// for the button.
///
/// Usable only once play has started, never in the intro or after the game,
/// and not while a choice the game is waiting for (a wild's colour, play or
/// keep, leave?) is open: the picker is a Low priority popup, so it can't
/// open over those, and they replace it.
/// </summary>
public class EmotePicker : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private EmoteView emoteView;
    [SerializeField] private EmoteLibrary library;

    [Header("UI")]
    [Tooltip("Opens and closes the emote picker popup.")]
    [SerializeField] private Button openButton;
    [Tooltip("Optional: a Filled (Radial 360) Image over the button that drains while you wait to emote again.")]
    [SerializeField] private Image cooldownFill;

    [Header("Rules")]
    [SerializeField] private float cooldownSeconds = 2f;
    [Tooltip("Off until the server handles SEND_EMOTE: your emote then only shows on your own " +
             "screen. Sending it to a server that doesn't know it would get an error and a resync.")]
    [SerializeField] private bool sendToServer;

    private float cooldownEndsAt;

    private MatchState State => presenter.State;

    private static IPopupService Popups => Services.Popups;
    private static bool PickerOpen => Popups != null && Popups.ActiveType == PopupType.EmotePicker;

    private void OnEnable()
    {
        if (openButton != null)
            openButton.onClick.AddListener(TogglePicker);
    }

    private void OnDisable()
    {
        if (openButton != null)
            openButton.onClick.RemoveListener(TogglePicker);
    }

    private void Update()
    {
        bool usable = CanEmote();
        float left = Mathf.Max(0f, cooldownEndsAt - Time.unscaledTime);

        // Greyed while cooling down, or while a game choice holds the popup slot
        if (openButton != null)
            openButton.interactable = usable && left <= 0f
                                      && (PickerOpen || (Popups != null && Popups.CanShow(PopupType.EmotePicker)));

        if (cooldownFill != null)
        {
            bool cooling = left > 0f && cooldownSeconds > 0f;
            cooldownFill.enabled = cooling;
            if (cooling)
                cooldownFill.fillAmount = left / cooldownSeconds;
        }

        // Play ended, or the intro started again after a resync: put the picker away
        if (!usable && PickerOpen)
            Popups.CloseActive();
    }

    private void TogglePicker()
    {
        if (Popups == null)
            return;

        if (PickerOpen)
        {
            Popups.CloseActive();
            return;
        }

        if (!CanEmote() || Time.unscaledTime < cooldownEndsAt)
            return;

        // Refused (returns false) while a more important popup is open
        Popups.Show(PopupType.EmotePicker, new EmoteChoice
        {
            Library = library,
            OnPick = Pick,
        });
    }

    private void Pick(Emote emote)
    {
        if (!CanEmote() || Time.unscaledTime < cooldownEndsAt)
            return;
        cooldownEndsAt = Time.unscaledTime + cooldownSeconds;

        // Yours shows straight away; the server sends it only to the others
        if (emoteView != null)
            emoteView.Show(State.YourSeat, (int)emote);

        if (sendToServer)
            Send(emote);
    }

    private async void Send(Emote emote)
    {
        if (Services.Connection == null)
            return;
        try
        {
            var json = JsonUtility.ToJson(new SendEmoteReq { emote = (int)emote });
            await Services.Connection.SendMatchStateAsync(UnoOpCodes.SendEmote, json);
        }
        catch (Exception e)
        {
            // An emote isn't worth retrying
            Debug.LogWarning($"Couldn't send emote: {e.Message}");
        }
    }

    // Only once the first turn has begun, while the game is on and we're connected
    private bool CanEmote()
    {
        if (presenter == null || State.Seats.Length == 0)
            return false;
        if (presenter.IntroPlaying || !State.HasStarted || State.IsOver)
            return false;
        return Services.Connection == null || Services.Connection.IsConnected;
    }
}
