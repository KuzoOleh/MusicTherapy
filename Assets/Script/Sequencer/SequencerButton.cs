using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Physical 3D poke-button behaviour, built on the project's existing PokeButton
// visual/press mechanism (XRSimpleInteractable + XRPokeFilter +
// XRPokeFollowAffordance) rather than flat Canvas UI. Pressing it toggles a
// continuously looping clip on/off — press once to start the loop (quantized
// to the shared clock's next bar), press again to stop it. Stopping doesn't
// cut the audio off mid-playback: the clip is left to finish its current
// loop cycle (its "last beat") and then stops on its own. Pressing again
// while it's winding down cancels the pending stop and keeps it looping.
// While playing, a small per-beat progress bar above the button fills in
// to show where the loop currently is, resetting each time it repeats.
public class SequencerButton : MonoBehaviour
{
    [SerializeField] private Renderer buttonRenderer;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private float debounceSeconds = 0.3f;

    [Header("Loop Progress Ring")]
    [SerializeField] private float ringOuterRadius = 0.03f;
    [SerializeField] private float ringInnerRadius = 0.018f;
    [SerializeField] private float ringSegmentGapDegrees = 6f;
    [SerializeField] private float ringVerticalOffset = 0.06f;
    [SerializeField] private int ringArcResolution = 8;
    [SerializeField] private Color ringEmptyColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color ringFilledColor = new Color(0.2f, 0.9f, 0.5f, 1f);

    private SequencerButtonDefinition definition;
    private SequencerClock clock;
    private SequencerRecordController recordController;
    private XRBaseInteractable interactable;
    private bool isPlaying;
    private bool isStopping;
    private float lastPressTime = float.NegativeInfinity;
    private double loopStartDspTime;

    private GameObject progressBarRoot;
    private readonly List<MeshFilter> progressFillMeshes = new List<MeshFilter>();
    private readonly List<float> progressSegmentStartAngle = new List<float>();
    private float progressSegmentSweep;

    public void Initialize(SequencerButtonDefinition def, SequencerClock sharedClock,
        SequencerRecordController controller)
    {
        definition = def;
        clock = sharedClock;
        recordController = controller;

        if (interactable == null)
        {
            interactable = GetComponent<XRBaseInteractable>();
        }
        interactable.selectEntered.AddListener(_ => OnPressed());

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.clip = def.clip;
        audioSource.volume = def.volume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        if (buttonRenderer == null)
        {
            buttonRenderer = GetComponentInChildren<Renderer>();
        }

        if (buttonRenderer != null)
        {
            buttonRenderer.material.color = ColorForGroup(def.groupName);
        }

        BuildProgressBar();
    }

