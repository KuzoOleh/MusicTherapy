using UnityEngine;

// Tracks the stick's angular velocity (rad/s) from frame-to-frame rotation changes.
// ParticleTrigger reads angularVelocity.magnitude at the moment of a hit.
public class MeasureSpeed : MonoBehaviour
{
    private Quaternion lastRotation;
    public Vector3 angularVelocity { get; private set; }

    [Tooltip("Read-only: current angular speed in rad/s, shown for debugging.")]
    [SerializeField] private float speed = 0f;

    void Awake()
    {
        lastRotation = transform.rotation;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= Mathf.Epsilon)
        {
            return; // paused / first frame — avoid division by zero
        }

        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;

        // ToAngleAxis can return a NaN/infinite axis for a zero rotation.
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x))
        {
            axis = Vector3.zero;
        }

        angularVelocity = (axis * angle * Mathf.Deg2Rad) / dt;
        lastRotation = transform.rotation;
        speed = angularVelocity.magnitude;
    }
}
