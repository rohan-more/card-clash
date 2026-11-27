package main

import (
	"encoding/json"

	"github.com/heroiclabs/nakama-common/runtime"
)

// SendEmoteReq is a player picking an emote (OpSendEmote, op 9), sent from
// the client to the server. Emote is a number from the C# Emote enum in
// Assets/Scripts/Data/EmoteLibrary.cs. There's no seat: the server works out
// who sent it from the sender's user ID, so a client can't emote for someone
// else. Mirrors SendEmoteReq in Assets/Scripts/Network/MatchMessages.cs.
type SendEmoteReq struct {
	Emote int `json:"emote"`
}

// EmoteMsg tells the other players that a seat sent an emote (OpEmote,
// op 105), sent from the server to every connected player except the sender.
// The sender has already shown it locally. No omitempty on either field:
// seat 0 and emote 0 (Laugh) are real values, and must still be sent.
// Mirrors EmoteMsg in Assets/Scripts/Network/MatchMessages.cs.
type EmoteMsg struct {
	Seat  int `json:"seat"`
	Emote int `json:"emote"`
}

// emoteCount is how many emotes exist: the C# Emote enum in
// Assets/Scripts/Data/EmoteLibrary.cs, numbered 0 to emoteCount-1. The server
// refuses any number outside that range, so a modified client can't send an
// emote the other players don't have.
//
// Keep it in step with the enum: when an emote is added there, raise this
// too, or the server will refuse the new one and nobody else will see it.
const emoteCount = 8

// validEmote reports whether emote is one of the emotes that exist.
func validEmote(emote int) bool {
	return emote >= 0 && emote < emoteCount
}

// tickEmoteCooldowns counts every seat's emote cooldown down by one tick,
// stopping at 0. Called once per tick from loop.
func (s *MatchState) tickEmoteCooldowns() {
	for i := range s.emoteCooldownTicks {
		if s.emoteCooldownTicks[i] > 0 {
			s.emoteCooldownTicks[i]--
		}
	}
}

// handleEmote checks a SEND_EMOTE from seat and, if it passes, starts that
// seat's cooldown and sends the emote to everyone else. A refused emote is
// dropped without an ERROR reply: it isn't worth one.
func (s *MatchState) handleEmote(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int, m runtime.MatchData) {
	if s.phase != phasePlaying || s.game == nil || s.preMatchTicks > 0 {
		return
	}
	var req SendEmoteReq
	if err := json.Unmarshal(m.GetData(), &req); err != nil {
		return
	}
	if !validEmote(req.Emote) {
		return
	}
	if s.emoteCooldownTicks[seat] > 0 {
		return
	}

	s.emoteCooldownTicks[seat] = s.cfg.EmoteCooldownMs / msPerTick

	s.broadcastEmote(logger, dispatcher, seat, req.Emote)
}

// broadcastEmote tells every connected player, except the one who sent it,
// that seat from sent this emote. The sender's own client showed it already,
// when they tapped it. Bots have no client, so they're skipped too.
func (s *MatchState) broadcastEmote(logger runtime.Logger, dispatcher runtime.MatchDispatcher, from int, emote int) {
	// Everyone gets the same message, so it's turned into JSON once.
	msg := EmoteMsg{Seat: from, Emote: emote}
	data, err := json.Marshal(msg)
	if err != nil {
		logger.WithField("error", err.Error()).Error("encode emote")
		return
	}

	// Who to send it to: every human seat with a connection, except the sender.
	var to []runtime.Presence
	for i, st := range s.seats {
		if i == from {
			continue // they've seen it
		}
		if st.kind != KindHuman || st.presence == nil {
			continue // a bot, an empty seat, or a player who's disconnected
		}
		to = append(to, st.presence)
	}

	// Nobody to tell, e.g. one player against bots. Sending with an empty
	// list would be wrong anyway: Nakama reads nil as "everyone".
	if len(to) == 0 {
		return
	}

	err = dispatcher.BroadcastMessage(OpEmote, data, to, nil, true)
	if err != nil {
		logger.WithField("error", err.Error()).Warn("send emote")
	}
}