    private Color ColorForGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            return defaultColor;
        }

        float hue = Mathf.Abs(groupName.GetHashCode() % 360) / 360f;
        return Color.HSVToRGB(hue, 0.55f, 0.9f);
    }

    private void OnPressed()
    {
        // XRI's poke filter re-evaluates hover/depth requirements every frame based on
        // fingertip velocity direction; a slightly hesitant real-world poke can flicker
        // across that boundary for a frame or two, firing selectEntered more than once
        // for what feels like a single press. Debounce so that doesn't toggle us twice.
        if (Time.unscaledTime - lastPressTime < debounceSeconds)
        {
            return;
        }
        lastPressTime = Time.unscaledTime;

        if (isPlaying)
        {
            // Don't cut the audio off — let the current loop cycle ring out to its
            // last beat, then stop naturally. Pressing again while it's winding
            // down cancels the pending stop and resumes looping indefinitely.
            isStopping = !isStopping;
            audioSource.loop = !isStopping;
            return;
        }

        double startAt = clock.GetNextQuantizedDspTime();
        audioSource.loop = true;
        audioSource.PlayScheduled(startAt);
        loopStartDspTime = startAt;
        isPlaying = true;
        isStopping = false;
        clock.NotifyLoopStarted();
        recordController?.NotifyButtonPressed(definition);

        if (progressBarRoot != null)
        {
            progressBarRoot.SetActive(true);
        }
    }

    private void Update()
    {
        if (isPlaying)
        {
            UpdateProgressBar();
        }

        // audioSource.isPlaying is false both before the scheduled start arrives (quantized
        // presses can wait up to a full bar) AND after the clip genuinely finishes — without
        // this guard, requesting a stop while still waiting for that scheduled start looked
        // identical to "already stopped," so the button's state reset itself immediately and
        // a real stop request right after got treated as a fresh start instead. Only trust
        // audioSource.isPlaying once we're actually past the moment it was told to start.
        bool hasReachedScheduledStart = AudioSettings.dspTime >= loopStartDspTime;

        // audioSource.loop was cleared on request, so it stops itself right after
        // finishing the cycle that was already playing — this just notices that and
        // reconciles our own state (and the shared clock) once it actually happens.
        if (isStopping && isPlaying && hasReachedScheduledStart && !audioSource.isPlaying)
        {
            isStopping = false;
            isPlaying = false;
            clock.NotifyLoopStopped();
            ResetProgressBar();
        }
    }

    // Small radial dial above the button, split into one wedge per beat (clockwise from
    // the top) — a dim static "track" wedge per beat plus a bright "fill" wedge on top
    // that sweeps from 0 to that beat's full arc as it plays.
    private void BuildProgressBar()
    {
        int segments = clock != null ? Mathf.Max(1, clock.BeatsPerBar) : 4;

        progressBarRoot = new GameObject("ProgressBar");
        progressBarRoot.transform.SetParent(transform, false);
        progressBarRoot.transform.localPosition = new Vector3(0f, ringVerticalOffset, 0f);
        progressBarRoot.transform.localRotation = Quaternion.identity;

        float sweepPerSegment = 360f / segments;
        progressSegmentSweep = sweepPerSegment - ringSegmentGapDegrees;

        progressFillMeshes.Clear();
        progressSegmentStartAngle.Clear();

        for (int i = 0; i < segments; i++)
        {
            float segStartAngle = i * sweepPerSegment + ringSegmentGapDegrees / 2f;
            progressSegmentStartAngle.Add(segStartAngle);

            var track = CreateRingSectorObject($"Beat {i} - Track", ringEmptyColor, 0f);
            track.GetComponent<MeshFilter>().mesh =
                BuildRingSectorMesh(segStartAngle, progressSegmentSweep, ringInnerRadius, ringOuterRadius, ringArcResolution);

            var fill = CreateRingSectorObject($"Beat {i} - Fill", ringFilledColor, -0.001f);
            // Starts as a zero-sweep (invisible) wedge; UpdateProgressBar grows it each frame.
            fill.GetComponent<MeshFilter>().mesh =
                BuildRingSectorMesh(segStartAngle, 0f, ringInnerRadius, ringOuterRadius, ringArcResolution);

            progressFillMeshes.Add(fill.GetComponent<MeshFilter>());
        }

        progressBarRoot.SetActive(false);
    }

    private GameObject CreateRingSectorObject(string name, Color color, float zOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(progressBarRoot.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, zOffset);
        go.transform.localRotation = Quaternion.identity;

        go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color")) { color = color };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go;
    }

    // Builds a flat ring-sector (annulus wedge) mesh spanning startAngleDeg to
    // startAngleDeg + sweepDeg, measured clockwise from straight up. Triangles are
    // emitted in both winding orders so the wedge is visible from either side
    // regardless of which way the button (and its parent hierarchy) happens to face.
    private static Mesh BuildRingSectorMesh(float startAngleDeg, float sweepDeg, float innerRadius, float outerRadius, int segments)
    {
        segments = Mathf.Max(1, segments);
        var mesh = new Mesh();
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 12];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angleRad = Mathf.Deg2Rad * (startAngleDeg + sweepDeg * t);
            float sin = Mathf.Sin(angleRad);
            float cos = Mathf.Cos(angleRad);
            vertices[i * 2] = new Vector3(sin * innerRadius, cos * innerRadius, 0f);
            vertices[i * 2 + 1] = new Vector3(sin * outerRadius, cos * outerRadius, 0f);
        }

        for (int i = 0; i < segments; i++)
        {
            int b = i * 2;
            int t = i * 12;
            triangles[t + 0] = b;
            triangles[t + 1] = b + 1;
            triangles[t + 2] = b + 2;
            triangles[t + 3] = b + 1;
            triangles[t + 4] = b + 3;
            triangles[t + 5] = b + 2;
            // Mirrored winding so the wedge renders regardless of viewing side.
            triangles[t + 6] = b + 2;
            triangles[t + 7] = b + 1;
            triangles[t + 8] = b;
            triangles[t + 9] = b + 2;
            triangles[t + 10] = b + 3;
            triangles[t + 11] = b + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Reconstructs the loop's current position from the dsp-scheduled start time rather
    // than counting frames, so it stays sample-accurate and self-corrects for any hitch.
    private void UpdateProgressBar()
    {
        if (progressFillMeshes.Count == 0 || audioSource.clip == null)
        {
            return;
        }

        double elapsed = AudioSettings.dspTime - loopStartDspTime;
        if (elapsed < 0)
        {
            return; // scheduled to start, but hasn't yet — stay empty
        }

        float clipLength = audioSource.clip.length;
        if (clipLength <= 0f)
        {
            return;
        }

        double cyclePos = elapsed % clipLength;
        float fractionInCycle = (float)(cyclePos / clipLength);

        int segments = progressFillMeshes.Count;
        float scaledPos = fractionInCycle * segments;
        int currentBeat = Mathf.Clamp(Mathf.FloorToInt(scaledPos), 0, segments - 1);
        float beatFraction = scaledPos - currentBeat;

        for (int i = 0; i < segments; i++)
        {
            float fraction = i < currentBeat ? 1f : (i == currentBeat ? beatFraction : 0f);
            float sweep = progressSegmentSweep * fraction;

            progressFillMeshes[i].mesh = BuildRingSectorMesh(
                progressSegmentStartAngle[i], sweep, ringInnerRadius, ringOuterRadius, ringArcResolution);
        }
    }

    private void ResetProgressBar()
    {
        for (int i = 0; i < progressFillMeshes.Count; i++)
        {
            progressFillMeshes[i].mesh = BuildRingSectorMesh(
                progressSegmentStartAngle[i], 0f, ringInnerRadius, ringOuterRadius, ringArcResolution);
        }

        if (progressBarRoot != null)
        {
            progressBarRoot.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (isPlaying)
        {
            clock.NotifyLoopStopped();
        }
    }
}
