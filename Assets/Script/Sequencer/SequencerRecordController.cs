using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Record/Stop state machine for the sequencer panel. Recording (both audio
// capture and CSV logging) only actually starts on the first button press
// after arming, so no dead silence gets captured at the start of a session.
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
                break;
            case State.Armed:
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
            UpdateLabel();
        }

        if (state == State.Recording)
        {
            gameManager.HitSequencerButton(definition.groupName, definition.displayLabel, definition.moodTag);
        }
    }

    private void FinalizeSession()
    {
        if (wavRecorder.IsCapturing)
        {
            string path = gameManager.GetSessionFilePath("sequencer_recording", "wav");
            string savedPath = wavRecorder.EndCaptureAndSave(path);
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
