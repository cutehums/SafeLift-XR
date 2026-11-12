using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// The official Meta Interaction SDK comprehensive prefab is enabled only in the Android Quest build.
/// Editor/Quest Link uses SafeLift's guarded controller-ray and OVRHand-pinch fallback so an incomplete
/// Editor data-source graph cannot flood the Console with DataModifier null-reference errors.
/// </summary>
public sealed class SafeLiftMetaInteractionRuntimeEnabler : MonoBehaviour
{
    private const float StartupWatchSeconds = 10f;
    private float watchUntil;

    private void Awake()
    {
        watchUntil = Time.realtimeSinceStartup + StartupWatchSeconds;
        SyncMetaInteractionPrefab();
    }

    private void Update()
    {
        SyncMetaInteractionPrefab();
        if (Time.realtimeSinceStartup > watchUntil) enabled = false;
    }

    private static void SyncMetaInteractionPrefab()
    {
        var shouldEnable = ShouldEnableMetaInteractionRuntime();
        foreach (var transform in FindObjectsOfType<Transform>(true))
        {
            if (transform == null) continue;
            if (!IsComprehensiveInteraction(transform.name)) continue;
            if (transform.gameObject.activeSelf != shouldEnable)
            {
                transform.gameObject.SetActive(shouldEnable);
                Debug.Log(shouldEnable
                    ? "SafeLift XR enabled Meta Interaction SDK for Quest controller and hand interactions."
                    : "SafeLift XR kept Meta Interaction SDK disabled in Editor to avoid detached hand/comprehensive input artifacts.");
            }
        }
    }

    private static bool IsComprehensiveInteraction(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.IndexOf("ComprehensiveInteraction", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("OVRInteractionComprehensive", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("SafeLiftMetaOVRInteraction", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ShouldEnableMetaInteractionRuntime()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
}
