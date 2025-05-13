using UnityEngine;

public class VRNoteTrigger : MonoBehaviour
{
    private GameManager gameManager;

    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;

    private Vector3 oldPosition;
    private float pressedSpeed = 0f;

    private bool isPressed = false;
    private const float emissionDefaultRate = 300f;
    private const float pressThreshold = 0.1f; // Adjust based on how much movement counts as a "press"

    private void Awake()
    {
        gameManager = FindFirstObjectByType<GameManager>();
    }

    private void Start()
    {
        if (particleSystem == null)
        {
            particleSystem = GetComponentInChildren<ParticleSystem>();
            if (particleSystem != null)
                Debug.Log("Particle system auto-assigned.");
            else
                Debug.LogWarning("No Particle System found.");
        }

        if (audioSource != null)
            audioSource.Stop();

        oldPosition = transform.position;
    }

    private void Update()
    {
        // Calculate press speed
        Vector3 displacement = transform.position - oldPosition;
        pressedSpeed = displacement.magnitude / Time.deltaTime;
        oldPosition = transform.position;

        // Detect "press" based on speed threshold
        if (pressedSpeed > pressThreshold && !isPressed)
        {
            isPressed = true;

            gameManager?.HitInstrument(gameObject.name, pressedSpeed);
            PlayEffects();

            Debug.Log("Note pressed via movement.");
        }
        else if (pressedSpeed <= 0.01f)
        {
            isPressed = false;
        }
    }

    private void PlayEffects()
    {
        if (audioSource != null)
        {
            audioSource.volume = 0.5f; // default volume
            audioSource.Play();
        }

        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionDefaultRate);
            particleSystem.Play();

            Debug.Log("Particle system played.");
        }
    }
}
