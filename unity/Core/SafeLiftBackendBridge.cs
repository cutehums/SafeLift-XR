using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public sealed class SafeLiftBackendBridge : MonoBehaviour
{
    private const string BackendUrlPrefsKey = "SafeLiftXR.BackendUrl";

    public string ServerUrl = "http://127.0.0.1:8090";
    public float PollInterval = .35f;
    private SafeLiftUIStateMachine stateMachine;
    private bool connected;
    private bool hasReportedDisconnected;
    private bool hasReportedOptionalEndpointIssue;
    private Texture2D currentFrame;

    private void Start()
    {
        stateMachine = GetComponent<SafeLiftUIStateMachine>() ?? FindObjectOfType<SafeLiftUIStateMachine>();
        ServerUrl = Application.isEditor
            ? "http://127.0.0.1:8090"
            : ResolveStartupServerUrl(ServerUrl);
        stateMachine?.ConfigureBackendUrl(ServerUrl);
        var sensorReceiver = FindObjectOfType<SafeLiftSquatSensorUdpReceiver>();
        sensorReceiver?.ConfigureBackendForwarding(ServerUrl, true);
        StartCoroutine(Poll());
        StartCoroutine(PollLiveFrame());
    }

    public void ConfigureServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return;

        ServerUrl = NormalizeServerUrl(serverUrl);
        connected = false;
        hasReportedDisconnected = false;
        hasReportedOptionalEndpointIssue = false;
        stateMachine?.ConfigureBackendUrl(ServerUrl);
        var sensorReceiver = FindObjectOfType<SafeLiftSquatSensorUdpReceiver>();
        sensorReceiver?.ConfigureBackendForwarding(ServerUrl, true);
        PlayerPrefs.SetString(BackendUrlPrefsKey, ServerUrl);
        PlayerPrefs.Save();
        Debug.Log("SafeLift backend URL configured: " + ServerUrl);
    }

    public void SendResetSessionCommand()
    {
        StartCoroutine(PostCommand("reset_rep"));
    }

    public void SendFinishReportCommand()
    {
        // Give the final REP score packet a short head start so the backend
        // can use the same Unity/Fusion score in the QR report.
        StartCoroutine(PostFinishReportAfterScoreFlush());
    }

    public void SendRepScore(SafeLiftScoreResult score, string setLabel, int repNumber)
    {
        if (score == null || string.IsNullOrWhiteSpace(setLabel) || repNumber < 1) return;
        var payload = new UnityRepScorePayload
        {
            set = setLabel,
            rep = repNumber,
            total_score = score.totalScore,
            fsr_score = score.fsr != null ? score.fsr.score : 0f,
            fsr_balance_score = score.fsr != null ? score.fsr.balanceScore : 0f,
            fsr_heel_score = score.fsr != null ? score.fsr.heelScore : 0f,
            bracing_score = score.bracing != null ? score.bracing.score : 0f,
            spine_score = score.spine != null ? score.spine.score : 0f,
            active_movement_seconds = score.activeMovementSeconds,
            balance_duration_seconds = score.fsr != null ? score.fsr.balanceDurationSeconds : 0f,
            heel_duration_seconds = score.fsr != null ? score.fsr.heelDurationSeconds : 0f,
            bracing_duration_seconds = score.bracing != null ? score.bracing.durationSeconds : 0f,
            spine_duration_seconds = score.spine != null ? score.spine.durationSeconds : 0f,
            source = "Unity/Fusion SafeLiftScoring"
        };
        StartCoroutine(PostRepScore(payload));
    }

    private IEnumerator PostCommand(string command)
    {
        yield return PostCommandRequest(command);
    }

    private IEnumerator PostFinishReportAfterScoreFlush()
    {
        yield return new WaitForSecondsRealtime(.25f);
        yield return PostCommandRequest("finish_rep");
    }

    private IEnumerator PostCommandRequest(string command)
    {
        var json = "{\"command\":\"" + command + "\"}";
        using (var request = new UnityWebRequest(ServerUrl.TrimEnd('/') + "/command", UnityWebRequest.kHttpVerbPOST))
        {
            var body = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 2;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("SafeLift backend command sent: " + command);
            }
            else
            {
                Debug.LogWarning("SafeLift backend command failed: " + command + " / " + (request.error ?? string.Empty));
            }
        }
    }

    private IEnumerator PostRepScore(UnityRepScorePayload payload)
    {
        var json = JsonUtility.ToJson(payload);
        using (var request = new UnityWebRequest(ServerUrl.TrimEnd('/') + "/api/unity_rep_score", UnityWebRequest.kHttpVerbPOST))
        {
            var body = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 2;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("SafeLift Unity REP score upload failed: " + (request.error ?? string.Empty));
        }
    }

    [Serializable]
    private sealed class UnityRepScorePayload
    {
        public string set;
        public int rep;
        public float total_score;
        public float fsr_score;
        public float fsr_balance_score;
        public float fsr_heel_score;
        public float bracing_score;
        public float spine_score;
        public float active_movement_seconds;
        public float balance_duration_seconds;
        public float heel_duration_seconds;
        public float bracing_duration_seconds;
        public float spine_duration_seconds;
        public string source;
    }

    private IEnumerator Poll()
    {
        while (enabled)
        {
            using (var request = UnityWebRequest.Get(ServerUrl.TrimEnd('/') + "/api/session_state"))
            {
                request.timeout = 5; yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { var state = JsonUtility.FromJson<SafeLiftState>(request.downloadHandler.text); if (state != null) { connected = true; hasReportedDisconnected = false; stateMachine?.ApplyBackendState(state); } }
                    catch (Exception ex) { Debug.LogWarning("SafeLift backend state parse failed: " + ex.Message); }
                }
                else
                {
                    SetDisconnectedFallback("state", request.error);
                }
            }
            yield return FetchReport();
            yield return FetchHealth();
            yield return new WaitForSecondsRealtime(Mathf.Max(.1f, PollInterval));
        }
    }

    private IEnumerator FetchReport()
    {
        using (var request = UnityWebRequest.Get(ServerUrl.TrimEnd('/') + "/api/latest_report"))
        {
            request.timeout = 5; yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                try { stateMachine?.ApplyBackendReport(JsonUtility.FromJson<SafeLiftReport>(request.downloadHandler.text)); }
                catch (Exception ex) { Debug.LogWarning("SafeLift backend report parse failed: " + ex.Message); }
            }
            else
            {
                ReportOptionalEndpointIssue("report", request.error);
            }
        }
    }

    private IEnumerator FetchHealth()
    {
        using (var request = UnityWebRequest.Get(ServerUrl.TrimEnd('/') + "/api/health"))
        {
            request.timeout = 5; yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                try { stateMachine?.ApplyBackendHealth(JsonUtility.FromJson<SafeLiftHealth>(request.downloadHandler.text)); }
                catch (Exception ex) { Debug.LogWarning("SafeLift backend health parse failed: " + ex.Message); }
            }
            else
            {
                ReportOptionalEndpointIssue("health", request.error);
            }
        }
    }

    private IEnumerator PollLiveFrame()
    {
        while (enabled)
        {
            if (stateMachine != null)
            {
                using (var request = UnityWebRequestTexture.GetTexture(ServerUrl.TrimEnd('/') + "/api/live_frame.jpg", false))
                {
                    // JPEG generation can briefly wait for the next camera
                    // frame. A two-second timeout caused transient libcurl 56
                    // resets while the backend itself was still healthy.
                    request.timeout = 5;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var next = DownloadHandlerTexture.GetContent(request);
                        if (next != null)
                        {
                            if (currentFrame != null) Destroy(currentFrame);
                            currentFrame = next;
                            stateMachine.ApplyLiveFrame(currentFrame);
                            stateMachine.SetLiveFrameStatus(true, null);
                        }
                        else
                        {
                            stateMachine.SetLiveFrameStatus(false, "empty frame");
                        }
                    }
                    else
                    {
                        stateMachine.SetLiveFrameStatus(false, request.error);
                        // A live preview is optional; retry transient transport
                        // failures without turning them into a persistent
                        // backend outage or a noisy red-looking console error.
                        if (!IsTransientTransportFailure(request.error))
                            ReportOptionalEndpointIssue("live frame", request.error);
                    }
                }
            }
            yield return new WaitForSecondsRealtime(.25f);
        }
    }

    private static bool IsTransientTransportFailure(string error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        return error.IndexOf("Curl error 56", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("connection was reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void SetDisconnectedFallback(string endpoint, string error)
    {
        connected = false;

        if (hasReportedDisconnected) return;
        hasReportedDisconnected = true;
        Debug.LogWarning("SafeLift backend " + endpoint + " unavailable. Real sensor/backend data is required; no mock fallback will be used. " + (error ?? string.Empty));
    }

    private void ReportOptionalEndpointIssue(string endpoint, string error)
    {
        if (hasReportedOptionalEndpointIssue) return;
        hasReportedOptionalEndpointIssue = true;
        Debug.LogWarning("SafeLift backend optional endpoint " + endpoint + " unavailable. Keeping current UI flow. " + (error ?? string.Empty));
    }

    private void OnDestroy()
    {
        if (currentFrame != null) Destroy(currentFrame);
    }

    private static string ResolveStartupServerUrl(string fallback)
    {
        var fromArgs = TryGetCommandLineBackendUrl();
        if (!string.IsNullOrWhiteSpace(fromArgs)) return NormalizeServerUrl(fromArgs);

        // The Unity Editor runs on the same PC as the local backend.  Do not
        // resurrect a stale Quest/PC-IP value from PlayerPrefs here; that
        // leaves the dashboard waiting for /api/session_state even though the
        // local backend is healthy. Standalone Quest builds still use the
        // explicitly configured PC address below.
        if (Application.isEditor) return "http://127.0.0.1:8090";

        var saved = PlayerPrefs.GetString(BackendUrlPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved)) return NormalizeServerUrl(saved);

        return NormalizeServerUrl(fallback);
    }

    private static string TryGetCommandLineBackendUrl()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg)) continue;

            if (arg.StartsWith("--safelift-url=", StringComparison.OrdinalIgnoreCase))
                return arg.Substring("--safelift-url=".Length);

            if (string.Equals(arg, "--safelift-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }

        return string.Empty;
    }

    private static string NormalizeServerUrl(string url)
    {
        var value = (url ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value)) return "http://127.0.0.1:8090";
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "http://" + value;
        return value.TrimEnd('/');
    }
}
