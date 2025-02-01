using UnityEngine;

public class FallResetPos : MonoBehaviour
{

    [SerializeField] private Vector3 startingPos;

    // Update is called once per frame
    void Update()
    {
        CheckFall();
    }

    private void CheckFall()
    {
        if (transform.position.y <= -5)
        {
            transform.position = startingPos;
        }
    }
}
