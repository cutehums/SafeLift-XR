using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ensures the manual's fallback demo is available in the copied scene,
/// keeps the official Meta graph Android-only, and hides legacy all-in-one
/// panels/cameras that would otherwise overlap the state UI.
/// </summary>
public static class SafeLiftRuntimeBootstrap
{
    private static readonly string[] LegacyObjectNames =
    {
        "DashboardCanvas",
        "SafeLiftDashboardRoot",
        "SensorPanel",
        "LivePanel",
        "ReportPanel",
        "PhaseCapturePanel",
        "WebcamOverlayRawImage",
        "SafeLiftLegacyEventSystem",
        "MediaPipe Action Tracking Runtime Fill Light",
        "MediaPipe Action Tracking Runtime Front Light"
    };

    private static readonly string[] DuplicateHelperNames =
    {
        "AvatarPoseReceiver",
        "PoseTargets",
        "MediaPipe Action Tracking Receiver"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDemoIfMissing()
    {
        EnsureMetaInteractionSdkRuntime();
        EnsureEditorMetaControllerHandVisuals();
        EnsureDesktopPreviewCameraFramer();
        DisableEditorOnlyOvrRig();
        DisableGistDebugCameras();
        var duplicateHelpers = DisableDuplicateGistSceneHelpers();
        DisableCompetingLegacyAvatarDrivers();
        IsolateLiveAvatarFromSceneCameras();
        DisableLegacyDashboardBinders();
        var hiddenCount = HideLegacyScenePanels();
        var sceneRoot = GameObject.Find("SafeLiftVRDashboard");
        if (sceneRoot != null && sceneRoot.GetComponent<SafeLiftDesktopJoystickOverlay>() == null)
            sceneRoot.AddComponent<SafeLiftDesktopJoystickOverlay>();
        var existing = Object.FindObjectOfType<SafeLiftUIStateMachine>();
        if (existing != null)
        {
            if (existing.GetComponent<SafeLiftBackendBridge>() == null) existing.gameObject.AddComponent<SafeLiftBackendBridge>();
            if (existing.GetComponent<SafeLiftXRInputManager>() == null) existing.gameObject.AddComponent<SafeLiftXRInputManager>();
            if (existing.GetComponent<SafeLiftDesktopJoystickOverlay>() == null) existing.gameObject.AddComponent<SafeLiftDesktopJoystickOverlay>();
            var existingPointer = existing.GetComponent<SafeLiftXRPointerVisualizer>();
            if (existingPointer == null) existingPointer = existing.gameObject.AddComponent<SafeLiftXRPointerVisualizer>();
            existingPointer.enabled = false;
            if (existing.GetComponent<SafeLiftAvatarRuntimeController>() == null) existing.gameObject.AddComponent<SafeLiftAvatarRuntimeController>();
            if (existing.GetComponent<SafeLiftXRViewComfortGuard>() == null) existing.gameObject.AddComponent<SafeLiftXRViewComfortGuard>();
            if (existing.GetComponent<SafeLiftMetaInteractionRuntimeEnabler>() == null) existing.gameObject.AddComponent<SafeLiftMetaInteractionRuntimeEnabler>();
            if (existing.GetComponent<SafeLiftOfficialMetaVisualBinder>() == null) existing.gameObject.AddComponent<SafeLiftOfficialMetaVisualBinder>();
            if (existing.GetComponent<SafeLiftSquatSensorUdpReceiver>() == null) existing.gameObject.AddComponent<SafeLiftSquatSensorUdpReceiver>();
            if (existing.GetComponent<SafeLiftUnityScoreController>() == null) existing.gameObject.AddComponent<SafeLiftUnityScoreController>();
            var editorPreview = GameObject.Find("SafeLiftEditorPreviewCamera");
            var editorPreviewCamera = editorPreview != null ? editorPreview.GetComponent<Camera>() : null;
            Debug.Log("SafeLift Editor camera check: object=" + (editorPreview != null ? editorPreview.name : "MISSING")
                + ", active=" + (editorPreview != null && editorPreview.activeInHierarchy)
                + ", enabled=" + (editorPreviewCamera != null && editorPreviewCamera.enabled)
                + ", main=" + (Camera.main != null ? Camera.main.name : "none"));
            Debug.Log("SafeLift XR runtime ready - existing state machine, hidden legacy objects: " + hiddenCount + ", disabled duplicate GIST helpers: " + duplicateHelpers);
            return;
        }
        var root = new GameObject("SafeLiftXRRuntime");
        root.AddComponent<SafeLiftUIStateMachine>();
        if (root.GetComponent<SafeLiftAvatarRuntimeController>() == null) root.AddComponent<SafeLiftAvatarRuntimeController>();
        root.AddComponent<SafeLiftXRViewComfortGuard>();
        root.AddComponent<SafeLiftMetaInteractionRuntimeEnabler>();
        root.AddComponent<SafeLiftOfficialMetaVisualBinder>();
        root.AddComponent<SafeLiftSquatSensorUdpReceiver>();
        root.AddComponent<SafeLiftUnityScoreController>();
        root.AddComponent<SafeLiftXRInputManager>();
        root.AddComponent<SafeLiftDesktopJoystickOverlay>();
        var pointer = root.AddComponent<SafeLiftXRPointerVisualizer>();
        pointer.enabled = false;
        root.AddComponent<SafeLiftBackendBridge>();
        Debug.Log("SafeLift XR runtime ready - created SafeLiftXRRuntime, hidden legacy objects: " + hiddenCount + ", disabled duplicate GIST helpers: " + duplicateHelpers);
    }

    private static void EnsureMetaInteractionSdkRuntime()
    {
        var shouldEnable = ShouldEnableMetaInteractionRuntime();
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            if (transform == null) continue;
            if (transform.name.IndexOf("ComprehensiveInteraction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transform.name.IndexOf("OVRInteractionComprehensive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transform.name.IndexOf("SafeLiftMetaOVRInteraction", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (transform.gameObject.activeSelf != shouldEnable)
                    transform.gameObject.SetActive(shouldEnable);
                if (!shouldEnable)
                    Debug.Log("SafeLift XR kept Meta Interaction SDK comprehensive prefab disabled in desktop/editor mode to avoid Meta input null-reference spam. It will be enabled on Quest/XR runtime.");
                return;
            }
        }

        var prefab = Resources.Load<GameObject>("Meta/OVRInteractionComprehensive");
        if (prefab == null)
        {
            Debug.LogWarning("SafeLift XR could not load Meta/OVRInteractionComprehensive. Reimport Meta XR All-in-One SDK or run SafeLift XR > Fix Dashboard Scene.");
            return;
        }

        var instance = Object.Instantiate(prefab);
        instance.name = "SafeLiftMetaOVRInteractionComprehensive";
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        instance.SetActive(shouldEnable);
        Object.DontDestroyOnLoad(instance);
        Debug.Log(shouldEnable
            ? "SafeLift XR created Meta Interaction SDK comprehensive prefab at runtime for Quest controller/hand ray and poke UI."
            : "SafeLift XR created Meta Interaction SDK comprehensive prefab but kept it disabled in desktop/editor mode.");
    }

    private static bool ShouldEnableMetaInteractionRuntime()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
    private static void EnsureEditorMetaControllerHandVisuals()
    {
#if UNITY_EDITOR
        // The copied dashboard scene uses the explicit InHand/OnController
        // anchor names. The older shorter names were left here after the
        // scene was rebuilt, so the editor preview silently found no anchor
        // and never created the controller/joystick visuals.
        EnsureEditorPrefabUnderAnchor("LeftControllerInHandAnchor", "SafeLiftLeftControllerVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab", new Vector3(-0.015f, 0f, 0.02f));
        EnsureEditorPrefabUnderAnchor("RightControllerInHandAnchor", "SafeLiftRightControllerVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab", new Vector3(0.015f, 0f, 0.02f));
        EnsureEditorPrefabUnderAnchor("LeftHandOnControllerAnchor", "SafeLiftLeftHandVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRCustomHandPrefab_L.prefab", Vector3.zero);
        EnsureEditorPrefabUnderAnchor("RightHandOnControllerAnchor", "SafeLiftRightHandVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRCustomHandPrefab_R.prefab", Vector3.zero);
#endif
    }

#if UNITY_EDITOR
    private static void EnsureEditorPrefabUnderAnchor(string anchorName, string childName, string prefabPath, Vector3 localPosition)
    {
        var anchor = FindRuntimeTransform(anchorName);
        if (anchor == null) return;

        var existing = anchor.Find(childName);
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            existing.localPosition = localPosition;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("SafeLift XR could not load official Meta visual prefab: " + prefabPath);
            return;
        }

        var instance = Object.Instantiate(prefab, anchor);
        instance.name = childName;
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        instance.SetActive(true);
        Debug.Log("SafeLift XR attached official Meta visual prefab: " + childName);
    }
#endif

    private static Transform FindRuntimeTransform(string name)
    {
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            if (transform != null && string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        }
        return null;
    }
    private static void EnsureDesktopPreviewCameraFramer()
    {
        if (!Application.isEditor) return;

        // Never create, tag, or enable the fixed desktop camera while an XR
        // eye camera is already alive. Otherwise Camera.main can resolve to
        // the preview camera and replace the HMD view with the gym scene.
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            var eye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (eye != null && eye.isActiveAndEnabled)
            {
                var stalePreview = GameObject.Find("SafeLiftEditorPreviewCamera");
                if (stalePreview != null)
                {
                    var staleCamera = stalePreview.GetComponent<Camera>();
                    if (staleCamera != null) staleCamera.enabled = false;
                }
                return;
            }
        }
        var xrDisplayActive = XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName);
        try { xrDisplayActive |= OVRManager.isHmdPresent; } catch { }
        if (xrDisplayActive)
        {
            // XR is still starting. Do not install a competing MainCamera;
            // the OVR rig will provide the eye camera as soon as the session
            // reaches the visible state.
            return;
        }
        if (Object.FindObjectOfType<SafeLiftDesktopPreviewCameraFramer>() == null)
        {
            var helper = new GameObject("SafeLiftDesktopPreviewCameraFramer");
            Object.DontDestroyOnLoad(helper);
            helper.AddComponent<SafeLiftDesktopPreviewCameraFramer>();
        }

