using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// The intro's deal: every player's hand arrives as a train of cards, back to
/// back from the draw pile like a Draw Four, and all the trains run at once,
/// one to each hand. Opponents' cards stay face down; yours turn face up on
/// the way. Hands and card counts grow as each card lands, and the deck
/// shrinks as cards leave it. Then the starting card flies to the discard pile
/// and turns over.
///
/// Many cards are in the air at once, so it doesn't use CardProxyView's single
/// proxy, only its seat points. Put it on any object in MatchScene and add it
/// to MatchIntro's steps.
/// </summary>
public class DealIntroStep : MatchIntroStep
{
    [SerializeField] private MatchView matchView;
    [SerializeField] private MatchSeatsView seatsView;
    [Tooltip("Only its seat points are used: where each player's cards land.")]
    [SerializeField] private CardProxyView cardProxy;
    [Tooltip("The panel layer the cards fly on: a full-screen RectTransform on the overlay canvas.")]
    [SerializeField] private RectTransform panelLayer;

    [Header("Timing (at normal speed)")]
    [Tooltip("Between one card of a train leaving the deck and the next.")]
    [SerializeField] private float trainGapSeconds = 0.08f;
    [Tooltip("Optional: each train sets off this long after the one before, in turn order. 0 is all at once.")]
    [SerializeField] private float trainStaggerSeconds = 0f;
    [SerializeField] private float flightSeconds = 0.45f;
    [Tooltip("Your cards turn over this far through their flight (0-1).")]
    [SerializeField, Range(0f, 1f)] private float flipAt = 0.45f;
    [SerializeField] private float flipSeconds = 0.2f;
    [Tooltip("The starting card's flight to the discard pile, before it turns over.")]
    [SerializeField] private float startingCardSeconds = 0.35f;
    [Tooltip("A beat with everything dealt before play begins.")]
    [SerializeField] private float holdSeconds = 0.2f;

    [Header("Look")]
    [Tooltip("Opponents' cards shrink to this as they reach the smaller hands at the edges.")]
    [SerializeField] private float opponentLandScale = 0.75f;

    private FlyingSprites flying;
    private int inFlight;

    // Bumped by Stop, so a deal that was cut short never carries on
    private int version;

    private static CardDatabase Cards => Services.Assets.Cards;

    public override float Seconds(MatchState state)
    {
        var order = TurnOrder(state);
        float deal = 0f;
        for (int i = 0; i < order.Count; i++)
        {
            int cards = HandSize(state, order[i]);
            if (cards > 0)
                deal = Mathf.Max(deal, i * trainStaggerSeconds + (cards - 1) * trainGapSeconds + flightSeconds);
        }

        float starting = state.TopCard != null ? startingCardSeconds + flipSeconds : 0f;
        return deal + starting + holdSeconds;
    }

    public override IEnumerator Play(MatchState state, float speed)
    {
        var deck = matchView.DrawPilePoint != null ? matchView.DrawPilePoint : cardProxy.PointFor(PlayerSeat.Deck);
        if (deck == null)
        {
            Debug.LogWarning("DealIntroStep: no draw pile or Deck seat point to deal from; skipping the deal");
            yield break;
        }

        int v = version;
        var layer = panelLayer != null ? panelLayer : (RectTransform)cardProxy.transform.parent;
        flying ??= new FlyingSprites(layer);
        var size = flying.SizeLike(deck);
        var from = flying.PointOf(deck);

        // Every card that will leave the deck, in the order they leave it
        var departures = new List<(float at, int seat, int index)>();
        var order = TurnOrder(state);
        for (int i = 0; i < order.Count; i++)
        {
            int seat = order[i];
            int cards = HandSize(state, seat);
            for (int c = 0; c < cards; c++)
                departures.Add((i * trainStaggerSeconds + c * trainGapSeconds, seat, c));
        }
        departures.Sort((a, b) => a.at.CompareTo(b.at));

        int deckCount = state.DeckBeforeDeal;
        inFlight = 0;
        float started = Time.time;

        foreach (var (at, seat, index) in departures)
        {
            // Wait for this card's moment; cards due together leave together
            while (Time.time - started < at / speed)
            {
                yield return null;
                if (v != version)
                    yield break;
            }

            matchView.ShowDeckCount(--deckCount);
            DealOne(state, seat, index, from, size, speed);
        }

        while (inFlight > 0)
        {
            yield return null;
            if (v != version)
                yield break;
        }

        if (state.TopCard != null)
        {
            matchView.ShowDeckCount(--deckCount);
            yield return TurnStartingCard(state, from, size, speed);
            if (v != version)
                yield break;
        }

        yield return new WaitForSeconds(holdSeconds / speed);
    }

