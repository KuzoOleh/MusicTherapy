using System;
using UnityEngine;

// Shared quantized clock for the sequencer panel. All buttons schedule their
// playback through this clock so simultaneous presses stay phase-locked
// instead of layering with arbitrary human-timing drift. The clock is
// considered idle only while nothing is currently looping — as long as at
// least one button is still playing, any later press (no matter how much
// real time has passed) snaps to the existing beat grid instead of
// restarting it.
public class SequencerClock : MonoBehaviour
{
    [SerializeField] private float bpm = 130f;
    [SerializeField] private int beatsPerBar = 4;

    private double? anchorDspTime;
    private int activeLoopCount;

    public int BeatsPerBar => beatsPerBar;
    public double BeatDuration => 60.0 / bpm;
    public bool IsIdle => activeLoopCount <= 0;

    // Returns the dsp time a newly pressed button's loop should start at.
    // Cold/idle presses start immediately and become the new reference phase;
    // otherwise the result snaps to the next beat boundary of the running clock.
    public double GetNextQuantizedDspTime()
    {
        double now = AudioSettings.dspTime;

        if (IsIdle || anchorDspTime == null)
        {
            anchorDspTime = now;
            return now;
        }

        double beat = BeatDuration;
        double elapsed = now - anchorDspTime.Value;
        double candidate = anchorDspTime.Value + Math.Ceiling(elapsed / beat) * beat;
        if (candidate <= now + 0.005)
        {
            candidate += beat;
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
