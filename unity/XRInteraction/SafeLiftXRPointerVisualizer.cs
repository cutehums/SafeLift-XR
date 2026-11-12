using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.EventSystems;

public sealed class SafeLiftXRPointerVisualizer : MonoBehaviour
{
    [SerializeField] private float rayLength = 2.8f;

    private InputDevice left;
    private InputDevice right;
    private LineRenderer leftRay;
    private LineRenderer rightRay;
    private Transform leftDot;
    private Transform rightDot;
    private Transform reticle;
    private Image hoveredImage;
    private Color hoveredOriginalColor;
    private bool leftHandPointer;
    private bool rightHandPointer;
    private bool lastLeftTrigger;
    private bool lastRightTrigger;
    private bool lastLeftPinch;
    private bool lastRightPinch;

    private void Awake()
    {
        CreateVisuals();
        FindDevices();
    }

    private void OnDisable()
    {
        // The project now uses joystick/A-button navigation. Make sure legacy ray
        // visuals disappear completely when this compatibility component is off.
        if (leftRay != null) leftRay.gameObject.SetActive(false);
        if (rightRay != null) rightRay.gameObject.SetActive(false);
        if (leftDot != null) leftDot.gameObject.SetActive(false);
        if (rightDot != null) rightDot.gameObject.SetActive(false);
        if (reticle != null) reticle.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!left.isValid || !right.isValid) FindDevices();

        UpdateHand(left, leftRay, leftDot, false);
        UpdateHand(right, rightRay, rightDot, true);

        var leftPinch = OvrIndexPinch(false);
        var rightPinch = OvrIndexPinch(true);
        UpdateHover(leftPinch || rightPinch);

