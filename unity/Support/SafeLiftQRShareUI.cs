using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class SafeLiftQRShareUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class QrLinkResponse
    {
        public string url;
        public string qr_url;
    }

    private GameObject panel;
    private TMP_Text linkText;
    private Coroutine loadRoutine;

    public string ReportUrl = "http://127.0.0.1:8090/report/latest";
    public string LaptopQrUrl = "http://127.0.0.1:8090/qr";
    public string BackendUrl = "http://127.0.0.1:8090";

    public void ConfigureBackendUrl(string backendUrl)
    {
        if (string.IsNullOrWhiteSpace(backendUrl)) return;
        BackendUrl = backendUrl.TrimEnd('/');
        LaptopQrUrl = BackendUrl + "/qr";
        ReportUrl = BackendUrl + "/report/latest";
        RefreshLinkText();

        if (panel == null) return;
        if (loadRoutine != null) StopCoroutine(loadRoutine);
        loadRoutine = StartCoroutine(LoadBackendQrLink());
    }

    public void Attach(Transform parent)
    {
        panel = new GameObject("QRSharePanel");
        panel.transform.SetParent(parent, false);
        panel.SetActive(false);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(.03f, .08f, .12f, 1f);

        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(430, 330);
        rt.anchoredPosition = new Vector2(0, -80);

        Text("LaptopQrTitle", "QR은 노트북 브라우저에 표시됩니다", new Vector2(0, 86), 18);
        Text("QRInstruction", "HMD를 벗고 노트북 화면의 QR을 스캔하세요.\nVR 화면에는 QR 이미지를 직접 띄우지 않습니다.", new Vector2(0, 24), 13);
        linkText = Text("QRLink", LaptopQrUrl, new Vector2(0, -72), 11);
        Text("QRReportHint", "브라우저 주소: /qr\n스캔 후 AI report, score, SET 비교를 확인합니다.", new Vector2(0, -136), 12);

        loadRoutine = StartCoroutine(LoadBackendQrLink());
    }

    public void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    private IEnumerator LoadBackendQrLink()
    {
        using (var request = UnityWebRequest.Get(BackendUrl.TrimEnd('/') + "/api/qr_link"))
        {
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var response = JsonUtility.FromJson<QrLinkResponse>(request.downloadHandler.text);
            if (response == null) yield break;

            if (!string.IsNullOrWhiteSpace(response.url)) ReportUrl = response.url;
            LaptopQrUrl = !string.IsNullOrWhiteSpace(response.qr_url)
                ? response.qr_url
                : BackendUrl.TrimEnd('/') + "/qr";

            RefreshLinkText();
        }
    }

    private void RefreshLinkText()
    {
        if (linkText != null)
            linkText.text = LaptopQrUrl + "\nReport: " + ReportUrl;
    }

    private TMP_Text Text(string name, string value, Vector2 pos, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var r = go.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(360, 60);
        r.anchoredPosition = pos;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = value;
        SafeLiftKoreanText.Apply(t);
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = true;
        return t;
    }
}
