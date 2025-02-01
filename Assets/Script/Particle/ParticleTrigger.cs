using Unity.VisualScripting;
using Unity.XR.CoreUtils;
using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;

    private void Start() {
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
    }
    private void OnTriggerEnter (Collider other) {
        if (other.CompareTag("Stick")){
            
            Instantiate(particleSystem, transform.position, transform.rotation);
            audioSource.Play();
        }
    }
}

