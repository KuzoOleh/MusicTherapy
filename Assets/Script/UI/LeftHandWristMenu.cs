using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;

// Summons a menu panel (e.g. an instance of "UI pause"/"UI pause 1") attached to the
// player's left hand, wrist-watch style — this is why it targets the LEFT hand
// specifically: on Quest/most VR controllers, the dedicated hardware "Menu" (≡)
// button only exists on the left controller, so "press the left controller's menu
// button" is already the standard, discoverable convention players expect for
// "bring up my menu," with no need to invent a new gesture or bind a new control.
//
// The menu is parented under leftHandAnchor once at Start and stays parented there
// for its whole life — it isn't spawned/destroyed or repositioned in world space —
// so it always reads as "a thing that lives on my wrist," not a panel that happens
// to float near the hand. It starts hidden (scaled to zero + inactive) regardless of
// how it was left in the Editor, and each press grows it in or shrinks it away with
// a short scale animation plus a controller haptic tick, so a press that has no
// physical click still feels confirmed.
//
// Uses the core UnityEngine.XR device API (InputDevices/CommonUsages) rather than
// an XR Interaction Toolkit interactor or a new Input Action binding, so it doesn't
// depend on editing the shared "XRI Default Input Actions" asset (which, as shipped
// in this project, has no dedicated Menu action) and keeps working regardless of
// which XRI package version/interactor classes are installed.
public class LeftHandWristMenu : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The left hand/controller transform to attach the menu to — in the default XRI Starter Assets rig this is 'XR Origin (XR Rig)/Camera Offset/Left Controller'.")]
    [SerializeField] private Transform leftHandAnchor;
    [Tooltip("The menu panel to show/hide, e.g. a scene instance of the 'UI pause' prefab. This script only re-parents and toggles it — it doesn't spawn or destroy it.")]
    [SerializeField] private GameObject menuRoot;

    [Header("Wrist Placement")]
    [Tooltip("Local offset from the hand anchor. Defaults put the menu just above/in front of the wrist, roughly where a watch face would sit.")]
    [SerializeField] private Vector3 wristLocalPosition = new Vector3(0f, 0.02f, 0.04f);
    [SerializeField] private Vector3 wristLocalRotationEuler = new Vector3(90f, 0f, 0f);

    [Header("Summon Button (Left Controller)")]
    [Tooltip("The dedicated hardware Menu button, where the controller has one (this is the left-only button on Quest-style controllers).")]
    [SerializeField] private bool useMenuButton = true;
    [Tooltip("Falls back to the primary face button (X/A) if the connected controller has no dedicated menu button.")]
    [SerializeField] private bool fallbackToPrimaryButton = true;
    [SerializeField] private float debounceSeconds = 0.3f;

    [Header("Feedback")]
    [SerializeField] private bool animateToggle = true;
    [SerializeField] private float animateDuration = 0.15f;
    [SerializeField] private bool hapticOnToggle = true;
    [SerializeField] [Range(0f, 1f)] private float hapticAmplitude = 0.4f;
    [SerializeField] private float hapticDuration = 0.05f;

    private InputDevice leftHandDevice;
    private bool wasButtonPressed;
    private float lastToggleTime = float.NegativeInfinity;
    private Coroutine animateRoutine;
    private Vector3 menuOpenScale = Vector3.one;

    private void Start()
    {
        if (menuRoot != null)
        {
            menuOpenScale = menuRoot.transform.localScale;
            if (menuOpenScale == Vector3.zero)
            {
                // Guards against inheriting an already-collapsed scale if someone last
                // left the panel hidden this way in the Editor.
                menuOpenScale = Vector3.one;
            }
        }

        AttachToWrist();
        RefreshDevice();

        if (menuRoot != null)
        {
            SetMenuActive(false, immediate: true);
        }
    }

    private void AttachToWrist()
    {
        if (leftHandAnchor == null || menuRoot == null)
        {
            Debug.LogWarning("[LeftHandWristMenu] leftHandAnchor or menuRoot not assigned — menu will not be attached or summonable.", this);
            return;
        }

        menuRoot.transform.SetParent(leftHandAnchor, false);
        menuRoot.transform.localPosition = wristLocalPosition;
        menuRoot.transform.localRotation = Quaternion.Euler(wristLocalRotationEuler);
    }

    private void RefreshDevice()
    {
        leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    private void Update()
    {
        if (menuRoot == null)
        {
            return;
        }

        if (!leftHandDevice.isValid)
        {
            // The controller can connect after this component starts (tracking loss,
            // powering on late, etc.) — keep retrying rather than only checking once.
            RefreshDevice();
            if (!leftHandDevice.isValid)
            {
                return;
            }
        }

        bool pressed = ReadSummonButtonPressed();
        bool justPressed = pressed && !wasButtonPressed;
        wasButtonPressed = pressed;

        if (justPressed && Time.unscaledTime - lastToggleTime >= debounceSeconds)
        {
            lastToggleTime = Time.unscaledTime;
            ToggleMenu();
        }
    }

    private bool ReadSummonButtonPressed()
    {
        bool pressed = false;

        if (useMenuButton && leftHandDevice.TryGetFeatureValue(CommonUsages.menuButton, out bool menuPressed))
        {
            pressed = menuPressed;
        }

        if (!pressed && fallbackToPrimaryButton && leftHandDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryPressed))
        {
            pressed = primaryPressed;
        }

        return pressed;
    }

    private void ToggleMenu()
    {
        bool willBeActive = !menuRoot.activeSelf;
        SetMenuActive(willBeActive, immediate: !animateToggle);

        if (hapticOnToggle && leftHandDevice.isValid)
        {
            leftHandDevice.SendHapticImpulse(0u, hapticAmplitude, hapticDuration);
        }
    }

    private void SetMenuActive(bool active, bool immediate)
    {
        if (animateRoutine != null)
        {
            StopCoroutine(animateRoutine);
            animateRoutine = null;
        }

        if (active)
        {
            menuRoot.SetActive(true);
            if (immediate)
            {
                menuRoot.transform.localScale = menuOpenScale;
            }
            else
            {
                menuRoot.transform.localScale = Vector3.zero;
                animateRoutine = StartCoroutine(ScaleRoutine(Vector3.zero, menuOpenScale, null));
            }
        }
        else if (immediate)
        {
            menuRoot.transform.localScale = menuOpenScale;
            menuRoot.SetActive(false);
        }
        else
        {
            animateRoutine = StartCoroutine(ScaleRoutine(menuRoot.transform.localScale, Vector3.zero, () => menuRoot.SetActive(false)));
        }
    }

    private IEnumerator ScaleRoutine(Vector3 from, Vector3 to, Action onComplete)
    {
        float duration = Mathf.Max(0.01f, animateDuration);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            menuRoot.transform.localScale = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }

        menuRoot.transform.localScale = to;
        animateRoutine = null;
        onComplete?.Invoke();
    }
}
