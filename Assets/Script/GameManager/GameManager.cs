using System.Collections.Generic;
using UnityEngine;
using System.IO;

[System.Serializable] public class InstrumentStats 
{
    public int hitCount = 0;
    public List<float> appliedForces = new List<float>();
}

public class GameManager : MonoBehaviour
{
    [SerializeField] private float timeRelapsed = 0f;
    [SerializeField] private Dictionary<string, InstrumentStats> instrumentPlayCount = new Dictionary<string, InstrumentStats>();
    [SerializeField] private List<string> buttonPressOrder = new List<string>(); // Stores the order of button presses

    private string patientName;
    private string therapistName;

    void Awake()
    {
        Debug.Log(Application.persistentDataPath); // Debug path for persistent data storage
        LoadPatientAndTherapistInfo(); // Load patient and therapist info
    }

    // Update is called once per frame
    void Update()
    {
        UpdateElapsedTime();         
    }

    // Update the total elapsed time
    private void UpdateElapsedTime() 
    {
        timeRelapsed += Time.deltaTime; // Update time
    }

    // Record the instrument hit with applied force
    public void HitInstrument(string instrument, float appliedForce) 
    {
        if (!instrumentPlayCount.ContainsKey(instrument))
        {
            // If the instrument doesn't exist, create a new entry for it
            instrumentPlayCount[instrument] = new InstrumentStats();
        }

        // Update the instrument stats
        instrumentPlayCount[instrument].hitCount++;
        instrumentPlayCount[instrument].appliedForces.Add(appliedForce);

        // Log the action
        Debug.Log($"Instrument: {instrument} | Hit Count: {instrumentPlayCount[instrument].hitCount} | Applied Force: {appliedForce}");
    }

    // Method to record button press
    public void RecordButtonPress(string buttonName)
    {
        if (!buttonPressOrder.Contains(buttonName)) // Ensure each button is pressed only once
        {
            buttonPressOrder.Add(buttonName);
            Debug.Log($"Button pressed: {buttonName}");
        }
        else
        {
            Debug.Log($"Button {buttonName} was already pressed.");
        }
    }

    // Save stats to a CSV file
    private void SaveStatsToCSV() 
    {
        string filePath = Path.Combine(Application.dataPath, "instrument_data.csv");

        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        // Write stats to CSV file
        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine($"Patient: {patientName}");
            writer.WriteLine($"Therapist: {therapistName}");
            writer.WriteLine($"Total time spent playing: {timeRelapsed:F2} sec\n");

            foreach (var entry in instrumentPlayCount)
            {
                writer.WriteLine($"Instrument: {entry.Key}");
                writer.WriteLine("Hit Count, Applied Force");

                // Write each hit and applied force for this instrument
                for (int i = 0; i < entry.Value.appliedForces.Count; i++)
                {
                    writer.WriteLine($"{i + 1}, {entry.Value.appliedForces[i]}");
                }
                writer.WriteLine(); // Empty line for separation
            }

            // Add the First Lusher Test button press order
            writer.WriteLine("First Lusher Test Button Press Order:");
            for (int i = 0; i < buttonPressOrder.Count; i++)
            {
                writer.WriteLine($"{i + 1}, {buttonPressOrder[i]}");
            }
        }

        Debug.Log("Stats and First Lusher Test saved to CSV in Assets: " + filePath);
    }

    // Method to save button press order to a CSV file
    public void SaveButtonPressOrderToCSV()
    {
        string filePath = Path.Combine(Application.dataPath, "first_lusher_test.csv");

        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        // Write button press order to CSV file
        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine("Button Press Order:");
            for (int i = 0; i < buttonPressOrder.Count; i++)
            {
                writer.WriteLine($"{i + 1}, {buttonPressOrder[i]}");
            }
        }

        Debug.Log("Button press order saved to CSV: " + filePath);
    }

    // Load patient and therapist info from a text file
    private void LoadPatientAndTherapistInfo()
    {
        string filePath = Path.Combine(Application.dataPath, "patient_therapist_info.txt");

        if (File.Exists(filePath))
        {
            string[] lines = File.ReadAllLines(filePath);

            if (lines.Length >= 2)
            {
                patientName = lines[0].Trim(); // First line is the patient name
                therapistName = lines[1].Trim(); // Second line is the therapist name
                Debug.Log($"Loaded Patient: {patientName}, Therapist: {therapistName}");
            }
            else
            {
                Debug.LogWarning("The file does not contain enough information.");
            }
        }
        else
        {
            Debug.LogWarning("Patient and therapist info file not found.");
        }
    }

    // Save stats when the application quits
    private void OnApplicationQuit()
    {
        SaveButtonPressOrderToCSV(); // Save button press order
        SaveStatsToCSV();
    }
}
