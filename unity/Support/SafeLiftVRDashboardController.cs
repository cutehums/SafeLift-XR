using UnityEngine;
using UnityEngine.UI;

public class SafeLiftVRDashboardController : MonoBehaviour
{
    [Header("Backend")][SerializeField] public string serverUrl = "http://127.0.0.1:8090";
    [SerializeField] public float stateRefreshInterval = .2f, frameRefreshInterval = .15f, reportRefreshInterval = 1f;
    [SerializeField] public bool useMockDataWhenDisconnected = false;
    [Header("Components")][SerializeField] private SafeLiftStateClient stateClient; [SerializeField] private LiveFrameReceiver liveFrameReceiver; [SerializeField] private ReportClient reportClient; [SerializeField] private PhaseCaptureReceiver phaseCaptureReceiver; [SerializeField] private SafeLiftUIBinder uiBinder; [SerializeField] private RawImage webcamOverlay;

    private void Awake()
    {
        if (uiBinder == null) uiBinder = GetComponent<SafeLiftUIBinder>();
        if (uiBinder != null) uiBinder.enabled = false;
        Debug.Log("SafeLift legacy dashboard controller disabled; SafeLiftUIStateMachine owns the demo flow. Avatar/pose objects are preserved for phase recording.");
        enabled = false;
    }
}
