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
    [SerializeField] private Button finishButton; // Reference to the Finish button
    [SerializeField] private int requiredButtonCount = 3; // Number of buttons that need to be pressed before enabling Finish

    private void Start()
    {
        gameManager = FindObjectOfType<GameManager>();

        // Disable the Finish button initially
        if (finishButton != null)
        {
            finishButton.interactable = false;
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

            gameManager.RecordButtonPress(buttonName);
            button.interactable = false;

            // Enable Finish button if all required buttons have been pressed
            if (pressedButtons.Count >= requiredButtonCount && finishButton != null)
            {
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
        if (surveyCanvas != null)
        {
            surveyCanvas.SetActive(false);
            surveyCanvas = null;
            Debug.Log("Survey finished. Canvas hidden and nullified.");
        }
        else
        {
            Debug.LogWarning("Survey canvas is already null.");
        }
    }

    public List<string> GetButtonPressOrder()
    {
        return buttonPressOrder;
    }
}