        if (rightPinch && !lastRightPinch)
            TryClickFromRay(rightRay);
        if (leftPinch && !lastLeftPinch)
            TryClickFromRay(leftRay);
        lastLeftPinch = leftPinch;
        lastRightPinch = rightPinch;
    }

    private void CreateVisuals()
    {
        leftRay = CreateRay("SafeLiftLeftControllerRay", new Color(.1f, .65f, 1f, .85f));
        rightRay = CreateRay("SafeLiftRightControllerRay", new Color(1f, .82f, .15f, .95f));
        leftDot = CreateDot("SafeLiftLeftControllerDot", new Color(.1f, .65f, 1f, 1f));
        rightDot = CreateDot("SafeLiftRightControllerDot", new Color(1f, .82f, .15f, 1f));
        reticle = CreateDot("SafeLiftRayCanvasReticle", new Color(1f, 1f, 1f, 1f));
        reticle.gameObject.SetActive(false);
    }

    private static LineRenderer CreateRay(string name, Color color)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = 0.008f;
        line.endWidth = 0.002f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, .15f);
        return line;
    }

    private static Transform CreateDot(string name, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.035f;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = color;
        return go.transform;
    }

    private void UpdateHand(InputDevice device, LineRenderer line, Transform dot, bool rightHand)
    {
        var viewCamera = FindSafeLiftViewCamera();
        var viewer = viewCamera != null ? viewCamera.transform : null;
        if (viewer == null || line == null || dot == null) return;

        Vector3 position;
        Quaternion rotation;
        bool isHandPointer;
        if (!TryGetBestPointerPose(device, rightHand, out position, out rotation, out isHandPointer))
        {
            line.gameObject.SetActive(false);
            dot.gameObject.SetActive(false);
            return;
        }

        if (rightHand) rightHandPointer = isHandPointer;
        else leftHandPointer = isHandPointer;

        line.gameObject.SetActive(true);
        dot.gameObject.SetActive(true);
        var direction = isHandPointer ? (rotation * Vector3.forward) : BestRayDirection(position, rotation, viewer.forward);
        dot.position = position;
        line.SetPosition(0, position);
        line.SetPosition(1, position + direction.normalized * rayLength);
    }

    private void UpdateHover(bool pinching)
    {
        ResetHover();
        var button = FindButtonFromAnyRay(out var hit);
        var hasCanvasHit = button != null || FindCanvasHitFromAnyRay(out hit);
        if (reticle != null)
        {
            reticle.gameObject.SetActive(hasCanvasHit);
            if (hasCanvasHit) reticle.position = hit;
            var renderer = reticle.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = pinching
                ? new Color(.25f, 1f, .35f, 1f)
                : button != null ? new Color(1f, .78f, .08f, 1f) : new Color(.15f, .85f, 1f, 1f);
            reticle.localScale = Vector3.one * (pinching ? .075f : .05f);
        }

        if (button == null) return;
        hoveredImage = button.GetComponent<Image>();
        if (hoveredImage == null) return;
        hoveredOriginalColor = hoveredImage.color;
        hoveredImage.color = pinching ? new Color(.25f, 1f, .35f, 1f) : new Color(1f, .7f, .18f, 1f);
    }

    private void TryClickFromRay(LineRenderer rayLine)
    {
        var button = FindButtonFromRay(rayLine, out _);
        if (button == null) return;

        button.Select();
        button.onClick.Invoke();
        Debug.Log("SafeLift XR pointer clicked button: " + button.name);
    }

    public bool TryClickPointedButton()
    {
        var button = FindButtonFromAnyRay(out _);
        if (button == null) return false;
        button.Select();
        button.onClick.Invoke();
        Debug.Log("SafeLift XR trigger clicked pointed button: " + button.name);
        return true;
    }

    private Button FindButtonFromAnyRay(out Vector3 hit)
    {
        var button = FindButtonFromRay(rightRay, out hit);
        if (button != null) return button;
        return FindButtonFromRay(leftRay, out hit);
    }

    private bool FindCanvasHitFromAnyRay(out Vector3 hit)
    {
        if (FindCanvasHitFromRay(rightRay, out hit)) return true;
        return FindCanvasHitFromRay(leftRay, out hit);
    }

    private bool FindCanvasHitFromRay(LineRenderer rayLine, out Vector3 hit)
    {
        hit = Vector3.zero;
        var canvas = FindSafeLiftCanvas();
        if (canvas == null || rayLine == null || !rayLine.gameObject.activeInHierarchy) return false;
        var ray = new Ray(rayLine.GetPosition(0), (rayLine.GetPosition(1) - rayLine.GetPosition(0)).normalized);
        return TryHitCanvasRect(canvas, ray, out hit, out _);
    }

    private Button FindButtonFromRay(LineRenderer rayLine, out Vector3 hit)
    {
        hit = Vector3.zero;
        var camera = FindSafeLiftViewCamera();
        var canvas = FindSafeLiftCanvas();
        if (camera == null || canvas == null || rayLine == null || !rayLine.gameObject.activeInHierarchy) return null;

        var ray = new Ray(rayLine.GetPosition(0), (rayLine.GetPosition(1) - rayLine.GetPosition(0)).normalized);
        if (!TryHitCanvasRect(canvas, ray, out hit, out var screenPoint)) return null;

        // World-space local-coordinate test is independent of the Editor eye
        // camera and remains correct for CenterEyeAnchor-parented canvases.
        foreach (var button in canvas.GetComponentsInChildren<Button>(false))
        {
            if (button == null || !button.IsActive() || !button.interactable) continue;
            var rect = button.GetComponent<RectTransform>();
            if (rect == null) continue;
            var local = rect.InverseTransformPoint(hit);
            if (rect.rect.Contains(new Vector2(local.x, local.y)))
                return button;
        }

        // Use Unity's real UGUI hit test first. The previous screen-rectangle
        // approximation can report no button for a world-space canvas even while
        // the visible ray clearly crosses it.
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var eventRoot = new GameObject("SafeLiftRuntimeEventSystem");
                eventSystem = eventRoot.AddComponent<EventSystem>();
                eventRoot.AddComponent<StandaloneInputModule>();
            }

            var pointer = new PointerEventData(eventSystem) { position = screenPoint };
            var results = new List<RaycastResult>();
            raycaster.Raycast(pointer, results);
            foreach (var result in results)
            {
                var button = result.gameObject != null ? result.gameObject.GetComponentInParent<Button>() : null;
                if (button != null && button.IsActive() && button.interactable)
                    return button;
            }
        }

        foreach (var button in canvas.GetComponentsInChildren<Button>(false))
        {
            if (button == null || !button.IsActive() || !button.interactable) continue;
            var rect = button.GetComponent<RectTransform>();
            if (rect == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera))
                return button;
        }

        return null;
    }

    private bool TryHitCanvasRect(Canvas canvas, Ray ray, out Vector3 hit, out Vector2 screenPoint)
    {
        hit = Vector3.zero;
        screenPoint = Vector2.zero;
        var camera = FindSafeLiftViewCamera();
        var rect = canvas.GetComponent<RectTransform>();
        if (camera == null || rect == null) return false;

        var plane = new Plane(canvas.transform.forward, canvas.transform.position);
        if (!plane.Raycast(ray, out var enter) || enter < 0f || enter > rayLength + .35f) return false;

        hit = ray.GetPoint(enter);
        var canvasLocal = rect.InverseTransformPoint(hit);
        if (!rect.rect.Contains(new Vector2(canvasLocal.x, canvasLocal.y))) return false;
        var screen = camera.WorldToScreenPoint(hit);
        if (screen.z < 0f) return false;
        screenPoint = new Vector2(screen.x, screen.y);
        return true;
    }

    private Vector3 BestRayDirection(Vector3 position, Quaternion rotation, Vector3 fallbackForward)
    {
        var candidates = new[]
        {
            rotation * Vector3.forward,
            rotation * -Vector3.forward,
            rotation * Vector3.up,
            fallbackForward
        };

        var canvas = FindSafeLiftCanvas();
        if (canvas != null)
        {
            foreach (var candidate in candidates)
            {
                var ray = new Ray(position, candidate.normalized);
                if (TryHitCanvasRect(canvas, ray, out _, out _))
                    return candidate.normalized;
            }
        }

        return fallbackForward.sqrMagnitude > .001f ? fallbackForward.normalized : Vector3.forward;
    }

    private void ResetHover()
    {
        if (hoveredImage != null)
            hoveredImage.color = hoveredOriginalColor;
        hoveredImage = null;
        if (reticle != null) reticle.localScale = Vector3.one * .035f;
    }


    private static Camera FindSafeLiftViewCamera()
    {
        var xrActive = XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName);
        try { xrActive = xrActive || OVRManager.isHmdPresent; } catch { }
        if (Application.isEditor && !xrActive)
        {
            foreach (var preview in UnityEngine.Object.FindObjectsOfType<Camera>(true))
            {
                if (preview != null && preview.isActiveAndEnabled && preview.name == "SafeLiftEditorPreviewCamera")
                    return preview;
            }
            if (Camera.main != null) return Camera.main;
        }

        var rig = UnityEngine.Object.FindObjectOfType<OVRCameraRig>();
        if (xrActive && rig != null && rig.centerEyeAnchor != null)
        {
            var centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (centerEyeCamera != null && centerEyeCamera.isActiveAndEnabled) return centerEyeCamera;
        }

        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null || !camera.isActiveAndEnabled) continue;
            if (camera.GetComponentInParent<OVRCameraRig>() != null) return camera;
        }

        return Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
    }

    private static bool OvrTrigger(bool rightHand)
    {
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            var controller = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller)
                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) >= .55f;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static bool Trigger(InputDevice device)
    {
        if (Button(device, CommonUsages.triggerButton)) return true;
        float value;
        return device.isValid && device.TryGetFeatureValue(CommonUsages.trigger, out value) && value >= .55f;
    }

    private static bool OvrIndexPinch(bool rightHand)
    {
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            var hand = FindTrackedOvrHand(rightHand);
            if (hand == null || !hand.IsTracked || !hand.IsPointerPoseValid) return false;
            return hand.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
                   hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= 0.72f;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static bool TryGetBestPointerPose(InputDevice device, bool rightHand, out Vector3 position, out Quaternion rotation, out bool isHandPointer)
    {
        isHandPointer = false;
        if (TryGetOvrControllerPose(rightHand, out position, out rotation))
            return true;

        if (TryGetOvrHandPointerPose(rightHand, out position, out rotation))
        {
            isHandPointer = true;
            return true;
        }

        // Generic device poses are a fallback only. On Quest Link they can be reported in
        // tracking-local space, which makes a world-space ray appear detached from the hand.
        if (TryGetTrackedPose(device, out position, out rotation))
            return true;

        return false;
    }

    private static bool TryGetOvrHandPointerPose(bool rightHand, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            var hand = FindTrackedOvrHand(rightHand);
            if (hand == null || !hand.IsTracked || !hand.IsPointerPoseValid) return false;
            var pointer = hand.GetPointerRayTransform();
            if (pointer == null) return false;
            position = pointer.position;
            rotation = pointer.rotation;
            return true;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static OVRHand FindTrackedOvrHand(bool rightHand)
    {
#if UNITY_ANDROID || UNITY_EDITOR
        foreach (var hand in UnityEngine.Object.FindObjectsOfType<OVRHand>(true))
        {
            if (hand == null) continue;
            var handName = hand.name ?? string.Empty;
            var parentName = hand.transform.parent != null ? hand.transform.parent.name ?? string.Empty : string.Empty;
            var pathName = handName + " " + parentName;
            var looksRight = pathName.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             pathName.IndexOf("RHand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             pathName.IndexOf("Hand_R", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var looksLeft = pathName.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            pathName.IndexOf("LHand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            pathName.IndexOf("Hand_L", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (rightHand && looksRight) return hand;
            if (!rightHand && looksLeft) return hand;
        }
#endif
        return null;
    }

    private static Canvas FindSafeLiftCanvas()
    {
        foreach (var canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas != null && canvas.name == "SafeLiftDemoWorldCanvas")
                return canvas;
        }

        return null;
    }

    private void FindDevices()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, devices);
        if (devices.Count > 0) left = devices[0];
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);
        if (devices.Count > 0) right = devices[0];
    }

    private static bool Button(InputDevice device, InputFeatureUsage<bool> usage)
    {
        bool value;
        return device.isValid && device.TryGetFeatureValue(usage, out value) && value;
    }

    private static bool TryGetOvrControllerPose(bool rightHand, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
#if UNITY_ANDROID || UNITY_EDITOR
        try
        {
            var controller = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            if (!OVRInput.GetControllerPositionTracked(controller) && !OVRInput.GetControllerOrientationTracked(controller))
                return false;

            var localPosition = OVRInput.GetLocalControllerPosition(controller);
            var localRotation = OVRInput.GetLocalControllerRotation(controller);
            var trackingSpace = FindTrackingSpace();
            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(localPosition);
                rotation = trackingSpace.rotation * localRotation;
            }
            else
            {
                position = localPosition;
                rotation = localRotation;
            }
            return true;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static Transform FindTrackingSpace()
    {
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.trackingSpace != null)
            return rig.trackingSpace;
        return null;
    }
    private static bool TryGetTrackedPose(InputDevice device, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!device.isValid) return false;
        if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out position)) return false;
        if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation)) return false;
        return true;
    }
}