        // Do not depend on OVRCameraRig providing the only MainCamera. The
        // Editor desktop test must still render after the XR rig is disabled.
        var preview = GameObject.Find("SafeLiftEditorPreviewCamera");
        if (preview == null)
        {
            preview = new GameObject("SafeLiftEditorPreviewCamera");
            Object.DontDestroyOnLoad(preview);
            preview.AddComponent<Camera>();
        }

        var camera = preview.GetComponent<Camera>();
        camera.enabled = true;
        camera.tag = "MainCamera";
        camera.depth = 1000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 80f;
        camera.fieldOfView = 34f;
        camera.transform.position = new Vector3(0f, 1.62f, -0.35f);
        camera.transform.LookAt(new Vector3(0f, 1.45f, 1.15f), Vector3.up);
    }

    private static void DisableGistDebugCameras()
    {
        var disabled = 0;
        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null || camera.gameObject == null) continue;
            if (camera.gameObject.name != "GISTDebugCamera") continue;
            if (!camera.enabled && !camera.gameObject.activeSelf) continue;

            camera.enabled = false;
            camera.gameObject.SetActive(false);
            disabled++;
        }

        if (disabled > 0)
            Debug.Log("SafeLift XR disabled " + disabled + " GISTDebugCamera object(s) so OVR/Main camera owns the dashboard view.");
    }

    private static void DisableEditorOnlyOvrRig()
    {
        // The dashboard has a desktop preview path in the Editor.  Letting
        // OVRCameraRig initialise at the same time can invoke the native OVR
        // plugin without a headset and crash the Editor before the UI avatar
        // can be tested.  Keep this guard Editor-only; device builds retain
        // the normal XR rig.
        if (!Application.isEditor) return;

        // Quest Link/PCVR must keep the OVR rig alive so head movement changes
        // the world view. Only disable OVR behaviours for a true desktop
        // preview without an active XR device.
        var activeRig = Object.FindObjectOfType<OVRCameraRig>();
        if (activeRig != null && activeRig.centerEyeAnchor != null)
        {
            var eyeCamera = activeRig.centerEyeAnchor.GetComponent<Camera>();
            if (eyeCamera != null && eyeCamera.isActiveAndEnabled)
            {
                Debug.Log("SafeLift XR kept OVRCameraRig active because the Center Eye camera is active.");
                return;
            }
        }
        var headsetPresent = XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName);
        try
        {
            headsetPresent |= OVRManager.isHmdPresent;
        }
        catch
        {
        }
        if (headsetPresent)
        {
            Debug.Log("SafeLift XR kept OVRCameraRig active because an XR display is active.");
            return;
        }

        var disabled = 0;
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            if (transform == null || transform.gameObject == null) continue;
            if (!string.Equals(transform.name, "OVRCameraRig", StringComparison.Ordinal)) continue;
            if (!transform.gameObject.activeSelf && !transform.gameObject.activeInHierarchy) continue;

            // Keep the rig hierarchy active so its controller/hand anchors
            // can still render the editor joystick visuals. The OVR
            // behaviours and cameras below are disabled separately; turning
            // off the whole parent also hides every visual child.
            disabled++;
        }

        foreach (var behaviour in Object.FindObjectsOfType<Behaviour>(true))
        {
            if (behaviour == null || !behaviour.enabled) continue;
            var typeName = behaviour.GetType().Name;
            if (!typeName.StartsWith("OVR", StringComparison.Ordinal)) continue;
            behaviour.enabled = false;
        }

        if (disabled > 0)
            Debug.Log("SafeLift XR disabled OVRCameraRig behaviours for the Editor desktop preview while keeping controller/hand visual anchors active.");
    }

    private static int DisableDuplicateGistSceneHelpers()
    {
        var disabled = 0;
        var transforms = Object.FindObjectsOfType<Transform>(true);

        // Prefer the complete MediaPipe pipeline.  The previous name-only
        // pass kept whichever receiver Unity returned first, which could be a
        // legacy/partial copy and leave the real avatar IK receiver disabled.
        MediaPipeActionTrackingReceiver preferredReceiver = null;
        foreach (var t in transforms)
        {
            if (t == null) continue;
            var candidate = t.GetComponent<MediaPipeActionTrackingReceiver>();
            if (candidate != null && candidate.GetComponent<ActionTrackingAvatarDriver>() != null &&
                candidate.GetComponent<MediaPipeLegIKDriver>() != null)
            {
                preferredReceiver = candidate;
                break;
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in transforms)
        {
            if (t == null || t.gameObject == null) continue;
            foreach (var helperName in DuplicateHelperNames)
            {
                if (!string.Equals(t.name, helperName, StringComparison.Ordinal)) continue;
                if (!t.gameObject.activeInHierarchy && !t.gameObject.activeSelf) break;

                var isPreferredReceiver = helperName == "MediaPipe Action Tracking Receiver" &&
                    preferredReceiver != null && t.gameObject == preferredReceiver.gameObject;
                if (isPreferredReceiver || !seen.Add(helperName))
                {
                    if (isPreferredReceiver) seen.Add(helperName);
                    break;
                }

                t.gameObject.SetActive(false);
                disabled++;
                break;
            }
        }

        if (disabled > 0)
            Debug.Log("SafeLift XR disabled " + disabled + " duplicate GIST helper object(s) so only one receiver/target set drives the scene.");

        return disabled;
    }

    private static void DisableLegacyDashboardBinders()
    {
        foreach (var binder in Object.FindObjectsOfType<SafeLiftUIBinder>(true))
        {
            if (binder != null) binder.enabled = false;
        }

        foreach (var dashboard in Object.FindObjectsOfType<SafeLiftVRDashboardController>(true))
        {
            if (dashboard != null) dashboard.enabled = false;
        }
    }

    private static void DisableCompetingLegacyAvatarDrivers()
    {
        // The selected GIST_AVATA pipeline is ActionTrackingAvatarDriver
        // + MediaPipeLegIKDriver on UDP 5054.  Some imported MyAvatar prefabs
        // also contain the older GIST HAI VRIKPoseTargetDriver/5005 receiver.
        // Both pipelines write the same humanoid bones, producing the visible
        // jitter, bent arms, and lower-body snap-back seen in the dashboard.
        var primaryReceiver = Object.FindObjectOfType<MediaPipeActionTrackingReceiver>();
        var primaryDriver = primaryReceiver != null
            ? primaryReceiver.GetComponent<ActionTrackingAvatarDriver>()
            : null;
        var primaryLegIK = primaryReceiver != null
            ? primaryReceiver.GetComponent<MediaPipeLegIKDriver>()
            : null;
        if (primaryReceiver == null || primaryDriver == null || primaryLegIK == null) return;

        // Do not turn off the VRIK path until the selected MediaPipe driver
        // has a real Humanoid and the required lower-body bones. Otherwise a
        // failed fallback leaves the dashboard with no active pose solver.
        if (!primaryDriver.ResolveHumanoid())
        {
            Debug.LogError("SafeLift XR kept the legacy VRIK pipeline enabled because the primary MediaPipe Humanoid could not be resolved.");
            return;
        }
        foreach (var behaviour in Object.FindObjectsOfType<Behaviour>(true))
        {
            if (behaviour == null || !behaviour.enabled) continue;
            var typeName = behaviour.GetType().Name;
            // VRIK itself also evaluates the skeleton every frame. Disabling
            // only its UDP helper still lets the old solver overwrite the
            // GIST_AVATA MediaPipe IK result, which produces bent arms and
            // snapping legs. MediaPipeActionTracking owns the whole humanoid.
            if (typeName != "VRIKPoseTargetDriver" && typeName != "AvatarPoseUdpReceiver" && typeName != "VRIK") continue;
            behaviour.enabled = false;
            Debug.Log("SafeLift XR disabled competing legacy avatar driver: " + typeName + ". GIST_AVATA MediaPipe UDP 5054 owns the avatar.");
        }
    }

    private static int HideLegacyScenePanels()
    {
        var hiddenCount = 0;
        var transforms = Object.FindObjectsOfType<Transform>(true);
        foreach (var t in transforms)
        {
            if (t == null) continue;
            foreach (var name in LegacyObjectNames)
            {
                if (t.name == name)
                {
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        hiddenCount++;
                    }
                    break;
                }
            }
        }
        return hiddenCount;
    }

    private static void IsolateLiveAvatarFromSceneCameras()
    {
        var avatar = GameObject.Find("MyAvatar");
        if (avatar == null) return;

        const int previewLayer = 30;
        SetLayerRecursively(avatar, previewLayer);
        var previewBit = 1 << previewLayer;
        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null) continue;
            camera.cullingMask &= ~previewBit;
        }
        Debug.Log("SafeLift XR isolated MyAvatar to preview layer 30; scene cameras render the UI only.");
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
