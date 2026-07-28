using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Captures whatever is audible at this GameObject's AudioListener while
// recording is active, and writes it out as a 16-bit PCM WAV file. This is a
// live capture of the actual performance (not an offline re-synthesis), so it
// picks up everything audible during the session, not just the sequencer
// panel's own sounds.
[RequireComponent(typeof(AudioListener))]
public class SequencerWavRecorder : MonoBehaviour
{
    public bool IsCapturing { get; private set; }

    private readonly List<float> captureBuffer = new List<float>(1 << 20);
    private readonly object bufferLock = new object();
    private int channels;
    private int sampleRate;

    public void BeginCapture()
    {
        sampleRate = AudioSettings.outputSampleRate;
        channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;

        lock (bufferLock)
        {
            captureBuffer.Clear();
        }

        IsCapturing = true;
    }

    // Runs on Unity's internal audio thread. Only touches the lock-guarded
    // buffer and cached primitive fields — no other Unity API calls here.
    private void OnAudioFilterRead(float[] data, int dataChannels)
    {
        if (!IsCapturing)
        {
            return;
        }

        lock (bufferLock)
        {
            captureBuffer.AddRange(data);
        }
        // Intentionally not modifying `data` — capture only, playback continues unaffected.
    }

    public string EndCaptureAndSave(string filePath)
    {
        IsCapturing = false;

        float[] samples;
        lock (bufferLock)
        {
            samples = captureBuffer.ToArray();
        }

        WriteWavPcm16(filePath, samples, sampleRate, channels);
        return filePath;
    }

    private static void WriteWavPcm16(string filePath, float[] samples, int sampleRate, int channels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = samples.Length * (bitsPerSample / 8);

        using (var stream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });

            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)bitsPerSample);

            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            foreach (float sample in samples)
            {
                short pcm = (short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
                writer.Write(pcm);
            }
        }
    }
}
