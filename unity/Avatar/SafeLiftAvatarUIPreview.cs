using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the selected live avatar into a RawImage so the avatar is part of
/// the world-space SafeLift dashboard instead of floating behind the UI.
/// </summary>
public sealed class SafeLiftAvatarUIPreview : MonoBehaviour
{
    private const int PreviewLayer = 30;
    private GameObject avatarRoot;
    private RawImage target;
    private Camera previewCamera;
    private RenderTexture renderTexture;
    private bool configured;

    public void Configure(GameObject root, RawImage image)
    {
        avatarRoot = root;
        if (avatarRoot == null)
        {
            // The fallback UI can be built before the MediaPipe driver has
            // finished resolving its Humanoid. Prefer the scene's actual live
            // MyAvatar instead of leaving the preview bound to a review/helper
            // avatar that never receives pose packets.
            avatarRoot = GameObject.Find("MyAvatar");
        }
        target = image;
        if (target == null) return;

        renderTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32)
        {
            name = "SafeLiftAvatarPreviewTexture",
            useMipMap = false,
            autoGenerateMips = false
        };
        renderTexture.Create();
        target.texture = renderTexture;

        var cameraObject = new GameObject("SafeLiftAvatarPreviewCamera");
        cameraObject.transform.SetParent(transform, false);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.01f, 0.02f, 0.035f, 1f);
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.targetTexture = renderTexture;
        previewCamera.fieldOfView = 36f;
        previewCamera.aspect = 1f;
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = 20f;
        previewCamera.enabled = false;
        configured = true;
    }

    public void SetAvatarRoot(GameObject root)
    {
        if (root != null) avatarRoot = root;
    }

    public void SetVisible(bool visible)
    {
        if (!configured || previewCamera == null || target == null) return;
        target.gameObject.SetActive(visible);
        previewCamera.enabled = visible;
        if (visible) PrepareAvatar();
    }

    private void LateUpdate()
    {
        if (!configured || previewCamera == null) return;

        // Main/OVR/editor cameras can be created or reconfigured after the
        // bootstrap runs. Re-apply this every frame so the live avatar has
        // exactly one renderer: this RawImage preview camera.
        ExcludePreviewLayerFromSceneCameras();

        if (!previewCamera.enabled || avatarRoot == null) return;
        PrepareAvatar();
    }

    private void PrepareAvatar()
    {
        var trackingDriver = FindObjectOfType<ActionTrackingAvatarDriver>(true);
        if (trackingDriver != null && trackingDriver.avatarRoot != null)
            avatarRoot = trackingDriver.avatarRoot.gameObject;
        if (avatarRoot == null) avatarRoot = GameObject.Find("MyAvatar");
        if (avatarRoot == null) return;
        if (!avatarRoot.activeSelf) avatarRoot.SetActive(true);
        SetLayerRecursively(avatarRoot, PreviewLayer);
        ExcludePreviewLayerFromSceneCameras();

        var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.enabled = true;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!hasBounds || bounds.size.sqrMagnitude < 0.0001f) return;

        // Fit the full body against the actual camera FOV.  A bounds-magnitude
        // distance clipped tall avatars (especially the head and feet) in the
        // square RawImage.
        var halfVerticalFov = previewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
        var halfHorizontalFov = Mathf.Atan(Mathf.Tan(halfVerticalFov) * Mathf.Max(0.25f, previewCamera.aspect));
        var heightDistance = bounds.size.y * 0.5f / Mathf.Max(0.01f, Mathf.Tan(halfVerticalFov));
        var widthDistance = bounds.size.x * 0.5f / Mathf.Max(0.01f, Mathf.Tan(halfHorizontalFov));
        var distance = Mathf.Max(1.1f, Mathf.Max(heightDistance, widthDistance) * 1.35f);
        var viewDirection = avatarRoot.transform.forward;
        if (viewDirection.sqrMagnitude < 0.001f) viewDirection = Vector3.forward;
        viewDirection.Normalize();
        var lookAt = bounds.center + Vector3.up * bounds.size.y * 0.02f;
        var cameraPosition = lookAt - viewDirection * distance;
        previewCamera.transform.position = cameraPosition;
        previewCamera.transform.LookAt(lookAt);
    }

    private void ExcludePreviewLayerFromSceneCameras()
    {
        var previewBit = 1 << PreviewLayer;
        foreach (var camera in FindObjectsOfType<Camera>(true))
        {
            if (camera == null || camera == previewCamera) continue;
            // The live avatar is rendered only by the RawImage preview camera.
            // Leaving layer 30 enabled on the main/OVR/editor camera creates a
            // second giant avatar outside the UI card and makes it look clipped.
            camera.cullingMask &= ~previewBit;
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
    }

    private void OnDestroy()
    {
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
