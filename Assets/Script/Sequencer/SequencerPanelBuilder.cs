using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// Reads a SequencerBankSO and builds the grouped panel at runtime. Both the
// physical (3D, poke-able) buttons and their group header labels are children
// of the same physicalButtonsRoot transform, in the same real-world-meters
// coordinate space — this keeps them together instead of drifting apart the
// way a separate Canvas (different scale/origin) would. Each group's buttons
// sit in a flat box grid (wrapping after maxColumns, so up to maxColumns x N
// rows per group), all facing the same direction as physicalButtonsRoot, and
// groups sit as side-by-side blocks next to each other.
public class SequencerPanelBuilder : MonoBehaviour
{
    [SerializeField] private SequencerBankSO bank;
    [SerializeField] private SequencerButton buttonPrefab;
    [SerializeField] private Transform physicalButtonsRoot;
    [SerializeField] private SequencerClock clock;
    [SerializeField] private SequencerRecordController recordController;
    [SerializeField] private TMP_FontAsset headerFont;
    [SerializeField] private int maxColumns = 4;
    [SerializeField] private float buttonSpacing = 0.2f;
    [SerializeField] private float rowSpacing = 0.2f;
    [SerializeField] private float groupGap = 0.18f;
    [SerializeField] private float headerHeight = 0.13f;
    [SerializeField] private float headerScale = 0.02f;

    // Positioned (not built) — this is the existing Record button, kept as a Canvas
    // control but re-anchored under physicalButtonsRoot so it moves and scales with
    // the panel instead of living wherever it was last hand-placed in the scene.
    [SerializeField] private Transform recordButtonRoot;
    [SerializeField] private float recordButtonGap = 0.1f;

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
            .Select(group => (Mathf.Min(maxColumns, group.Count()) - 1) * buttonSpacing)
            .ToList();

        // Gap between the last column of one group and the first column of the next
        // must clear a full button-spacing step (not just groupGap on its own), since
        // groupGap is extra padding on top of that, not the whole inter-group distance.
        float interGroupStep = buttonSpacing + groupGap;
        float totalWidth = groupWidths.Sum() + (groups.Count - 1) * interGroupStep;
        float cursorX = -totalWidth / 2f;

        int maxRows = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            var definitions = groups[i].ToList();
            BuildGroupSection(groups[i].Key, definitions, cursorX);
            cursorX += groupWidths[i] + interGroupStep;

            int columns = Mathf.Min(maxColumns, definitions.Count);
            int rows = Mathf.CeilToInt(definitions.Count / (float)columns);
            maxRows = Mathf.Max(maxRows, rows);
        }

        PositionRecordButton(maxRows);
    }

    private void BuildGroupSection(string groupName, List<SequencerButtonDefinition> definitions, float startX)
    {
        int columns = Mathf.Min(maxColumns, definitions.Count);
        float blockWidth = (columns - 1) * buttonSpacing;
        float centerX = startX + blockWidth / 2f;
        BuildHeader(groupName, centerX);

        for (int i = 0; i < definitions.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;

            var buttonInstance = Instantiate(buttonPrefab, physicalButtonsRoot);
            buttonInstance.transform.localPosition = new Vector3(startX + col * buttonSpacing, -row * rowSpacing, 0f);
            // Identity local rotation — buttons inherit physicalButtonsRoot's own
            // rotation, which is what's already aimed at the player, so every
            // button in the grid faces the same way, flat, with no per-button tilt.
            buttonInstance.transform.localRotation = Quaternion.identity;
            buttonInstance.Initialize(definitions[i], clock, recordController);
        }
    }

    // Anchors the Record button directly beneath the lowest row of the panel it was
    // just built next to, centered on the panel — instead of leaving it wherever it
    // was last hand-placed, which is what let it drift away from the panel.
    private void PositionRecordButton(int maxRows)
    {
        if (recordButtonRoot == null)
        {
            return;
        }

        float bottomRowY = -(maxRows - 1) * rowSpacing;
        float y = bottomRowY - recordButtonGap;
        recordButtonRoot.SetParent(physicalButtonsRoot, false);
        recordButtonRoot.localPosition = new Vector3(0f, y, 0f);
        recordButtonRoot.localRotation = Quaternion.identity;
    }

    private void BuildHeader(string groupName, float centerX)
    {
        var headerGO = new GameObject($"Header - {groupName}");
        headerGO.transform.SetParent(physicalButtonsRoot, false);
        headerGO.transform.localPosition = new Vector3(centerX, headerHeight, 0f);
        headerGO.transform.localRotation = Quaternion.identity;
        headerGO.transform.localScale = new Vector3(headerScale, headerScale, headerScale);

        var headerText = headerGO.AddComponent<TextMeshPro>();
        headerText.text = groupName;
        headerText.fontSize = 36;
        headerText.alignment = TextAlignmentOptions.Center;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = Color.white;
        if (headerFont != null)
        {
            headerText.font = headerFont;
        }
    }
}
