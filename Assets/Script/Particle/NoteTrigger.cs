using System.Collections.Generic;
using UnityEngine;

public class NoteTrigger : MonoBehaviour
{

    [SerializeField] private List<GameObject> notes;
    [SerializeField] private GameObject instrumentTrigger;
    [SerializeField] private GameObject visualFeedback;
    [SerializeField] private float particleDestroy = 2f;
    
    void Awake() {
        //Assign particle material for each note
        foreach (GameObject obj in notes){
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null) {
                foreach (Material mat in renderer.materials) {
                     visualFeedback.GetComponent<Renderer>().material = mat;
                     Debug.Log($"Particle material for {obj.name}: {visualFeedback.GetComponent<Renderer>().sharedMaterial}");}
            }
        }
        
    }

    private void OnTriggerEnter (Collider other) {
        if (other.CompareTag("Stick")){
            
            Instantiate(visualFeedback, transform.position, transform.rotation);
            //audioSource.Play();
            particleDestroy -= Time.deltaTime;
            if (particleDestroy == 0f){ Destroy(gameObject);}
        }
    }

}
