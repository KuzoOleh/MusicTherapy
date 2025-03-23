using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private float leftHandAppliedForce = 0.5f;  // Left hand force
    [SerializeField] private float rightHandAppliedForce = 0.5f; // Right hand force
    const float emmisionDefaultRate = 1000f;

    public void SetInstrumentTriggers(GameObject leftStick, GameObject rightStick)
    {
        leftHandStick = leftStick;
        rightHandStick = rightStick;
    }

    private void Start()
    {
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
    }

    public void Update()
    {
        // Update applied force for each stick independently
        if (leftHandStick != null)
            leftHandAppliedForce = leftHandStick.GetComponent<MeasureSpeed>().angularVelocity.x;
        if (rightHandStick != null)
            rightHandAppliedForce = rightHandStick.GetComponent<MeasureSpeed>().angularVelocity.x;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Stick"))
        {
            Transform parentStick = other.transform.parent;
            // Check which stick is involved (left or right) and apply the force accordingly
             if (parentStick == leftHandStick.transform)
            {
                Debug.Log("Calling applied force for left hand");
                AppliedForce(leftHandAppliedForce);
            }
            else if (parentStick == rightHandStick.transform)
            {
                Debug.Log("Calling applied force for right hand");
                AppliedForce(rightHandAppliedForce);
            } 

            particleSystem.Play();
            audioSource.Play();
        }
    }

    private void AppliedForce(float appliedForce)
    {
        Debug.Log("Applied force");
        var emmision = particleSystem.emission;
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionDefaultRate);

        // Normalize applied force for the stick and clamp between 0 and 1
        float clampDrumStickForce = Mathf.Clamp(appliedForce / 10f, 0f, 1f);
        // Change the volume based on applied force
        audioSource.volume = clampDrumStickForce;

        // Change the emission rate based on applied force
        float emmisionRate = particleSystem.emission.rateOverTime.constant * clampDrumStickForce;
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionRate);

        Debug.Log("Rate Over Time: " + emmision.rateOverTime.constant);
    }
}
