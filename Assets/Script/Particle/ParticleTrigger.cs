using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private float leftHandStickAppliedForce;  // Force for left hand stick
    [SerializeField] private float rightHandStickAppliedForce; // Force for right hand stick
    private const float EmissionDefaultRate = 1000f;

    public void SetInstrumentTrigger(GameObject leftStick, GameObject rightStick)
    {
        // Assign the left and right hand sticks
        leftHandStick = leftStick;
        rightHandStick = rightStick;
    }

    private void Start()
    {
        particleSystem = GetComponentInChildren<ParticleSystem>();
        if (particleSystem == null)
        {
            Debug.LogError("ParticleSystem not found in the children of this object.");
        }
    }

    private void Update()
    {
        // Apply force for left hand stick
        if (leftHandStick != null)
        {
            leftHandStickAppliedForce = Mathf.Abs(leftHandStick.GetComponent<MeasureSpeed>().angularVelocity.x);
            AppliedForce(leftHandStick, leftHandStickAppliedForce);
        }

        // Apply force for right hand stick
        if (rightHandStick != null)
        {
            rightHandStickAppliedForce = Mathf.Abs(rightHandStick.GetComponent<MeasureSpeed>().angularVelocity.x);
            AppliedForce(rightHandStick, rightHandStickAppliedForce);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((other.gameObject == leftHandStick || other.gameObject == rightHandStick) && other.CompareTag("Stick"))
        {
            Debug.Log($"Trigger Entered by: {other.gameObject.name}");
            particleSystem.Play();
            audioSource.Play();
        }
    }

    private void AppliedForce(GameObject drumStick, float drumStickAppliedForce)
    {
        var emission = particleSystem.emission;
        emission.enabled = true; // Ensure emission module is enabled

        // Clamp the applied force for this specific drumstick
        float clampDrumStickForce = Mathf.Clamp(drumStickAppliedForce / 10f, 0f, 1f);

        // Apply effect based on the force of the specific drumstick
        audioSource.volume = clampDrumStickForce;

        // Ensure the emission rate isn't zero if the force is too low
        float emissionRate = EmissionDefaultRate * clampDrumStickForce;
        emissionRate = Mathf.Max(emissionRate, 0.1f);  // Ensure emission rate doesn't become too small to see.

        emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate);

        if (!particleSystem.isPlaying)
        {
            particleSystem.Play();
        }
    }
}
