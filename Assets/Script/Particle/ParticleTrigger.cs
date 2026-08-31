using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    GameManager gameManager;
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private float leftHandAppliedForce = 0.5f;  // Left hand force
    [SerializeField] private float rightHandAppliedForce = 0.5f; // Right hand force
    const float emmisionDefaultRate = 1000f;
    private float force = 0f;
    [SerializeField] private bool doesStickNeeded = false; // Set this per instrument in the Inspector


    private void Awake()
    {
        gameManager = FindFirstObjectByType<GameManager>();
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
    }

    public void Update()
    {
        // Update applied force for each stick independently
        if (leftHandStick != null && leftHandStick.TryGetComponent(out MeasureSpeed leftSpeed))
            leftHandAppliedForce = leftSpeed.angularVelocity.x;
        if (rightHandStick != null && rightHandStick.TryGetComponent(out MeasureSpeed rightSpeed))
            rightHandAppliedForce = rightSpeed.angularVelocity.x;
    }

    public bool DoesStickNeeded()
    {
        return doesStickNeeded;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Stick"))
        {
            Transform parentStick = other.transform.parent;
            // Check which stick is involved (left or right) and apply the force accordingly.
            // leftHandStick/rightHandStick are only assigned when doesStickNeeded is true, so
            // an instrument that doesn't need sticks (and never got them wired up) can still
            // be hit by a "Stick"-tagged collider without either comparison touching a null transform.
            if (leftHandStick != null && parentStick == leftHandStick.transform)
            {
                gameManager.HitInstrument(gameObject.name, force);
                AppliedForce(leftHandAppliedForce);
            }
            else if (rightHandStick != null && parentStick == rightHandStick.transform)
            {
                gameManager.HitInstrument(gameObject.name, force);
                AppliedForce(rightHandAppliedForce);
            }

            particleSystem.Play();
            audioSource.Play();
        }
    }

    private void AppliedForce(float appliedForce)
    {
        var emmision = particleSystem.emission;
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionDefaultRate);

        // Normalize applied force for the stick and clamp between 0 and 1
        float clampDrumStickForce = Mathf.Clamp(appliedForce / 10f, 0f, 1f);
        // Change the volume based on applied force
        audioSource.volume = clampDrumStickForce;

        // Change the emission rate based on applied force
        float emmisionRate = particleSystem.emission.rateOverTime.constant * clampDrumStickForce;
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionRate);
        force = clampDrumStickForce;


        //Debug.Log("Rate Over Time: " + emmision.rateOverTime.constant);
    }

    public void SetInstrumentTriggers(GameObject leftStick, GameObject rightStick)
    {
        leftHandStick = leftStick;
        rightHandStick = rightStick;
    }


    public void SetParticleSystem(ParticleSystem ps)
    {
        this.particleSystem = ps;
    }

}
