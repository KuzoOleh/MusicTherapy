using System;
using System.Collections;
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
//
// VR polish pass: every control state (idle / playing / winding-down) now
// has its own unmistakable visual signal instead of relying on the player
// remembering what they last pressed, group colors are assigned so they
// can't collide, a dark backplate keeps the progress ring legible against
// any background, presses get a short tactile "punch" so pressing in
// mid-air feels confirmed, and the ring no longer rebuilds a brand-new
// Mesh + Material every frame (see BuildRingSectorMesh / CreateRingSectorObject
// comments below for the bug that fixed).
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
    [SerializeField] private float ringForwardOffset = -0.05f;
    [SerializeField] private int ringArcResolution = 8;
    [SerializeField] private Color ringEmptyColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color ringFilledColor = new Color(0.2f, 0.9f, 0.5f, 1f);

    [Header("VR Feedback Polish")]
    [Tooltip("Shown on the ring (pulsing) and tinted into the button while a loop is winding down, so 'about to stop' reads completely differently from 'about to loop again' at a glance.")]
    [SerializeField] private Color stoppingColor = new Color(0.95f, 0.65f, 0.15f, 1f);
    [SerializeField] private bool pulseWhileStopping = true;
    [SerializeField] private float pulseSpeed = 6f;
    [Tooltip("Brief scale 'punch' on press so a mid-air poke — which has no physical click — still feels confirmed.")]
    [SerializeField] private bool pressPunchEnabled = true;
    [SerializeField] [Range(0.5f, 0.99f)] private float pressPunchScale = 0.9f;
    [SerializeField] private float pressPunchDuration = 0.12f;
    [Tooltip("Dark disc behind the progress ring so it stays readable regardless of what's visible behind it in the room/scene. Off by default: with a ring sized/positioned to sit right on top of the button (as this project's SeqButton prefab does), a solid backplate hides the button's own color while it's playing instead of just outlining it.")]
    [SerializeField] private bool ringBackplateEnabled = false;
    [SerializeField] private Color ringBackplateColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private float ringBackplateRadiusPadding = 0.006f;

    [Header("Playing Glow")]
    [Tooltip("Soft glow behind the button, tinted to match its current color, that fades in while its loop is playing and fades out once it fully stops — a 'this one is making sound right now' cue you can pick up in peripheral vision without staring at the small progress ring.")]
    [SerializeField] private bool glowEnabled = true;
    [Tooltip("World-space radius of the glow at full audio level, in meters. Deliberately much bigger than the button itself so it reads as an obvious ambient wash rather than a subtle rim — the soft falloff texture keeps even a large glow from looking like a hard-edged disc.")]
    [SerializeField] private float glowRadius = 0.3f;
    [Tooltip("How far behind the button's own face the glow sits, in the button's own local space (positive = away from the player, matching this project's forward-facing convention). Kept clear of both the button's front face and the group color panel further back — too small and it reads as sitting flush on the button; too large and it can land level with the group panel and flicker against it.")]
    [SerializeField] private float glowForwardOffset = 0.03f;
    [SerializeField] [Range(0f, 1f)] private float glowMaxAlpha = 0.75f;
    [SerializeField] private float glowFadeDuration = 0.25f;

    [Header("Glow Audio Reactivity")]
    [Tooltip("Makes the glow's brightness and size continuously track how loud the audio is right now, instead of just holding steady at full brightness/size the whole time it's playing — a soft 'breathing with the music' pulse.")]
    [SerializeField] private bool glowReactsToAudio = true;
    [Tooltip("Multiplies the audio's raw RMS level before it's clamped to 0-1. Raise this if the glow barely reacts (quiet source clips); lower it if it's pinned at full brightness/size the whole time.")]
    [SerializeField] private float audioReactivitySensitivity = 8f;
    [Tooltip("How smoothly the glow follows the audio level, 0-1. 0 snaps instantly to the raw (noisy) per-frame level; closer to 1 glides between values instead of flickering with every sample.")]
    [SerializeField] [Range(0f, 0.98f)] private float audioReactivitySmoothing = 0.85f;
    [Tooltip("The glow's brightness/size floor during quiet passages, as a fraction of full — so it dims and shrinks a little with the music instead of disappearing or collapsing to a point between hits.")]
    [SerializeField] [Range(0f, 1f)] private float audioReactivityFloor = 0.6f;

    private SequencerButtonDefinition definition;
    private SequencerClock clock;
    private SequencerRecordController recordController;
    private XRBaseInteractable interactable;
    private bool isPlaying;
    private bool isStopping;
    private float lastPressTime = float.NegativeInfinity;
    private double loopStartDspTime;

    // Set from Initialize's optional groupIndex/groupCount. -1 means "not
    // provided", which falls back to the legacy hash-based color so any
    // other caller that doesn't pass them still gets a deterministic color.
    private int groupIndex = -1;
    private int groupCount = 1;

    private Vector3 baseScale = Vector3.one;
    private Coroutine punchRoutine;

    private GameObject progressBarRoot;
    private readonly List<MeshFilter> progressFillMeshes = new List<MeshFilter>();
    private readonly List<float> progressSegmentStartAngle = new List<float>();
    private float progressSegmentSweep;

    // Two shared materials reused by every track/fill wedge on this button
    // (see BuildProgressBar) instead of one Material instance per wedge.
    private Material ringTrackMaterial;
    private Material ringFillMaterial;
    private Material ringBackplateMaterial;

    private GameObject glowRoot;
    private Material glowMaterial;
    private Coroutine glowFadeRoutine;

    // The "envelope" is the fade/pulse-driven target alpha and tint, written by
    // FadeGlowRoutine (playing on/off) and UpdateStoppingPulse (wind-down pulse).
    // The value actually written to glowMaterial each frame is this envelope
    // combined with the continuously-updating audio-reactivity multiplier below
    // (see UpdateGlowAudioReactivity) — separating them means a coroutine's alpha
    // fade and the per-frame audio pulse never overwrite each other's work.
    private float glowEnvelopeAlpha;
    private Color glowEnvelopeTint = Color.white;

    // Smoothed 0-1 "how loud is the audio right now" signal that drives the
    // glow's audio reactivity, plus a reusable sample buffer for
    // AudioSource.GetOutputData so reading it every frame doesn't allocate
    // garbage (same anti-GC reasoning as the ring's mesh-rewrite-in-place fix).
    private float smoothedAudioLevel;
    private float[] audioSampleBuffer;
    private const int AudioSampleBufferSize = 256;

    // The glow's soft radial falloff is the same shape for every button regardless
    // of group color (only the tint differs), so — same reasoning as the shared
    // ring materials — it's built once and reused across every SequencerButton
    // instance instead of one texture per button.
    private static Texture2D sharedGlowTexture;

    public void Initialize(SequencerButtonDefinition def, SequencerClock sharedClock,
        SequencerRecordController controller, int assignedGroupIndex = -1, int assignedGroupCount = 1)
    {
        definition = def;
        clock = sharedClock;
        recordController = controller;
        groupIndex = assignedGroupIndex;
        groupCount = Mathf.Max(1, assignedGroupCount);
        baseScale = transform.localScale;

        if (interactable == null)
        {
            interactable = GetComponent<XRBaseInteractable>();
        }
        interactable.selectEntered.AddListener(_ => OnPressed());
        interactable.hoverEntered.AddListener(_ => OnHoverEntered());
        interactable.hoverExited.AddListener(_ => OnHoverExited());

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
            buttonRenderer.material.color = ColorForGroup();
        }

        BuildProgressBar();
        BuildGlow();
    }

    private Color ColorForGroup()
    {
        string groupName = definition != null ? definition.groupName : null;
        if (string.IsNullOrEmpty(groupName))
        {
            return defaultColor;
        }

        float hue;
        if (groupIndex >= 0)
        {
            // Evenly spaced hues via the golden-ratio conjugate so neighboring
            // groups never land on near-identical colors the way a raw
            // hash-mod-360 sometimes did (two different instrument groups could
            // hash to hues a few degrees apart and read as "the same color").
            hue = Mathf.Repeat(groupIndex * 0.618033988749895f, 1f);
        }
        else
        {
            // Legacy fallback for any caller that doesn't have index/count context.
            hue = Mathf.Abs(groupName.GetHashCode() % 360) / 360f;
        }

        return Color.HSVToRGB(hue, 0.55f, 0.9f);
    }

    private void OnHoverEntered()
    {
        if (buttonRenderer != null && !isStopping)
        {
            buttonRenderer.material.color = Color.Lerp(ColorForGroup(), Color.white, 0.4f);
        }
    }

    private void OnHoverExited()
    {
        if (buttonRenderer != null && !isStopping)
        {
            buttonRenderer.material.color = ColorForGroup();
        }
    }

    private void OnPressed()
    {
        Debug.Log($"[SequencerButton] Pressed: {definition.groupName}/{definition.displayLabel}", this);

        // XRI's poke filter re-evaluates hover/depth requirements every frame based on
        // fingertip velocity direction; a slightly hesitant real-world poke can flicker
        // across that boundary for a frame or two, firing selectEntered more than once
        // for what feels like a single press. Debounce so that doesn't toggle us twice.
        if (Time.unscaledTime - lastPressTime < debounceSeconds)
        {
            return;
        }
        lastPressTime = Time.unscaledTime;

        TriggerPressPunch();

        if (isPlaying)
        {
            // Don't cut the audio off immediately — quantize the stop to the next beat
            // boundary (not the end of the whole clip, which is what merely clearing
            // audioSource.loop would wait for). Pressing again while it's winding down
            // cancels the pending stop and resumes looping indefinitely.
            isStopping = !isStopping;
            if (isStopping)
            {
                audioSource.SetScheduledEndTime(GetNextBeatDspTime());
            }
            else
            {
                // No API to "unschedule" an end time — push it far enough out that it
                // never actually arrives, which is equivalent to canceling the stop.
                audioSource.SetScheduledEndTime(AudioSettings.dspTime + 1e9);
                // Cancelling mid-pulse could otherwise leave the button visibly tinted
                // amber forever even though it's back to a normal, indefinitely-looping state.
                if (buttonRenderer != null)
                {
                    buttonRenderer.material.color = ColorForGroup();
                }
            }
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
        SetGlowVisible(true);
    }

    // Finds the next beat boundary (relative to this loop's own start time, not the shared
    // clock's bar anchor) that's still safely in the future for SetScheduledEndTime to land on.
    private double GetNextBeatDspTime()
    {
        double now = AudioSettings.dspTime;
        double beat = clock.BeatDuration;
        double elapsedSinceStart = now - loopStartDspTime;
        double nextBeatDspTime = loopStartDspTime + Math.Ceiling(elapsedSinceStart / beat) * beat;
        if (nextBeatDspTime <= now)
        {
            nextBeatDspTime += beat;
        }
        return nextBeatDspTime;
    }

    private void Update()
    {
        if (isPlaying)
        {
            UpdateProgressBar();
            UpdateStoppingPulse();
        }

        // audioSource.isPlaying is false both before the scheduled start arrives (quantized
        // presses can wait up to a full bar) AND after the clip genuinely finishes — without
        // this guard, requesting a stop while still waiting for that scheduled start looked
        // identical to "already stopped," so the button's state reset itself immediately and
        // a real stop request right after got treated as a fresh start instead. Only trust
        // audioSource.isPlaying once we're actually past the moment it was told to start.
        bool hasReachedScheduledStart = AudioSettings.dspTime >= loopStartDspTime;

        // The scheduled end time set on request forces a hard stop once it arrives — this
        // just notices that and reconciles our own state (and the shared clock) once it does.
        if (isStopping && isPlaying && hasReachedScheduledStart && !audioSource.isPlaying)
        {
            isStopping = false;
            isPlaying = false;
            clock.NotifyLoopStopped();
            ResetProgressBar();

            if (buttonRenderer != null)
            {
                buttonRenderer.material.color = ColorForGroup();
            }
            SetGlowVisible(false);
        }
    }

    // Billboards the glow to always face the viewer. Left coplanar with the button
    // (its default orientation, inherited from the button's own transform), a flat
    // quad viewed from an angled viewpoint — which in VR is the normal case, not an
    // edge case, since the player is rarely looking dead-on at every button on the
    // panel — foreshortens down to a thin sliver or vanishes outright well before it
    // gets anywhere near edge-on. Rotating it to face the camera every frame is the
    // standard fix for exactly this (the same technique lens flares and light
    // sprites use), and is far more robust than just sizing the glow bigger.
    private void LateUpdate()
    {
        if (glowRoot != null && glowRoot.activeSelf && Camera.main != null)
        {
            glowRoot.transform.rotation = Camera.main.transform.rotation;
        }

        if (glowEnabled && glowRoot != null && glowRoot.activeSelf && glowMaterial != null)
        {
            UpdateGlowAudioReactivity();
        }
    }

    // Computes a smoothed 0-1 "how loud is the audio right now" level from the
    // audio source's live output (there's no literal note-velocity value in this
    // project's simple looping-clip playback, so real-time loudness is the closest
    // stand-in for "how hard was this hit"), then applies it — together with the
    // envelope alpha/tint written by FadeGlowRoutine/UpdateStoppingPulse — to the
    // glow's actual material color and world scale. This runs every frame the glow
    // is visible, regardless of which coroutine (if any) is currently driving the
    // envelope, so the audio pulse and the fade/stop-pulse never fight over who
    // writes glowMaterial.color last.
    private void UpdateGlowAudioReactivity()
    {
        float targetLevel = 0f;
        if (glowReactsToAudio && isPlaying && audioSource != null && audioSource.isPlaying && audioSource.clip != null)
        {
            if (audioSampleBuffer == null)
            {
                audioSampleBuffer = new float[AudioSampleBufferSize];
            }
            audioSource.GetOutputData(audioSampleBuffer, 0);

            float sumSquares = 0f;
            for (int i = 0; i < audioSampleBuffer.Length; i++)
            {
                float sample = audioSampleBuffer[i];
                sumSquares += sample * sample;
            }
            float rms = Mathf.Sqrt(sumSquares / audioSampleBuffer.Length);
            targetLevel = Mathf.Clamp01(rms * audioReactivitySensitivity);
        }

        // Exponential smoothing so the glow "breathes" with the music instead of
        // flickering with every noisy per-buffer RMS reading.
        smoothedAudioLevel = Mathf.Lerp(smoothedAudioLevel, targetLevel, 1f - audioReactivitySmoothing);

        float reactivity = glowReactsToAudio ? Mathf.Lerp(audioReactivityFloor, 1f, smoothedAudioLevel) : 1f;

        Color finalColor = glowEnvelopeTint;
        finalColor.a = glowEnvelopeAlpha * reactivity;
        glowMaterial.color = finalColor;

        float scale = glowRadius * 2f * reactivity;
        glowRoot.transform.localScale = new Vector3(scale, scale, 1f);
    }

    // Pulses the button and ring fill color between the group color and
    // stoppingColor while winding down, so "this loop is about to stop" is
    // visible without staring at the progress ring to catch it finishing.
    private void UpdateStoppingPulse()
    {
        if (!isStopping || !pulseWhileStopping)
        {
            return;
        }

        float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
        Color pulsed = Color.Lerp(ColorForGroup(), stoppingColor, t);

        if (buttonRenderer != null)
        {
            buttonRenderer.material.color = pulsed;
        }
        if (ringFillMaterial != null)
        {
            ringFillMaterial.color = pulsed;
        }
        if (glowMaterial != null)
        {
            // The glow is still fully visible throughout the wind-down (it only fades
            // out once playback actually stops, via SetGlowVisible below), so its
            // envelope alpha stays pinned at glowMaxAlpha here — only the tint itself
            // pulses. This writes the envelope, not glowMaterial.color directly; the
            // per-frame audio-reactivity multiplier is layered on top in
            // UpdateGlowAudioReactivity.
            glowEnvelopeTint = pulsed;
            glowEnvelopeAlpha = glowMaxAlpha;
        }
    }

    // Short "squish" so a mid-air poke — which has no physical click — still
    // reads as a confirmed press. Purely cosmetic; state changes are already
    // decided by the time this runs.
    private void TriggerPressPunch()
    {
        if (!pressPunchEnabled)
        {
            return;
        }

        if (punchRoutine != null)
        {
            StopCoroutine(punchRoutine);
        }
        punchRoutine = StartCoroutine(PressPunchRoutine());
    }

    private IEnumerator PressPunchRoutine()
    {
        float half = Mathf.Max(0.01f, pressPunchDuration) * 0.5f;

        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            transform.localScale = baseScale * Mathf.Lerp(1f, pressPunchScale, t / half);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            transform.localScale = baseScale * Mathf.Lerp(pressPunchScale, 1f, t / half);
            yield return null;
        }

        transform.localScale = baseScale;
        punchRoutine = null;
    }

    // Small radial dial above the button, split into one wedge per beat (clockwise from
    // the top) — a dim static "track" wedge per beat plus a bright "fill" wedge on top
    // that sweeps from 0 to that beat's full arc as it plays. A solid dark backplate disc
    // sits behind both so the ring stays legible against whatever happens to be behind it.
    private void BuildProgressBar()
    {
        int segments = clock != null ? Mathf.Max(1, clock.BeatsPerBar) : 4;

        progressBarRoot = new GameObject("ProgressBar");
        progressBarRoot.transform.SetParent(transform, false);
        // ringForwardOffset pushes the ring out along the button's front-facing axis so it
        // renders clearly in front of the button's own surface instead of flush with (or
        // clipping into) it.
        progressBarRoot.transform.localPosition = new Vector3(0f, ringVerticalOffset, ringForwardOffset);
        progressBarRoot.transform.localRotation = Quaternion.identity;

        float sweepPerSegment = 360f / segments;
        progressSegmentSweep = sweepPerSegment - ringSegmentGapDegrees;

        progressFillMeshes.Clear();
        progressSegmentStartAngle.Clear();

        // One shared material per role (track / fill) instead of one instantiated
        // Material per wedge — a 4-beat ring used to leak 8 never-destroyed Material
        // instances per button (16+ for busier banks), which adds up fast across a
        // full sequencer panel. These two are cleaned up in OnDestroy.
        ringTrackMaterial = new Material(Shader.Find("Unlit/Color")) { color = ringEmptyColor };
        ringFillMaterial = new Material(Shader.Find("Unlit/Color")) { color = ringFilledColor };

        if (ringBackplateEnabled)
        {
            BuildRingBackplate(ringOuterRadius + ringBackplateRadiusPadding);
        }

        for (int i = 0; i < segments; i++)
        {
            float segStartAngle = i * sweepPerSegment + ringSegmentGapDegrees / 2f;
            progressSegmentStartAngle.Add(segStartAngle);

            var track = CreateRingSectorObject($"Beat {i} - Track", ringTrackMaterial, 0f);
            track.GetComponent<MeshFilter>().mesh =
                BuildRingSectorMesh(segStartAngle, progressSegmentSweep, ringInnerRadius, ringOuterRadius, ringArcResolution);

            var fill = CreateRingSectorObject($"Beat {i} - Fill", ringFillMaterial, -0.001f);
            // Starts as a zero-sweep (invisible) wedge; UpdateProgressBar grows it each frame.
            var fillMesh = BuildRingSectorMesh(segStartAngle, 0f, ringInnerRadius, ringOuterRadius, ringArcResolution);
            fillMesh.MarkDynamic(); // this mesh's vertices are rewritten every frame while playing
            fill.GetComponent<MeshFilter>().mesh = fillMesh;

            progressFillMeshes.Add(fill.GetComponent<MeshFilter>());
        }

        progressBarRoot.SetActive(false);
    }

    private void BuildRingBackplate(float radius)
    {
        ringBackplateMaterial = new Material(Shader.Find("Unlit/Color")) { color = ringBackplateColor };

        var backplate = new GameObject("Backplate");
        backplate.transform.SetParent(progressBarRoot.transform, false);
        // Sits just behind the track wedges (z=0) along the ring's own forward axis.
        backplate.transform.localPosition = new Vector3(0f, 0f, 0.0015f);
        backplate.transform.localRotation = Quaternion.identity;

        backplate.AddComponent<MeshFilter>().mesh = BuildRingSectorMesh(0f, 360f, 0f, radius, Mathf.Max(12, ringArcResolution * 4));

        var renderer = backplate.AddComponent<MeshRenderer>();
        renderer.material = ringBackplateMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Additive-blended radial glow sitting just behind the button, tinted to match
    // ColorForGroup(). Built once per button (inactive/invisible) and toggled on/off
    // by SetGlowVisible; kept SetActive(false) whenever fully faded out rather than
    // just alpha-0, matching progressBarRoot's own pattern above, so an idle button
    // isn't still paying for a draw call that renders nothing.
    private void BuildGlow()
    {
        if (!glowEnabled)
        {
            return;
        }

        glowRoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glowRoot.name = "PlayingGlow";
        var glowCollider = glowRoot.GetComponent<Collider>();
        if (glowCollider != null)
        {
            // Purely visual — must not block pokes meant for the button in front of it.
            Destroy(glowCollider);
        }

        glowRoot.transform.SetParent(transform, false);
        glowRoot.transform.localPosition = new Vector3(0f, 0f, glowForwardOffset);
        glowRoot.transform.localRotation = Quaternion.identity;
        glowRoot.transform.localScale = new Vector3(glowRadius * 2f, glowRadius * 2f, 1f);

        // Sprites/Default, not a "glow" shader like Particles/Additive — this project's
        // Unity version doesn't ship that one (Shader.Find returned null and crashed
        // every button on Initialize). Sprites/Default is already proven to work here
        // (SequencerPanelBuilder's group color panels use it), supports alpha blending
        // so the radial falloff texture can fade to see-through at the edge, and maps
        // Material.color to its tint the normal way — no "_TintColor"-style special case.
        glowMaterial = new Material(Shader.Find("Sprites/Default"))
        {
            mainTexture = GetOrBuildGlowTexture()
        };
        glowEnvelopeTint = ColorForGroup();
        glowEnvelopeAlpha = 0f;
        Color startColor = glowEnvelopeTint;
        startColor.a = 0f;
        glowMaterial.color = startColor;

        // Reused every frame by UpdateGlowAudioReactivity — allocated once here
        // rather than per-call.
        audioSampleBuffer = new float[AudioSampleBufferSize];

        var glowRenderer = glowRoot.GetComponent<Renderer>();
        glowRenderer.material = glowMaterial;
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;

        glowRoot.SetActive(false);
    }

    // Builds (once, then reuses) a soft circular falloff mask: opaque white at the
    // center, fading to fully transparent at the edge. Squaring the linear falloff
    // gives it a brighter core with a faster taper near the rim, which reads more
    // like a glow/bloom than a flat cone of light.
    private static Texture2D GetOrBuildGlowTexture()
    {
        if (sharedGlowTexture != null)
        {
            return sharedGlowTexture;
        }

        const int size = 64;
        float half = size * 0.5f;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float py = (y + 0.5f) - half;
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) - half;
                float normalizedDist = Mathf.Sqrt(px * px + py * py) / half;
                float falloff = Mathf.Clamp01(1f - normalizedDist);
                float alpha = falloff * falloff;
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        sharedGlowTexture = texture;
        return texture;
    }

    private void SetGlowVisible(bool visible)
    {
        if (!glowEnabled || glowRoot == null)
        {
            return;
        }

        if (glowFadeRoutine != null)
        {
            StopCoroutine(glowFadeRoutine);
        }

        if (visible)
        {
            glowRoot.SetActive(true);
        }
        glowFadeRoutine = StartCoroutine(FadeGlowRoutine(visible));
    }

    private IEnumerator FadeGlowRoutine(bool visible)
    {
        float startAlpha = glowEnvelopeAlpha;
        float targetAlpha = visible ? glowMaxAlpha : 0f;
        float duration = Mathf.Max(0.01f, glowFadeDuration);

        // Writes the envelope fields, not glowMaterial.color directly — the actual
        // material color/scale is applied every frame in UpdateGlowAudioReactivity,
        // which layers the audio-reactivity multiplier on top of this envelope.
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            glowEnvelopeTint = ColorForGroup();
            glowEnvelopeAlpha = Mathf.Lerp(startAlpha, targetAlpha, t / duration);
            yield return null;
        }

        glowEnvelopeTint = ColorForGroup();
        glowEnvelopeAlpha = targetAlpha;

        if (!visible)
        {
            glowRoot.SetActive(false);
        }
        glowFadeRoutine = null;
    }

    private GameObject CreateRingSectorObject(string name, Material sharedMaterial, float zOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(progressBarRoot.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, zOffset);
        go.transform.localRotation = Quaternion.identity;

        go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.material = sharedMaterial;
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
        WriteRingSectorVertices(mesh, startAngleDeg, sweepDeg, innerRadius, outerRadius, segments, out int[] triangles);
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Shared vertex/triangle layout used both to build a fresh mesh (above) and to
    // re-sweep an existing one every frame (below) without reallocating triangles.
    private static void WriteRingSectorVertices(Mesh mesh, float startAngleDeg, float sweepDeg, float innerRadius, float outerRadius, int segments, out int[] triangles)
    {
        var vertices = new Vector3[(segments + 1) * 2];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angleRad = Mathf.Deg2Rad * (startAngleDeg + sweepDeg * t);
            float sin = Mathf.Sin(angleRad);
            float cos = Mathf.Cos(angleRad);
            vertices[i * 2] = new Vector3(sin * innerRadius, cos * innerRadius, 0f);
            vertices[i * 2 + 1] = new Vector3(sin * outerRadius, cos * outerRadius, 0f);
        }

        mesh.vertices = vertices;

        triangles = new int[segments * 12];
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
    }

    // Reconstructs the loop's current position from the dsp-scheduled start time rather
    // than counting frames, so it stays sample-accurate and self-corrects for any hitch.
    //
    // BUG FIX: this used to call BuildRingSectorMesh(...) here — allocating a brand-new
    // Mesh object plus fresh vertex/triangle arrays for every one of the ring's segments,
    // every single frame, for every currently-playing button. That's continuous GC
    // garbage on the hot path, which is exactly the kind of thing that causes the frame
    // hitches/dropped frames that break VR comfort. Now it rewrites the *existing*
    // mesh's vertex buffer in place (triangles/topology never change, only the sweep
    // angle does) and skips the buttons that aren't actually animating.
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

            WriteRingSectorVertices(progressFillMeshes[i].mesh, progressSegmentStartAngle[i], sweep, ringInnerRadius, ringOuterRadius, ringArcResolution, out _);
        }
    }

    private void ResetProgressBar()
    {
        for (int i = 0; i < progressFillMeshes.Count; i++)
        {
            WriteRingSectorVertices(progressFillMeshes[i].mesh, progressSegmentStartAngle[i], 0f, ringInnerRadius, ringOuterRadius, ringArcResolution, out _);
        }

        if (progressBarRoot != null)
        {
            progressBarRoot.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (isPlaying && clock != null)
        {
            clock.NotifyLoopStopped();
        }

        // The two ring materials are instantiated per-button in BuildProgressBar and
        // aren't referenced anywhere else, so they won't get cleaned up on their own.
        if (ringTrackMaterial != null) Destroy(ringTrackMaterial);
        if (ringFillMaterial != null) Destroy(ringFillMaterial);
        if (ringBackplateMaterial != null) Destroy(ringBackplateMaterial);

        // Same deal for the glow's material (created in BuildGlow). The shared glow
        // texture it references is NOT destroyed here — every button points at that
        // same one instance.
        if (glowMaterial != null) Destroy(glowMaterial);
    }
}
