using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Physical 3D poke-button behaviour, built on the project's existing PokeButton
// visual/press mechanism (XRSimpleInteractable + XRPokeFilter +
// XRPokeFollowAffordance) rather than flat Canvas UI. Pressing it toggles a
// continuously looping clip on/off — press once to start the loop (quantized
// to the shared clock), press again to stop it. It keeps playing on its own
// until you press it again.
public class SequencerButton : MonoBehaviour
{
    [SerializeField] private Renderer buttonRenderer;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Color defaultColor = Color.white;

    private SequencerButtonDefinition definition;
    private SequencerClock clock;
    private SequencerRecordController recordController;
    private XRBaseInteractable interactable;
    private bool isPlaying;

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
        if (isPlaying)
        {
            audioSource.Stop();
            isPlaying = false;
            clock.NotifyLoopStopped();
            return;
        }

        double startAt = clock.GetNextQuantizedDspTime();
        audioSource.PlayScheduled(startAt);
        isPlaying = true;
        clock.NotifyLoopStarted();
        recordController?.NotifyButtonPressed(definition);
    }

    private void OnDestroy()
    {
        if (isPlaying)
        {
            clock.NotifyLoopStopped();
        }
    }
}
