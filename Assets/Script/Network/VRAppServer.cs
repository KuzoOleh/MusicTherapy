using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
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

// Receives session data from the TherapistVision app over HTTP and serves the
// session CSV back once the second Lüscher test has completed.
public class VRAppServer : MonoBehaviour
{
    private const string SenderAppName = "TherapistVision";

    [Header("HTTP Server")]
    [SerializeField] private string listenAddress = "127.0.0.1";
    [SerializeField] private int listenPort = 8080;
    [SerializeField] private string startSessionEndpoint = "/session/start";
    [SerializeField] private string sessionResultEndpoint = "/session/export";

    private GameManager gameManager;
    private HttpListener listener;
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
        string prefix = $"http://{listenAddress}:{listenPort}/";
        listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRAppServer] Failed to start listening on {prefix}: {ex.Message}");
            return;
        }

        Debug.Log($"[VRAppServer] Listening on {prefix}");

        listenerThread = new Thread(ListenLoop) { IsBackground = true };
        listenerThread.Start();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            action();
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
        while (listener != null && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception)
            {
                break; // listener was stopped
            }

            HandleRequest(context);
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == startSessionEndpoint)
            {
                HandleSessionStart(request, response);
            }
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == sessionResultEndpoint)
            {
                HandleSessionExport(response);
            }
            else
            {
                WriteResponse(response, 404, "{\"error\":\"not found\"}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRAppServer] Error handling request: {ex.Message}");
            try { WriteResponse(response, 500, "{\"error\":\"internal server error\"}"); } catch { /* response already closed */ }
        }
    }

    private void HandleSessionStart(HttpListenerRequest request, HttpListenerResponse response)
    {
        string json;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
        {
            json = reader.ReadToEnd();
        }

        SessionInfo info = JsonUtility.FromJson<SessionInfo>(json);

        mainThreadActions.Enqueue(() =>
        {
            if (gameManager == null)
            {
                return;
            }

            gameManager.SetPatientName(info.patientName);
            gameManager.SetTherapistName(info.therapistName);
            gameManager.SavePatientAndTherapistInfo();
            Debug.Log($"[VRAppServer] Hey, I got info from {SenderAppName} — we're good to write in!");
        });

        WriteResponse(response, 200, "{\"status\":\"ok\"}");
    }

    private void HandleSessionExport(HttpListenerResponse response)
    {
        bool ready = gameManager != null && gameManager.IsSecondTestComplete;
        string csvPath = ready ? gameManager.LastCsvExportPath : null;

        if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
        {
            WriteResponse(response, 425, "{\"error\":\"session not finished yet\"}");
            return;
        }

        byte[] bytes = File.ReadAllBytes(csvPath);
        string fileName = Path.GetFileName(csvPath);

        response.StatusCode = 200;
        response.ContentType = "text/csv";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();

        mainThreadActions.Enqueue(() =>
        {
            Debug.Log($"[VRAppServer] Second Lüscher test passed — sending CSV back to {SenderAppName}.");
            quitRequested = true;
        });
    }

    private void WriteResponse(HttpListenerResponse response, int statusCode, string jsonBody)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
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
        if (listener != null && listener.IsListening)
        {
            listener.Stop();
            listener.Close();
        }
        listener = null;
    }
}
