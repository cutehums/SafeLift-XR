using UnityEngine;

/// <summary>
/// Keeps the SafeLift dashboard usable when the Quest/Editor camera starts at
/// floor height or when leftover scene helpers push the user's view under the
/// world-space UI. It only corrects obviously invalid startup heights so it
/// does not fight real squat motion during the workout.
/// </summary>
public sealed class SafeLiftXRViewComfortGuard : MonoBehaviour
{
    private const float DesiredStartupEyeHeight = 1.50f;
    private const float MinimumUsableEyeHeight = 1.05f;
    private const float MaximumStartupEyeHeight = 2.2f;
    private const float StartupBackwardOffset = 0.75f;
    private bool startupCorrectionApplied;

    private void Awake()
    {
        startupCorrectionApplied = false;
    }

    private System.Collections.IEnumerator Start()
    {
        // Let OVRCameraRig/CenterEyeAnchor receive the first tracking pose before
        // deciding whether the rig was spawned at floor height.
        yield return new WaitForSecondsRealtime(.75f);
        ApplyStartupCorrection();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) || Input.GetKeyDown(KeyCode.R))
            ApplyStartupCorrection();
    }

    private void ApplyStartupCorrection()
    {
        if (startupCorrectionApplied || !IsXrRuntimeActive()) return;

        var camera = FindCenterEyeCamera();
        if (camera == null) return;

        var rig = FindMovableRigRoot(camera.transform);
        if (rig == null) return;

        var y = camera.transform.position.y;
        var correction = y >= MinimumUsableEyeHeight && y <= MaximumStartupEyeHeight
            ? 0f
            : DesiredStartupEyeHeight - y;
        var flatForward = Vector3.ProjectOnPlane(rig.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < .001f)
            flatForward = Vector3.forward;

        startupCorrectionApplied = true;
        rig.position += Vector3.up * correction - flatForward * StartupBackwardOffset;
        Debug.Log("SafeLift XR startup view adjusted: height=" + correction.ToString("0.00")
            + "m, backward=" + StartupBackwardOffset.ToString("0.00") + "m for readable dashboard view.");
    }

    private static Camera FindCenterEyeCamera()
    {
        // In Quest Link the editor preview camera may still be enabled for a
        // frame. Always prefer the actual Center Eye camera so the height
        // correction is applied to the tracked rig, not to the fixed preview.
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            var centerEye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (centerEye != null && centerEye.isActiveAndEnabled) return centerEye;
        }

#if UNITY_EDITOR
        var previewObject = GameObject.Find("SafeLiftEditorPreviewCamera");
        var preview = previewObject != null ? previewObject.GetComponent<Camera>() : null;
        if (preview != null && preview.isActiveAndEnabled) return preview;
#endif

        return Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
    }

    private static bool IsXrRuntimeActive()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive || !string.IsNullOrEmpty(UnityEngine.XR.XRSettings.loadedDeviceName))
            return true;

        try
        {
            if (OVRManager.isHmdPresent) return true;
        }
        catch { }

        return false;
    }

    private static Transform FindMovableRigRoot(Transform camera)
    {
        var current = camera;
        while (current != null)
        {
            if (current.name == "OVRCameraRig" || current.name == "XR Origin" || current.name == "XR Rig")
                return current;
            current = current.parent;
        }

        return camera;
    }
}
