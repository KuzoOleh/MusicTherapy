using UnityEngine;

public class AreaAudioTrigger : MonoBehaviour
{
    public AudioSource audioSource;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) // Or whatever your player tag is
        {
            audioSource.Play();
            Debug.Log("Audio started");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            audioSource.Stop();
            Debug.Log("Audio stopped");
        }
    }
}

