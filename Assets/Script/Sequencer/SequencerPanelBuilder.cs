using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// Reads a SequencerBankSO and builds the grouped panel at runtime: group
// header labels stay on the existing World Space Canvas, while the actual
// sound buttons are real physical (3D, poke-able) objects placed in front of
// it, so adding/removing sounds, groups, or moods is purely a data change.
// Groups are laid out as side-by-side blocks in a single row (buttons within
// a group sit next to each other, groups sit next to each other) rather than
// stacked/centered rows of differing width.
public class SequencerPanelBuilder : MonoBehaviour
{
    [SerializeField] private SequencerBankSO bank;
    [SerializeField] private SequencerButton buttonPrefab;
    [SerializeField] private Transform panelContainer;
    [SerializeField] private Transform physicalButtonsRoot;
    [SerializeField] private SequencerClock clock;
    [SerializeField] private SequencerRecordController recordController;
    [SerializeField] private TMP_FontAsset headerFont;
    [SerializeField] private float buttonSpacing = 0.16f;
    [SerializeField] private float groupGap = 0.1f;

    private void Awake()
    {
        if (clock == null)
        {
            clock = GetComponent<SequencerClock>();
        }

        if (recordController == null)
        {
            recordController = GetComponent<SequencerRecordController>();
        }
    }

    private void Start()
    {
        BuildPanel();
    }

    private void BuildPanel()
    {
        if (bank == null || buttonPrefab == null || physicalButtonsRoot == null)
        {
            Debug.LogError("[SequencerPanelBuilder] Missing bank, button prefab, or physical buttons root — nothing built.");
            return;
        }

        var groups = bank.buttons
            .Where(def => def != null && def.clip != null)
            .GroupBy(def => def.groupName)
            .ToList();

        var groupWidths = groups
            .Select(group => (group.Count() - 1) * buttonSpacing)
            .ToList();

        float totalWidth = groupWidths.Sum() + (groups.Count - 1) * groupGap;
        float cursorX = -totalWidth / 2f;

        for (int i = 0; i < groups.Count; i++)
        {
            var definitions = groups[i].ToList();
            BuildGroupSection(groups[i].Key, definitions, cursorX);
            cursorX += groupWidths[i] + groupGap;
        }
    }

    private void BuildGroupSection(string groupName, List<SequencerButtonDefinition> definitions, float startX)
    {
        if (panelContainer != null)
        {
            float centerX = startX + (definitions.Count - 1) * buttonSpacing / 2f;
            BuildHeader(groupName, centerX);
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var buttonInstance = Instantiate(buttonPrefab, physicalButtonsRoot);
            buttonInstance.transform.localPosition = new Vector3(startX + i * buttonSpacing, 0f, 0f);
            buttonInstance.transform.localRotation = Quaternion.identity;
            buttonInstance.Initialize(definitions[i], clock, recordController);
        }
    }

    private void BuildHeader(string groupName, float centerX)
    {
        var headerGO = new GameObject($"Header - {groupName}", typeof(RectTransform));
        headerGO.transform.SetParent(panelContainer, false);

        var headerRect = headerGO.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0.5f, 1f);
        headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(centerX * 100f, 0f);
        headerRect.sizeDelta = new Vector2(150f, 26f);

        var headerText = headerGO.AddComponent<TextMeshProUGUI>();
        headerText.text = groupName;
        headerText.fontSize = 20;
        headerText.alignment = TextAlignmentOptions.Center;
        headerText.fontStyle = FontStyles.Bold;
        if (headerFont != null)
        {
            headerText.font = headerFont;
        }
    }
}
