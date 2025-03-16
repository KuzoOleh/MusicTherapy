using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class NoteTrigger : MonoBehaviour
{

    [SerializeField] private List<GameObject> notes;
    [SerializeField] private GameObject instrumentTrigger;
    [SerializeField] private GameObject visualFeedback;

    
    void Awake() {
        //Assign particle material and instrument trigger for each note
        foreach (GameObject obj in notes){
            //Assign instrument trigger for each note
            ParticleTrigger childTrigger = obj.GetComponent<ParticleTrigger>();
            childTrigger.SetInstrumentTrigger(instrumentTrigger);

            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null) {
                foreach (Material mat in renderer.materials) {
                     visualFeedback.GetComponent<Renderer>().material = mat;
                     GameObject particleInstance = Instantiate(visualFeedback, obj.transform.position, quaternion.identity, obj.transform);
                     ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
                     //make sure that PS won't play on Awake
                     ps.Stop();
                     Debug.Log($"Particle material for {obj.name}: {visualFeedback.GetComponent<Renderer>().sharedMaterial}");}
            }
        }
    }
}
