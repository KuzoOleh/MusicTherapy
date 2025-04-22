using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ParticleTriggerKeyBased : MonoBehaviour
{
    GameManager gameManager;

    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;

    [SerializeField] private float pressedSpeed = 0f;
    [SerializeField] private bool isPressed = false;

    public XRBaseInteractable interactable;
    const float emissionDefaultRate = 1000f;

    private Vector3 oldPosition;
    private float speed;
    private bool particleFound = false;

    private void Awake()
    {
        gameManager = FindFirstObjectByType<GameManager>();
    }
    private void Start()
    {
        TryAssignParticleSystem();
        oldPosition = transform.position;

        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    private void Update()
    {
        // Check for particle system if not yet found
        if (!particleFound && particleSystem == null)
        {
            TryAssignParticleSystem();
        }

        // Calculate press speed
        Vector3 displacement = transform.position - oldPosition;
        speed = displacement.magnitude / Time.deltaTime;
        pressedSpeed = speed;
        oldPosition = transform.position;
    }

    private void TryAssignParticleSystem()
    {
        particleSystem = GetComponentInChildren<ParticleSystem>();
        if (particleSystem != null)
        {
            particleFound = true;
            Debug.Log("Particle system dynamically assigned.");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Hand"))
        {
            if (!isPressed)
            {
                gameManager.HitInstrument(gameObject.name, pressedSpeed);
                isPressed = true;
                pressedNote();
                Debug.Log("Hands entered");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Hand"))
        {
            isPressed = false;
            Debug.Log("Hands exited");
        }
    }

    private void pressedNote()
    {
        if (audioSource != null)
        {
            audioSource.volume = Mathf.Clamp01(pressedSpeed * 10f);
            audioSource.Play();
        }

        if (particleSystem != null)
        {
            var emission = particleSystem.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionDefaultRate);

            float emissionRate = emissionDefaultRate * pressedSpeed;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate);

            particleSystem.Play();

            Debug.Log("Particle system is playing");
        }
        else
        {
            Debug.LogWarning("No Particle System found when trying to play effect.");
        }
    }
}
