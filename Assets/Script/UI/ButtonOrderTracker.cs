using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonOrderTracker : MonoBehaviour
{
    private GameManager gameManager;
    private List<string> buttonPressOrder = new List<string>(); // Stores the order of button presses
    private HashSet<string> pressedButtons = new HashSet<string>(); // Tracks already pressed buttons

    [SerializeField] private Text orderDisplayText;
    [SerializeField] private GameObject surveyCanvas; // Reference to the survey canvas

    private void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
    }

    public void OnButtonPressed(Button button)
    {
        string buttonName = button.name;

        // Ensure the button is pressed only once
        if (!pressedButtons.Contains(buttonName))
        {
            buttonPressOrder.Add(buttonName);
            pressedButtons.Add(buttonName);

            // Update the UI text
            if (orderDisplayText != null)
            {
                orderDisplayText.text = $"Order: {string.Join(", ", buttonPressOrder)}";
            }

            Debug.Log($"Button pressed: {buttonName}");
            Debug.Log($"Current order: {string.Join(", ", buttonPressOrder)}");

            // Record the button press in GameManager
            gameManager.RecordButtonPress(buttonName);
            button.interactable = false; // Disable the button after pressing
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
            surveyCanvas.SetActive(false); // Hide the canvas
            surveyCanvas = null; // Nullify the reference
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
