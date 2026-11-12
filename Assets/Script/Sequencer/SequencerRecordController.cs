using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Record/Stop state machine for the sequencer panel. Controls ONLY the WAV audio
// capture — every sequencer button press is logged for the session report regardless
// (see SequencerButton.LogAction). Capture starts on the first button press after
// arming, so no dead silence gets recorded. Each step of the Record workflow
// (armed / started / stopped / cancelled) is itself logged so the therapist can see
// in the report when the patient used Record.
public class SequencerRecordController : MonoBehaviour
{
    private enum State { Idle, Armed, Recording }

    [SerializeField] private SequencerWavRecorder wavRecorder;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private Button recordButton;
    [SerializeField] private TMP_Text recordButtonLabel;

    private State state = State.Idle;

    private void Awake()
    {
        if (wavRecorder == null)
        {
            wavRecorder = FindObjectOfType<SequencerWavRecorder>();
        }

        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManager>();
        }

        if (recordButton != null)
        {
            recordButton.onClick.AddListener(OnRecordButtonPressed);
        }

        UpdateLabel();
    }

    public void OnRecordButtonPressed()
    {
        switch (state)
        {
            case State.Idle:
                state = State.Armed;
                gameManager?.RecordSequencerRecordingEvent("armed");
                break;
            case State.Armed:
                gameManager?.RecordSequencerRecordingEvent("cancelled");
                state = State.Idle;
                break;
            case State.Recording:
                FinalizeSession();
                state = State.Idle;
                break;
        }

        UpdateLabel();
    }

    public void NotifyButtonPressed(SequencerButtonDefinition definition)
    {
        if (state == State.Armed)
        {
            wavRecorder.BeginCapture();
            state = State.Recording;
            gameManager?.RecordSequencerRecordingEvent("started");
            UpdateLabel();
        }
    }

    // Stops an armed or running recording (used before the results are sent to the therapist,
    // so the WAV is complete). Returns true if anything changed.
    public bool FinalizeIfRecording()
    {
        switch (state)
        {
            case State.Armed:
                gameManager?.RecordSequencerRecordingEvent("cancelled");
                break;
            case State.Recording:
                FinalizeSession();
                break;
            default:
                return false;
        }

        state = State.Idle;
        UpdateLabel();
        return true;
    }

    private void FinalizeSession()
    {
        if (wavRecorder.IsCapturing)
        {
            string path = gameManager.GetSessionFilePath("sequencer_recording", "wav");
            string savedPath = wavRecorder.EndCaptureAndSave(path);
            gameManager?.RecordSequencerRecordingEvent("stopped", System.IO.Path.GetFileName(savedPath));
            gameManager?.RegisterSessionFile(savedPath);
            Debug.Log($"[Sequencer] Session recording saved to {savedPath}");
        }
    }

    private void UpdateLabel()
    {
        if (recordButtonLabel == null)
        {
            return;
        }

        recordButtonLabel.text = state == State.Idle ? "Record" : "Stop";
    }
}
