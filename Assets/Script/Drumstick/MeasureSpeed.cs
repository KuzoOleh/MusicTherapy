using UnityEngine;

public class MeasureSpeed : MonoBehaviour
{

    private Quaternion lastRotation;
    public Vector3 angularVelocity {get; set; }
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
        //Debug.Log("stick speed: " + Mathf.Clamp(angularVelocity.x, 0F, 10F));
    } 

}
