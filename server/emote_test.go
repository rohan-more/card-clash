package main

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/heroiclabs/nakama-common/runtime"
)

func TestEmoteMsg_KeepsZeroValues(t *testing.T) {
	msg := EmoteMsg{Seat: 0, Emote: 0}

	data, err := json.Marshal(msg)

	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	want := `{"seat":0,"emote":0}`
	got := string(data)

	if want != got {
		t.Errorf("marshal: want %q, got %q", want, got)
	}
}

func TestValidEmote(t *testing.T) {
	tests := []struct {
		name  string
		emote int
		want  bool
	}{
		{name: "negative emote", emote: -1, want: false},
		{name: "valid emote_1", emote: 0, want: true},
		{name: "valid emote_2", emote: 7, want: true},
		{name: "invalid emote", emote: 8, want: false},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			got := validEmote(test.emote)
			if got != test.want {
				t.Errorf("validEmote(%d): want %v, got %v", test.emote, test.want, got)
			}
		})
	}
}

func TestTickEmoteCooldowns_StopsAtZero(t *testing.T) {
	s := &MatchState{
		emoteCooldownTicks: [seatCount]int{1, 0, 2, 0},
	}

	s.tickEmoteCooldowns()

	want := [seatCount]int{0, 0, 1, 0}
	if s.emoteCooldownTicks != want {
		t.Errorf("tickEmoteCooldowns: want %v, got %v", want, s.emoteCooldownTicks)
	}

	s.tickEmoteCooldowns()

	want = [seatCount]int{0, 0, 0, 0}
	if s.emoteCooldownTicks != want {
		t.Errorf("tickEmoteCooldowns: want %v, got %v", want, s.emoteCooldownTicks)
	}
}

// emoteMatch is a match in play, past the intro, with two connected humans:
// u0 in seat 0 and u2 in seat 2. Seats 1 and 3 are bots.
func emoteMatch(t *testing.T) (*MatchState, *fakeDispatcher, fakePresence) {
	t.Helper()
	s := dealtMatch(t)
	s.preMatchTicks = 0
	u0 := fakePresence{userID: "u0"}
	s.seats[0].presence = u0
	s.seats[2].presence = fakePresence{userID: "u2"}
	return s, &fakeDispatcher{}, u0
}

// sendEmote has one player send SEND_EMOTE with this body, through the real
// handleMessages, as if it had arrived from their client.
func sendEmote(s *MatchState, d *fakeDispatcher, from fakePresence, body string) {
	message := fakeMatchData{
		fakePresence: from,
		opCode:       OpSendEmote,
		data:         []byte(body),
	}
	s.handleMessages(context.Background(), testLogger{}, nil, d, []runtime.MatchData{message})
}

// emotes returns every EMOTE message the handler sent, with who it went to.
func (d *fakeDispatcher) emotes() []sentMessage {
	var found []sentMessage
	for _, msg := range d.sent {
		if msg.opCode == OpEmote {
			found = append(found, msg)
		}
	}
	return found
}

func TestEmote_ReachesOthersNotSender(t *testing.T) {
	s, d, u0 := emoteMatch(t)
	sendEmote(s, d, u0, `{"emote":3}`)
	sent := d.emotes()
	if len(sent) != 1 {
		t.Fatalf("EMOTE messages = %d, want 1", len(sent))
	}
	msg := sent[0]
	if len(msg.to) != 1 || msg.to[0].GetUserId() != "u2" {
		t.Errorf("sent to %v, want only u2", msg.to)
	}
	var got EmoteMsg
	if err := json.Unmarshal(msg.data, &got); err != nil {
		t.Fatalf("decode EMOTE: %v", err)
	}
	if got.Seat != 0 || got.Emote != 3 {
		t.Errorf("got %+v, want seat 0, emote 3", got)
	}
}

func TestEmote_BadMessageIsDropped(t *testing.T) {
	tests := []struct {
		name string
		body string
	}{
		{name: "below the first", body: `{"emote":-1}`},
		{name: "past the last", body: `{"emote":8}`},
		{name: "not json", body: `not json`},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			s, d, u0 := emoteMatch(t)
			sendEmote(s, d, u0, test.body)
			if n := len(d.emotes()); n != 0 {
				t.Errorf("EMOTE messages = %d, want 0", n)
			}
			if n := d.count(OpError); n != 0 {
				t.Errorf("ERROR replies = %d, want 0: a dropped emote is silent", n)
			}
			if s.emoteCooldownTicks[0] != 0 {
				t.Errorf("cooldown = %d, want 0: a dropped emote mustn't start one", s.emoteCooldownTicks[0])
			}
		})
	}
}

func TestEmote_CooldownThenAllowedAgain(t *testing.T) {
	s, d, u0 := emoteMatch(t)
	sendEmote(s, d, u0, `{"emote":3}`)
	sendEmote(s, d, u0, `{"emote":4}`)
	sendEmote(s, d, fakePresence{userID: "u2"}, `{"emote":1}`) // u0 is blocked; u2 isn't
	if n := len(d.emotes()); n != 2 {
		t.Fatalf("after u0's two quick emotes and one from u2, EMOTE messages = %d, want 2", n)
	}
	ticks := s.cfg.EmoteCooldownMs / msPerTick
	for i := 0; i < ticks-1; i++ {
		s.tickEmoteCooldowns()
	}
	sendEmote(s, d, u0, `{"emote":5}`)
	if n := len(d.emotes()); n != 2 {
		t.Fatalf("one tick early, EMOTE messages = %d, want 2", n)
	}
	s.tickEmoteCooldowns()
	sendEmote(s, d, u0, `{"emote":6}`)
	if n := len(d.emotes()); n != 3 {
		t.Errorf("after the cooldown, EMOTE messages = %d, want 3", n)
	}
}

func TestEmote_DroppedDuringIntro(t *testing.T) {
	s, d, u0 := emoteMatch(t)
	s.preMatchTicks = 5 // the intro is still playing
	sendEmote(s, d, u0, `{"emote":3}`)
	if n := len(d.emotes()); n != 0 {
		t.Errorf("EMOTE messages = %d, want 0", n)
	}
	if n := d.count(OpError); n != 0 {
		t.Errorf("ERROR replies = %d, want 0: a dropped emote is silent", n)
	}
	if s.emoteCooldownTicks[0] != 0 {
		t.Errorf("cooldown = %d, want 0: a dropped emote mustn't start one", s.emoteCooldownTicks[0])
	}
}
