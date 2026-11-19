using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass as a popup's context to give it a countdown. When it reaches zero the
/// popup calls OnTimeout, which decides what happens and closes it.
/// </summary>
public class PopupCountdown
{
    public float TimeoutSeconds;
    public Action OnTimeout;
}

public class PopupView : MonoBehaviour
{
    [SerializeField] private PopupType popupType;
    public PopupType Type => popupType;

    [Tooltip("High for choices the game waits on (a wild's colour, play or keep, leave?). " +
             "Low for extras like the emote picker: a Low popup can't open over a High one, " +
             "and a High one replaces it.")]
    [SerializeField] private PopupPriority priority = PopupPriority.High;
    public PopupPriority Priority => priority;

    [Header("Transition")]
    [Tooltip("Slides come in from off screen. Scale grows the popup from nothing where it is " +
             "placed (try Ease In = OutBack for a pop). A Canvas Group is added if missing: it " +
             "fades the popup, and stops taps while it animates out.")]
    [SerializeField] private PopupTransitionType transitionType = PopupTransitionType.SlideFromBottom;
    [SerializeField] private float transitionDuration = 0.35f;
    [SerializeField] private Ease easeIn = Ease.OutCubic;
    [SerializeField] private Ease easeOut = Ease.InCubic;

    [Header("Closing")]
    [Tooltip("A tap anywhere outside the popup closes it. The tap is used up, so it doesn't " +
             "also hit what's underneath. For extras like the emote picker; leave it off for " +
             "choices that need an answer.")]
    [SerializeField] private bool closeOnOutsideTap;

    [Header("Countdown (optional)")]
    [Tooltip("Filled Image that drains while a PopupCountdown runs.")]
    [SerializeField] private Image countdownFill;
    [Tooltip("Whole seconds left.")]
    [SerializeField] private TMP_Text countdownText;

    protected Action onCompleted;
    protected RectTransform rt;
    private float countdownTotal;
    private float countdownEndsAt;
    private Action onTimeout;
    private Vector2 hiddenPos;
    private Vector2 visiblePos;
    private Tween activeTween;
    private CanvasGroup canvasGroup;
    private GameObject outsideTapBlocker;
    private bool closing;
    private bool destroyWhenClosed;

    protected virtual void Awake()
    {
        // Each one hides the popup in Awake, so a second one hides it again the
        // moment Show activates it
        if (GetComponents<PopupView>().Length > 1)
            Debug.LogError($"{name} has more than one PopupView component; keep only one " +
                           "(e.g. just DrawnCardChoicePopup), or it opens hidden", this);

        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
        hiddenPos = GetHiddenPosition();
        gameObject.SetActive(false);
    }

    protected virtual void OnEnable()
    {
        if (rt != null) return;

        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
    }


    public virtual void Show(object context = null, Action onCompleted = null)
    {
        KillTween();
        gameObject.SetActive(true);
        this.onCompleted = onCompleted;
        closing = false;
        destroyWhenClosed = false;
        var group = Group();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        activeTween = TransitionIn();

        if (closeOnOutsideTap)
            AddOutsideTapBlocker();

        var countdown = context as PopupCountdown;
        onTimeout = countdown != null && countdown.TimeoutSeconds > 0f ? countdown.OnTimeout : null;
        countdownTotal = countdown?.TimeoutSeconds ?? 0f;
        countdownEndsAt = Time.time + countdownTotal;

        bool counting = onTimeout != null;
        if (countdownFill != null)
            countdownFill.gameObject.SetActive(counting);
        if (countdownText != null)
            countdownText.gameObject.SetActive(counting);
    }

    protected virtual void Update()
    {
        if (onTimeout == null)
            return;

        float left = Mathf.Max(0f, countdownEndsAt - Time.time);
        if (countdownFill != null)
            countdownFill.fillAmount = countdownTotal > 0f ? left / countdownTotal : 0f;
        if (countdownText != null)
            countdownText.text = Mathf.CeilToInt(left).ToString();

        if (left <= 0f)
        {
            var timedOut = onTimeout;
            onTimeout = null; // once
            timedOut.Invoke();
        }
    }

    protected void Complete()
    {
        onCompleted?.Invoke();
        onCompleted = null;
    }

    public virtual void Hide()
    {
        onTimeout = null; // answered or closed: the countdown no longer matters
        RemoveOutsideTapBlocker();

        // It stays on screen while it animates out, but no longer answers: a
        // second tap on Play mustn't act again, or close the next popup
        closing = true;
        var group = Group();
        group.interactable = false;
        group.blocksRaycasts = false;

        KillTween();
        activeTween = TransitionOut();
        if (activeTween != null)
            activeTween.OnComplete(Closed);
        else
            Closed();
        Complete();
    }

    /// <summary>
    /// For PopupManager: destroy this popup once its exit animation has
    /// played, or now if it isn't animating out.
    /// </summary>
    public void DestroyWhenClosed()
    {
        if (closing && activeTween != null && activeTween.IsActive())
            destroyWhenClosed = true;
        else
            Destroy(gameObject);
    }

