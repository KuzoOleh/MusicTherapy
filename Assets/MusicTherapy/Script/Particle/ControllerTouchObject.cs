using Unity.VisualScripting;
using UnityEngine;

public class ControllerTouchObject : MonoBehaviour
{
    private void OnTriggerEnter (Collider other){
        if(other.CompareTag("Note")){
            Debug.Log("$Touched object: {other.name}.");
        }
    }
}
