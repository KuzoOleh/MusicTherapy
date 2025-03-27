using UnityEngine;

public class ParticleTriggerKeyBased : MonoBehaviour
{
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private AudioSource audioSource;
    
    [SerializeField] private float pressedSpeed = 0f;
    const float emmisionDefaultRate = 1000f;

    Vector3 oldPosition;
    float speed;

    private void Start()
    {
        particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
        oldPosition = transform.position;
        
    }

    public void Update()
    {
        Vector3 displacement = transform.position - oldPosition;
        speed = displacement.magnitude/ Time.deltaTime;
        if (speed != 0f){
            audioSource.volume = speed;
            audioSource.Play();
        }
        oldPosition = transform.position;

        
        Debug.Log("Key pressed speed: " + speed);
    }
}
