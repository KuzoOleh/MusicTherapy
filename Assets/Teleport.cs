using UnityEngine;

public class Teleport : MonoBehaviour
{
    public Transform targetTeleport; // The other teleport location
    public float cooldown = 1f;      // Prevent immediate re-teleport

    private bool isCoolingDown = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isCoolingDown) return;

        if (other.CompareTag("Player"))
        {
            // Move the player to the target position
            other.transform.position = targetTeleport.position;

            // Start cooldown on BOTH portals to avoid looping
            StartCoroutine(Cooldown());
            Teleport otherTeleport = targetTeleport.GetComponent<Teleport>();
            if (otherTeleport != null)
                otherTeleport.StartCoroutine(otherTeleport.Cooldown());
        }
    }

    private System.Collections.IEnumerator Cooldown()
    {
        isCoolingDown = true;
        yield return new WaitForSeconds(cooldown);
        isCoolingDown = false;
    }
}
