using TMPro;
using UnityEngine;

public class SafeLiftUIBinder : MonoBehaviour
{
    [Header("Live")][SerializeField] private TMP_Text liveTrackingQualityText, phaseText, currentCueText;
    [Header("Sensors")][SerializeField] private TMP_Text sensorModeText, leftRightPressureText, imuText, corePressureText;
    [Header("Report")][SerializeField] private TMP_Text lastRepQualityText, reportSourceText, aiReportText;
    [SerializeField] private TMP_Text connectionStatusText;
    [SerializeField] private GameObject livePanel, sensorPanel, reportPanel, phaseCapturePanel;

    private bool suppressLegacyPanels;

    private void Awake()
    {
        suppressLegacyPanels = ShouldSuppressLegacyPanels();
        AutoWire();
        if (suppressLegacyPanels) HideLegacyPanels();
    }

    private void AutoWire()
    {
        liveTrackingQualityText = FindText("LiveTrackingQualityText"); phaseText = FindText("PhaseText"); currentCueText = FindText("CurrentCueText");
        sensorModeText = FindText("SensorModeText"); leftRightPressureText = FindText("LeftRightPressureText"); imuText = FindText("IMUText"); corePressureText = FindText("CorePressureText");
        lastRepQualityText = FindText("LastRepQualityText"); reportSourceText = FindText("ReportSourceText"); aiReportText = FindText("AIReportText"); connectionStatusText = FindText("ConnectionStatusText");
        livePanel = FindObject("LivePanel"); sensorPanel = FindObject("SensorPanel"); reportPanel = FindObject("ReportPanel"); phaseCapturePanel = FindObject("PhaseCapturePanel");
    }
    private TMP_Text FindText(string name) { var child = FindObject(name); return child == null ? null : child.GetComponent<TMP_Text>(); }
    private GameObject FindObject(string name)
    {
        foreach (var child in GetComponentsInChildren<Transform>(true)) if (child.name == name) return child.gameObject;
        return null;
    }

    public void BindState(SafeLiftState state)
    {
        if (state == null) return;
        if (ShouldSuppressLegacyPanels()) { HideLegacyPanels(); return; }
        Set(liveTrackingQualityText, $"Live Tracking: {ShortQuality(state.live_tracking_quality)}"); Set(phaseText, $"Phase: {state.phase}"); Set(currentCueText, state.current_cue ?? "No cue");
        Set(sensorModeText, $"Sensor Mode: {(state.sensor_mode == "MOCK" ? "MOCK SENSOR" : state.sensor_mode)}");
        if (state.sensors != null) { Set(leftRightPressureText, $"Pressure  L {state.sensors.left_pressure_ratio:P0} / R {state.sensors.right_pressure_ratio:P0}"); Set(imuText, $"IMU  Pitch {state.sensors.imu_pitch:0.0}°  Roll {state.sensors.imu_roll:0.0}°"); Set(corePressureText, $"Core Pressure: {state.sensors.core_pressure_ratio:P0}"); }
        Set(lastRepQualityText, $"Last Rep Quality: {ShortQuality(state.last_rep_quality)}"); Set(reportSourceText, $"Source: {state.report_source?.source ?? "-"}");
        var errors = state.detected_errors == null || state.detected_errors.Length == 0 ? "None" : string.Join(", ", state.detected_errors);
        if (reportPanel != null && reportPanel.activeSelf == false) Set(aiReportText, "No completed rep yet.");
    }
    public void BindReport(SafeLiftReport report)
    {
        if (report == null) return;
        if (ShouldSuppressLegacyPanels()) { HideLegacyPanels(); return; }
        var reportText = report.report_text ?? string.Empty;
        if (reportText.Length > 650) reportText = reportText.Substring(0, 650) + "\n...";
        Set(aiReportText, report.status == "generating" ? "AI Coach Report: generating..." : report.status == "ready" ? reportText : report.status == "none" ? "No completed rep yet." : "Report unavailable.");
        bool afterRep = report.status == "generating" || report.status == "ready";
        SetActive(reportPanel, afterRep); SetActive(phaseCapturePanel, afterRep); SetActive(livePanel, true); SetActive(sensorPanel, true);
        if (aiReportText != null) { aiReportText.enableWordWrapping = true; aiReportText.overflowMode = TextOverflowModes.Truncate; aiReportText.alignment = TextAlignmentOptions.TopLeft; aiReportText.fontSize = 16; }
    }
    public void SetConnection(string text) => Set(connectionStatusText, text);
    private static bool ShouldSuppressLegacyPanels()
    {
        return FindObjectOfType<SafeLiftUIStateMachine>() != null;
    }

    private void HideLegacyPanels()
    {
        suppressLegacyPanels = true;
        SetActive(livePanel, false);
        SetActive(sensorPanel, false);
        SetActive(reportPanel, false);
        SetActive(phaseCapturePanel, false);
    }
    private static void Set(TMP_Text text, string value) { if (text != null) text.text = value ?? string.Empty; }
    private static string ShortQuality(string value)
    {
        if (string.IsNullOrEmpty(value)) return "UNKNOWN";
        if (value.IndexOf("WEAK", System.StringComparison.OrdinalIgnoreCase) >= 0) return "WEAK";
        if (value.IndexOf("GOOD", System.StringComparison.OrdinalIgnoreCase) >= 0) return "GOOD";
        if (value.IndexOf("BAD", System.StringComparison.OrdinalIgnoreCase) >= 0) return "BAD";
        if (value.IndexOf("NONE", System.StringComparison.OrdinalIgnoreCase) >= 0) return "NONE";
        int marker = value.IndexOf("QUALITY_LEVEL", System.StringComparison.OrdinalIgnoreCase);
        if (marker < 0) marker = value.IndexOf("OVERALL_QUALITY", System.StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            int colon = value.IndexOf(':', marker); if (colon >= 0) value = value.Substring(colon + 1).Trim(' ', '\'', '"', '{', '}');
        }
        return value.Length > 18 ? value.Substring(0, 18) : value;
    }
    private static void SetActive(GameObject target, bool value) { if (target != null) target.SetActive(value); }
}
