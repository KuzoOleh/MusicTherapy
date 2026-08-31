using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Matches the JSON shape sent by TherapistVision's VRAppClient.SendSessionInfo.
[Serializable]
public class SessionInfo
{
    public string patientName;
    public string therapistName;
    public string sessionDate;
}

// Receives session data from the TherapistVision app over a raw TCP socket and serves
// the session CSV back once the second Lüscher test has completed. Switched from
// HttpListener to TcpListener: HttpListener's cross-platform support under Unity's
// Mono/IL2CPP backends is inconsistent (it's the reason the two Linux builds couldn't
// see each other), while System.Net.Sockets is core BCL functionality Unity supports
// reliably on every platform/scripting backend.
public class VRAppServer : MonoBehaviour
{
    // Wire protocol (VRAppClient on the TherapistVision side must match this exactly):
    //   [1 byte]  MessageType
    //   [4 bytes] payload length, big-endian int32
    //   [N bytes] payload
    // One request message in, one response message out, then the connection closes —
    // mirrors the old one-request-per-connection HTTP behavior.
    private enum MessageType : byte
    {
        SessionStart = 1,
        Ack = 2,
        ExportRequest = 3,
        ExportResponse = 4,
    }

    [Header("TCP Server")]
    // 0.0.0.0 (all interfaces) rather than loopback — the server needs to be reachable
    // from another device on the network (e.g. the therapist's laptop), not just from
    // a client running on the same machine as the VR app.
    [SerializeField] private string listenAddress = "0.0.0.0";
    [SerializeField] private int listenPort = 8080;

    private GameManager gameManager;
    private TcpListener listener;
    private Thread listenerThread;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private volatile bool quitRequested;

