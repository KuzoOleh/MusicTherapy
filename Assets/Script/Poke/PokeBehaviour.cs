using UnityEngine;

public class PokeBehaviour : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OggerEnter(Collider other)
    {
        if (other.CompareTag("Controller")) {
            Debug.Log("You poked me");
        }      
    }
}
