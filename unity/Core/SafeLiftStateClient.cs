using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class SafeLiftStateClient : MonoBehaviour
{
    [SerializeField] private string serverUrl = "http://127.0.0.1:8090";
    [SerializeField] private float refreshInterval = .2f;
    public event Action<SafeLiftState> StateReceived;
    public event Action<string> RequestFailed;
    public string ServerUrl { get => serverUrl; set => serverUrl = value; }
    public float RefreshInterval { get => refreshInterval; set => refreshInterval = value; }

    public IEnumerator Poll(bool useMockWhenDisconnected)
    {
        while (enabled)
        {
            yield return GetState(false);
            yield return new WaitForSeconds(Mathf.Max(.05f, refreshInterval));
        }
    }

    public IEnumerator GetState(bool useMockWhenDisconnected)
    {
        using (var request = UnityWebRequest.Get(BuildUrl("/api/session_state")))
        {
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                SafeLiftState state = null;
                try { state = JsonUtility.FromJson<SafeLiftState>(request.downloadHandler.text); } catch (Exception ex) { RequestFailed?.Invoke(ex.Message); }
                if (state != null) StateReceived?.Invoke(state);
            }
            else
            {
                var endpoint = BuildUrl("/api/session_state");
                var detail = "GET " + endpoint + " failed (" + request.responseCode + "): " + request.error;
                Debug.LogWarning("SafeLift backend: " + detail);
                RequestFailed?.Invoke(detail);
                Debug.LogWarning("SafeLift backend state unavailable; waiting for real backend data.");
            }
        }
    }

    public string BuildUrl(string path) => serverUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
