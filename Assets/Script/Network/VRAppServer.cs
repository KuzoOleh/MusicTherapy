using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using MusicTherapy.Net;
using UnityEngine;

// Matches the JSON shape sent by TherapistVision's SessionStart message.
[Serializable]
public class SessionInfo
{
    public string patientName;
    public string therapistName;
    public string sessionDate;
}

// Unity side of the headset's connection to TherapistVision. The protocol itself (discovery,
// handshake, heartbeats, file transfer) lives in SessionClient / MusicTherapyProtocol.cs; this
// component only hands the therapist's requests to the main thread and quits the app once
// the therapist's computer has confirmed it saved the session files.
//
// NOTE on the class name: still "VRAppServer" (although it's the TCP *client*) so the
// component reference in ActualScene.unity keeps working.
public class VRAppServer : MonoBehaviour
{
    // How long a network request waits for the main thread. When the headset is taken off the
    // app is paused and Update doesn't run — SessionStart is then queued and applied on wake-up.
    private const int MainThreadWaitMs = 1500;
    private const int ExportPrepareWaitMs = 10000;
    private const float QuitDelaySeconds = 1f;

    private GameManager gameManager;
    private SessionClient client;
    private string persistentDataPath;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private float quitAt = -1f;
    private volatile bool quitRequested;

    private sealed class UnityJson : IJsonSerializer
    {
        public string ToJson(object value) => JsonUtility.ToJson(value);
        public T FromJson<T>(string json) => JsonUtility.FromJson<T>(json);
    }

    private void Awake()
    {
        gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[VRAppServer] No GameManager found in scene — session data can't be received or sent.");
        }
        persistentDataPath = Application.persistentDataPath; // main-thread-only API, cached for the network thread
    }

    private void Start()
    {
        client = new SessionClient(new UnityJson(), message => Debug.Log("[VRAppServer] " + message),
            SystemInfo.deviceName, Application.version)
        {
            ManualEndpoint = TryReadManualOverride,
            SessionStartHandler = HandleSessionStart,
            ExportHandler = HandleExportRequest,
            ExportConfirmed = HandleExportConfirmed,
        };
        client.Start();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRAppServer] Error in queued main-thread action: {ex}");
            }
        }

        if (quitRequested && quitAt < 0f)
        {
            quitAt = Time.unscaledTime + QuitDelaySeconds;
        }

        if (quitAt >= 0f && Time.unscaledTime >= quitAt)
        {
            quitAt = float.MaxValue;
            Debug.Log("[VRAppServer] Results saved on the therapist's computer — closing.");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    // ------------------------------------------------------------------ handlers (network thread)

    private string HandleSessionStart(string json)
    {
        SessionInfo info = JsonUtility.FromJson<SessionInfo>(json);
        if (info == null) throw new InvalidDataException("Invalid session info.");

        string error = null;
        using (var done = new ManualResetEventSlim(false))
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    gameManager?.BeginSession(info.patientName, info.therapistName, info.sessionDate);
                    Debug.Log("[VRAppServer] Session info received from TherapistVision — new session started.");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                finally
                {
                    try { done.Set(); } catch (ObjectDisposedException) { /* we already answered "queued" */ }
                }
            });

            if (!done.Wait(MainThreadWaitMs))
                return "queued"; // the app is paused (headset off) — applied as soon as it resumes
        }

        if (error != null) throw new Exception(error);
        return "ok";
    }

    private ExportPlan HandleExportRequest()
    {
        ExportPlan plan = null;
        using (var done = new ManualResetEventSlim(false))
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    plan = gameManager != null
                        ? gameManager.PrepareExport()
                        : new ExportPlan { Ready = false, Error = "The VR app has no GameManager." };
                }
                catch (Exception ex)
                {
                    plan = new ExportPlan { Ready = false, Error = ex.Message };
                }
                finally
                {
                    try { done.Set(); } catch (ObjectDisposedException) { /* timed out already */ }
                }
            });

            if (!done.Wait(ExportPrepareWaitMs))
            {
                return new ExportPlan
                {
                    Ready = false,
                    Error = "The headset is asleep (taken off?). Wake it up and fetch the results again.",
                };
            }
        }
        return plan;
    }

    private void HandleExportConfirmed(bool saved, string error)
    {
        if (saved)
        {
            quitRequested = true; // closes on the main thread after a short delay
        }
        else
        {
            Debug.LogWarning($"[VRAppServer] The therapist's computer could not save the results ({error}). Staying open so they can try again.");
        }
    }

    // A text file at <persistentDataPath>/server_ip.txt containing "ip[:port]" skips discovery
    // and connects there directly — for networks that block broadcast traffic.
    private IPEndPoint TryReadManualOverride()
    {
        try
        {
            string path = Path.Combine(persistentDataPath, "server_ip.txt");
            if (!File.Exists(path)) return null;

            string line = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(line)) return null;

            string[] parts = line.Split(':');
            if (!IPAddress.TryParse(parts[0].Trim(), out IPAddress address))
            {
                Debug.LogWarning($"[VRAppServer] server_ip.txt has an invalid IP address '{parts[0]}' — ignored.");
                return null;
            }

            int port = Protocol.DefaultTcpPort;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsed)) port = parsed;
            return new IPEndPoint(address, port);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRAppServer] Failed to read server_ip.txt: {ex.Message}");
            return null;
        }
    }

    private void OnDestroy() => client?.Dispose();

    private void OnApplicationQuit() => client?.Dispose();
}
