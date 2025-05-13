using System.Collections.Generic;
using UnityEngine;
using System.IO;

[System.Serializable]
public class InstrumentStats
{
    public int hitCount = 0;
    public List<float> appliedForces = new List<float>();
}

public class GameManager : MonoBehaviour
{
    [SerializeField] private float timeRelapsed = 0f;

    [SerializeField] private Dictionary<string, InstrumentStats> instrumentPlayCount = new Dictionary<string, InstrumentStats>();
    [SerializeField] private List<string> buttonPressOrder = new List<string>();               
    [SerializeField] private List<string> secondTestButtonPressOrder = new List<string>();    

    private string patientName;
    private string therapistName;

    void Awake()
    {
        Debug.Log(Application.persistentDataPath);
        LoadPatientAndTherapistInfo(); 
    }

    void Update()
    {
        UpdateElapsedTime();
    }

    private void UpdateElapsedTime()
    {
        timeRelapsed += Time.deltaTime;
    }

    public void HitInstrument(string instrument, float appliedForce)
    {
        if (!instrumentPlayCount.ContainsKey(instrument))
        {
            instrumentPlayCount[instrument] = new InstrumentStats();
        }

        instrumentPlayCount[instrument].hitCount++;
        instrumentPlayCount[instrument].appliedForces.Add(appliedForce);

        Debug.Log($"Інструмент: {instrument} | Кількість ударів: {instrumentPlayCount[instrument].hitCount} | Прикладена сила: {appliedForce}");
    }

    // First Lüscher Test
    public void RecordButtonPress(string buttonName)
    {
        if (!buttonPressOrder.Contains(buttonName))
        {
            buttonPressOrder.Add(buttonName);
            Debug.Log($"[Перший тест] Натиснута кнопка: {buttonName}");
        }
        else
        {
            Debug.Log($"[First Test] Button {buttonName} was already pressed.");
        }
    }

    // Second Luscher Test
    public void RecordSecondTestButtonPress(string buttonName)
    {
        if (!secondTestButtonPressOrder.Contains(buttonName))
        {
            secondTestButtonPressOrder.Add(buttonName);
            Debug.Log($"[Другий тест] Натиснута кнопка: {buttonName}");
        }
        else
        {
            Debug.Log($"[Другий тест] Button {buttonName} was already pressed.");
        }
    }

    private void SaveStatsToCSV()
    {
        string filePath = Path.Combine(Application.dataPath, "instrument_data.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine($"Пацієнт: {patientName}");
            writer.WriteLine($"Спеціаліст: {therapistName}");
            writer.WriteLine($"Загальний проведений час: {timeRelapsed:F2} sec\n");

            foreach (var entry in instrumentPlayCount)
            {
                writer.WriteLine($"Інструмент: {entry.Key}");
                writer.WriteLine("Кількість ударів, Прикладена сила");

                for (int i = 0; i < entry.Value.appliedForces.Count; i++)
                {
                    writer.WriteLine($"{i + 1}, {entry.Value.appliedForces[i]}");
                }
                writer.WriteLine();
            }

            // First Luscher Test
            writer.WriteLine("Перше тестування Люшера:");
            for (int i = 0; i < buttonPressOrder.Count; i++)
            {
                writer.WriteLine($"{i + 1}, {buttonPressOrder[i]}");
            }
            writer.WriteLine();

            // Second Luscher Test
            writer.WriteLine("Друге тестування Люшера:");
            for (int i = 0; i < secondTestButtonPressOrder.Count; i++)
            {
                writer.WriteLine($"{i + 1}, {secondTestButtonPressOrder[i]}");
            }
        }

        Debug.Log("Stats, First, and Second Lüscher Test data saved to CSV.");
    }

    public void SaveButtonPressOrderToCSV()
    {
        string filePath = Path.Combine(Application.dataPath, "first_lusher_test.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine("Button Press Order:");
            for (int i = 0; i < buttonPressOrder.Count; i++)
            {
                writer.WriteLine($"{i + 1}, {buttonPressOrder[i]}");
            }
        }

        Debug.Log("First Lüscher button press order saved to CSV: " + filePath);
    }

    private void LoadPatientAndTherapistInfo()
    {
        string filePath = Path.Combine(Application.dataPath, "patient_therapist_info.txt");

        if (File.Exists(filePath))
        {
            string[] lines = File.ReadAllLines(filePath);

            if (lines.Length >= 2)
            {
                patientName = lines[0].Trim();
                therapistName = lines[1].Trim();
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

    private void OnApplicationQuit()
    {
        SaveButtonPressOrderToCSV();
        SaveStatsToCSV();
    }

 
    public void ResetFirstTestOrder()
    {
        buttonPressOrder.Clear();
    }

    public void ResetSecondTestOrder()
    {
        secondTestButtonPressOrder.Clear();
    }
}
