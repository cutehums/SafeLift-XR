using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Uses official Meta SDK controller/hand prefabs on the real OVRCameraRig anchors.
/// This is especially important in Quest Link Play Mode where scene prefab modifications can be late
/// and controller/hand visuals must follow the tracked anchors instead of floating as detached objects.
/// </summary>
public sealed class SafeLiftOfficialMetaVisualBinder : MonoBehaviour
{
    private const float StartupWatchSeconds = 15f;
    private float watchUntil;

    private void Awake()
    {
        watchUntil = Time.realtimeSinceStartup + StartupWatchSeconds;
    }

    private void Update()
    {
        EnsureOfficialVisuals();
        if (Time.realtimeSinceStartup > watchUntil) enabled = false;
    }

    private static void EnsureOfficialVisuals()
    {
#if UNITY_EDITOR
        // The desktop Game View is also the visual QA surface.  Do not gate
        // creation on a physical XR device: without a Quest/Link session the
        // controller prefabs were never attached, so the joystick affordance
        // could not appear even though the anchors existed in the scene.
        EnsurePrefabUnderOvrAnchor("LeftControllerInHandAnchor", "SafeLiftLeftControllerVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab", OVRInput.Controller.LTouch);
        EnsurePrefabUnderOvrAnchor("RightControllerInHandAnchor", "SafeLiftRightControllerVisual", "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab", OVRInput.Controller.RTouch);
        HideAllHandVisuals();
#endif
    }

#if UNITY_EDITOR
    private static void EnsurePrefabUnderOvrAnchor(string anchorName, string childName, string prefabPath, OVRInput.Controller controller)
    {
        var anchor = FindOvrAnchor(anchorName);
        if (anchor == null) return;

        var existing = anchor.Find(childName);
        GameObject visual;
        if (existing != null)
        {
            visual = existing.gameObject;
        }
        else
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("SafeLift XR could not load official Meta visual prefab: " + prefabPath);
                return;
            }

            visual = PrefabUtility.InstantiatePrefab(prefab, anchor) as GameObject;
            if (visual == null) visual = Instantiate(prefab, anchor);
            visual.name = childName;
        }

        visual.transform.SetParent(anchor, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        UpdateVisualVisibility(visual, controller);

        var controllerHelper = visual.GetComponentInChildren<OVRControllerHelper>(true);
        if (controllerHelper != null)
        {
            controllerHelper.m_controller = controller;
            // Quest Link often does not report ControllerInHand reliably during Editor Play.
            // The user must see the held controller model whenever Touch tracking is available.
            controllerHelper.m_showState = OVRInput.InputDeviceShowState.Always;
            controllerHelper.showWhenHandsArePoweredByNaturalControllerPoses = true;
        }

        var hand = visual.GetComponentInChildren<OVRHand>(true);
        if (hand != null)
            hand.m_showState = OVRInput.InputDeviceShowState.Always;
    }


    private static void UpdateVisualVisibility(GameObject visual, OVRInput.Controller visualController)
    {
        var isLeftHand = visualController == OVRInput.Controller.LHand;
        var isRightHand = visualController == OVRInput.Controller.RHand;
        var isHandVisual = isLeftHand || isRightHand;
        var touchController = isRightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

        bool touchAvailable = IsTouchControllerUsable(touchController);
        bool handTracked = IsControllerTracked(visualController);

        // Priority rule requested for the demo: when a physical controller is held/tracked,
        // show the official controller object and hide the hand mesh for that side. Only show
        // hand tracking visuals when Touch is not available and the hand pose is genuinely tracked.
        // In the desktop/editor preview there is no physical Touch device,
        // but the user still needs to see the joystick/controller affordance
        // while testing the dashboard. Device tracking remains authoritative
        // on Quest; editor preview keeps controller visuals visible.
        // SafeLift uses the small joystick/controller cue as the interaction
        // affordance. Do not render full Meta hand meshes in the Game View or
        // Quest Link: they can appear detached and cover the dashboard/avatar.
        bool shouldShow = !isHandVisual
            && (Application.isEditor || IsTouchControllerUsable(visualController));
        if (visual.activeSelf != shouldShow)
            visual.SetActive(shouldShow);
    }

    private static void HideAllHandVisuals()
    {
        // Do not instantiate hand prefabs at all. Also hide hand objects that
        // may already exist in a copied scene or may have been created by an
        // older Play session. Controller prefabs and the SafeLift joystick
        // overlay are intentionally left untouched.
        foreach (var hand in FindObjectsOfType<OVRHand>(true))
        {
            if (hand != null && hand.gameObject.activeSelf)
                hand.gameObject.SetActive(false);
        }

        foreach (var transform in FindObjectsOfType<Transform>(true))
        {
            if (transform == null) continue;
            var name = transform.name ?? string.Empty;
            if (name.IndexOf("HandVisual", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("CustomHandPrefab", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (transform.gameObject.activeSelf)
                transform.gameObject.SetActive(false);
        }
    }

    private static bool IsTouchControllerUsable(OVRInput.Controller controller)
    {
        return OVRInput.IsControllerConnected(controller) || IsControllerTracked(controller);
    }

    private static bool IsControllerTracked(OVRInput.Controller controller)
    {
        return OVRInput.GetControllerPositionTracked(controller) || OVRInput.GetControllerOrientationTracked(controller);
    }

    private static Transform FindOvrAnchor(string anchorName)
    {
        Transform fallback = null;
        foreach (var transform in FindObjectsOfType<Transform>(true))
        {
            if (transform == null || !string.Equals(transform.name, anchorName, StringComparison.Ordinal)) continue;
            if (transform.GetComponentInParent<OVRCameraRig>(true) != null) return transform;
            fallback = transform;
        }
        return fallback;
    }
#endif
}
