using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// The intro's pot, in three beats:
///   1. a stake badge (coin icon and amount, e.g. "50") fades in over every
///      player's hand;
///   2. all of them fly to the pot at once, fading out as they arrive;
///   3. the pot badge (icons and total, e.g. "200") fades in there, holds, and
///      fades out. Then the next step (the deal) begins.
/// Skipped in a free match. Nothing stays on screen afterwards.
///
/// Both badges are UI prefabs with a TMP text somewhere inside for the amount.
/// The stake prefab is cloned once per player; the pot prefab once. Put this on
/// any object in MatchScene and add it to MatchIntro's steps.
/// </summary>
public class PotIntroStep : MatchIntroStep
{
    [SerializeField] private MatchSeatsView seatsView;
    [Tooltip("Only its seat points are used: the stakes appear over each player's hand.")]
    [SerializeField] private CardProxyView cardProxy;

    [Tooltip("The panel layer the badges are made on: a full-screen RectTransform on the overlay canvas.")]
    [SerializeField] private RectTransform panelLayer;

    [Header("Prefabs")]
    [Tooltip("One player's stake: a coin icon and a TMP text for the amount. Cloned for every player.")]
    [SerializeField] private RectTransform stakePrefab;
    [Tooltip("The pot: icons and a TMP text for the total.")]
    [SerializeField] private RectTransform potPrefab;
    [Tooltip("Optional: where the pot appears and the stakes fly to. Unset: the middle of the panel layer.")]
    [SerializeField] private RectTransform potPoint;

    [Header("Text")]
    [Tooltip("{0} is the stake, e.g. \"{0}\" or \"+{0}\".")]
    [SerializeField] private string stakeFormat = "{0}";
    [Tooltip("{0} is the pot, e.g. \"{0}\" or \"Pot {0}\".")]
    [SerializeField] private string potFormat = "{0}";

    [Header("Timing (at normal speed)")]
    [SerializeField] private float stakeFadeInSeconds = 0.4f;
    [Tooltip("The stakes sit over the hands this long before flying.")]
    [SerializeField] private float stakeHoldSeconds = 1f;
    [SerializeField] private float flightSeconds = 0.7f;
    [Tooltip("The stakes fade out over this last part of their flight (0-1).")]
    [SerializeField, Range(0f, 1f)] private float fadeOutFrom = 0.6f;
    [SerializeField] private float potFadeInSeconds = 0.35f;
    [SerializeField] private float potHoldSeconds = 1.5f;
    [SerializeField] private float potFadeOutSeconds = 0.35f;

    [Header("Look")]
    [Tooltip("The stakes shrink to this as they reach the pot.")]
    [SerializeField] private float stakeArriveScale = 0.6f;
    [Tooltip("The pot grows in from this.")]
    [SerializeField] private float potStartScale = 0.8f;

    [Header("Testing")]
    [Tooltip("Quick matches are free, so they have no pot. Above 0, they play the pot anyway as if " +
             "every seat staked this much. Editor and development builds only.")]
    [SerializeField] private long freeMatchTestStake;

    private readonly List<RectTransform> stakes = new List<RectTransform>();
    private RectTransform pot;

    // Bumped by Stop, so a pot that was cut short never carries on
    private int version;

    private RectTransform Layer => panelLayer != null ? panelLayer : (RectTransform)transform;

    public override bool Applies(MatchState state) => StakeOf(state) > 0;

    // The real stake and pot, or the test stake's in a free match
    private long StakeOf(MatchState state)
    {
        if (state.Stake > 0)
            return state.Stake;
        return Debug.isDebugBuild ? freeMatchTestStake : 0;
    }

    private long PotOf(MatchState state) => state.Pot > 0 ? state.Pot : StakeOf(state) * state.Seats.Length;

    public override float Seconds(MatchState state)
    {
        return stakeFadeInSeconds + stakeHoldSeconds + flightSeconds
               + potFadeInSeconds + potHoldSeconds + potFadeOutSeconds;
    }

