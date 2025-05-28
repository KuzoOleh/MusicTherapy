using UnityEngine;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject surveyPanel;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Slider volumeSlider;

    private CanvasGroup surveyCanvasGroup;

    private void Start()
    {
        if (volumeSlider != null && audioSource != null)
        {
            volumeSlider.value = audioSource.volume;
            volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }

        if (surveyPanel != null)
        {
            surveyCanvasGroup = surveyPanel.GetComponent<CanvasGroup>();
            if (surveyCanvasGroup == null)
            {
                surveyCanvasGroup = surveyPanel.AddComponent<CanvasGroup>();
            }
        }
    }

    public void OnExitPressed()
    {
        if (surveyCanvasGroup != null)
        {
            surveyCanvasGroup.alpha = 1;
            surveyCanvasGroup.interactable = true;
            surveyCanvasGroup.blocksRaycasts = true;
            Debug.Log("Survey panel restored.");
        }
    }

    private void ChangeVolume(float value)
    {
        if (audioSource != null)
        {
            audioSource.volume = value;
        }
    }
}
