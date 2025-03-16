using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class NoteTrigger : MonoBehaviour
{
    [SerializeField] private List<GameObject> notes;
    [SerializeField] private GameObject leftHandStick;  // Left hand stick
    [SerializeField] private GameObject rightHandStick; // Right hand stick
    [SerializeField] private GameObject visualFeedback;

    private void Awake()
    {
        AssignMaterial();
        AssignTrigger();
    }

    private void AssignMaterial()
    {
        foreach (GameObject obj in notes)
        {
            ParticleTrigger childTrigger = obj.GetComponent<ParticleTrigger>();

            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (Material mat in renderer.materials)
                {
                    visualFeedback.GetComponent<Renderer>().material = mat;
                    GameObject particleInstance = Instantiate(visualFeedback, obj.transform.position, quaternion.identity, obj.transform);
                    ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
                    ps.Stop();
                    Debug.Log($"Particle material for {obj.name}: {visualFeedback.GetComponent<Renderer>().sharedMaterial}");
                }
            }
        }
    }

    private void AssignTrigger()
    {
        foreach (GameObject obj in notes)
        {
            ParticleTrigger childTrigger = obj.GetComponent<ParticleTrigger>();
            if (childTrigger != null)
            {
                // Pass the left and right hand sticks individually to each note's ParticleTrigger
                childTrigger.SetInstrumentTrigger(leftHandStick, rightHandStick); 
            }
            else
            {
                Debug.LogWarning($"No ParticleTrigger found on {obj.name}");
            }
        }
    }
}
