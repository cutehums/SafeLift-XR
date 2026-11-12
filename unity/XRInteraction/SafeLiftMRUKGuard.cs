using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the optional Meta MRUtilityKit global updater from running in the
/// SafeLift dashboard. SafeLift does not use scene meshing/room understanding;
/// on Quest Link the package can be present but uninitialized and repeatedly
/// throw from MRUKGlobalContext.Update().
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class SafeLiftMRUKGuard : MonoBehaviour
{
    private bool disabled;
    private readonly List<Behaviour> disabledMrukBehaviours = new List<Behaviour>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        var existing = FindObjectOfType<SafeLiftMRUKGuard>();
        if (existing != null) return;
        var go = new GameObject("SafeLiftMRUKGuard");
        DontDestroyOnLoad(go);
        go.AddComponent<SafeLiftMRUKGuard>();
    }

    private void Update()
    {
        if (disabled) return;
        if (!Application.isPlaying)
        {
            disabled = true;
            return;
        }
        var behaviours = Resources.FindObjectsOfTypeAll<Behaviour>();
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this) continue;
            var type = behaviour.GetType();
            if (type.FullName != "Meta.XR.MRUtilityKit.MRUKGlobalContext") continue;
            if (behaviour.enabled)
            {
                behaviour.enabled = false;
                disabledMrukBehaviours.Add(behaviour);
                Debug.Log("SafeLift: disabled optional MRUK global updater; dashboard does not require scene understanding.");
            }
        }
        disabled = true;
    }

    private void OnApplicationQuit()
    {
        // MRUKGlobalContext expects its singleton to remain valid during the
        // normal Unity shutdown sequence.  Restore anything this guard
        // disabled before DDOL objects are torn down; otherwise MRUK attempts
        // to recreate its context while Play mode is already ending and calls
        // DontDestroyOnLoad from a non-Play lifecycle callback.
        foreach (var behaviour in disabledMrukBehaviours)
        {
            if (behaviour != null) behaviour.enabled = true;
        }
        disabledMrukBehaviours.Clear();
        disabled = true;
    }
}
