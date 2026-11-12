using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

[Serializable]
public class InstrumentHitRecord
{
    public string instrument;
    public float force;        // normalized 0..1 for drums/maracas
    public float sessionTime;  // seconds since BeginSession
}

[Serializable]
public class ColorChoiceRecord
{
    public string color;
    public float sessionTime;
}

[Serializable]
public class SequencerHitRecord
{
    public string groupName;
    public string buttonLabel;
    public string moodTag;
    public string action;      // start / stop / resume
    public float sessionTime;
}

[Serializable]
public class SequencerRecordingEvent
{
    public string eventName;   // armed / started / stopped / cancelled
    public string fileName;    // WAV file, for "stopped"
    public float sessionTime;
}

// Collects everything that happens during one therapy session and exports it as a single
// machine-readable CSV (RFC 4180, UTF-8 with BOM, invariant-culture numbers).
//
// CSV layout — one row per event, same columns for every section:
//   section, index, item, detail, value, t_sec
//   meta               — session info (patient, therapist, date, duration...)
//   instrument_summary — per instrument: detail = hits / avg_force / max_force
//   instrument_hit     — every hit: item = instrument, value = normalized force
//   lusher_1/lusher_2  — Lüscher choices: index = position, item = color
//   sequencer          — loop started:        item = button, detail = group, value = mood tag
//   sequencer_stop     — stop requested (the loop ends on the next beat), same columns
//   sequencer_resume   — a pending stop was cancelled, same columns
//   sequencer_record   — Record button: item = armed/started/stopped/cancelled, value = WAV file
public class GameManager : MonoBehaviour
{
    private const string CsvHeader = "section,index,item,detail,value,t_sec";
    private const string LegacyPatientInfoFileName = "patient_therapist_info.txt";

    [SerializeField] private float sessionElapsed = 0f;

    private readonly List<InstrumentHitRecord> instrumentHits = new List<InstrumentHitRecord>();
    private readonly List<ColorChoiceRecord> firstTestChoices = new List<ColorChoiceRecord>();
    private readonly List<ColorChoiceRecord> secondTestChoices = new List<ColorChoiceRecord>();
    private readonly List<SequencerHitRecord> sequencerHits = new List<SequencerHitRecord>();
    private readonly List<SequencerRecordingEvent> sequencerRecordingEvents = new List<SequencerRecordingEvent>();
    // Extra files of this session sent to the therapist with the CSV (e.g. the sequencer WAV).
    private readonly List<string> sessionFiles = new List<string>();

    private string patientName;
    private string therapistName;
    private string sessionDate;
    private string sessionId;
    private DateTime sessionStartedAt;

    public bool IsSecondTestComplete { get; private set; }
    public string LastCsvExportPath { get; private set; }
    public float SessionElapsed => sessionElapsed;

    void Awake()
    {
        Debug.Log("Persistent Data Path: " + Application.persistentDataPath);
        DeleteLegacyPatientInfoFile();

        // Until the therapist app sends real session info, everything is recorded under an
        // anonymous session — a previous patient's name is never reused.
        ResetSessionData();
    }

    void Update()
    {
        sessionElapsed += Time.deltaTime;
    }

    // Called (on the main thread) when TherapistVision sends SessionStart. Starts a clean
    // session: new id, timer from zero, all previously collected data discarded.
    public void BeginSession(string patient, string therapist, string date)
    {
        patientName = patient;
        therapistName = therapist;
        sessionDate = date;
        ResetSessionData();
        Debug.Log($"[GameManager] New session {sessionId}: patient '{patientName}', therapist '{therapistName}', date '{sessionDate}'.");
    }

    private void ResetSessionData()
    {
        sessionStartedAt = DateTime.Now;
        sessionId = sessionStartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        sessionElapsed = 0f;

        instrumentHits.Clear();
        firstTestChoices.Clear();
        secondTestChoices.Clear();
        sequencerHits.Clear();
        sequencerRecordingEvents.Clear();
        sessionFiles.Clear();

        IsSecondTestComplete = false;
        LastCsvExportPath = null;
    }

    // Older builds stored the last patient's name here and silently reused it for the next
    // session. The file is no longer read — remove it so personal data doesn't linger.
    private static void DeleteLegacyPatientInfoFile()
    {
        try
        {
            string legacyPath = Path.Combine(Application.persistentDataPath, LegacyPatientInfoFileName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GameManager] Could not delete legacy {LegacyPatientInfoFileName}: {ex.Message}");
        }
    }

    // Builds a per-session, filesystem-safe file name so a new session never
    // overwrites a previous patient's exported data.
    private string GetSessionFileName(string baseName, string extension = "csv")
    {
        string safePatientName = string.IsNullOrWhiteSpace(patientName) ? "unknown" : patientName;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safePatientName = safePatientName.Replace(invalidChar, '_');
        }

