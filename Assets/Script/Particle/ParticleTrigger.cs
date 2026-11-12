using UnityEngine;
using UnityEngine.Serialization;

public class ParticleTrigger : MonoBehaviour
{
    // Stick angular speed (rad/s) that counts as a full-strength (1.0) hit.
    private const float MaxStickAngularSpeed = 10f;
    // Used when a stick has no MeasureSpeed component (same value as the old default).
    private const float FallbackForce = 0.5f;
    private const float EmissionDefaultRate = 1000f;
    // Keeps very soft hits audible/visible; the recorded force is NOT affected by this.
    private const float MinFeedbackLevel = 0.1f;

    [FormerlySerializedAs("particleSystem")]
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private bool doesStickNeeded = false; // Set this per instrument in the Inspector
    [Tooltip("Minimum time between two registered hits. A stick with several 'Stick' colliders (or a jittery entry) would otherwise register one physical hit two or three times.")]
    [SerializeField] private float hitCooldownSeconds = 0.08f;

    private GameManager gameManager;
    private bool particlesAssignedExternally;
    private float lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        gameManager = FindFirstObjectByType<GameManager>();

        // NoteTrigger may already have handed us its instantiated particle system — keep it.
        if (!particlesAssignedExternally)
        {
            hitParticles = GetComponentInChildren<ParticleSystem>();
        }
    }

    public bool DoesStickNeeded()
    {
        return doesStickNeeded;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Stick"))
        {
            return;
        }

        if (Time.time - lastHitTime < hitCooldownSeconds)
        {
            return;
        }
        lastHitTime = Time.time;

        // leftHandStick/rightHandStick are only assigned when doesStickNeeded is true, so an
        // instrument that never got them wired up can still be hit without touching a null transform.
        Transform parentStick = other.transform.parent;
        GameObject hittingStick = null;
        if (leftHandStick != null && parentStick == leftHandStick.transform)
        {
            hittingStick = leftHandStick;
        }
        else if (rightHandStick != null && parentStick == rightHandStick.transform)
        {
            hittingStick = rightHandStick;
        }

        if (hittingStick != null)
        {
            // Force is measured for THIS hit first, and only then recorded — previously the
            // value written to the CSV was the one left over from the previous hit.
            float force = GetNormalizedStickForce(hittingStick);
            ApplyForceToEffects(force);

            if (gameManager != null)
            {
                gameManager.HitInstrument(gameObject.name, force);
            }
        }

        if (hitParticles != null) hitParticles.Play();
        if (audioSource != null) audioSource.Play();
    }

    // Uses the full angular speed of the stick, not just its rotation around the world X axis —
    // a hit from any direction now produces a comparable value.
    private static float GetNormalizedStickForce(GameObject stick)
    {
        if (stick.TryGetComponent(out MeasureSpeed measureSpeed))
        {
            return Mathf.Clamp01(measureSpeed.angularVelocity.magnitude / MaxStickAngularSpeed);
        }

        return FallbackForce;
    }

    private void ApplyForceToEffects(float force)
    {
        float feedbackLevel = Mathf.Max(force, MinFeedbackLevel);

        if (audioSource != null)
        {
            audioSource.volume = feedbackLevel;
        }

        if (hitParticles != null)
        {
            var emission = hitParticles.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(EmissionDefaultRate * feedbackLevel);
        }
    }

    public void SetInstrumentTriggers(GameObject leftStick, GameObject rightStick)
    {
        leftHandStick = leftStick;
        rightHandStick = rightStick;
    }

    public void SetParticleSystem(ParticleSystem ps)
    {
        hitParticles = ps;
        particlesAssignedExternally = true;
    }
}
