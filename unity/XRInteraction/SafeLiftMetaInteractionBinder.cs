using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Bridges the SafeLift world-space UGUI canvas to Meta Interaction SDK.
/// The Meta OVRInteractionComprehensive prefab supplies controller visuals,
/// hand visuals, ray interactors, poke interactors, and pinch/select input.
/// This binder only registers the SafeLift canvas as a pointable canvas.
/// </summary>
public static class SafeLiftMetaInteractionBinder
{
    public static void Ensure(Canvas canvas)
    {
        if (canvas == null) return;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        if (ShouldUseMetaPointableCanvas())
        {
            var pointableCanvas = canvas.GetComponent<PointableCanvas>();
            if (pointableCanvas == null)
                pointableCanvas = canvas.gameObject.AddComponent<PointableCanvas>();

            pointableCanvas.InjectCanvas(canvas);
            EnsurePointableCanvasModule();
        }
        else
        {
            EnsureStandaloneEventSystem();
        }
    }


    private static bool ShouldUseMetaPointableCanvas()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
    private static void EnsureStandaloneEventSystem()
    {
        var eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            var root = new GameObject("SafeLiftDesktopEventSystem");
            eventSystem = root.AddComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
    }
    private static void EnsurePointableCanvasModule()
    {
        var eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            var root = new GameObject("SafeLiftMetaEventSystem");
            eventSystem = root.AddComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<PointableCanvasModule>() == null)
            eventSystem.gameObject.AddComponent<PointableCanvasModule>();
    }

}
