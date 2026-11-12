using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SafeLiftBreathingReportUI : MonoBehaviour
{
    private GameObject panel;
    private TMP_Text timerText;
    private TMP_Text statusText;
    private TMP_Text sourceText;
    private Image ringFill;
    private GameObject breathingAvatar;
    private float duration = 5f;
    private float startedAt;
    private bool hasExternalReportStatus;

    public void Attach(Transform parent)
    {
        panel = new GameObject("BreathingReportPanel");
        panel.transform.SetParent(parent, false);
        panel.SetActive(false);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(.05f, .08f, .12f, .98f);

        var rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360f, 360f);
        // Center the report card beneath the shifted report header.
        rect.anchoredPosition = new Vector2(-135f, -100f);

        Text("BreathingTitle", "BREATHE + REPORT", new Vector2(0f, 132f), 22f);
        Text("BreathingGuide", "Workout complete.\nBreathe slowly while the AI coach report is prepared.", new Vector2(0f, 88f), 14f);
        timerText = Text("BreathingTimer", "Report ready in 5 sec", new Vector2(0f, -122f), 15f);
        statusText = Text("ReportStatus", "Preparing score, phase snapshots, and AI report.", new Vector2(0f, -168f), 12f);
        sourceText = Text("ReportSource", "Source: waiting", new Vector2(0f, -210f), 10f);

        ringFill = GaugeRing(new Vector2(0f, 0f));
        breathingAvatar = CreateBreathingAvatar();
    }

    public void SetVisible(bool visible, float reportDuration)
    {
        if (panel == null) return;

        if (visible && !panel.activeSelf)
        {
            startedAt = Time.unscaledTime;
            duration = Mathf.Max(.1f, reportDuration);
            hasExternalReportStatus = false;
        }

        panel.SetActive(visible);
    }

    public void ApplyReportStatus(SafeLiftReport report)
    {
        if (report == null || panel == null) return;
        hasExternalReportStatus = true;

        var timeout = report.timeout_seconds > 0f ? report.timeout_seconds : 10f;
        var elapsed = Mathf.Max(0f, report.elapsed_seconds);
        var progress = Mathf.Clamp01(elapsed / Mathf.Max(.1f, timeout));
        if (ringFill != null) ringFill.fillAmount = progress;

        var status = string.IsNullOrWhiteSpace(report.status) ? "none" : report.status.ToLowerInvariant();
        var message = string.IsNullOrWhiteSpace(report.message) ? DefaultMessage(status, report.fallback, report.timed_out) : report.message;
        if (statusText != null) statusText.text = message;

        if (timerText != null)
        {
            if (status == "ready")
                timerText.text = report.fallback ? "Fallback report ready" : "AI report ready";
            else if (status == "timeout" || report.timed_out)
                timerText.text = "AI report delayed - showing basic analysis";
            else if (status == "generating")
                timerText.text = "Generating... " + elapsed.ToString("0.0") + " / " + timeout.ToString("0") + " sec";
            else
                timerText.text = "Waiting for report generation";
        }

        if (sourceText != null)
            sourceText.text = "Status: " + status.ToUpperInvariant()
                + " | Source: " + (string.IsNullOrWhiteSpace(report.source) ? "-" : report.source)
                + (report.fallback ? " | template fallback" : "");
    }

    private void Update()
    {
        if (panel == null || !panel.activeSelf) return;

        var elapsed = Time.unscaledTime - startedAt;
        var progress = Mathf.Clamp01(elapsed / duration);
        var remaining = Mathf.Max(0, Mathf.CeilToInt(duration - elapsed));

        if (ringFill != null) ringFill.fillAmount = progress;
        if (timerText != null && !hasExternalReportStatus)
            timerText.text = "Report ready in " + remaining + " sec";

        if (breathingAvatar != null)
        {
            var scale = 1f + Mathf.Sin(elapsed * Mathf.PI * .8f) * .08f;
            breathingAvatar.transform.localScale = new Vector3(42f, 62f, 26f) * scale;
            breathingAvatar.transform.localRotation = Quaternion.Euler(0f, elapsed * 12f, 0f);
        }
    }

    private static string DefaultMessage(string status, bool fallback, bool timedOut)
    {
        if (status == "ready") return fallback ? "Template fallback report is ready." : "Gemini AI report is ready.";
        if (status == "timeout" || timedOut) return "AI report is delayed. Review can use the rule-based summary first.";
        if (status == "generating") return "AI is generating the coach report. Keep breathing slowly.";
        return "Waiting for report generation to begin.";
    }

    private GameObject CreateBreathingAvatar()
    {
        var avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        avatar.name = "BreathingAvatarPlaceholder";
        avatar.transform.SetParent(panel.transform, false);
        avatar.transform.localPosition = new Vector3(0f, 10f, 0f);
        avatar.transform.localScale = new Vector3(42f, 62f, 26f);

        var renderer = avatar.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(.25f, .85f, .95f);

        return avatar;
    }

    private TMP_Text Text(string name, string value, Vector2 position, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(310f, 60f);
        rect.anchoredPosition = position;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        SafeLiftKoreanText.Apply(text);
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        return text;
    }

    private Image GaugeRing(Vector2 position)
    {
        var go = new GameObject("BreathingProgressRing");
        go.transform.SetParent(panel.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(190f, 190f);
        rect.anchoredPosition = position;

        var image = go.AddComponent<Image>();
        image.color = new Color(.25f, .8f, 1f, .45f);
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillAmount = 0f;
        return image;
    }
}
