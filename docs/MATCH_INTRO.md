# Match intro

Status: **built and working.** Planned 2026-11-11, built 2026-11-18. The scene
setup is described in [Setting it up](#setting-it-up).

## Why

When a match opens, the cards are simply there and the stake is never shown.
Players don't see what they're playing for, or the cards being dealt. Two
short animations at the start fix that:

1. **The pot.** A stake badge (coin icon and amount, e.g. 50) fades in over
   every player's hand. They all fly to the centre at once, fading out as they
   arrive. Then a pot badge with the total (e.g. 200) fades in, holds, and
   fades out.
2. **The deal.** Each hand arrives as a train of 7 cards, back to back from the
   draw pile like a Draw Four, with all four trains running at once: face down
   to opponents, flipping face up on the way to your hand. Then the starting
   card turns over onto the discard pile.

**The order must be flexible:** pot then deal, deal then pot, or one alone. It's
a setting, not code.

## Shape

- **`MatchIntro`** in MatchScene runs an ordered list of **intro steps** set in
  the Inspector, one after another, then lets play begin. Reordering the list
  reorders the intro.
- Each step is a component with its own animation, like the special-card
  panels (`TableEventPanel`):
  - `PotIntroStep`: stake badges over the hands, flying to the centre, then
    the pot badge with the total. Nothing stays on screen afterwards.
  - `DealIntroStep`: one train of cards per player, all at once. Hands and
    card counts grow as cards land.
- The steps read the snapshot that started the match: seats, `stake`, `pot`, and
  your hand (from `GAME_STATE`). No new server data is needed.

## Things to get right

- **Nobody plays until every intro is done.** `MatchSceneLoader` opens
  MatchScene as soon as the deal arrives and the intro plays straight away.
  Each client then sends `INTRO_DONE` (or sends it at once if it has no intro),
  and the server starts the first turn when every connected player has. Bots
  and players who are away aren't waited for.
- **But never forever.** `preMatchSeconds` (8 s) is the longest the server
  waits, in case a client never answers. The intro is fitted inside it: if the
  steps would take longer than the time left, `MatchIntro` speeds them all up
  (up to 2.5x), and with too little time there's no intro at all.
- **Hide what hasn't happened yet.** The snapshot already contains every hand,
  so `MatchView`, the seats and the piles must hold back their cards and counts
  until the deal shows them landing.
- **Skip it when it doesn't apply.**
  - Rejoining a match in progress: no intro, straight to the table.
  - Free matches (`stake` 0): no pot step.
- **Sound:** a coin clink as the stakes reach the pot (`Sfx.Coins`), and a card
  sound as each of *your* cards lands (`Sfx.CardDrawn`); four trains landing
  at once would just be a clatter.
- **Everyone sees the same intro,** but from their own seat: stakes and cards
  move to and from each player's position on *their* screen.

## Testing

Quick matches (Instant Match on, on `NakamaConnection`) deal straight away,
so they're a fast way to see the intro. They're free, though, so they have no
pot: set Free Match Test Stake on `PotIntroStep` (e.g. 50) to play the pot in
them anyway, in the editor and development builds. Or turn Instant Match off
for a real staked match against bots.

## How it's built

- **`MatchPresenter`** decides: the first snapshot in the scene, with at least
  ~2 s before the first turn, plays the intro. While it plays, `IntroPlaying` is
  true and events queue behind it like behind any animation. When it ends (or
  is cut short by a new snapshot or a timeout) the snapshot is raised again, so
  every view redraws the dealt table.
- **The views** draw the table "before the deal" while `IntroPlaying`:
  `MatchView` shows empty hands, an empty discard pile and the full deck;
  `MatchSeatsView` shows card counts of 0.
- **`MatchIntro`** runs its steps: every step's `Prepare`, then each `Play` in
  order, then every `Finish`.
- **`PotIntroStep`**: clones the stake prefab once per player and places
  each over that player's hand (the `CardProxyView` seat points), fades them
  in, flies them all to the pot, then fades a clone of the pot prefab in and
  out there.
  Skipped when `stake` is 0.
- **`DealIntroStep`**: works out when every card leaves the deck (each train's
  cards a fixed gap apart, all trains starting together, or staggered if
  Train Stagger is set) and flies them on the panel layer (`FlyingSprites`).
  Your cards flip face up mid-flight. Hands, seat counts and the deck update as
  each card lands. Then the starting card flies to the pile and turns over.
- **Canvases.** The panel layer can be on a different canvas from the table
  (the overlay canvas): every position is mapped through the screen, and
  flying cards are sized to match the draw pile.

## Setting it up

In MatchScene:

1. **Panel layer.** In the OverlayCanvas, add an empty full-screen
   RectTransform (stretch anchors). Stakes, the pot and dealt cards all move on
   it. Give the OverlayCanvas a Sort Order above the table `Canvas` (0).
2. **Prefabs.** Two UI prefabs, each with a TMP text inside for the amount:
   the stake (a coin and the amount) and the pot (coins and the total). Keep
   them as prefab assets; don't leave copies in the scene, or they'll show.
3. **Intro object.** On any object in MatchScene (the panel layer works) add
   three components:
   - `PotIntroStep`: Seats View, Card Proxy, Panel Layer, Stake Prefab, Pot
     Prefab. Pot Point is optional: an object marking where the pot appears;
     unset, it's the middle of the panel layer.
   - `DealIntroStep`: Match View, Match Seats View, Card Proxy, Panel Layer.
   - `MatchIntro`: Steps = `PotIntroStep`, then `DealIntroStep`. Reorder the list
     to deal first; remove one to play the other alone.
4. **Presenter.** On `MatchPresenter`, set Intro to the `MatchIntro` component.
   Leaving it empty turns the intro off: the table appears dealt, as before.

The panel layer covers the whole screen above the table, so it must not catch
taps or draw anything itself: no Image on it, or one with alpha 0 and Raycast
Target off.

**Server config.** The 8 s wait is the new default, but a `config/match`
object already in storage keeps its own `preMatchSeconds`. Set it to 8 in the
Nakama console (Storage, collection `config`, key `match`). With 3 s the intro
still plays, just faster, and play may start before it ends.