    public override void Stop()
    {
        version++;
        flying?.ReleaseAll();
        inFlight = 0;
    }

    private void OnDisable() => Stop();

    private void DealOne(MatchState state, int seat, int index, Vector3 from, Vector2 size, float speed)
    {
        var point = cardProxy.PointFor(seatsView.SeatPositionFor(seat));
        bool yours = seat == state.YourSeat;
        Sprite face = yours && index < state.Hand.Count ? matchView.FaceOf(state.Hand[index]) : null;

        var card = flying.Spawn(Cards.CardBack, size, from);
        var rect = card.rectTransform;
        inFlight++;

        float seconds = flightSeconds / speed;
        var to = point != null ? flying.PointOf(point) : from;
        var flight = DOTween.Sequence()
            .SetTarget(rect) // so releasing the card kills its flight too
            .Append(rect.DOMove(to, seconds).SetEase(Ease.OutCubic));

        if (face != null)
        {
            // Squeeze flat, show the face, open again, all while still flying
            float flip = Mathf.Min(flipSeconds / speed, seconds);
            float at = Mathf.Clamp(seconds * flipAt, 0f, seconds - flip);
            flight.Insert(at, rect.DOScaleX(0f, flip * 0.5f).SetEase(Ease.InQuad))
                  .InsertCallback(at + flip * 0.5f, () => card.sprite = face)
                  .Insert(at + flip * 0.5f, rect.DOScaleX(1f, flip * 0.5f).SetEase(Ease.OutQuad));
        }
        else if (!yours)
        {
            flight.Join(rect.DOScale(opponentLandScale, seconds).SetEase(Ease.InQuad));
        }

        flight.OnComplete(() =>
        {
            flying.Release(card);
            inFlight--;
            matchView.ShowDealt(seat, index + 1);
            seatsView.ShowIntroCardCount(seat, index + 1);

            // Four trains landing at once would be a clatter: your own cards set the beat
            if (yours)
                Services.Audio?.Play(Sfx.CardDrawn);
        });
    }

    /// <summary>Face down from the deck to the discard pile, then turned over.</summary>
    private IEnumerator TurnStartingCard(MatchState state, Vector3 from, Vector2 size, float speed)
    {
        int v = version;
        var card = flying.Spawn(Cards.CardBack, size, from);
        var rect = card.rectTransform;
        var face = matchView.FaceOf(state.TopCard);
        float flip = flipSeconds / speed;

        bool done = false;
        DOTween.Sequence()
            .SetTarget(rect)
            .Append(rect.DOMove(flying.PointOf(matchView.DiscardPilePoint), startingCardSeconds / speed)
                .SetEase(Ease.OutCubic))
            .Append(rect.DOScaleX(0f, flip * 0.5f).SetEase(Ease.InQuad))
            .AppendCallback(() => card.sprite = face)
            .Append(rect.DOScaleX(1f, flip * 0.5f).SetEase(Ease.OutQuad))
            .OnComplete(() => done = true);

        while (!done)
        {
            if (v != version)
                yield break;
            yield return null;
        }

        matchView.ShowStartingCard();
        flying.Release(card);
        Services.Audio?.Play(Sfx.CardPlayed);
    }

    // Your hand is known card by card; everyone else's only as a count
    private static int HandSize(MatchState state, int seat)
    {
        if (seat == state.YourSeat)
            return state.Hand.Count;
        return state.SeatAt(seat)?.CardCount ?? 0;
    }
}
