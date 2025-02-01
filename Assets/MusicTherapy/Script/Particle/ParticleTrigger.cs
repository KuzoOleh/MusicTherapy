using Unity.VisualScripting;
using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private GameObject particleSystem;

    void Start() {
        //Instantiate(particleSystem, transform.position, transform.rotation);
        Debug.Log("It's on");
    }

    private void OnTriggerEnter (Collider other) {
        if (other.CompareTag("Stick")){
            Debug.Log("Drum Sound");
            Instantiate(particleSystem, transform.position, transform.rotation);
        }
    }
}

