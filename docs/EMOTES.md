# Emotes: plan

Status: **client built, server not yet.** Planned 2026-11-18. The Unity side
(`EmoteLibrary`, `EmoteView`, `EmotePicker`) works locally; the Go side is next.

## Why

A match is four people who can't talk to each other. A quick reaction (a laugh
at a +4, a cry when it lands on you) makes the table feel alive, and it's the
kind of moment a demo video needs. Most online card games have them.

## What players see

- An **emote button** by your hand. Tapping it opens a small panel of 8 emotes;
  tapping one sends it and closes the panel.
- The emote **pops up over the sender's avatar** on every screen: grows in with
  a bounce, wobbles, holds about 1.5 s, fades. Yours pops up just above your
  emote button.
- A **cooldown** stops spam: about 2 s between emotes. The button shows it
  filling back up.
- **Reactions:** the game emotes for players on cue, bots and people alike,
  every time:

  | When | Emote | On |
  |---|---|---|
  | plays a Draw Four | `Laugh` | the player |
  | ends up drawing from a Draw Two or Draw Four (after any stacking) | `Crying` | that player |
  | gets skipped | `Angry` | that player |
  | half their turn gone without a move | `Thinking` | that player, once a turn |
  | wins | `Cool` | the winner |

  A reaction never replaces an emote a player picked themselves.

## The art

Microsoft **Fluent Emoji**, 3D style (github.com/microsoft/fluentui-emoji),
MIT licence: free to use; keep the licence text in the repo (a
`THIRD_PARTY.md`). It matches the 3D avatars. Eight faces from the 3D set:

| `Emote` | # | Face | For |
|---|---|---|---|
| `Laugh` | 0 | Face with tears of joy | laughing at a play |
| `Angry` | 1 | Face with symbols on mouth | angry |
| `Scared` | 2 | Face screaming in fear | a +4 coming your way |
| `Cool` | 3 | Smiling face with sunglasses | a good play, a win |
| `Crying` | 4 | Loudly crying face | drawing a pile of cards |
| `StarStruck` | 5 | Star-struck | impressed |
| `Thinking` | 6 | Thinking face | taking your time |
| `Zany` | 7 | Zany face | being cheeky |

The sprites are in `Assets/Sprites/Emojis`. The **`Emote`** enum gives every
emote a fixed number, and that number is what travels between players: a new
emote takes the next number, and a number is never changed or reused. The
**`EmoteLibrary`** ScriptableObject pairs each `Emote` with its sprite, so its
entries can be reordered freely; their order is just the order the picker
shows them in. It warns in the Inspector about a missing or doubled emote.

## Shape

**Server (Go)**
- New client message **`SEND_EMOTE`** (op 9) `{"emote": 3}`.
- New server message **`EMOTE`** (op 105) `{"seat": 2, "emote": 3}`, sent at
  once to every other human at the table. It is **not** an `EVENTS` batch: it
  has no `seq`, doesn't wait behind animations, and never affects the game.
- Checks: the match is in play, the number is one the `Emote` enum
  defines (mirrored on the server as `emoteCount`), and the seat's cooldown (`emoteCooldownSeconds`) has passed.
  Anything else is dropped quietly; an emote isn't worth an `ERROR`.
- **Nothing for reactions.** The server only relays emotes people pick.

**Client (Unity)**
- **`EmoteLibrary`**: the sprite for each `Emote`, and the picker's order.
- **`EmoteView`** on the overlay canvas: listens to `NakamaConnection`'s
  `OnMatchState` for `EMOTE` directly, not through `MatchPresenter`, so emotes
  never queue behind card animations. Pops the sprite over the seat's avatar
  (`MatchSeatsView.PanelForSeat`), with DOTween; yours pops up above your
  emote button instead. A new emote from the same seat
  replaces the one showing.
- **`EmoteReactions`**: works out the reactions from the match events every
  client already gets (`MatchPresenter`'s `OnCardPlayed`, `OnCardsDrawn`,
  `OnTurnStarted`, `OnGameOver`), as each one plays. So they line up with the
  animations, every screen shows the same (the rules have no chance in them),
  and the server sends nothing extra. They go through
  `EmoteView.ShowReaction`, which skips a seat whose own pick is still showing.
- **`EmotePicker`**: the button and its cooldown fill. Sending shows your
  emote straight away, then sends `SEND_EMOTE`; the server doesn't echo it
  back.
- **`EmotePickerPopup`**: the panel of 8, a normal popup through
  `PopupManager` (`PopupType.EmotePicker`) with an `EmoteChoice` context. It
  has **Low priority**: it can't open over a choice the game is waiting for (a
  wild's colour, play or keep, leave?), and those replace it. A tap outside it
  closes it.
- **Sound:** a soft pop (`Sfx.Emote`), quiet, so four players' emotes don't
  drown out the cards.

## Things to get right

- **Emotes never touch the game.** Separate opcode, no `seq`, no presenter step,
  so a burst of emotes can't delay a turn or cause a resync.
- **The cooldown is the server's.** The client mirrors it to grey the button,
  but the server drops anything early, so a modified client can't spam.
- **Reactions stay deterministic.** Every screen decides them alone, so a
  random roll would differ between screens. If chance is ever wanted, seed it
  from the event (its `seq`), the same on every screen.
- **Bots pass as people** because they get exactly the same reactions.
- **Hidden in the intro and results:** the picker only works once play starts.
- **Away players** can still receive emotes after rejoining, but missed ones
  aren't replayed; they're gone, like a spoken word.

## Testing

Server tests: an emote reaches the other humans and not the sender; out of
range, early or before the first turn is dropped; the cooldown resets. In
the client, two windows side by side show emotes both ways, and the same
reactions on both.

## Later

Muting a player's emotes (tap their avatar), and emotes on the results screen.

## Setting it up (Unity)

1. **Library.** Assets > Create > UNO > Emote Library. One entry per emote:
   pick the Emote, drag in its sprite. The entry order is the picker's order.
2. **EmoteView.** On the OverlayCanvas (e.g. on IntroAnimPanel): Presenter,
   Seats View, Library, Panel Layer (the intro's panel layer works). Offset
   (0, 0) puts emotes over the avatar's face. Your seat has no avatar on the
   table, so give it **Your Emote Point**: an empty RectTransform on the
   OverlayCanvas just above the emote button, where your own emotes pop up.
3. **Picker popup.** A prefab like the other popups, with `EmotePickerPopup`
   on its root:
   - Popup Type **EmotePicker**, Priority **Low**, Transition **Scale**
     (Ease In OutBack, about 0.25 s), **Close On Outside Tap** on;
   - placed in the prefab where it should appear, just above the emote button;
   - inside it, a Grid Layout Group holding one **template button** (Image +
     Button, inactive), sized like one emote: Emote Button Template.

   Add the prefab to the `PopupManager`'s Popup Prefabs in MatchScene.
4. **Emote button.** By your hand, an emote **button** (an icon, e.g. a
   smiley), with an optional child Image set to Filled / Radial 360 for the
   cooldown. Add **EmotePicker** to it: Presenter, Emote View, Library, Open
   Button, Cooldown Fill. Leave **Send To Server off** until the server
   handles `SEND_EMOTE`.
5. **Reactions.** Add **EmoteReactions** next to EmoteView: Presenter, Emote
   View. Each reaction has a tickbox; Think After Fraction sets when Thinking
   shows (0.5 is halfway through the turn).
6. **Sound (optional).** In the SoundLibrary, add an entry for `Emote` with a
   soft pop.

**Testing without the server:** your own picks show above your emote button,
and the reactions show for everyone, bots included.

