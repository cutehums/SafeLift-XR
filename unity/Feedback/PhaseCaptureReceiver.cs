using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PhaseCaptureReceiver : MonoBehaviour
{
    [SerializeField] private RawImage standingImage, descentImage, bottomImage, ascentImage;
    [SerializeField] private float refreshInterval = 1f;
    private string baseUrl;
    private readonly Texture2D[] textures = new Texture2D[4];
    public float RefreshInterval { get => refreshInterval; set => refreshInterval = value; }
    private void Awake()
    {
        standingImage = FindImage("StandingImage"); descentImage = FindImage("DescentImage"); bottomImage = FindImage("BottomImage"); ascentImage = FindImage("AscentImage");
    }
    private RawImage FindImage(string name) { foreach (var child in GetComponentsInChildren<Transform>(true)) if (child.name == name) return child.GetComponent<RawImage>(); return null; }
    public void Configure(string serverUrl) => baseUrl = serverUrl.TrimEnd('/') + "/api/phase_capture/";
    public IEnumerator Poll()
    {
        while (enabled)
        {
            yield return Download(0, "standing", standingImage); yield return Download(1, "descent", descentImage);
            yield return Download(2, "bottom", bottomImage); yield return Download(3, "ascent", ascentImage);
            yield return new WaitForSeconds(Mathf.Max(.5f, refreshInterval));
        }
    }
    private IEnumerator Download(int index, string phase, RawImage target)
    {
        if (target == null || string.IsNullOrEmpty(baseUrl)) yield break;
        using (var request = UnityWebRequestTexture.GetTexture(baseUrl + phase + ".jpg", false))
        {
            request.timeout = 3; yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var next = DownloadHandlerTexture.GetContent(request); if (next == null) yield break;
            if (textures[index] != null) Destroy(textures[index]); textures[index] = next; target.texture = next;
        }
    }
    private void OnDestroy() { foreach (var texture in textures) if (texture != null) Destroy(texture); }
}
