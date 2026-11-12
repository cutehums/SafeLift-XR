using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Keeps the Unity Editor / no-Quest preview camera at a comfortable eye-level
/// view. Without this, OVRCameraRig can start at floor height and make the
/// dashboard look far away, tiny, and blurry behind the gym environment.
/// </summary>
public sealed class SafeLiftDesktopPreviewCameraFramer : MonoBehaviour
{
    private const string PreviewCameraName = "SafeLiftEditorPreviewCamera";
    private const float EyeHeight = 1.62f;
    private const float CameraZ = -0.35f;
    private const float LookAtY = 1.45f;
    private const float LookAtZ = 1.15f;
    private const float FieldOfView = 34f;
    private const float CanvasDistance = 1.05f;
    private const float CanvasHeight = 1.48f;
    private const float CanvasScale = 0.00088f;
    private const float EnforceSeconds = 5f;

    private float stopEnforcingAt;

    private void Awake()
    {
        if (IsXrRuntimeActive()) { enabled = false; return; }
        stopEnforcingAt = Time.realtimeSinceStartup + EnforceSeconds;
        ReframeForEditorPreview(EnforceSeconds);
    }

    private void Start()
    {
        if (IsXrRuntimeActive()) { enabled = false; return; }
        ReframeForEditorPreview(EnforceSeconds);
    }

    private void LateUpdate()
    {
        if (!Application.isEditor) return;
        if (IsXrRuntimeActive())
        {
            ReleasePreviewCameraForXr();
            enabled = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.F9) || Input.GetKeyDown(KeyCode.R))
            ReframeForEditorPreview(EnforceSeconds);

        if (Time.realtimeSinceStartup > stopEnforcingAt) return;

        FrameCamera();
        FrameRuntimeCanvas();
    }

    public void ReframeForEditorPreview(float seconds = EnforceSeconds)
    {
        if (IsXrRuntimeActive()) return;
        stopEnforcingAt = Time.realtimeSinceStartup + Mathf.Max(.5f, seconds);
        FrameCamera();
        FrameRuntimeCanvas();
    }

    private void FrameCamera()
    {
        var camera = EnsurePreviewCamera();
        if (camera == null) return;

        camera.transform.position = new Vector3(0f, EyeHeight, CameraZ);
        camera.transform.LookAt(new Vector3(0f, LookAtY, LookAtZ), Vector3.up);
        camera.fieldOfView = FieldOfView;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 80f;
        camera.tag = "MainCamera";
        camera.enabled = true;
        DisableCompetingEditorCameras(camera);
    }

    private void FrameRuntimeCanvas()
    {
        var canvas = FindSafeLiftCanvas();
        var camera = EnsurePreviewCamera();
        if (canvas == null || camera == null) return;

        var forward = Vector3.forward;
        canvas.transform.position = new Vector3(0f, CanvasHeight, CameraZ + CanvasDistance);
        canvas.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        canvas.transform.localScale = Vector3.one * CanvasScale;
        canvas.worldCamera = camera;
    }

    private static Camera EnsurePreviewCamera()
    {
        if (!Application.isEditor)
            return Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();

        foreach (var existing in Object.FindObjectsOfType<Camera>(true))
        {
            if (existing != null && existing.name == PreviewCameraName)
                return existing;
        }

        var go = new GameObject(PreviewCameraName);
        Object.DontDestroyOnLoad(go);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.depth = 1000f;
        return camera;
    }

    private static void DisableCompetingEditorCameras(Camera previewCamera)
    {
        if (!Application.isEditor) return;

        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null || camera == previewCamera) continue;
            // The Center Eye camera belongs to the HMD tracking pipeline and
            // must never be disabled by this desktop-only helper.
            if (camera.GetComponentInParent<OVRCameraRig>() != null) continue;
            if (camera.name == "GISTDebugCamera")
            {
                camera.enabled = false;
                camera.gameObject.SetActive(false);
            }
        }
    }

    private Canvas FindSafeLiftCanvas()
    {
        foreach (var canvas in Object.FindObjectsOfType<Canvas>(true))
        {
            if (canvas != null && canvas.name == "SafeLiftDemoWorldCanvas")
                return canvas;
        }

        return null;
    }

    private static void ReleasePreviewCameraForXr()
    {
        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null) continue;
            if (camera.name == PreviewCameraName)
            {
                camera.enabled = false;
                camera.gameObject.SetActive(false);
                continue;
            }

            if (camera.GetComponentInParent<OVRCameraRig>() != null)
            {
                camera.gameObject.SetActive(true);
                camera.enabled = true;
                camera.tag = "MainCamera";
            }
        }
    }

    private static bool IsXrRuntimeActive()
    {
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            var eyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (eyeCamera != null && eyeCamera.isActiveAndEnabled) return true;
        }
#if UNITY_EDITOR
        // Preserve real Quest Link/PCVR tracking in the Editor. Only use the
        // fixed desktop camera when no XR display is active.
        if (XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            return true;
        try
        {
            if (OVRManager.isHmdPresent) return true;
        }
        catch { }
        return false;
#else
        if (XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            return true;

        try
        {
            if (OVRManager.isHmdPresent) return true;
        }
        catch { }

        return false;
#endif
    }
}