    private void Closed()
    {
        closing = false;
        if (destroyWhenClosed)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    // ---- transitions ----

    private Tween TransitionIn()
    {
        switch (transitionType)
        {
            case PopupTransitionType.None:
                return null;

            case PopupTransitionType.Scale:
                rt.localScale = Vector3.zero;
                return rt.DOScale(1f, transitionDuration).SetEase(easeIn);

            case PopupTransitionType.Fade:
                var group = Group();
                group.alpha = 0f;
                group.blocksRaycasts = true;
                return group.DOFade(1f, transitionDuration).SetEase(easeIn);

            default: // the slides
                rt.anchoredPosition = hiddenPos;
                return rt.DOAnchorPos(visiblePos, transitionDuration).SetEase(easeIn);
        }
    }

    private Tween TransitionOut()
    {
        switch (transitionType)
        {
            case PopupTransitionType.None:
                return null;

            case PopupTransitionType.Scale:
                return rt.DOScale(0f, transitionDuration).SetEase(easeOut);

            case PopupTransitionType.Fade:
                var group = Group();
                group.blocksRaycasts = false; // no taps while it fades
                return group.DOFade(0f, transitionDuration).SetEase(easeOut);

            default: // the slides
                return rt.DOAnchorPos(GetExitPosition(), transitionDuration).SetEase(easeOut);
        }
    }

    private CanvasGroup Group()
    {
        if (canvasGroup == null && !TryGetComponent(out canvasGroup))
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        return canvasGroup;
    }

    private Vector2 GetHiddenPosition()
    {
        return GetOffsetPosition(-1);
    }

    private Vector2 GetExitPosition()
    {
        return GetOffsetPosition(1);
    }

    private Vector2 GetOffsetPosition(int direction)
    {
        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
        float h = ((RectTransform)rt.parent).rect.height;
        return visiblePos + GetTransitionDirection() * h * direction;
    }

    private Vector2 GetTransitionDirection()
    {
        if (transitionType == PopupTransitionType.SlideFromBottom)
            return Vector2.up;

        if (transitionType == PopupTransitionType.SlideFromTop)
            return Vector2.down;

        return Vector2.zero;
    }

    // ---- tap outside to close ----

    // An invisible button the size of the screen, just behind the popup, so it
    // catches every tap that misses the popup
    private void AddOutsideTapBlocker()
    {
        RemoveOutsideTapBlocker();

        var parent = (RectTransform)rt.parent;
        var canvas = GetComponentInParent<Canvas>();
        var screen = canvas != null ? (RectTransform)canvas.rootCanvas.transform : parent;

        outsideTapBlocker = new GameObject($"{name} (outside tap)",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

        var blocker = (RectTransform)outsideTapBlocker.transform;
        blocker.SetParent(parent, false);
        blocker.SetSiblingIndex(rt.GetSiblingIndex()); // just behind the popup
        blocker.anchorMin = blocker.anchorMax = blocker.pivot = new Vector2(0.5f, 0.5f);
        blocker.position = screen.TransformPoint(screen.rect.center);
        Vector3 screenScale = screen.lossyScale, parentScale = parent.lossyScale;
        blocker.sizeDelta = new Vector2(screen.rect.width * screenScale.x / parentScale.x,
                                        screen.rect.height * screenScale.y / parentScale.y);

        outsideTapBlocker.GetComponent<LayoutElement>().ignoreLayout = true;

        var image = outsideTapBlocker.GetComponent<Image>();
        image.color = Color.clear; // invisible, but still catches taps

        var button = outsideTapBlocker.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OutsideTapped);
    }

    private void RemoveOutsideTapBlocker()
    {
        if (outsideTapBlocker != null)
            Destroy(outsideTapBlocker);
        outsideTapBlocker = null;
    }

    // Close, unless something else has taken over the slot meanwhile
    protected virtual void OutsideTapped()
    {
        if (Services.Popups != null && Services.Popups.ActiveType == Type)
            Services.Popups.CloseActive();
    }

    // Destroyed after its exit animation, or with the scene part way through one
    protected virtual void OnDestroy()
    {
        KillTween();
        RemoveOutsideTapBlocker();
    }

    private void KillTween()
    {
        if (activeTween != null && activeTween.IsActive())
            activeTween.Kill();
    }
}


public enum PopupType
{
    None = 0,
    ChooseColor,
    ConfirmAction,
    Info,
    DrawnCardChoice,
    EmotePicker // add new types at the end: prefabs store the number
}

public enum PopupTransitionType
{
    None,
    SlideFromBottom,
    SlideFromTop,
    Fade,
    Scale
}

/// <summary>
/// Which popup gives way to which: see PopupManager. Prefabs store the number;
/// the gap leaves room for a level in between.
/// </summary>
public enum PopupPriority
{
    Low = 10,  // extras, e.g. the emote picker
    High = 20, // choices the game waits on
}
