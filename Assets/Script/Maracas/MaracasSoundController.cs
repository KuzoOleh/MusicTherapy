using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Plays the maracas sound/particles on a shake and records every shake in GameManager,
// so the maracas show up in the session CSV like the drums do.
//
// A shake is detected from EITHER a fast rotation (wrist flick — the original behaviour)
// OR a strong linear acceleration (the back-and-forth motion most people actually use).
// Acceleration rather than raw speed is used for the linear part so that simply walking
// or teleporting while holding the maracas doesn't count as shaking.
[RequireComponent(typeof(AudioSource))]
public class MaracasSoundController : MonoBehaviour
{
    [Header("Rotation shake")]
    [SerializeField] private float speedThreshold = 1f; // rad/s — minimum angular speed to play sound
    [SerializeField] private float maxSpeed = 10f;      // rad/s — angular speed for full volume

    [Header("Linear shake")]
    [SerializeField] private bool detectLinearShake = true;
    [Tooltip("m/s² — minimum (smoothed) linear acceleration that counts as a shake. Tune in the headset if gentle shakes are missed or walking triggers sound.")]
    [SerializeField] private float accelerationThreshold = 15f;
    [Tooltip("m/s² — acceleration that maps to full volume / force 1.0.")]
    [SerializeField] private float maxAcceleration = 50f;
    [Tooltip("A per-frame jump larger than this (m) is treated as a teleport, not a shake.")]
    [SerializeField] private float teleportDistance = 0.5f;

    [Header("Feedback")]
    [SerializeField] private AudioClip maracasSound;
    [FormerlySerializedAs("particleSystem")]
    [SerializeField] private ParticleSystem shakeParticles;

    [Header("Session data")]
    [Tooltip("Only record shakes while the maracas are held, so a maraca rolling off the table isn't logged as play.")]
    [SerializeField] private bool recordOnlyWhileHeld = true;

    private Quaternion lastRotation;
    private Vector3 lastPosition;
    private Vector3 lastVelocity;
    private float smoothedAcceleration;
    private AudioSource audioSource;
    private GameManager gameManager;
    private XRBaseInteractable interactable;

    void Awake()
    {
        lastRotation = transform.rotation;
        lastPosition = transform.position;

        audioSource = GetComponent<AudioSource>();
        if (maracasSound != null)
        {
            audioSource.clip = maracasSound;
        }
        audioSource.loop = false; // one sound per shake

        if (shakeParticles == null)
        {
            shakeParticles = GetComponentInChildren<ParticleSystem>();
        }
        if (shakeParticles == null)
        {
            Debug.LogWarning("No ParticleSystem found in the children of MaracasSoundController.", this);
        }

        gameManager = FindFirstObjectByType<GameManager>();
        interactable = GetComponentInParent<XRBaseInteractable>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= Mathf.Epsilon)
        {
            return; // paused / first frame — avoid division by zero
        }

        float angularSpeed = MeasureAngularSpeed(dt);
        float acceleration = detectLinearShake ? MeasureLinearAcceleration(dt) : 0f;

        bool rotationShake = angularSpeed > speedThreshold;
        bool linearShake = detectLinearShake && acceleration > accelerationThreshold;
        if (!rotationShake && !linearShake)
        {
            return;
        }

        float intensity = Mathf.Max(angularSpeed / maxSpeed, detectLinearShake ? acceleration / maxAcceleration : 0f);
        intensity = Mathf.Clamp01(intensity);

        // One sound (and one recorded shake) at a time — same as before, a new shake
        // only starts once the previous sound has finished.
        if (!audioSource.isPlaying)
        {
            audioSource.volume = Mathf.Clamp(intensity, 0.1f, 1f);
            audioSource.Play();
            RecordShake(intensity);
        }

        if (shakeParticles != null && !shakeParticles.isPlaying)
        {
            shakeParticles.Play();
        }
    }

    private float MeasureAngularSpeed(float dt)
    {
        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
        lastRotation = transform.rotation;

        if (angle > 180f) angle -= 360f;
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x))
        {
            return 0f;
        }

        return Mathf.Abs(angle * Mathf.Deg2Rad) / dt;
    }

    private float MeasureLinearAcceleration(float dt)
    {
        Vector3 displacement = transform.position - lastPosition;
        lastPosition = transform.position;

        if (displacement.magnitude > teleportDistance)
        {
            // Teleport / tracking re-centre: restart measurement instead of reporting a huge spike.
            lastVelocity = Vector3.zero;
            smoothedAcceleration = 0f;
            return 0f;
        }

        Vector3 velocity = displacement / dt;
        float acceleration = (velocity - lastVelocity).magnitude / dt;
        lastVelocity = velocity;

        // Light smoothing against per-frame tracking jitter.
        smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, acceleration, 0.5f);
        return smoothedAcceleration;
    }

    private void RecordShake(float intensity)
    {
        if (gameManager == null)
        {
            return;
        }

        if (recordOnlyWhileHeld && interactable != null && !interactable.isSelected)
        {
            return;
        }

        gameManager.HitInstrument(gameObject.name, intensity);
    }
}
