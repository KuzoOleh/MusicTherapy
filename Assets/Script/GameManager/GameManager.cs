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

    void Awake()
    {
        Debug.Log(Application.persistentDataPath); // Debug path for persistent data storage
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

    // Save stats to a CSV file
    private void SaveStatsToCSV() 
    {
        string filePath = Path.Combine(Application.persistentDataPath, "instrument_data.csv");

        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        // Write stats to CSV file
        using (StreamWriter writer = new StreamWriter(filePath))
        {
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
        }

        Debug.Log("Stats saved to CSV: " + filePath);
    }

    // Save stats when the application quits
    private void OnApplicationQuit()
    {
        SaveStatsToCSV();
    }
}
