using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Emotes the game shows for players, on cue:
/// - Laugh on whoever plays a Draw Four;
/// - Crying on whoever ends up drawing from a Draw Two or Draw Four (after
///   any stacking);
/// - Angry on whoever gets skipped;
/// - Thinking on a player who has used half their turn without moving;
/// - Cool on the winner.
///
/// Every screen works these out for itself from the match events it already
/// gets, as each event plays: so they line up with the animations, all four
/// screens show the same, and the server sends nothing extra. Bots get them
/// the same as people.
///
/// They go through EmoteView.ShowReaction, which never replaces an emote a
/// player picked themselves. Put it next to EmoteView.
/// </summary>
public class EmoteReactions : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private EmoteView emoteView;

    [Header("Which")]
    [SerializeField] private bool laughOnDrawFour = true;
    [Tooltip("Crying when someone draws what a Draw Two or Draw Four (or a stack of them) left them owing.")]
    [FormerlySerializedAs("cryOnDrawingFour")]
    [SerializeField] private bool cryOnDrawPenalty = true;
    [SerializeField] private bool angryWhenSkipped = true;
    [SerializeField] private bool thinkWhenSlow = true;
    [SerializeField] private bool coolOnWin = true;

    [Header("Thinking")]
    [Tooltip("Show Thinking once this much of a player's turn has gone by without a move: " +
             "0.5 is halfway. Bots never take that long, so only people get it.")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float thinkAfterFraction = 0.5f;

    // The seat whose turn it is and how long they have, while they might still
    // get Thinking; -1 once they've moved or already got it
    private int thinkingSeat = -1;
    private float thinkingTurnSeconds;

    // The seat that owes cards from a draw card, and how many
    private int owingSeat = -1;
    private int owedCards;

    private MatchState State => presenter.State;

    private void OnEnable()
    {
        if (presenter == null)
            return;
        presenter.OnSnapshot += HandleSnapshot;
        presenter.OnCardPlayed += HandleCardPlayed;
        presenter.OnCardsDrawn += HandleCardsDrawn;
        presenter.OnPlayerSkipped += HandlePlayerSkipped;
        presenter.OnTurnStarted += HandleTurnStarted;
        presenter.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        if (presenter == null)
            return;
        presenter.OnSnapshot -= HandleSnapshot;
        presenter.OnCardPlayed -= HandleCardPlayed;
        presenter.OnCardsDrawn -= HandleCardsDrawn;
        presenter.OnPlayerSkipped -= HandlePlayerSkipped;
        presenter.OnTurnStarted -= HandleTurnStarted;
        presenter.OnGameOver -= HandleGameOver;
    }

    // A snapshot (joining, rejoining, a resync) starts from scratch: no
    // reactions to what happened before it
    private void HandleSnapshot(MatchState _)
    {
        thinkingSeat = -1;
        owingSeat = -1;
    }

    private void HandleCardPlayed(MatchPresenter.CardPlayed played)
    {
        if (played.Seat == thinkingSeat)
            thinkingSeat = -1; // they moved

        // Stacking passes the debt on: whoever owed it no longer does
        owingSeat = -1;

        if (laughOnDrawFour && IsDrawFour(played.Card))
            React(played.Seat, Emote.Laugh);
    }

    private void HandleCardsDrawn(MatchPresenter.CardsDrawn drawn)
    {
        if (drawn.Seat == thinkingSeat)
            thinkingSeat = -1; // they moved

        // Drawing what they owed from a Draw Two or Draw Four (a stack is
        // always one kind, and the top card says which)
        bool payingDebt = drawn.Seat == owingSeat && drawn.Count >= owedCards;
        if (payingDebt)
        {
            owingSeat = -1;
            if (cryOnDrawPenalty && IsDrawCard(State.TopCard))
                React(drawn.Seat, Emote.Crying);
        }
    }

    private void HandlePlayerSkipped(int seat)
    {
        if (angryWhenSkipped)
            React(seat, Emote.Angry);
    }

    private void HandleTurnStarted(MatchPresenter.TurnStarted turn)
    {
        // A bot's turn has no clock (TurnMs 0), so bots never think
        thinkingSeat = thinkWhenSlow && turn.TurnMs > 0 ? turn.Seat : -1;
        thinkingTurnSeconds = turn.TurnMs / 1000f;

        owingSeat = turn.PendingDraw > 0 ? turn.Seat : -1;
        owedCards = turn.PendingDraw;
    }

    private void HandleGameOver(int[] places)
    {
        thinkingSeat = -1;
        owingSeat = -1;

        if (coolOnWin && places != null && places.Length > 0)
            React(places[0], Emote.Cool);
    }

    private void Update()
    {
        if (thinkingSeat < 0 || presenter == null)
            return;

        // The turn moved on without us hearing about a move (a resync, say)
        if (State.IsOver || State.CurrentSeat != thinkingSeat)
        {
            thinkingSeat = -1;
            return;
        }

        // The clock includes the pause before the turn starts, so count down
        // from the turn's own length: halfway is half of that left
        float left = presenter.SecondsLeftInTurn;
        if (left <= thinkingTurnSeconds * (1f - thinkAfterFraction))
        {
            React(thinkingSeat, Emote.Thinking);
            thinkingSeat = -1; // once a turn
        }
    }

    private void React(int seat, Emote emote)
    {
        // Not during the intro, which replays the start of the match
        if (emoteView == null || presenter.IntroPlaying)
            return;
        emoteView.ShowReaction(seat, emote);
    }

    private static bool IsDrawFour(CardInstance card) => TypeOf(card) == CardType.WildDrawFour;

    private static bool IsDrawCard(CardInstance card)
    {
        var type = TypeOf(card);
        return type == CardType.DrawTwo || type == CardType.WildDrawFour;
    }

    private static CardType? TypeOf(CardInstance card)
    {
        var cards = Services.Assets != null ? Services.Assets.Cards : null;
        var def = card != null && cards != null ? card.GetDefinition(cards) : null;
        return def?.Type;
    }
}
