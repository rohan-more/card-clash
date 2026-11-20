using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows emotes: one pops up over the sender's avatar on this screen, bounces
/// in, wobbles, holds, and fades. A new emote from the same seat replaces the
/// one showing.
///
/// Emotes from other players arrive as EMOTE (opcode 105) straight from the
/// connection, not through MatchPresenter: they're not game events, so they
/// never wait behind card animations or hold them up. Your own comes from
/// EmotePicker, shown at once without waiting for the server.
///
/// Put it on the overlay canvas, give it the panel layer, and it makes one
/// bubble per seat as needed.
/// </summary>
public class EmoteView : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private MatchSeatsView seatsView;
    [SerializeField] private EmoteLibrary library;
    [Tooltip("A full-screen RectTransform on the overlay canvas, e.g. the intro's panel layer.")]
    [SerializeField] private RectTransform panelLayer;

    [Header("Bubble")]
    [Tooltip("How big an emote shows, in the panel layer's units.")]
    [SerializeField] private Vector2 size = new Vector2(150f, 150f);
    [Tooltip("Shift from the middle of the avatar, in the panel layer's units. (0, 0) covers the face.")]
    [SerializeField] private Vector2 offset = Vector2.zero;
    [Tooltip("Where your own emote shows, e.g. an empty RectTransform on the OverlayCanvas just " +
             "above the emote button. Your seat has no avatar on the table, so without this your " +
             "emote only shows if it gets one.")]
    [SerializeField] private RectTransform yourEmotePoint;

    [Header("Timing")]
    [SerializeField] private float popInSeconds = 0.25f;
    [Tooltip("Degrees it rocks either way after popping in. 0 for no wobble.")]
    [SerializeField] private float wobbleDegrees = 12f;
    [SerializeField] private float wobbleSeconds = 0.5f;
    [SerializeField] private float holdSeconds = 1.5f;
    [SerializeField] private float fadeOutSeconds = 0.3f;

    // One bubble per server seat, made the first time that seat emotes
    private readonly Dictionary<int, RectTransform> bubbles = new Dictionary<int, RectTransform>();
    // Until when each seat's own pick is on screen; reactions wait for it
    private readonly Dictionary<int, float> pickedShowingUntil = new Dictionary<int, float>();

    private MatchState State => presenter.State;
    private RectTransform Layer => panelLayer != null ? panelLayer : (RectTransform)transform;

    private void OnEnable()
    {
        if (Services.Connection != null)
            Services.Connection.OnMatchState += HandleMatchState;
    }

    private void OnDisable()
    {
        if (Services.Connection != null)
            Services.Connection.OnMatchState -= HandleMatchState;

        foreach (var bubble in bubbles.Values)
        {
            if (bubble != null)
                DOTween.Kill(bubble);
        }
    }

    private void HandleMatchState(long opCode, string json)
    {
        if (opCode != UnoOpCodes.Emote)
            return;

        var msg = JsonUtility.FromJson<EmoteMsg>(json);
        if (msg == null)
            return;

        // The server doesn't send you your own; ignore it if it ever does
        if (msg.seat == State.YourSeat)
            return;

        Show(msg.seat, msg.emote);
    }

    /// <summary>
    /// Pops an emote a player picked over their avatar, or yours at Your Emote
    /// Point. Unknown emotes and seats are ignored.
    /// </summary>
    public void Show(int seat, int emote)
    {
        if (Play(seat, emote))
            pickedShowingUntil[seat] = Time.unscaledTime + ShowSeconds;
    }

    /// <summary>
    /// Pops an emote the game picked for a player (see EmoteReactions), unless
    /// one they picked themselves is still showing: theirs always wins.
    /// </summary>
    public void ShowReaction(int seat, Emote emote)
    {
        if (pickedShowingUntil.TryGetValue(seat, out var until) && Time.unscaledTime < until)
            return;
        Play(seat, (int)emote);
    }

    // How long one emote is on screen, start to finish
    private float ShowSeconds => popInSeconds + (wobbleDegrees > 0f ? wobbleSeconds : 0f)
                                 + holdSeconds + fadeOutSeconds;

    private bool Play(int seat, int emote)
    {
        var sprite = library != null ? library.Get(emote) : null;
        if (sprite == null)
            return false;

        if (!TryGetPosition(seat, out var position))
            return false;

        var bubble = BubbleFor(seat);
        var image = bubble.GetComponent<Image>();
        var group = bubble.GetComponent<CanvasGroup>();
        bubble.position = position;

        // Replace whatever this seat was showing
        DOTween.Kill(bubble);
        image.sprite = sprite;
        group.alpha = 0f;
        bubble.localScale = Vector3.zero;
        bubble.localRotation = Quaternion.identity;
        bubble.SetAsLastSibling();
        bubble.gameObject.SetActive(true);

        var sequence = DOTween.Sequence()
            .SetTarget(bubble)
            .Append(group.DOFade(1f, popInSeconds * 0.5f))
            .Join(bubble.DOScale(1f, popInSeconds).SetEase(Ease.OutBack));

        if (wobbleDegrees > 0f)
            sequence.Append(bubble.DOPunchRotation(new Vector3(0f, 0f, wobbleDegrees), wobbleSeconds, 6, 0.5f));

        sequence.AppendInterval(holdSeconds)
            .Append(group.DOFade(0f, fadeOutSeconds))
            .Join(bubble.DOScale(0.8f, fadeOutSeconds))
            .OnComplete(() => bubble.gameObject.SetActive(false));

        Services.Audio?.Play(Sfx.Emote);
        return true;
    }

    // Yours at Your Emote Point; everyone else's over their avatar, wherever
    // this screen puts that seat
    private bool TryGetPosition(int seat, out Vector3 position)
    {
        if (seat == State.YourSeat && yourEmotePoint != null)
        {
            position = FlyingSprites.PointOn(Layer, yourEmotePoint);
            return true;
        }

        var avatar = seatsView != null ? seatsView.PanelForSeat(seat)?.avatar : null;
        if (avatar == null)
        {
            position = default;
            return false;
        }

        var centre = FlyingSprites.PointOn(Layer, avatar.rectTransform);
        var local = Layer.InverseTransformPoint(centre) + (Vector3)offset;
        position = Layer.TransformPoint(local);
        return true;
    }

    private RectTransform BubbleFor(int seat)
    {
        if (bubbles.TryGetValue(seat, out var existing) && existing != null)
            return existing;

        var go = new GameObject($"Emote{seat}", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        var rect = (RectTransform)go.transform;
        rect.SetParent(Layer, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false; // never catches a tap meant for the cards

        var group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        go.SetActive(false);
        bubbles[seat] = rect;
        return rect;
    }
}
