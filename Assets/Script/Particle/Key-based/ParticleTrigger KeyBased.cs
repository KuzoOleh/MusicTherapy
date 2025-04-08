using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ParticleTriggerKeyBased : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    
    [SerializeField] private float pressedSpeed = 0f;
    [SerializeField] private bool isPressed = false;

    public XRBaseInteractable interactable;
    const float emmisionDefaultRate = 1000f;

    Vector3 oldPosition;
    float speed;

    private void Start()
    {
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
        oldPosition = transform.position;
        audioSource.Stop();
    }

    public void Update()
    {
        Vector3 displacement = transform.position - oldPosition;
        speed = displacement.magnitude/ Time.deltaTime;
        pressedSpeed = speed;
        oldPosition = transform.position;
        //Debug.Log("Key pressed speed: " + speed);
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Hand")){
            if(!isPressed){
                isPressed = true;
                pressedNote();
                Debug.Log("Hands entered");
            }

        }      
    }
    private void OnTriggerExit(Collider other)
    {
     if(other.CompareTag("Hand")){
        isPressed = false;
        Debug.Log("Hands exited");
     }   
    }

    private void pressedNote(){
            audioSource.volume = pressedSpeed * 10f;
            audioSource.Play();
            var emmision = particleSystem.emission;
            emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionDefaultRate);

            float emmisionRate = particleSystem.emission.rateOverTime.constant * pressedSpeed;
            emmision.rateOverTime = new ParticleSystem.MinMaxCurve(emmisionRate);

            particleSystem.Play();

            Debug.Log("particle system is up");
    }
}
