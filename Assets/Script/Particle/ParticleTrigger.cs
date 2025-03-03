using UnityEngine;

public class ParticleTrigger : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private GameObject drumStick;
    [SerializeField] private float drumStickAppliedForce = 0.5f;
    const float emmisionDefaultRate = 1000f;
     
    

    public void SetInstrumentTrigger(GameObject trigger) {
        drumStick = trigger;
    }

    private void Start() {
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>(); 
    }

    public void Update()
    {
        drumStickAppliedForce = drumStick.GetComponent<MeasureSpeed>().angularVelocity.x;
    }
    private void OnTriggerEnter (Collider other) {
        if (other.CompareTag("Stick")){
            AppliedForce();
            particleSystem.Play();
            //Instantiate(particleSystem, transform.position, transform.rotation);
            audioSource.Play();
        }
    }
    private void AppliedForce() {
        //change the volume based on applied force
        audioSource.volume = Mathf.Clamp((drumStickAppliedForce / 10f), 0f, 1f);
        //change the emmision rate based on applied force
        float emmisionRate = particleSystem.emission.rateOverTime.constant / drumStickAppliedForce;
        var emmision = particleSystem.emission;
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionRate);
        Debug.Log("Rate Over Time: " + emmisionRate);
        //return back to default emmision rate after applying force
        emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionDefaultRate);
    }
}


