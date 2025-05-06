using UnityEngine;

public class MeasureSpeed : MonoBehaviour
{

    private Quaternion lastRotation;
    public Vector3 angularVelocity {get; set; }

    [SerializeField] private float speed = 0f;
    void Awake()
    {
        lastRotation = transform.rotation;
    }

    void Update()
    {
        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360;

        angularVelocity = (axis * angle * Mathf.Deg2Rad) / Time.deltaTime;
        lastRotation = transform.rotation;
        speed = Mathf.Clamp(angularVelocity.x, 0F, 10F); // Clamp the speed to a range of 0 to 10
        //Debug.Log("stick speed: " + Mathf.Clamp(angularVelocity.x, 0F, 10F));
    } 

}
