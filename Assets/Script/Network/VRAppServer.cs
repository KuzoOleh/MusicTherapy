using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Matches the JSON shape sent by TherapistVision's SessionStart message.
[Serializable]
public class SessionInfo
{
    public string patientName;
    public string therapistName;
    public string sessionDate;
}

// Connects out to the TherapistVision app (which now hosts the TCP server) and handles
// session data over a raw TCP socket, serving the session CSV back once the second
// Lüscher test has completed.
//
// NOTE on the class name: this class is still called "VRAppServer" (and lives in the same
// file/asset) even though it is now the TCP *client* side of the connection, purely to avoid
// re-linking this component in ActualScene.unity's Inspector. See the migration note in
// TherapistVision's VRAppClient.cs (now the TCP *server*) for the matching half of this swap.
//
// Unlike the old one-shot "accept a connection, handle one message, close" server, this side
// never has anything to say on its own — every message (SessionStart, ExportRequest) is
// initiated by a therapist clicking a button on the other end, at an unpredictable time. So the
// connection is held open indefinitely: discover + connect once (retrying until the therapist
// app is reachable), then sit in a loop reading whatever message arrives next, for as long as
// the app runs.
//
// No IP address is configured anywhere: the headset and the therapist's laptop are only ever
// on the same WiFi LAN, with no cable between them and no guarantee either device's IP is
// stable (DHCP, network switches, therapist running Windows one day and Linux the next). So
// instead of a fixed address, this side broadcasts a small UDP "who's out there" packet on the
// LAN and connects to whichever machine answers — see DiscoverServer below.
public class VRAppServer : MonoBehaviour
{
    // Wire protocol for the TCP session connection (must match the TherapistVision server
    // exactly):
    //   [1 byte]  MessageType
    //   [4 bytes] payload length, big-endian int32
    //   [N bytes] payload
    private enum MessageType : byte
    {
        SessionStart = 1,
        Ack = 2,
        ExportRequest = 3,
        ExportResponse = 4,
    }

    // --- Discovery protocol (UDP, separate from the TCP session port above) ---
    // The client broadcasts DiscoveryRequestMagic on DiscoveryPort; the TherapistVision server
    // listens there and unicasts back "DiscoveryResponsePrefix<tcpPort>" to whoever asked. The
    // client reads the reply packet's *source IP* to learn the server's address — nobody has to
    // type an IP in anywhere. Both magic strings and the port must match VRAppClient.cs exactly.
    private const string DiscoveryRequestMagic = "MUSICTHERAPY_DISCOVERY_REQUEST";
    private const string DiscoveryResponsePrefix = "MUSICTHERAPY_DISCOVERY_RESPONSE:";
    private const int DiscoveryPort = 8081;
    private const int DiscoveryTimeoutMs = 1000;

    private const int ConnectTimeoutMs = 3000;
    private const int ReconnectDelayMs = 2000;
    // How long HandleSessionExport waits after writing the CSV response before triggering
    // Application.Quit(), so the OS has time to actually put the bytes on the wire first.
    private const int PostExportQuitDelayMs = 2000;
    // Only bounds how long a Write can block if the peer stops reading; the read side of the
    // dispatch loop is deliberately left without a timeout below, since it's supposed to block
    // indefinitely while waiting for the therapist to click a button.
    private const int SendTimeoutMs = 10000;

    private GameManager gameManager;
    private Thread connectionThread;
    private TcpClient activeTcpClient;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private volatile bool quitRequested;
    private volatile bool stopRequested;

