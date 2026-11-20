using DG.Tweening;
using UnityEngine;

/// <summary>
/// A finger that taps on something while the finger is switched on, e.g. the
/// draw pile when you have nothing to play. It waits a moment, fades in, then
/// presses down and lifts again on a loop until switched off.
///
/// It decides nothing: whoever switches the finger on and off (MatchView's
/// Draw Hint) decides when. Put it on the finger Image, or on something above
/// it that stays switched on (e.g. the draw pile), with Finger set. It only
/// ever touches the finger's own Canvas Group, adding one if needed, so taps
/// always pass through to the pile.
/// </summary>
public class TapHint : MonoBehaviour
{
    [Tooltip("The finger Image that moves and taps; the object MatchView's Draw Hint switches " +
             "on and off. This object if empty.")]
    [SerializeField] private RectTransform finger;

    [Header("Timing")]
    [Tooltip("Waits this long before showing, so it doesn't flash up on a quick turn. " +
             "Matches the \"Draw!\" bar's delay by default.")]
    [SerializeField] private float startDelaySeconds = 1f;
    [SerializeField] private float fadeInSeconds = 0.2f;
    [Tooltip("Down, and the same again back up.")]
    [SerializeField] private float pressSeconds = 0.18f;
    [Tooltip("Rest between taps.")]
    [SerializeField] private float pauseSeconds = 0.5f;

    [Header("Motion")]
    [Tooltip("How far the finger moves down to tap, in its canvas's units.")]
    [SerializeField] private float pressDistance = 24f;
    [Tooltip("The finger shrinks a little as it presses, as if pushing into the screen.")]
    [SerializeField] private float pressScale = 0.9f;

    private CanvasGroup group;
    private Vector2 restPosition;
    private Sequence playing;
    private bool showing;

    private bool FingerShown => finger != null && finger.gameObject.activeInHierarchy;

    private void Awake()
    {
        if (finger == null)
            finger = (RectTransform)transform;
        restPosition = finger.anchoredPosition;

        // The finger's own group, never this object's: on the draw pile, that
        // would switch off the pile's taps
        if (!finger.TryGetComponent(out group))
            group = finger.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false; // the tap is for the pile underneath
        group.alpha = 0f;
    }

    // Starts when the finger is switched on and stops when it's switched off,
    // whether this script is on the finger or above it
    private void Update()
    {
        bool shown = FingerShown;
        if (shown && !showing)
            Begin();
        else if (!shown && showing)
            Stop();
    }

    private void OnDisable() => Stop();

    private void Begin()
    {
        Stop();
        showing = true;
        group.alpha = 0f;

        playing = DOTween.Sequence()
            .AppendInterval(startDelaySeconds)
            .Append(group.DOFade(1f, fadeInSeconds))
            .AppendCallback(StartTapping);
    }

    private void StartTapping()
    {
        playing = DOTween.Sequence()
            .Append(finger.DOAnchorPosY(restPosition.y - pressDistance, pressSeconds).SetEase(Ease.InQuad))
            .Join(finger.DOScale(pressScale, pressSeconds).SetEase(Ease.InQuad))
            .Append(finger.DOAnchorPosY(restPosition.y, pressSeconds).SetEase(Ease.OutQuad))
            .Join(finger.DOScale(1f, pressSeconds).SetEase(Ease.OutQuad))
            .AppendInterval(pauseSeconds)
            .SetLoops(-1);
    }

    private void Stop()
    {
        showing = false;
        playing?.Kill();
        playing = null;
        if (finger != null)
        {
            finger.anchoredPosition = restPosition;
            finger.localScale = Vector3.one;
        }
        if (group != null)
            group.alpha = 0f;
    }
}
