using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class ReportClient : MonoBehaviour
{
    [SerializeField] private float refreshInterval = 1f;
    private string url;
    private bool hasRealReport;
    private SafeLiftReport lastStableReport;
    public event Action<SafeLiftReport> ReportReceived;
    public event Action<string> RequestFailed;
    public float RefreshInterval { get => refreshInterval; set => refreshInterval = value; }
    public void Configure(string serverUrl) => url = serverUrl.TrimEnd('/') + "/api/latest_report";
    public IEnumerator Poll(bool useMockWhenDisconnected)
    {
        while (enabled)
        {
            yield return Fetch(useMockWhenDisconnected);
            yield return new WaitForSeconds(Mathf.Max(.2f, refreshInterval));
        }
    }
    private IEnumerator Fetch(bool useMockWhenDisconnected)
    {
        if (string.IsNullOrEmpty(url)) yield break;
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 4;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var report = JsonUtility.FromJson<SafeLiftReport>(request.downloadHandler.text);
                    if (report == null) yield break;
                    if (report.status == "ready" && !string.IsNullOrWhiteSpace(report.report_text))
                    {
                        hasRealReport = true;
                        lastStableReport = report;
                        ReportReceived?.Invoke(report);
                    }
                    else if (report.status == "generating")
                    {
                        hasRealReport = true;
                        ReportReceived?.Invoke(report);
                    }
                    else if (report.status == "none" && report.rep_id == 0)
                    {
                        lastStableReport = null;
                        hasRealReport = false;
                        ReportReceived?.Invoke(report);
                    }
                    else if (report.status == "none" && lastStableReport != null)
                    {
                        ReportReceived?.Invoke(lastStableReport);
                    }
                    else
                    {
                        ReportReceived?.Invoke(report);
                    }
                }
                catch (Exception ex) { RequestFailed?.Invoke(ex.Message); }
            }
            else if (useMockWhenDisconnected && !hasRealReport)
                ReportReceived?.Invoke(new SafeLiftReport { status = "ready", rep_id = 0, report_text = "Mock report: maintain a neutral torso and balanced foot pressure." });
            else RequestFailed?.Invoke(request.error);
        }
    }
}