        return $"{baseName}_{safePatientName}_{sessionId}.{extension}";
    }

    // Public convenience wrapper returning a full path under persistentDataPath,
    // for callers outside GameManager (e.g. the sequencer's WAV recorder) that
    // want the same per-session naming without duplicating the sanitize logic.
    public string GetSessionFilePath(string baseName, string extension)
    {
        return Path.Combine(Application.persistentDataPath, GetSessionFileName(baseName, extension));
    }

    public void HitInstrument(string instrument, float appliedForce)
    {
        instrumentHits.Add(new InstrumentHitRecord
        {
            instrument = instrument,
            force = appliedForce,
            sessionTime = sessionElapsed
        });

        int hitCount = instrumentHits.Count(h => h.instrument == instrument);
        Debug.Log($"Інструмент: {instrument} | Кількість ударів: {hitCount} | Прикладена сила: {appliedForce.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    // First Lüscher Test
    public void RecordButtonPress(string buttonName)
    {
        RecordColorChoice(firstTestChoices, buttonName, "Перший тест");
    }

    // Second Lüscher Test
    public void RecordSecondTestButtonPress(string buttonName)
    {
        RecordColorChoice(secondTestChoices, buttonName, "Другий тест");
    }

    private void RecordColorChoice(List<ColorChoiceRecord> choices, string color, string testLabel)
    {
        if (choices.Any(c => c.color == color))
        {
            Debug.Log($"[{testLabel}] Кнопка {color} вже була натиснута.");
            return;
        }

        choices.Add(new ColorChoiceRecord { color = color, sessionTime = sessionElapsed });
        Debug.Log($"[{testLabel}] Натиснута кнопка: {color}");
    }

    // Logged for every press, whether or not the Record button is active.
    public void HitSequencerButton(string groupName, string buttonLabel, string moodTag, string action = "start")
    {
        sequencerHits.Add(new SequencerHitRecord
        {
            groupName = groupName,
            buttonLabel = buttonLabel,
            moodTag = moodTag,
            action = string.IsNullOrEmpty(action) ? "start" : action,
            sessionTime = sessionElapsed
        });

        Debug.Log($"[Секвенсор] {groupName}/{buttonLabel} — {action} (настрій: {moodTag})");
    }

    // Record button workflow: armed → started (first loop press) → stopped, or armed → cancelled.
    public void RecordSequencerRecordingEvent(string eventName, string fileName = null)
    {
        sequencerRecordingEvents.Add(new SequencerRecordingEvent
        {
            eventName = eventName,
            fileName = fileName,
            sessionTime = sessionElapsed
        });

        Debug.Log($"[Секвенсор] Запис: {eventName}{(string.IsNullOrEmpty(fileName) ? "" : " → " + fileName)}");
    }

    public void MarkSecondTestComplete()
    {
        IsSecondTestComplete = true;
    }

    public void RegisterSessionFile(string path)
    {
        if (!string.IsNullOrEmpty(path) && !sessionFiles.Contains(path))
        {
            sessionFiles.Add(path);
        }
    }

    // Called (on the main thread) when the therapist fetches the results. Files are only
    // offered once the second Lüscher test is done; a sequencer recording that's still running
    // is stopped and saved first, and the CSV is rewritten so it includes that.
    public MusicTherapy.Net.ExportPlan PrepareExport()
    {
        if (!IsSecondTestComplete)
        {
            return new MusicTherapy.Net.ExportPlan
            {
                Ready = false,
                Error = "The session isn't finished yet — the patient hasn't completed the second Lüscher test."
            };
        }

        var recorder = FindFirstObjectByType<SequencerRecordController>();
        bool recordingChanged = recorder != null && recorder.FinalizeIfRecording();

        if (recordingChanged || string.IsNullOrEmpty(LastCsvExportPath) || !File.Exists(LastCsvExportPath))
        {
            SaveStatsToCSV();
        }

        var plan = new MusicTherapy.Net.ExportPlan { Ready = true };
        plan.Paths.Add(LastCsvExportPath);
        plan.Paths.AddRange(sessionFiles.Where(File.Exists));
        return plan;
    }

    // ---------------------------------------------------------------- CSV export

    public void SaveStatsToCSV()
    {
        var csv = new StringBuilder();
        csv.Append(CsvHeader).Append("\r\n");

        AppendMetaRows(csv);

        foreach (var group in instrumentHits.GroupBy(h => h.instrument))
        {
            AppendRow(csv, "instrument_summary", null, group.Key, "hits", FormatNumber(group.Count()), null);
            AppendRow(csv, "instrument_summary", null, group.Key, "avg_force", FormatNumber(group.Average(h => h.force)), null);
            AppendRow(csv, "instrument_summary", null, group.Key, "max_force", FormatNumber(group.Max(h => h.force)), null);
        }

        for (int i = 0; i < instrumentHits.Count; i++)
        {
            var hit = instrumentHits[i];
            AppendRow(csv, "instrument_hit", i + 1, hit.instrument, null, FormatNumber(hit.force), hit.sessionTime);
        }

        AppendColorChoices(csv, "lusher_1", firstTestChoices);
        AppendColorChoices(csv, "lusher_2", secondTestChoices);

        for (int i = 0; i < sequencerHits.Count; i++)
        {
            var hit = sequencerHits[i];
            string section = hit.action == "start" ? "sequencer" : "sequencer_" + hit.action;
            AppendRow(csv, section, i + 1, hit.buttonLabel, hit.groupName, EscapeText(hit.moodTag), hit.sessionTime);
        }

        for (int i = 0; i < sequencerRecordingEvents.Count; i++)
        {
            var evt = sequencerRecordingEvents[i];
            AppendRow(csv, "sequencer_record", i + 1, evt.eventName, null, EscapeText(evt.fileName), evt.sessionTime);
        }

        string filePath = GetSessionFilePath("instrument_data", "csv");
        WriteCsv(filePath, csv);
        LastCsvExportPath = filePath;

        Debug.Log($"Stats, First, and Second Lüscher Test data saved to CSV: {filePath}");
    }

    public void SaveButtonPressOrderToCSV()
    {
        var csv = new StringBuilder();
        csv.Append(CsvHeader).Append("\r\n");
        AppendMetaRows(csv);
        AppendColorChoices(csv, "lusher_1", firstTestChoices);

        string filePath = GetSessionFilePath("first_lusher_test", "csv");
        WriteCsv(filePath, csv);

        Debug.Log("First Lüscher button press order saved to CSV: " + filePath);
    }

    public void ExportAllData()
    {
        SaveStatsToCSV();
        SaveButtonPressOrderToCSV();
    }

    private void AppendMetaRows(StringBuilder csv)
    {
        AppendRow(csv, "meta", null, "session_id", null, EscapeText(sessionId), null);
        AppendRow(csv, "meta", null, "patient", null, EscapeText(patientName), null);
        AppendRow(csv, "meta", null, "therapist", null, EscapeText(therapistName), null);
        AppendRow(csv, "meta", null, "session_date", null, EscapeText(sessionDate), null);
        AppendRow(csv, "meta", null, "session_start", null,
            EscapeText(sessionStartedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)), null);
        AppendRow(csv, "meta", null, "duration_sec", null, FormatNumber(sessionElapsed), null);
        AppendRow(csv, "meta", null, "app_version", null, EscapeText(Application.version), null);
    }

    private static void AppendColorChoices(StringBuilder csv, string section, List<ColorChoiceRecord> choices)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            AppendRow(csv, section, i + 1, choices[i].color, null, null, choices[i].sessionTime);
        }
    }

    // `value` must already be escaped (EscapeText) or formatted (FormatNumber).
    private static void AppendRow(StringBuilder csv, string section, int? index, string item, string detail, string value, float? sessionTime)
    {
        csv.Append(section).Append(',')
           .Append(index.HasValue ? index.Value.ToString(CultureInfo.InvariantCulture) : "").Append(',')
           .Append(EscapeText(item)).Append(',')
           .Append(EscapeText(detail)).Append(',')
           .Append(value ?? "").Append(',')
           .Append(sessionTime.HasValue ? sessionTime.Value.ToString("0.000", CultureInfo.InvariantCulture) : "")
           .Append("\r\n");
    }

    private static string FormatNumber(float number) => number.ToString("0.####", CultureInfo.InvariantCulture);
    private static string FormatNumber(int number) => number.ToString(CultureInfo.InvariantCulture);

    // RFC 4180 quoting, plus a guard against spreadsheet formula injection
    // (a patient name like "=HYPERLINK(...)" must stay plain text in Excel).
    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if ("=+-@".IndexOf(text[0]) >= 0)
        {
            text = "'" + text;
        }

        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }

    // UTF-8 *with* BOM so Excel detects the encoding and shows Cyrillic correctly.
    private static void WriteCsv(string filePath, StringBuilder csv)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void OnApplicationQuit()
    {
        try
        {
            ExportAllData();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameManager] Failed to export session data on quit: {ex.Message}");
        }
    }

    public void ResetFirstTestOrder()
    {
        firstTestChoices.Clear();
    }

    public void ResetSecondTestOrder()
    {
        secondTestChoices.Clear();
    }
}
