using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The opening of a match: an ordered list of intro steps (the pot, the deal)
/// played one after another while the server pauses before the first turn.
/// Reorder or remove steps in the Inspector to change the intro; no code.
///
/// MatchPresenter decides when it plays (a fresh deal only, never a rejoin)
/// and holds every event until it ends. The intro always fits the server's
/// pause: if the steps would take longer than the time left, they all speed up.
/// Put it on any object in MatchScene and give it to MatchPresenter's Intro.
/// </summary>
public class MatchIntro : MonoBehaviour
{
    [Tooltip("Played in this order. A step that doesn't apply (the pot in a free match) is skipped.")]
    [SerializeField] private List<MatchIntroStep> steps = new List<MatchIntroStep>();

    [Tooltip("Finish this long before the first turn is due, to allow for network delay.")]
    [SerializeField] private float safetyMarginSeconds = 0.4f;

    [Tooltip("No intro at all with less time than this left: the table just appears.")]
    [SerializeField] private float minimumSeconds = 1.5f;

    [Tooltip("Steps never run faster than this many times their normal speed; " +
             "below that much time, there's no intro.")]
    [SerializeField] private float maxSpeedUp = 2.5f;

    private Coroutine running;
    private readonly List<MatchIntroStep> playing = new List<MatchIntroStep>();

    /// <summary>Whether there's an intro worth playing in the time before the first turn.</summary>
    public bool CanPlay(MatchState state, float secondsToFirstTurn)
    {
        float available = secondsToFirstTurn - safetyMarginSeconds;
        if (available < minimumSeconds)
            return false;

        float total = TotalSeconds(state, out int count);
        return count > 0 && total / available <= maxSpeedUp;
    }

    /// <summary>Plays every step that applies, then calls onDone. MatchPresenter calls this.</summary>
    public void Play(MatchState state, float secondsToFirstTurn, Action onDone)
    {
        Stop();

        playing.Clear();
        foreach (var step in steps)
        {
            if (step != null && step.Applies(state))
                playing.Add(step);
        }

        // Everything is hidden before anything moves, e.g. the pot before the deal
        foreach (var step in playing)
            step.Prepare(state);

        float available = Mathf.Max(0.1f, secondsToFirstTurn - safetyMarginSeconds);
        float total = TotalSeconds(state, out _);
        float speed = Mathf.Max(1f, total / available);
        if (speed > 1f)
            Debug.Log($"[INTRO] {total:0.0}s of intro in {available:0.0}s: playing at {speed:0.00}x");

        running = StartCoroutine(Run(state, speed, onDone));
    }

    /// <summary>Cuts the intro short and clears anything still on screen. Safe to call any time.</summary>
    public void Stop()
    {
        if (running != null)
            StopCoroutine(running);
        running = null;

        foreach (var step in playing)
            step.Stop();
        playing.Clear();
    }

    private IEnumerator Run(MatchState state, float speed, Action onDone)
    {
        foreach (var step in playing)
            yield return step.Play(state, speed);

        foreach (var step in playing)
            step.Finish(state);

        running = null;
        playing.Clear();
        onDone?.Invoke();
    }

    private float TotalSeconds(MatchState state, out int count)
    {
        float total = 0f;
        count = 0;
        foreach (var step in steps)
        {
            if (step == null || !step.Applies(state))
                continue;
            total += step.Seconds(state);
            count++;
        }
        return total;
    }

    private void OnDisable() => Stop();
}