    private void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[VRAppServer] No GameManager found in scene — server will not be able to write incoming session data.");
        }
    }

    private void Start()
    {
        try
        {
            listener = new TcpListener(IPAddress.Parse(listenAddress), listenPort);
            listener.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRAppServer] Failed to start listening on {listenAddress}:{listenPort}: {ex.Message}");
            return;
        }

        Debug.Log($"[VRAppServer] Listening on {listenAddress}:{listenPort}");

        listenerThread = new Thread(ListenLoop) { IsBackground = true };
        listenerThread.Start();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRAppServer] Unhandled exception in queued main-thread action: {ex.Message}");
            }
        }

        if (quitRequested)
        {
            quitRequested = false;
            Debug.Log("[VRAppServer] CSV delivered — shutting down.");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    private void ListenLoop()
    {
        while (listener != null)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                break; // listener was stopped
            }

            try
            {
                HandleClient(client);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRAppServer] Error handling connection: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }
    }

    // Without a read/write timeout, a client that connects and then never sends anything
    // (a dead connection, a port scanner, a network hiccup) would block this single-threaded
    // accept loop forever on Stream.Read, since AcceptTcpClient/HandleClient run sequentially —
    // no other client could ever connect until that hang cleared on its own.
    private const int SocketTimeoutMs = 10000;

    private void HandleClient(TcpClient client)
    {
        client.ReceiveTimeout = SocketTimeoutMs;
        client.SendTimeout = SocketTimeoutMs;

        using NetworkStream stream = client.GetStream();

        var type = (MessageType)ReadByte(stream);
        byte[] payload = ReadFramedPayload(stream);

        switch (type)
        {
            case MessageType.SessionStart:
                HandleSessionStart(payload, stream);
                break;
            case MessageType.ExportRequest:
                HandleSessionExport(stream);
                break;
            default:
                WriteMessage(stream, MessageType.Ack, Encoding.UTF8.GetBytes("{\"error\":\"unknown message type\"}"));
                break;
        }
    }

    private void HandleSessionStart(byte[] payload, NetworkStream stream)
    {
        string json = Encoding.UTF8.GetString(payload);
        SessionInfo info = JsonUtility.FromJson<SessionInfo>(json);

        // Wait for the save to actually happen on the main thread (instead of firing the
        // ack immediately after enqueueing) so a write failure — e.g. a bad path, a locked
        // file — is reported back as an error instead of a false "ok".
        bool saveSucceeded = false;
        string errorMessage = null;
        using var doneSignal = new ManualResetEventSlim(false);

        mainThreadActions.Enqueue(() =>
        {
            try
            {
                if (gameManager != null)
                {
                    gameManager.SetPatientName(info.patientName);
                    gameManager.SetTherapistName(info.therapistName);
                    gameManager.SavePatientAndTherapistInfo();
                    Debug.Log("[VRAppServer] Hey, I got info from TherapistVision — we're good to write in!");
                }
                saveSucceeded = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Debug.LogError($"[VRAppServer] Failed to save session info: {ex.Message}");
            }
            finally
            {
                doneSignal.Set();
            }
        });

        if (!doneSignal.Wait(SocketTimeoutMs))
        {
            WriteMessage(stream, MessageType.Ack, Encoding.UTF8.GetBytes("{\"error\":\"timed out saving session info\"}"));
            return;
        }

        string ackJson = saveSucceeded
            ? "{\"status\":\"ok\"}"
            : $"{{\"error\":\"{EscapeJsonString(errorMessage)}\"}}";
        WriteMessage(stream, MessageType.Ack, Encoding.UTF8.GetBytes(ackJson));
    }

    private static string EscapeJsonString(string value)
    {
        return string.IsNullOrEmpty(value) ? "unknown error" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void HandleSessionExport(NetworkStream stream)
    {
        // IsSecondTestComplete/LastCsvExportPath are written on the main thread (by
        // ButtonOrderTracker/GameManager) — read them through the same main-thread
        // hand-off HandleSessionStart uses below, instead of touching them directly
        // from this listener thread with no synchronization.
        string csvPath = null;
        using var readySignal = new ManualResetEventSlim(false);
        mainThreadActions.Enqueue(() =>
        {
            bool ready = gameManager != null && gameManager.IsSecondTestComplete;
            csvPath = ready ? gameManager.LastCsvExportPath : null;
            readySignal.Set();
        });

        if (!readySignal.Wait(SocketTimeoutMs))
        {
            WriteExportFailure(stream, "timed out waiting for session state");
            return;
        }

        if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
        {
            WriteExportFailure(stream, "session not finished yet");
            return;
        }

        byte[] csvBytes = File.ReadAllBytes(csvPath);
        byte[] fileNameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(csvPath));

        using var payloadStream = new MemoryStream();
        payloadStream.WriteByte(1); // success
        WriteUInt16(payloadStream, (ushort)fileNameBytes.Length);
        payloadStream.Write(fileNameBytes, 0, fileNameBytes.Length);
        payloadStream.Write(csvBytes, 0, csvBytes.Length);

        WriteMessage(stream, MessageType.ExportResponse, payloadStream.ToArray());

        mainThreadActions.Enqueue(() =>
        {
            Debug.Log("[VRAppServer] Second Lüscher test passed — sending CSV back to TherapistVision.");
            quitRequested = true;
        });
    }

    private static void WriteExportFailure(NetworkStream stream, string errorMessage)
    {
        byte[] errorBytes = Encoding.UTF8.GetBytes(errorMessage);
        byte[] payload = new byte[1 + errorBytes.Length];
        payload[0] = 0; // failure
        Array.Copy(errorBytes, 0, payload, 1, errorBytes.Length);
        WriteMessage(stream, MessageType.ExportResponse, payload);
    }

    // --- Framing helpers (mirrored on the TherapistVision client) ---

    private static void WriteMessage(NetworkStream stream, MessageType type, byte[] payload)
    {
        stream.WriteByte((byte)type);
        WriteInt32(stream, payload.Length);
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)value));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte ReadByte(Stream stream)
    {
        int b = stream.ReadByte();
        if (b < 0)
            throw new IOException("Connection closed while reading message type.");
        return (byte)b;
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new IOException("Connection closed while reading message.");
            offset += read;
        }
        return buffer;
    }

    private static byte[] ReadFramedPayload(Stream stream)
    {
        int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ReadExact(stream, 4), 0));
        if (length < 0 || length > 64 * 1024 * 1024) // 64MB sanity cap
            throw new IOException($"Invalid payload length: {length}");
        return length == 0 ? Array.Empty<byte>() : ReadExact(stream, length);
    }

    private void OnDestroy()
    {
        StopListener();
    }

    private void OnApplicationQuit()
    {
        StopListener();
    }

    private void StopListener()
    {
        try
        {
            listener?.Stop();
        }
        catch
        {
            // ignore — already stopped
        }
        listener = null;
    }
}
