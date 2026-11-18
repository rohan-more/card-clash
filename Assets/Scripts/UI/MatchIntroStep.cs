using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One part of the match intro, e.g. the pot or the deal. MatchIntro runs its
/// steps in order: every step's Prepare first, then each Play in turn, then
/// every Finish. Stop can come at any point and must leave nothing on screen
/// that the dealt table wouldn't show.
/// </summary>
public abstract class MatchIntroStep : MonoBehaviour
{
    /// <summary>Whether this step belongs in this match's intro, e.g. no pot in a free match.</summary>
    public virtual bool Applies(MatchState state) => true;

    /// <summary>How long Play takes at normal speed, so the intro can fit the server's pause.</summary>
    public abstract float Seconds(MatchState state);

    /// <summary>Before any step plays: hide what hasn't happened yet.</summary>
    public virtual void Prepare(MatchState state) { }

    /// <summary>
    /// The animation. speed is 1 normally and higher when time is short: divide
    /// every duration by it.
    /// </summary>
    public abstract IEnumerator Play(MatchState state, float speed);

    /// <summary>After the last step: leave the step's result showing, e.g. the pot.</summary>
    public virtual void Finish(MatchState state) { }

    /// <summary>Cut short: stop moving and clear anything only the intro shows.</summary>
    public virtual void Stop() { }

    /// <summary>
    /// Server seats in turn order, starting with whoever plays first. Every
    /// client goes round in the same order, each from their own seat.
    /// </summary>
    protected static List<int> TurnOrder(MatchState state)
    {
        var order = new List<int>();
        int n = state.Seats.Length;
        if (n == 0)
            return order;

        int step = state.Direction >= 0 ? 1 : -1;
        int seat = ((state.CurrentSeat % n) + n) % n;
        for (int i = 0; i < n; i++)
        {
            order.Add(seat);
            seat = ((seat + step) % n + n) % n;
        }
        return order;
    }
}
