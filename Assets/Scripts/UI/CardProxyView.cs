using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct SeatEntry
{
    public PlayerSeat seat;
    public RectTransform point;
}
public class CardProxyView : MonoBehaviour
{
    [Header("Refs")] 
    [SerializeField] private SeatEntry[] seatEntries;
    private Dictionary<PlayerSeat, RectTransform> seatLookup;
    [SerializeField] private Image cardImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform centerTransform;
    [SerializeField] private Vector2  proxyIdlePosition;
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1500f; // units per second, on average
    [Tooltip("Even short hops take at least this long, so every move can be followed.")]
    [SerializeField] private float minMoveSeconds = 0.35f;

    private RectTransform rt;
    private Coroutine moveRoutine;
    private Tween flipTween;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        proxyIdlePosition = rt.anchoredPosition;
        seatLookup = new Dictionary<PlayerSeat, RectTransform>(seatEntries.Length);
        foreach (var e in seatEntries)
        {
            seatLookup[e.seat] = e.point;
        }
        HideImmediate();
    }

    /// <summary>
    /// Where cards fly to for a seat, e.g. for the intro's deal; null if the
    /// seat has no entry.
    /// </summary>
    public RectTransform PointFor(PlayerSeat seat)
    {
        if (seatLookup == null)
            return null;
        return seatLookup.TryGetValue(seat, out var point) ? point : null;
    }

    /// <summary>
    /// Initializes the proxy at a given anchored position.
    /// </summary>
    public void Show(Sprite sprite, PlayerSeat seat)
    {
        StopMove();

        if (!seatLookup.TryGetValue(seat, out RectTransform start))
        {
            Debug.LogError($"Missing SeatEntry for {seat}");
            return;
        }

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        rt.anchoredPosition = start.anchoredPosition;
        canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// Moves the proxy to the target RectTransform at constant speed.
    /// </summary>
    public void MoveTo(System.Action onComplete = null)
    {
        StopMove();
        // Onto the discard pile: end at the proxy's own size, which is the pile's
        moveRoutine = StartCoroutine(MoveAtConstantSpeed(centerTransform.anchoredPosition, 1f, onComplete, 1f));
    }

    /// <summary>
    /// Flies a card from one seat point to another, e.g. Deck to a player who
    /// drew. Completes straight away if either point is missing.
    /// </summary>
    public void Fly(Sprite sprite, PlayerSeat from, PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        if (!seatLookup.TryGetValue(from, out var start))
        {
            Debug.LogError($"Missing SeatEntry for {from}");
            onComplete?.Invoke();
            return;
        }

        Fly(sprite, start.anchoredPosition, to, speedMultiplier, onComplete);
    }

    /// <summary>
    /// Flies a card from any object on screen, e.g. the draw pile, to a seat.
    /// The object can live anywhere in the hierarchy.
    /// </summary>
    public void Fly(Sprite sprite, RectTransform from, PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        Fly(sprite, AnchoredPositionOf(from), to, speedMultiplier, onComplete, ScaleToMatch(from));
    }

    private void Fly(Sprite sprite, Vector2 startPos, PlayerSeat to, float speedMultiplier, System.Action onComplete,
                     float startScale = 1f)
    {
        StopMove();

        if (!seatLookup.TryGetValue(to, out var end))
        {
            Debug.LogError($"Missing SeatEntry for {to}");
            onComplete?.Invoke();
            return;
        }

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one * startScale;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = startPos;
        canvasGroup.alpha = 1f;

        moveRoutine = StartCoroutine(MoveAtConstantSpeed(end.anchoredPosition, speedMultiplier, onComplete));
    }

    /// <summary>
    /// Flies a card from one object on screen to another, e.g. the deck to a
    /// hand. It leaves at the size of the object it flies from and keeps it,
    /// unless growToFullSize: then it grows to its usual size on the way (your
    /// own draws, so you can read them).
    /// </summary>
    public void Fly(Sprite sprite, RectTransform from, RectTransform to, float speedMultiplier, System.Action onComplete,
                    bool growToFullSize = false)
    {
        StopMove();

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one * ScaleToMatch(from);
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = AnchoredPositionOf(from);
        canvasGroup.alpha = 1f;

        float? endScale = growToFullSize ? 1f : (float?)null;
        moveRoutine = StartCoroutine(MoveAtConstantSpeed(AnchoredPositionOf(to), speedMultiplier, onComplete, endScale));
    }

    // The scale that makes the card as tall on screen as the given object, e.g.
    // the draw pile, so a drawn card leaves looking like the deck's top card
    private float ScaleToMatch(RectTransform target)
    {
        if (target == null)
            return 1f;
        float ours = rt.rect.height * rt.lossyScale.y / Mathf.Max(0.0001f, Mathf.Abs(rt.localScale.y));
        float theirs = target.rect.height * target.lossyScale.y;
        return ours > 0f && theirs > 0f ? theirs / ours : 1f;
    }

    /// <summary>Flies the card already showing, from wherever it is now, to an object on screen, e.g. your hand.</summary>
    public void FlyFromHere(RectTransform to, float speedMultiplier, System.Action onComplete)
    {
        StopMove();
        moveRoutine = StartCoroutine(MoveAtConstantSpeed(AnchoredPositionOf(to), speedMultiplier, onComplete));
    }

    /// <summary>Flies the card already showing, from wherever it is now, to a seat.</summary>
    public void FlyFromHere(PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        StopMove();

        if (!seatLookup.TryGetValue(to, out var end))
        {
            Debug.LogError($"Missing SeatEntry for {to}");
            onComplete?.Invoke();
            return;
        }

        moveRoutine = StartCoroutine(MoveAtConstantSpeed(end.anchoredPosition, speedMultiplier, onComplete));
    }

    /// <summary>
    /// Turns the card over in place: squeezes it flat, swaps the face, opens it
    /// again, keeping whatever size it is.
    /// </summary>
    public void Flip(Sprite newFace, float duration, System.Action onComplete)
    {
        KillFlip();
        float width = rt.localScale.y;
        flipTween = DOTween.Sequence()
            .Append(rt.DOScaleX(0f, duration * 0.5f).SetEase(Ease.InQuad))
            .AppendCallback(() => cardImage.sprite = newFace)
            .Append(rt.DOScaleX(width, duration * 0.5f).SetEase(Ease.OutQuad))
            .OnComplete(() => onComplete?.Invoke());
    }

    // Where the proxy would sit, in its own anchored space, to cover the given object
    private Vector2 AnchoredPositionOf(RectTransform target)
    {
        var saved = rt.position;
        rt.position = target.TransformPoint(target.rect.center);
        var anchored = rt.anchoredPosition;
        rt.position = saved;
        return anchored;
    }

    // Takes as long as the distance at moveSpeed would, but eases out and in,
    // so the eye catches where a card leaves from and where it lands.
    // endScale: the size to grow or shrink to on the way; null keeps the size
    // it started at (a card from the deck stays deck-sized all the way)
    private IEnumerator MoveAtConstantSpeed(Vector2 targetAnchoredPos, float speedMultiplier, System.Action onComplete,
                                            float? endScale = null)
    {
        Vector2 from = rt.anchoredPosition;
        float fromScale = rt.localScale.y;
        float toScale = endScale ?? fromScale;
        float seconds = Vector2.Distance(from, targetAnchoredPos) / Mathf.Max(1f, moveSpeed * speedMultiplier);
        seconds = Mathf.Max(seconds, minMoveSeconds / Mathf.Max(1f, speedMultiplier));

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, targetAnchoredPos, k);
            rt.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);
            yield return null;
        }

        rt.anchoredPosition = targetAnchoredPos;
        rt.localScale = Vector3.one * toScale;
        onComplete?.Invoke();
    }

    private void ResetProxy()
    {
        rt.anchoredPosition = proxyIdlePosition;
        canvasGroup.alpha = 0f;
    }

    public void HideImmediate()
    {
        cardImage.raycastTarget = false;
        StopMove();
        ResetProxy();
    }
    private void StopMove()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
        KillFlip();
    }

    private void KillFlip()
    {
        if (flipTween != null && flipTween.IsActive())
            flipTween.Kill();
        flipTween = null;
        // Undo a half-done squeeze, keeping the card's size (the flip only touches x)
        if (rt != null)
            rt.localScale = Vector3.one * rt.localScale.y;
    }
}
