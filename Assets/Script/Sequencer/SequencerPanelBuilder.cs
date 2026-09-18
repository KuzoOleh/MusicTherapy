using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// Reads a SequencerBankSO and builds the grouped panel at runtime. physicalButtonsRoot
// is used only as a one-time world-space reference point/orientation for layout math —
// the physical (3D, poke-able) buttons and their group header labels are spawned as
// independent objects (parented under this builder, not physicalButtonsRoot) with their
// world position/rotation computed via physicalButtonsRoot.TransformPoint/.rotation, so
// they can't be distorted by whatever scale physicalButtonsRoot's own transform has. Each
// group's buttons sit in a flat box grid (wrapping after maxColumns, so up to maxColumns x
// N rows per group), all facing the same direction as physicalButtonsRoot, and groups sit
// as side-by-side blocks next to each other.
//
// VR polish pass: group headers get a dark backdrop so labels stay legible against any
// background, each group is handed a stable index so SequencerButton can assign every
// group a distinct, collision-free color, and an optional gentle concave curve
// (panelCurveRadius) is available for wide banks — a flat row of buttons puts the outer
// columns at a worse viewing angle/distance than the middle ones, which is a common VR
// ergonomics complaint; curving the row keeps every button roughly equidistant from the
// player. Curving is off by default (panelCurveRadius = 0) so existing setups look
// exactly as before unless you opt in.
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
    // control but repositioned to sit relative to physicalButtonsRoot instead of
    // living wherever it was last hand-placed in the scene.
    [SerializeField] private Transform recordButtonRoot;
    [SerializeField] private float recordButtonGap = 0.1f;

    [Header("VR Ergonomics")]
    [Tooltip("0 = flat panel (original layout). Set above 0 (try ~1.0-1.5) to bend a wide panel into a gentle concave arc around the player instead of a flat line, so the outer columns aren't further away/harder to see than the middle ones.")]
    [SerializeField] private float panelCurveRadius = 0f;

    [Header("Board Clearance")]
    [Tooltip("How far each button's root sits in front of physicalButtonsRoot's own plane (negative = toward the player, matching this project's forward-facing convention). Buttons used to rely on a small offset baked into the button prefab itself to clear whatever board/backing surface sits at the anchor's plane — that offset scales with the prefab's own size, so shrinking the buttons for a comfortable VR footprint also shrank their standoff from the board down to almost nothing, leaving them rendering tucked inside it. This is a separate, dedicated clearance so resizing the buttons again later won't re-break it.")]
    [SerializeField] private float buttonForwardOffset = -0.06f;

    [Header("Header Legibility")]
    [SerializeField] private bool headerBackdropEnabled = true;
    [SerializeField] private Color headerBackdropColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private Vector2 headerBackdropPadding = new Vector2(40f, 24f);

    [Header("Group Panels")]
    [Tooltip("Adds a colored backdrop plate behind each group's whole button block (tinted with that group's own hue, dimmed way down so the buttons still pop on top of it), so groups read as distinct 'zones' instead of one undifferentiated grid of buttons.")]
    [SerializeField] private bool groupBackdropEnabled = true;
    [SerializeField] [Range(0f, 1f)] private float groupBackdropSaturation = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float groupBackdropValue = 0.22f;
    [Tooltip("Extra room the panel extends past the outer edge of its group's buttons: x = left/right, y = below the bottom row (the top edge is derived from headerHeight instead, so the panel tucks in just under the header).")]
    [SerializeField] private Vector2 groupBackdropPadding = new Vector2(0.05f, 0.07f);
    [Tooltip("How far behind the buttons' own plane (i.e. added on top of buttonForwardOffset, not measured from physicalButtonsRoot) the panel sits. Small and positive so it hugs just behind the buttons themselves rather than sitting back at the board's surface, wherever that is.")]
    [SerializeField] private float groupBackdropForwardOffset = 0.015f;
    [Tooltip("Rounds the panel's corners (in real-world meters, not a fraction) instead of a hard-edged rectangle. Each panel gets its own correctly-proportioned rounding — a wide 4-column group and a lone single-button group don't share one texture stretched to fit, so the corner reads as a true circular arc on both regardless of the panel's own aspect ratio.")]
    [SerializeField] private float groupBackdropCornerRadius = 0.03f;
    [Tooltip("Width of the anti-aliased fade at the rounded edge, in meters. Higher = softer/blurrier edge.")]
    [SerializeField] private float groupBackdropEdgeSoftness = 0.006f;

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
            BuildGroupSection(groups[i].Key, definitions, cursorX, i, groups.Count);
            cursorX += groupWidths[i] + interGroupStep;

            int columns = Mathf.Min(maxColumns, definitions.Count);
            int rows = Mathf.CeilToInt(definitions.Count / (float)columns);
            maxRows = Mathf.Max(maxRows, rows);
        }

        PositionRecordButton(maxRows);
    }

    private void BuildGroupSection(string groupName, List<SequencerButtonDefinition> definitions, float startX, int groupIndex, int groupCount)
    {
        int columns = Mathf.Min(maxColumns, definitions.Count);
        float blockWidth = (columns - 1) * buttonSpacing;
        float centerX = startX + blockWidth / 2f;
        int rows = Mathf.CeilToInt(definitions.Count / (float)columns);

        BuildHeader(groupName, centerX);
        BuildGroupBackdrop(groupIndex, centerX, blockWidth, rows);

        for (int i = 0; i < definitions.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;

            // A group whose button count isn't a multiple of maxColumns leaves its last
            // row with fewer buttons than the rows above it (e.g. 5 buttons at 4 columns
            // leaves 1 lonely button on row 2). Left-aligning that partial row under
            // column 0 made it look like a stray, detached button instead of part of the
            // group — centering each row within the group's own block width fixes that
            // for any row, while leaving fully-populated rows exactly where they were.
            int buttonsInThisRow = Mathf.Min(columns, definitions.Count - row * columns);
            float rowBlockWidth = (buttonsInThisRow - 1) * buttonSpacing;
            float rowStartX = startX + (blockWidth - rowBlockWidth) / 2f;

            // Buttons are spawned as independent world-space objects (parented under this
            // builder, not physicalButtonsRoot) so their position/scale can't be distorted
            // by whatever scale or rotation physicalButtonsRoot's own transform happens to
            // have — physicalButtonsRoot is used purely as a one-time reference point/orientation.
            var buttonInstance = Instantiate(buttonPrefab, transform);
            var localOffset = new Vector3(rowStartX + col * buttonSpacing, -row * rowSpacing, buttonForwardOffset);
            var pose = GetWorldPose(localOffset);
            buttonInstance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            buttonInstance.Initialize(definitions[i], clock, recordController, groupIndex, groupCount);
        }
    }

    // Anchors the Record button directly beneath the lowest row of the panel it was
    // just built next to, centered on the panel — instead of leaving it wherever it
    // was last hand-placed, which is what let it drift away from the panel. Parented
    // directly under physicalButtonsRoot (unlike the individual sequencer buttons) so
    // it stays bound to wherever that anchor is moved, since it's a single object and
    // physicalButtonsRoot is a plain, unscaled transform — no risk of the distortion
    // that motivated keeping the many spawned buttons independent of it.
    private void PositionRecordButton(int maxRows)
    {
        if (recordButtonRoot == null)
        {
            return;
        }

        float bottomRowY = -(maxRows - 1) * rowSpacing;
        float y = bottomRowY - recordButtonGap;

        // Routed through the same GetWorldPose the buttons/headers use (instead of a
        // hardcoded localPosition/localRotation) so it stays correctly centered under
        // the panel even if panelCurveRadius is ever turned on — otherwise the record
        // button would stay flat while the buttons around it curve, guaranteeing it
        // looks off-center. x=0 here is the true horizontal center of the whole panel,
        // the same origin the button/header layout is centered around.
        recordButtonRoot.SetParent(physicalButtonsRoot, false);
        var pose = GetWorldPose(new Vector3(0f, y, 0f));
        recordButtonRoot.SetPositionAndRotation(pose.position, pose.rotation);
    }

    private void BuildHeader(string groupName, float centerX)
    {
        var headerGO = new GameObject($"Header - {groupName}");
        headerGO.transform.SetParent(transform, false);
        var pose = GetWorldPose(new Vector3(centerX, headerHeight, 0f));
        headerGO.transform.SetPositionAndRotation(pose.position, pose.rotation);
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

        if (headerBackdropEnabled)
        {
            BuildHeaderBackdrop(headerGO.transform, headerText);
        }
    }

    // One colored plate per group, sized to that group's own button block (plus a
    // little padding) and tinted with the same golden-ratio hue SequencerButton
    // assigns to that group's buttons — so the color-coding started on the buttons
    // extends to a visible "zone" behind them instead of stopping at the buttons
    // themselves. Saturation/value are both pulled well down from the buttons' own
    // (0.55, 0.9) so the plate reads as a backdrop, not another button. Built from
    // the exact same startX/blockWidth/rowSpacing/headerHeight this group's header
    // and buttons use, so it's derived from — and always moves with — their layout;
    // there's no separate position to keep in sync by hand.
    private void BuildGroupBackdrop(int groupIndex, float centerX, float blockWidth, int rows)
    {
        if (!groupBackdropEnabled)
        {
            return;
        }

        float width = blockWidth + groupBackdropPadding.x * 2f;
        // Top edge tucks in just under the header; bottom edge clears the last row.
        float topY = headerHeight * 0.55f;
        float bottomY = -(rows - 1) * rowSpacing - groupBackdropPadding.y;
        float height = topY - bottomY;
        float centerY = (topY + bottomY) / 2f;

        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = $"GroupPanel - {groupIndex}";
        var backdropCollider = backdrop.GetComponent<Collider>();
        if (backdropCollider != null)
        {
            // Purely visual — must not block pokes meant for the buttons in front of it.
            Destroy(backdropCollider);
        }

        backdrop.transform.SetParent(transform, false);
        // buttonForwardOffset is where the buttons themselves now sit (see "Board
        // Clearance" above); this panel needs to track that, not the anchor's own
        // plane, or it'd be left sitting back at the board's surface while the
        // buttons poke out in front of it — visually detached from the group they're
        // meant to be grouped with.
        var pose = GetWorldPose(new Vector3(centerX, centerY, buttonForwardOffset + groupBackdropForwardOffset));
        backdrop.transform.SetPositionAndRotation(pose.position, pose.rotation);
        backdrop.transform.localScale = new Vector3(width, height, 1f);

        // Unlit/Color has no texture slot to mask with, so the rounded corners need
        // a shader that both samples a texture and still respects material.color as
        // a tint — Sprites/Default does both and, being built into every Unity
        // install regardless of render pipeline, is safe to reach for without
        // knowing which pipeline this project uses.
        float hue = Mathf.Repeat(groupIndex * 0.618033988749895f, 1f);
        var renderer = backdrop.GetComponent<Renderer>();
        var material = new Material(Shader.Find("Sprites/Default"))
        {
            color = Color.HSVToRGB(hue, groupBackdropSaturation, groupBackdropValue),
            mainTexture = BuildRoundedPanelTexture(width, height, groupBackdropCornerRadius, groupBackdropEdgeSoftness)
        };
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Builds a rounded-rectangle alpha mask sized to this specific panel's own
    // width/height (rather than one shared square texture stretched to fit),
    // opaque inside the rounded rect and fading to transparent at the boundary.
    // Width and height are scaled by the *same* factor to reach the target
    // resolution, so the corner radius comes out as a true circular arc instead
    // of being stretched into an ellipse on a non-square panel. Called once per
    // group when the panel is built, not per-frame, so a small per-panel
    // allocation here isn't the kind of cost that matters for VR frame timing.
    private static Texture2D BuildRoundedPanelTexture(float worldWidth, float worldHeight, float cornerRadiusMeters, float edgeSoftnessMeters)
    {
        const float TargetMaxDimension = 128f;
        const int MinDimension = 16;

        float longestSide = Mathf.Max(worldWidth, worldHeight, 0.001f);
        float pixelsPerMeter = TargetMaxDimension / longestSide;

        int texWidth = Mathf.Max(MinDimension, Mathf.RoundToInt(worldWidth * pixelsPerMeter));
        int texHeight = Mathf.Max(MinDimension, Mathf.RoundToInt(worldHeight * pixelsPerMeter));
        float halfW = texWidth * 0.5f;
        float halfH = texHeight * 0.5f;
        float radius = Mathf.Min(cornerRadiusMeters * pixelsPerMeter, Mathf.Min(halfW, halfH));
        float softness = Mathf.Max(0.5f, edgeSoftnessMeters * pixelsPerMeter);

        var texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[texWidth * texHeight];
        for (int y = 0; y < texHeight; y++)
        {
            float py = (y + 0.5f) - halfH;
            for (int x = 0; x < texWidth; x++)
            {
                float px = (x + 0.5f) - halfW;

                // Standard rounded-box signed-distance function: distance to a box
                // shrunk by the corner radius, then offset back out by that radius.
                float qx = Mathf.Abs(px) - (halfW - radius);
                float qy = Mathf.Abs(py) - (halfH - radius);
                float outsideX = Mathf.Max(qx, 0f);
                float outsideY = Mathf.Max(qy, 0f);
                float signedDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                float alpha = 1f - Mathf.Clamp01((signedDistance + softness * 0.5f) / softness);
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[y * texWidth + x] = new Color32(255, 255, 255, a);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    // Dark pill behind the group label. Group headers sit directly above the poke
    // buttons where the "background" is whatever's visible through the panel from
    // the player's position — without something behind the text, light-on-light or
    // busy backgrounds can make the label hard to read at a glance.
    private void BuildHeaderBackdrop(Transform headerTransform, TextMeshPro headerText)
    {
        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "Backdrop";
        var backdropCollider = backdrop.GetComponent<Collider>();
        if (backdropCollider != null)
        {
            // Purely visual — must not block pokes/rays meant for the buttons behind it.
            Destroy(backdropCollider);
        }

        backdrop.transform.SetParent(headerTransform, false);
        // Sits just behind the text along the header's own local forward axis.
        backdrop.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        backdrop.transform.localRotation = Quaternion.identity;

        // Header text renders at a small world scale (headerScale), so the backdrop's
        // *local* size needs to be correspondingly large to read as a normal-sized
        // pill once headerScale shrinks it back down; approximate from the label's
        // character count so short and long group names both get a snug fit.
        float textWidthEstimate = Mathf.Max(1, headerText.text.Length) * headerText.fontSize * 0.62f;
        backdrop.transform.localScale = new Vector3(
            textWidthEstimate + headerBackdropPadding.x,
            headerText.fontSize + headerBackdropPadding.y,
            1f);

        var renderer = backdrop.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color")) { color = headerBackdropColor };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Converts a flat (x, y, z) offset relative to physicalButtonsRoot into a world
    // pose. With panelCurveRadius <= 0 this reproduces the original flat-panel math
    // exactly. Above 0, x is instead treated as an arc-length along a circle of that
    // radius, bending the row into a gentle concave curve and yawing each object to
    // keep facing outward from the arc's center (i.e. roughly toward the player).
    private (Vector3 position, Quaternion rotation) GetWorldPose(Vector3 localOffset)
    {
        if (panelCurveRadius <= 0f)
        {
            return (physicalButtonsRoot.TransformPoint(localOffset), physicalButtonsRoot.rotation);
        }

        float angle = localOffset.x / panelCurveRadius;
        float arcX = Mathf.Sin(angle) * panelCurveRadius;
        float arcZ = localOffset.z + panelCurveRadius - Mathf.Cos(angle) * panelCurveRadius;
        var curvedLocal = new Vector3(arcX, localOffset.y, arcZ);
        var rotation = physicalButtonsRoot.rotation * Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);

        return (physicalButtonsRoot.TransformPoint(curvedLocal), rotation);
    }
}
