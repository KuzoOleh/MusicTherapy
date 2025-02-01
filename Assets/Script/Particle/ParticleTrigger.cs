using Unity.VisualScripting;
using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private GameObject particleSystem;
    [SerializeField] private AudioSource audioSource;

    //Particle destroy means deleting particles from hierarchy after some time for optimization purposes
    [SerializeField] private float particleDestroy = 2f;

    private void OnTriggerEnter (Collider other) {
        if (other.CompareTag("Stick")){
            
            Instantiate(particleSystem, transform.position, transform.rotation);
            audioSource.Play();
            particleDestroy -= Time.deltaTime;
            if (particleDestroy == 0f){ Destroy(gameObject);}
        }
    }
}

