using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonOrderTracker : MonoBehaviour
{
    private GameManager gameManager;
    private List<string> buttonPressOrder = new List<string>();
    private HashSet<string> pressedButtons = new HashSet<string>();

    [SerializeField] private Text orderDisplayText;
    [SerializeField] private GameObject surveyCanvas;
    [SerializeField] private Button finishButton;
    [SerializeField] private int requiredButtonCount = 8;

    [Header("Test Configuration")]
    [SerializeField] private bool isSecondTest = false;

    private CanvasGroup surveyCanvasGroup;

    private void Start()
    {
        gameManager = FindObjectOfType<GameManager>();

        if (finishButton != null)
        {
            finishButton.interactable = false;
            finishButton.gameObject.SetActive(false);
        }

        if (surveyCanvas != null)
        {
            surveyCanvasGroup = surveyCanvas.GetComponent<CanvasGroup>();
            if (surveyCanvasGroup == null)
            {
                surveyCanvasGroup = surveyCanvas.AddComponent<CanvasGroup>();
            }
        }
    }

    public void OnButtonPressed(Button button)
    {
        string buttonName = button.name;

        if (!pressedButtons.Contains(buttonName))
        {
            buttonPressOrder.Add(buttonName);
            pressedButtons.Add(buttonName);

            if (orderDisplayText != null)
            {
                orderDisplayText.text = $"Order: {string.Join(", ", buttonPressOrder)}";
            }

            Debug.Log($"Button pressed: {buttonName}");
            Debug.Log($"Current order: {string.Join(", ", buttonPressOrder)}");

            if (gameManager != null)
            {
                if (isSecondTest)
                    gameManager.RecordSecondTestButtonPress(buttonName);
                else
                    gameManager.RecordButtonPress(buttonName);
            }

            button.interactable = false;

            if (pressedButtons.Count >= requiredButtonCount && finishButton != null)
            {
                finishButton.gameObject.SetActive(true);
                finishButton.interactable = true;
            }
        }
        else
        {
            Debug.Log($"Button {buttonName} was already pressed.");
        }
    }

    public void OnFinishSurvey()
    {
        if (surveyCanvasGroup != null)
        {
            surveyCanvasGroup.alpha = 0;
            surveyCanvasGroup.interactable = false;
            surveyCanvasGroup.blocksRaycasts = false;
            Debug.Log("Survey finished. Canvas hidden.");
        }

        if (isSecondTest)
        {
            Debug.Log("Second test completed. Saving and quitting...");

            if (gameManager != null)
            {
                gameManager.SaveButtonPressOrderToCSV();

                var method = typeof(GameManager).GetMethod("SaveStatsToCSV", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(gameManager, null);
            }

            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        else
        {
            isSecondTest = true;
            ResetSurvey();
        }
    }

    public void ResetSurvey()
    {
        buttonPressOrder.Clear();
        pressedButtons.Clear();

        foreach (Button button in GetComponentsInChildren<Button>())
        {
            if (button != finishButton)
            {
                button.interactable = true;
            }
        }

        if (finishButton != null)
        {
            finishButton.interactable = false;
            finishButton.gameObject.SetActive(false);
        }

        if (orderDisplayText != null)
        {
            orderDisplayText.text = "Order:";
        }
    }

    public List<string> GetButtonPressOrder()
    {
        return buttonPressOrder;
    }

    public void SetTestPhase(bool isSecond)
    {
        isSecondTest = isSecond;
    }
}
