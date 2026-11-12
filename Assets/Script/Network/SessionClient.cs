using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MusicTherapy.Net
{
    // Headset-side network core (no UnityEngine — VRAppServer wraps it).
    //
    // Finds the therapist's computer (UDP broadcast, or a manual address), connects, introduces
    // itself, keeps the link alive with heartbeats and answers the therapist's requests. If the
    // link dies it simply reconnects. The handlers are called on the network thread; the
    // Unity side marshals them to the main thread.
    public sealed class SessionClient : IDisposable
    {
        // Receives the SessionInfo JSON; returns "ok" (applied) or "queued" (will apply when
        // the app resumes). Throw to report an error to the therapist.
        public Func<string, string> SessionStartHandler;

        // Decides what to send when the therapist fetches results.
        public Func<ExportPlan> ExportHandler;

        // The therapist's computer reported whether it saved the files (saved, error).
        public Action<bool, string> ExportConfirmed;

        // Optional fixed address (server_ip.txt) instead of discovery.
        public Func<IPEndPoint> ManualEndpoint;

        private const int ConnectTimeoutMs = 3000;
        private const int DiscoveryTimeoutMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int RejectedRetryDelayMs = 5000;

        private readonly IJsonSerializer json;
        private readonly Action<string> log;
        private readonly string deviceName;
        private readonly string appVersion;

        private Thread connectionThread;
        private volatile bool stopping;
        private volatile TcpClient activeClient;
        private volatile bool connected;
        private readonly object writeLock = new object();

        public bool IsConnected => connected;
        public string ServerName { get; private set; }

        public SessionClient(IJsonSerializer json, Action<string> log, string deviceName, string appVersion)
        {
            this.json = json;
            this.log = log ?? (_ => { });
            this.deviceName = string.IsNullOrEmpty(deviceName) ? "VR headset" : deviceName;
            this.appVersion = appVersion ?? "";
        }

        public void Start()
        {
            connectionThread = new Thread(ConnectionLoop) { IsBackground = true, Name = "MT connection" };
            connectionThread.Start();
        }

        private void ConnectionLoop()
        {
            while (!stopping)
            {
                IPEndPoint endpoint = null;
                try
                {
                    endpoint = ManualEndpoint?.Invoke() ?? Discover();
                }
                catch (Exception ex)
                {
                    log("Looking for the therapist's computer failed: " + ex.Message);
                }

                if (endpoint != null)
                {
                    try
                    {
                        RunConnection(endpoint);
                    }
                    catch (Exception ex)
                    {
                        if (!stopping) log($"Connection to {endpoint} lost or unavailable ({ex.Message}) — retrying.");
                    }
                    finally
                    {
                        connected = false;
                        try { activeClient?.Close(); } catch { /* ignore */ }
                        activeClient = null;
                    }
                }

                if (!stopping) Thread.Sleep(ReconnectDelayMs);
            }
        }

        private void RunConnection(IPEndPoint endpoint)
        {
            TcpClient client = ConnectWithTimeout(endpoint);
            activeClient = client;
            client.NoDelay = true;
            client.SendTimeout = 10000;
            client.ReceiveTimeout = Protocol.HandshakeTimeoutMs;
            NetworkStream stream = client.GetStream();

            Send(stream, MessageType.Hello, json.ToJson(new HelloMessage
            {
                app = Protocol.AppId,
                protocol = Protocol.Version,
                device = deviceName,
                appVersion = appVersion,
            }));

            Frame first = Framing.ReadFrame(stream);
            HelloReply reply = first.Type == MessageType.HelloReply ? json.FromJson<HelloReply>(first.Text) : null;
            if (reply == null || !reply.accepted)
            {
                log($"The computer at {endpoint} refused the connection: {reply?.reason ?? "no reply"}");
                Thread.Sleep(RejectedRetryDelayMs);
                return;
            }

            ServerName = reply.server;
            client.ReceiveTimeout = Protocol.ConnectionTimeoutMs;
            connected = true;
            log($"Connected to the therapist's computer \"{reply.server}\" at {endpoint}.");

            var heartbeat = new Thread(() => HeartbeatLoop(client, stream)) { IsBackground = true, Name = "MT heartbeat" };
            heartbeat.Start();

            // Reading blocks for at most ConnectionTimeoutMs: the computer pings every 2 s, so a
            // timeout here means the link is dead (computer asleep, Wi-Fi gone, app closed).
            while (!stopping)
            {
                Frame frame = Framing.ReadFrame(stream);
                switch (frame.Type)
                {
                    case MessageType.Ping:
                        break;
                    case MessageType.SessionStart:
                        HandleSessionStart(stream, frame.Text);
                        break;
                    case MessageType.ExportRequest:
                        HandleExport(stream);
                        break;
                    case MessageType.ExportConfirm:
                        var confirmation = json.FromJson<ExportConfirmation>(frame.Text);
                        ExportConfirmed?.Invoke(confirmation != null && confirmation.saved, confirmation?.error);
                        break;
                    default:
                        log($"Ignoring unexpected message {frame.Type}.");
                        break;
                }
            }
        }

        private void HeartbeatLoop(TcpClient client, NetworkStream stream)
        {
            while (!stopping && connected && activeClient == client)
            {
                Thread.Sleep(Protocol.HeartbeatIntervalMs);
                if (stopping || !connected || activeClient != client) break;
                try
                {
                    lock (writeLock) Framing.WriteFrame(stream, MessageType.Ping, null);
                }
                catch
                {
                    try { client.Close(); } catch { /* ignore */ } // unblocks the reader, which reconnects
                    break;
                }
            }
        }

        private void HandleSessionStart(NetworkStream stream, string sessionJson)
        {
            AckMessage ack;
            try
            {
                string status = SessionStartHandler?.Invoke(sessionJson) ?? "ok";
                ack = new AckMessage { status = status };
            }
            catch (Exception ex)
            {
                ack = new AckMessage { error = ex.Message };
            }
            Send(stream, MessageType.Ack, json.ToJson(ack));
        }

        private void HandleExport(NetworkStream stream)
        {
            ExportPlan plan;
            try
            {
                plan = ExportHandler?.Invoke() ?? new ExportPlan { Ready = false, Error = "Export isn't available." };
            }
            catch (Exception ex)
            {
                plan = new ExportPlan { Ready = false, Error = ex.Message };
            }

            if (!plan.Ready)
            {
                Send(stream, MessageType.ExportBegin, json.ToJson(new ExportManifest { ready = false, error = plan.Error, files = new ExportFileInfo[0] }));
                return;
            }

            var files = new List<(string path, long size)>();
            foreach (string path in plan.Paths)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists) files.Add((path, info.Length));
                    else log($"Export: {path} is missing, skipped.");
                }
                catch (Exception ex)
                {
                    log($"Export: {path} skipped ({ex.Message}).");
                }
            }

            var manifest = new ExportManifest { ready = true, files = new ExportFileInfo[files.Count] };
            for (int i = 0; i < files.Count; i++)
                manifest.files[i] = new ExportFileInfo { name = Path.GetFileName(files[i].path), size = files[i].size };
            Send(stream, MessageType.ExportBegin, json.ToJson(manifest));

            var buffer = new byte[2 + Protocol.ChunkSize];
            for (int i = 0; i < files.Count; i++)
            {
                using (var input = new FileStream(files[i].path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long remaining = files[i].size; // exactly what was announced, even if the file changed since
                    while (remaining > 0)
                    {
                        int want = (int)Math.Min(Protocol.ChunkSize, remaining);
                        int read = input.Read(buffer, 2, want);
                        if (read <= 0)
                        {
                            // File shrank after it was announced — pad so the computer's byte count still matches.
                            Array.Clear(buffer, 2, want);
                            read = want;
                        }
                        Framing.WriteUInt16(buffer, 0, i);
                        lock (writeLock) Framing.WriteFrame(stream, MessageType.FileChunk, buffer, 2 + read);
                        remaining -= read;
                    }
                }
            }

            Send(stream, MessageType.ExportEnd, "");
            log($"Export: sent {files.Count} file(s); waiting for the computer to confirm it saved them.");
        }

        private void Send(NetworkStream stream, MessageType type, string text)
        {
            lock (writeLock) Framing.WriteFrame(stream, type, Framing.Utf8(text));
        }

        // Broadcasts "who's out there" on the LAN; the therapist's computer answers with its
        // TCP port and we take its address from the reply. Null = nobody answered (yet).
        private IPEndPoint Discover()
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    udp.Client.ReceiveTimeout = DiscoveryTimeoutMs;
                    byte[] request = Encoding.UTF8.GetBytes(Protocol.DiscoveryRequestMagic);
                    udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, Protocol.DiscoveryPort));

                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    string response = Encoding.UTF8.GetString(udp.Receive(ref remote));
                    if (!response.StartsWith(Protocol.DiscoveryResponsePrefix, StringComparison.Ordinal)) return null;
                    if (!int.TryParse(response.Substring(Protocol.DiscoveryResponsePrefix.Length), out int port)) return null;
                    return new IPEndPoint(remote.Address, port);
                }
            }
            catch
            {
                return null; // timeout — the therapist app isn't running yet
            }
        }

        private static TcpClient ConnectWithTimeout(IPEndPoint endpoint)
        {
            var client = new TcpClient();
            IAsyncResult pending = client.BeginConnect(endpoint.Address, endpoint.Port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
            {
                try { client.Close(); } catch { /* ignore */ }
                throw new SocketException((int)SocketError.TimedOut);
            }
            client.EndConnect(pending);
            return client;
        }

        public void Dispose()
        {
            stopping = true;
            connected = false;
            try { activeClient?.Close(); } catch { /* ignore */ } // unblocks a pending read
        }
    }
}
