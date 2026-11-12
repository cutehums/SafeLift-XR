using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

/// Quest controller shortcuts for the continuous squat analysis.
/// Right A / stick-click: finish rep and generate report.
/// Right B: reset rep. Left A: save snapshot. Left B: recenter dashboard.
public sealed class QuestSquatInputController : MonoBehaviour
{
    [SerializeField] private string serverUrl = "http://127.0.0.1:8090";
    [SerializeField] private DashboardBillboard billboard;
    [SerializeField] private QuestDashboardFollow dashboardFollow;
    [SerializeField] private float stickThreshold = .7f;
    [SerializeField] private float commandCooldown = .7f;

    private InputDevice leftHand;
    private InputDevice rightHand;
    private bool previousRightStickClick;
    private bool previousRightDown;
    private bool previousRightA;
    private bool previousRightB;
    private bool previousLeftA;
    private bool previousLeftB;
    private float nextCommandTime;

    private void Awake()
    {
        if (billboard == null) billboard = GetComponent<DashboardBillboard>();
        if (dashboardFollow == null) dashboardFollow = GetComponent<QuestDashboardFollow>();
        FindDevices();
    }

    public void Configure(string backendUrl)
    {
        if (!string.IsNullOrWhiteSpace(backendUrl)) serverUrl = backendUrl;
    }

    private void Update()
    {
        if (!leftHand.isValid || !rightHand.isValid) FindDevices();

        var rightStick = Vector2.zero;
        rightHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out rightStick);
        var rightClick = false;
        rightHand.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out rightClick);
        var rightA = false;
        var rightB = false;
        rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out rightA);
        rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out rightB);
        var leftA = false;
        var leftB = false;
        leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out leftA);
        leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out leftB);

        if ((rightA && !previousRightA) || (rightClick && !previousRightStickClick)) SendCommand("finish_rep");
        if (rightB && !previousRightB) SendCommand("reset_rep");
        if (leftA && !previousLeftA) SendCommand("save_snapshot");
        if (leftB && !previousLeftB) Recenter();

        var rightDown = rightStick.y < -stickThreshold;
        if (rightDown && !previousRightDown) SendCommand("finish_rep");
        previousRightStickClick = rightClick;
        previousRightDown = rightDown;
        previousRightA = rightA;
        previousRightB = rightB;
        previousLeftA = leftA;
        previousLeftB = leftB;
    }

    private void Recenter()
    {
        if (dashboardFollow != null) dashboardFollow.Recenter();
        else billboard?.Recenter();
    }

    private void FindDevices()
    {
        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, devices);
        if (devices.Count > 0) leftHand = devices[0];
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);
        if (devices.Count > 0) rightHand = devices[0];
    }

    private void SendCommand(string command)
    {
        if (Time.unscaledTime < nextCommandTime) return;
        nextCommandTime = Time.unscaledTime + commandCooldown;
        StartCoroutine(PostCommand(command));
    }

    private IEnumerator PostCommand(string command)
    {
        var body = "{\"command\":\"" + command + "\"}";
        using (var request = new UnityWebRequest(serverUrl.TrimEnd('/') + "/command", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("SafeLift Quest command failed: " + request.error);
            else
                Debug.Log("SafeLift Quest command sent: " + command);
        }
    }
}