    private void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[VRAppServer] No GameManager found in scene — will not be able to write incoming session data.");
        }
    }

    private void Start()
    {
        connectionThread = new Thread(ConnectionLoop) { IsBackground = true };
        connectionThread.Start();
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

    private void ConnectionLoop()
    {
        while (!stopRequested)
        {
            IPEndPoint serverEndpoint = ResolveServerEndpoint();
            if (serverEndpoint == null)
            {
                if (!stopRequested)
                    Thread.Sleep(ReconnectDelayMs);
                continue;
            }

            TcpClient client = null;
            try
            {
                client = ConnectWithTimeout(serverEndpoint.Address.ToString(), serverEndpoint.Port);
                client.SendTimeout = SendTimeoutMs;
                activeTcpClient = client;

                Debug.Log($"[VRAppServer] Connected to therapist app at {serverEndpoint}");

                using NetworkStream stream = client.GetStream();
                while (!stopRequested)
                {
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
            }
            catch (Exception ex)
            {
                if (!stopRequested)
                    Debug.LogWarning($"[VRAppServer] Connection to therapist app lost or unavailable ({ex.Message}) — retrying in {ReconnectDelayMs / 1000f:0.#}s.");
            }
            finally
            {
                try { client?.Close(); } catch { /* ignore */ }
                activeTcpClient = null;
            }

            if (!stopRequested)
                Thread.Sleep(ReconnectDelayMs);
        }
    }

    // A text file at <persistentDataPath>/server_ip.txt containing "ip[:port]" bypasses
    // discovery entirely and forces a direct connection — an escape hatch for troubleshooting
    // (e.g. a network that blocks broadcast traffic) without needing a new build. Not required
    // for normal use.
    private IPEndPoint ResolveServerEndpoint()
    {
        IPEndPoint manualOverride = TryReadManualOverride();
        return manualOverride ?? DiscoverServer();
    }

    private IPEndPoint TryReadManualOverride()
    {
        try
        {
            string overridePath = Path.Combine(Application.persistentDataPath, "server_ip.txt");
            if (!File.Exists(overridePath))
                return null;

            string line = File.ReadAllText(overridePath).Trim();
            if (string.IsNullOrEmpty(line))
                return null;

            string[] parts = line.Split(':');
            if (!IPAddress.TryParse(parts[0].Trim(), out IPAddress address))
            {
                Debug.LogWarning($"[VRAppServer] server_ip.txt contains an invalid IP address: '{parts[0]}' — ignoring override.");
                return null;
            }

            int port = 8080;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsedPort))
                port = parsedPort;

            Debug.Log($"[VRAppServer] Using manual server address override from server_ip.txt: {address}:{port}");
            return new IPEndPoint(address, port);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRAppServer] Failed to read server_ip.txt override: {ex.Message}");
            return null;
        }
    }

    // Broadcasts a small "who's out there" packet on the LAN and returns whoever answers.
    // Returns null (rather than throwing) on a timeout/no-answer so the caller can just retry —
    // that's the expected, common case whenever the therapist app hasn't been started yet.
    private IPEndPoint DiscoverServer()
    {
        try
        {
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            udpClient.Client.ReceiveTimeout = DiscoveryTimeoutMs;

            byte[] requestBytes = Encoding.UTF8.GetBytes(DiscoveryRequestMagic);
            udpClient.Send(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] responseBytes = udpClient.Receive(ref remoteEndPoint);
            string response = Encoding.UTF8.GetString(responseBytes);

            if (!response.StartsWith(DiscoveryResponsePrefix, StringComparison.Ordinal))
                return null;

            if (!int.TryParse(response.Substring(DiscoveryResponsePrefix.Length), out int tcpPort))
                return null;

            Debug.Log($"[VRAppServer] Discovered therapist app at {remoteEndPoint.Address}:{tcpPort}");
            return new IPEndPoint(remoteEndPoint.Address, tcpPort);
        }
        catch (Exception)
        {
            // Timeout / no responder yet — normal while waiting for the therapist app to start.
            return null;
        }
    }

    private static TcpClient ConnectWithTimeout(string ipAddress, int port)
    {
        var client = new TcpClient();
        IAsyncResult connectResult = client.BeginConnect(ipAddress, port, null, null);
        bool connected = connectResult.AsyncWaitHandle.WaitOne(ConnectTimeoutMs);
        if (!connected)
        {
            try { client.EndConnect(connectResult); } catch { /* expected — we're timing out */ }
            client.Close();
            throw new SocketException((int)SocketError.TimedOut);
        }
        client.EndConnect(connectResult);
        return client;
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

        if (!doneSignal.Wait(SendTimeoutMs))
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
        // hand-off HandleSessionStart uses above, instead of touching them directly
        // from this connection thread with no synchronization.
        string csvPath = null;
        using var readySignal = new ManualResetEventSlim(false);
        mainThreadActions.Enqueue(() =>
        {
            bool ready = gameManager != null && gameManager.IsSecondTestComplete;
            csvPath = ready ? gameManager.LastCsvExportPath : null;
            readySignal.Set();
        });

        if (!readySignal.Wait(SendTimeoutMs))
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

        // NetworkStream.Write only guarantees the CSV bytes were handed to the OS send
        // buffer, not that TherapistVision has actually received them — quitting (which tears
        // the socket down, see Shutdown) on literally the next frame races that delivery and
        // can drop the response before it ever reaches the peer, especially over WiFi. Give the
        // OS a moment to actually flush it first.
        Thread.Sleep(PostExportQuitDelayMs);

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

    // --- Framing helpers (mirrored on the TherapistVision server) ---

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
        Shutdown();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        stopRequested = true;
        // Unblocks a thread parked in a blocking Read/Connect call so ConnectionLoop can exit
        // promptly instead of waiting out a timeout that no longer matters.
        try { activeTcpClient?.Close(); } catch { /* ignore — already closed/never opened */ }
    }
}