    public override IEnumerator Play(MatchState state, float speed)
    {
        if (stakePrefab == null || potPrefab == null)
        {
            Debug.LogWarning("PotIntroStep: needs a Stake Prefab and a Pot Prefab; skipping the pot");
            yield break;
        }

        int v = version;
        var potAt = potPoint != null ? FlyingSprites.PointOn(Layer, potPoint) : Layer.TransformPoint(Layer.rect.center);

        // 1. A stake over every hand
        var order = TurnOrder(state);
        var shown = new List<RectTransform>();
        for (int i = 0; i < order.Count; i++)
        {
            var hand = cardProxy.PointFor(seatsView.SeatPositionFor(order[i]));
            if (hand == null)
                continue;

            var stake = StakeAt(i);
            stake.position = FlyingSprites.PointOn(Layer, hand);
            stake.localScale = Vector3.one;
            SetText(stake, string.Format(stakeFormat, CoinFormat.Short(StakeOf(state))));
            var group = GroupOf(stake);
            group.alpha = 0f;
            stake.gameObject.SetActive(true);
            group.DOFade(1f, stakeFadeInSeconds / speed).SetTarget(stake);
            shown.Add(stake);
        }

        yield return new WaitForSeconds((stakeFadeInSeconds + stakeHoldSeconds) / speed);
        if (v != version)
            yield break;

        // 2. All of them into the pot at once, fading as they arrive
        float flight = flightSeconds / speed;
        foreach (var stake in shown)
        {
            DOTween.Sequence()
                .SetTarget(stake)
                .Append(stake.DOMove(potAt, flight).SetEase(Ease.InCubic))
                .Join(stake.DOScale(stakeArriveScale, flight).SetEase(Ease.InQuad))
                .Insert(flight * fadeOutFrom, GroupOf(stake).DOFade(0f, flight * (1f - fadeOutFrom)));
        }

        yield return new WaitForSeconds(flight);
        if (v != version)
            yield break;

        HideStakes();
        Services.Audio?.Play(Sfx.Coins);

        // 3. The pot appears with the total, then goes
        pot ??= Make(potPrefab, "Pot");
        pot.position = potAt;
        pot.localScale = Vector3.one * potStartScale;
        SetText(pot, string.Format(potFormat, CoinFormat.Short(PotOf(state))));
        var potGroup = GroupOf(pot);
        potGroup.alpha = 0f;
        pot.gameObject.SetActive(true);

        bool done = false;
        DOTween.Sequence()
            .SetTarget(pot)
            .Append(potGroup.DOFade(1f, potFadeInSeconds / speed))
            .Join(pot.DOScale(1f, potFadeInSeconds / speed).SetEase(Ease.OutBack))
            .AppendInterval(potHoldSeconds / speed)
            .Append(potGroup.DOFade(0f, potFadeOutSeconds / speed))
            .OnComplete(() => done = true);

        while (!done)
        {
            if (v != version)
                yield break;
            yield return null;
        }

        HidePot();
    }

    public override void Stop()
    {
        version++;
        HideStakes();
        HidePot();
    }

    private void OnDisable() => Stop();

    // One clone of the stake prefab per player, made the first time and reused
    private RectTransform StakeAt(int index)
    {
        while (stakes.Count <= index)
            stakes.Add(Make(stakePrefab, $"Stake{stakes.Count}"));
        return stakes[index];
    }

    private RectTransform Make(RectTransform prefab, string name)
    {
        var copy = Instantiate(prefab, Layer);
        copy.name = name;
        copy.anchorMin = copy.anchorMax = new Vector2(0.5f, 0.5f);
        copy.gameObject.SetActive(false);
        return copy;
    }

    private void HideStakes()
    {
        foreach (var stake in stakes)
        {
            if (stake == null)
                continue;
            DOTween.Kill(stake); // its fade and flight
            stake.gameObject.SetActive(false);
        }
    }

    private void HidePot()
    {
        if (pot == null)
            return;
        DOTween.Kill(pot);
        pot.gameObject.SetActive(false);
    }

    private static CanvasGroup GroupOf(RectTransform rect)
    {
        var group = rect.GetComponent<CanvasGroup>();
        if (group == null)
            group = rect.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false; // never catches a tap meant for the cards
        return group;
    }

    private static void SetText(RectTransform badge, string text)
    {
        var label = badge.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = text;
    }
}
