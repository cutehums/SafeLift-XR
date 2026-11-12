using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LiveFrameReceiver : MonoBehaviour
{
    [SerializeField] private RawImage target;
    [SerializeField] private float refreshInterval = .15f;
    private Texture2D currentTexture;
    private string url;
    public float RefreshInterval { get => refreshInterval; set => refreshInterval = value; }
    public void Configure(string serverUrl, RawImage image) { url = serverUrl.TrimEnd('/') + "/api/live_frame.jpg"; target = image; }
    public IEnumerator Poll(bool usePlaceholderWhenDisconnected)
    {
        while (enabled)
        {
            yield return DownloadFrame(usePlaceholderWhenDisconnected);
            yield return new WaitForSeconds(Mathf.Max(.05f, refreshInterval));
        }
    }
    private IEnumerator DownloadFrame(bool keepPreviousFrame)
    {
        if (target == null || string.IsNullOrEmpty(url)) yield break;
        using (var request = UnityWebRequestTexture.GetTexture(url, false))
        {
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { yield break; }
            var next = DownloadHandlerTexture.GetContent(request);
            if (next == null) yield break;
            if (currentTexture != null) Destroy(currentTexture);
            currentTexture = next;
            target.texture = currentTexture;
        }
    }
    private void OnDestroy() { if (currentTexture != null) Destroy(currentTexture); }
}
