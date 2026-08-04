using System;
using UnityEngine;

// Shared quantized clock for the sequencer panel. All buttons schedule their
// playback through this clock so simultaneous presses stay phase-locked
// instead of layering with arbitrary human-timing drift. New presses snap to
// the next full BAR boundary (not the next single beat) so the wait is always
// clearly perceptible rather than sometimes too short to notice. The clock is
// considered idle only while nothing is currently looping — as long as at
// least one button is still playing, any later press (no matter how much
// real time has passed) snaps to the existing beat grid instead of
// restarting it.
public class SequencerClock : MonoBehaviour
{
    [SerializeField] private float bpm = 130f;
    [SerializeField] private int beatsPerBar = 4;

    // PlayScheduled needs its target dsp time to still be in the future by the time the
    // audio thread actually reads it — scheduling for "right now" is frequently already
    // slightly in the past by then, which is what made loops start unpredictably (out of
    // sync with each other) instead of exactly on the beat. This margin fixes that for
    // both the cold-start anchor and every later quantized candidate below.
    private const double ScheduleLookahead = 0.1;

    private double? anchorDspTime;
    private int activeLoopCount;

    public int BeatsPerBar => beatsPerBar;
    public double BeatDuration => 60.0 / bpm;
    public double BarDuration => BeatDuration * beatsPerBar;
    public bool IsIdle => activeLoopCount <= 0;

    // Returns the dsp time a newly pressed button's loop should start at.
    // Cold/idle presses start after a small lookahead and become the new reference
    // phase; otherwise the result snaps to the next bar boundary of the running clock.
    public double GetNextQuantizedDspTime()
    {
        double now = AudioSettings.dspTime;

        if (IsIdle || anchorDspTime == null)
        {
            double startAt = now + ScheduleLookahead;
            anchorDspTime = startAt;
            return startAt;
        }

        double bar = BarDuration;
        double elapsed = now - anchorDspTime.Value;
        double candidate = anchorDspTime.Value + Math.Ceiling(elapsed / bar) * bar;
        if (candidate <= now + ScheduleLookahead)
        {
            candidate += bar;
        }

        return candidate;
    }

    public void NotifyLoopStarted()
    {
        activeLoopCount++;
    }

    public void NotifyLoopStopped()
    {
        activeLoopCount = Mathf.Max(0, activeLoopCount - 1);
    }
}
