using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class NoteTrigger : MonoBehaviour
{
    [SerializeField] private List<GameObject> notes;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private GameObject visualFeedback;

    void Awake()
    {
        // Assign particle material and instrument triggers for each note
        foreach (GameObject obj in notes)
        {
            // Assign instrument triggers for each note
            ParticleTrigger childTrigger = obj.GetComponent<ParticleTrigger>();

            // Set the left and right hand sticks for each note
            childTrigger.SetInstrumentTriggers(leftHandStick, rightHandStick);

            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (Material mat in renderer.materials)
                {
                    visualFeedback.GetComponent<Renderer>().material = mat;
                    GameObject particleInstance = Instantiate(visualFeedback, obj.transform.position, quaternion.identity, obj.transform);
                    ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
                    // Make sure that PS won't play on Awake
                    ps.Stop();
                    Debug.Log($"Particle material for {obj.name}: {visualFeedback.GetComponent<Renderer>().sharedMaterial}");
                }
            }
        }
    }
}
