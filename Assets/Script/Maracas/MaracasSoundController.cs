using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MaracasSoundController : MonoBehaviour
{
    private Quaternion lastRotation;
    private Vector3 angularVelocity;
    private AudioSource audioSource;

    [SerializeField] private float speedThreshold = 1f; // Minimum speed to play sound
    [SerializeField] private float maxSpeed = 10f; // Maximum speed for scaling volume
    [SerializeField] private AudioClip maracasSound;
    [SerializeField] private ParticleSystem particleSystem; // Reference to the particle system

    void Awake()
    {
        lastRotation = transform.rotation;
        audioSource = GetComponent<AudioSource>();
        if (maracasSound != null)
        {
            audioSource.clip = maracasSound;
        }
        audioSource.loop = false; // Ensure the sound plays only once per shake

        // Find the particle system in the object's children if not assigned
        particleSystem = GetComponentInChildren<ParticleSystem>();
        if (particleSystem == null)
        {
            Debug.LogWarning("No ParticleSystem found in the children of MaracasSoundController.");
        }
    }

    void Update()
    {
        // Calculate angular velocity
        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360;

        angularVelocity = (axis * angle * Mathf.Deg2Rad) / Time.deltaTime;
        lastRotation = transform.rotation;

        // Calculate speed based on angular velocity magnitude
        float speed = angularVelocity.magnitude;

        // Play sound and particle system if speed exceeds the threshold
        if (speed > speedThreshold)
        {
            if (!audioSource.isPlaying)
            {
                audioSource.volume = Mathf.Clamp(speed / maxSpeed, 0.1f, 1f); // Scale volume based on speed
                audioSource.Play();
            }

            if (particleSystem != null && !particleSystem.isPlaying)
            {
                particleSystem.Play();
            }
        }
    }
}
