using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Temporary diagnostic — logs hover/select on a grabbable so we can see, from the
// console, exactly what happens (or doesn't) when a second hand tries to grab a
// second drumstick while the first is already held.
public class GrabDebugLogger : MonoBehaviour
{
    private void Awake()
    {
        var interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null)
        {
            Debug.LogWarning($"[GrabDebugLogger] No XRBaseInteractable on {name}.", this);
            return;
        }

        interactable.hoverEntered.AddListener(args =>
            Debug.Log($"[GrabDebugLogger] {name} hover ENTER by {args.interactorObject}", this));
        interactable.hoverExited.AddListener(args =>
            Debug.Log($"[GrabDebugLogger] {name} hover EXIT by {args.interactorObject}", this));
        interactable.selectEntered.AddListener(args =>
            Debug.Log($"[GrabDebugLogger] {name} GRABBED by {args.interactorObject}", this));
        interactable.selectExited.AddListener(args =>
            Debug.Log($"[GrabDebugLogger] {name} RELEASED by {args.interactorObject}", this));
    }
}
