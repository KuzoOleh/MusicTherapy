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
        foreach (GameObject obj in notes)
        {
            ParticleTrigger childTrigger = obj.GetComponent<ParticleTrigger>();

            // Check dynamically if the instrument needs sticks
            if (childTrigger.DoesStickNeeded())
            {
                childTrigger.SetInstrumentTriggers(leftHandStick, rightHandStick);
            }

            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (Material mat in renderer.materials)
                {
                    visualFeedback.GetComponent<Renderer>().material = mat;
                    GameObject particleInstance = Instantiate(visualFeedback, obj.transform.position, quaternion.identity, obj.transform);

                    // Assign the instantiated ParticleSystem directly to the ParticleTrigger
                    ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
                    ps.Stop(); // Make sure it doesn't play on awake
                    childTrigger.SetParticleSystem(ps);

                    Debug.Log($"Particle material for {obj.name}: {visualFeedback.GetComponent<Renderer>().sharedMaterial}");
                }
            }
        }
    }
}
