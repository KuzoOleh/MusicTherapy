using UnityEngine;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;      // Only the visible part of pause UI
    [SerializeField] private GameObject surveyPanel;     // Only the visible part of survey UI
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Slider volumeSlider;

    private bool isSurveyPendingQuit = false;

    private bool isPaused = false;

    void Start()
    {

        if (volumeSlider != null && audioSource != null)
        {
            volumeSlider.value = audioSource.volume;
            volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }
    }

    public void ShowSurveyThenQuit()
    {   
        surveyPanel.SetActive(true); // Show survey UI
        isSurveyPendingQuit = true;
     
    }

private void ConfirmExit()
{
    surveyPanel.SetActive(true);
//     if(isSurveyPendingQuit)
//     {
// #if UNITY_EDITOR
//     UnityEditor.EditorApplication.isPlaying = false;
// #else
//     Application.Quit();
// #endif
// }
}

    private void ChangeVolume(float value)
    {
        if (audioSource != null)
        {
            audioSource.volume = value;
        }
    }
}
