using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SafeLiftLiveFeedbackUI : MonoBehaviour
{
    public SafeLiftDemoScenario Scenario { get; set; } = SafeLiftDemoScenario.NoScenario;

    private GameObject panel;
    private TMP_Text cueText;
    private TMP_Text sensorText;
    private TMP_Text ratioText;
    private TMP_Text imuText;
    private TMP_Text errorsText;
    private Image coreFill;
    private Image torsoFill;
    private readonly Image[] pressureBlobs = new Image[6];
    private SafeLiftXRHapticsManager haptics;
    private SafeLiftState externalState;
    private float nextCuePulse;
    private int hapticRepIndex = -1;
    private int hapticsThisRep;
    private string lastHapticIssue = "";
    private float elapsed;
    private const float CueHapticCooldownSeconds = 3.5f;
    private const int MaxCueHapticsPerRep = 2;

    public void Attach(Transform parent)
    {
        haptics = GetComponent<SafeLiftXRHapticsManager>();

        panel = new GameObject("Set2LiveFeedbackPanel");
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        panel.SetActive(false);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(.02f, .12f, .16f, .98f);

        // Keep cue/pressure and sensor/graph content in separate bounded columns.
        // The section backgrounds are inserted behind all labels and gauges.
        Section("LiveCueSection", new Vector2(-250f, 0f), new Vector2(480f, 220f), new Color(.025f, .07f, .10f, .98f));
        Section("LiveSensorSection", new Vector2(260f, 0f), new Vector2(480f, 220f), new Color(.035f, .055f, .11f, .98f));

        var panelRect = panel.GetComponent<RectTransform>();
        // Live feedback is a right-side information column inside the main card.
        // The previous 190px panel clipped its lower gauges and appeared centered.
        panelRect.sizeDelta = new Vector2(1100f, 280f);
        panelRect.anchoredPosition = new Vector2(0f, -180f);

        cueText = Text("CurrentCue", "CUE: Keep your torso stable", new Vector2(-250f, 45f), 19f);
        sensorText = Text("Sensor", "MOCK SENSOR - FSR + IMU + AIR", new Vector2(260f, 45f), 17f);
        ratioText = Text("PressureRatio", "Foot pressure  L 58% / R 42%", new Vector2(-250f, 0f), 17f);
        imuText = Text("IMU", "IMU pitch +2.1 deg  roll -1.4 deg - STABLE", new Vector2(260f, 0f), 17f);
        errorsText = Text("Errors", "Detected: None", new Vector2(0f, -55f), 17f);

        coreFill = Gauge("CoreBracingGauge", new Vector2(-250f, -38f), Color.cyan);
        torsoFill = Gauge("TorsoStabilityGauge", new Vector2(260f, -38f), Color.green);

        var labels = new[] { "L forefoot", "L mid", "L heel", "R forefoot", "R mid", "R heel" };
        for (var i = 0; i < pressureBlobs.Length; i++)
        {
            pressureBlobs[i] = Blob(labels[i], new Vector2(i < 3 ? -320f + i * 55f : 140f + (i - 3) * 55f, -78f));
        }
    }

    public void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    public void ApplyState(SafeLiftState state)
    {
        externalState = state;
    }

    public void ClearExternalState()
    {
        externalState = null;
        hapticRepIndex = -1;
        hapticsThisRep = 0;
        lastHapticIssue = "";
        nextCuePulse = 0f;
    }

    private void Update()
    {
        if (panel == null || !panel.activeSelf) return;

        elapsed += Time.unscaledDeltaTime;

        if (externalState != null)
        {
            RenderExternalState();
            return;
        }

        RenderMockState();
    }

    private void RenderMockState()
    {
        var left = .58f + .06f * Mathf.Sin(elapsed);
        var core = .72f + .08f * Mathf.Sin(elapsed * 1.4f);
        var torso = .82f + .07f * Mathf.Cos(elapsed * 1.1f);
        var cue = "CUE: Keep your torso stable";
        var errors = "Detected: None";

        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus:
                cue = "CUE: Push knees outward over toes";
                errors = "Detected: KneeValgus";
                torso -= .03f;
                break;
            case SafeLiftDemoScenario.WeightShift:
                left = .70f + .04f * Mathf.Sin(elapsed);
                cue = "CUE: Move pressure back to midfoot";
                errors = "Detected: WeightShiftLeft";
                break;
            case SafeLiftDemoScenario.CorePressureDrop:
                core = .48f + .08f * Mathf.Sin(elapsed);
                cue = "CUE: Re-brace before ascent";
                errors = "Detected: CorePressureDrop";
                break;
            case SafeLiftDemoScenario.ComplexError:
                left = .68f;
                core = .48f;
                torso = .68f;
                cue = "CUE: Slow down and stabilize";
                errors = "Detected: KneeValgus, TorsoLean, CorePressureDrop";
                break;
        }

        cueText.text = cue;
        ratioText.text = $"Foot pressure  L {left:P0} / R {(1f - left):P0} - {(left > .63f ? "LEFT SHIFT WARNING" : "BALANCED")}";
        errorsText.text = errors;
        coreFill.fillAmount = core;
        torsoFill.fillAmount = torso;
        imuText.text = $"IMU pitch {Mathf.Sin(elapsed) * 3f:+0.0;-0.0} deg  roll {Mathf.Cos(elapsed) * 2f:+0.0;-0.0} deg - {(torso < .76f ? "LEAN WARNING" : "STABLE")}";

        if (Time.unscaledTime >= nextCuePulse && haptics != null)
        {
            if (left > .63f)
            {
                haptics.WeightShiftLeft();
                nextCuePulse = Time.unscaledTime + 3f;
            }
            else if (core < .68f)
            {
                haptics.CorePressureDrop();
                nextCuePulse = Time.unscaledTime + 3f;
            }
        }

        UpdatePressureBlobs(left);
    }

    private void RenderExternalState()
    {
        var sensors = externalState.sensors ?? new SafeLiftSensors();
        var left = Mathf.Clamp01(sensors.left_pressure_ratio);
        var core = Mathf.Clamp01(sensors.core_pressure_ratio);
        var torsoAngle = externalState.metrics == null ? 0f : externalState.metrics.torso_angle;
        var torso = Mathf.Clamp01(1f - Mathf.Abs(torsoAngle) / 30f);

        var contract = externalState.live_feedback;
        var cue = contract != null && !string.IsNullOrWhiteSpace(contract.cue) ? contract.cue : externalState.current_cue;
        var issue = ResolveIssueForDisplay(externalState);
        var area = contract != null ? contract.body_area : externalState.live_feedback_body_area;
        var severity = contract != null ? contract.severity : externalState.live_feedback_severity;
        var reason = contract != null ? contract.reason : externalState.live_feedback_reason;
        cueText.text = string.IsNullOrEmpty(cue) ? "CUE: No active cue" : $"CUE: {cue}";
        sensorText.text = "REAL SENSOR - " + (externalState.sensor_mode ?? "UNKNOWN");
        ratioText.text = $"Foot pressure  L {left:P0} / R {sensors.right_pressure_ratio:P0}";
        imuText.text = $"IMU pitch {sensors.imu_pitch:+0.0;-0.0} deg  roll {sensors.imu_roll:+0.0;-0.0} deg - {(torso < .76f ? "LEAN WARNING" : "STABLE")}";
        coreFill.fillAmount = core;
        torsoFill.fillAmount = torso;
        errorsText.text = $"Issue: {issue} | Area: {area ?? "none"} | Severity: {severity ?? "none"} | Reason: {(string.IsNullOrWhiteSpace(reason) ? "-" : reason)}";

        MaybeTriggerExternalHaptic(externalState, left);
        UpdatePressureBlobs(left);
    }

    private void MaybeTriggerExternalHaptic(SafeLiftState state, float leftPressureRatio)
    {
        if (haptics == null || state == null) return;

        var repIndex = Mathf.Max(0, state.rep_count);
        if (repIndex != hapticRepIndex)
        {
            hapticRepIndex = repIndex;
            hapticsThisRep = 0;
            lastHapticIssue = "";
        }

        if (hapticsThisRep >= MaxCueHapticsPerRep) return;
        if (Time.unscaledTime < nextCuePulse) return;

        var issue = ResolveIssueForHaptics(state);
        if (string.IsNullOrWhiteSpace(issue)) return;
        if (string.Equals(issue, lastHapticIssue, System.StringComparison.OrdinalIgnoreCase) && hapticsThisRep > 0) return;

        var channel = state.live_feedback != null ? state.live_feedback.haptic_channel : state.live_feedback_haptic_channel;
        TriggerContractHaptic(channel, issue, leftPressureRatio);
        hapticsThisRep++;
        lastHapticIssue = issue;
        nextCuePulse = Time.unscaledTime + CueHapticCooldownSeconds;
    }

    private void TriggerContractHaptic(string channel, string issue, float leftPressureRatio)
    {
        switch ((channel ?? "").ToLowerInvariant())
        {
            case "left":
                haptics.WeightShiftLeft();
                return;
            case "right":
                haptics.WeightShiftRight();
                return;
            case "double":
                haptics.CorePressureDrop();
                return;
            case "warning":
                haptics.TrackingBad();
                return;
            default:
                haptics.LiveCue(issue, leftPressureRatio);
                return;
        }
    }

    private static string ResolveIssueForDisplay(SafeLiftState state)
    {
        if (state == null) return "None";
        if (state.live_feedback != null && !string.IsNullOrWhiteSpace(state.live_feedback.issue))
            return state.live_feedback.issue;
        if (!string.IsNullOrWhiteSpace(state.live_feedback_issue))
            return state.live_feedback_issue;
        return ResolveIssueForHaptics(state);
    }

    private static string ResolveIssueForHaptics(SafeLiftState state)
    {
        if (state.live_feedback != null && !string.IsNullOrWhiteSpace(state.live_feedback.issue) && !string.Equals(state.live_feedback.issue, "None", System.StringComparison.OrdinalIgnoreCase))
            return state.live_feedback.issue;
        if (!string.IsNullOrWhiteSpace(state.live_feedback_issue) && !string.Equals(state.live_feedback_issue, "None", System.StringComparison.OrdinalIgnoreCase))
            return state.live_feedback_issue;
        if (!string.IsNullOrWhiteSpace(state.main_issue) && !string.Equals(state.main_issue, "None", System.StringComparison.OrdinalIgnoreCase))
            return state.main_issue;
        if (state.detected_errors != null && state.detected_errors.Length > 0)
            return state.detected_errors[0];
        return state.current_cue;
    }

    private void UpdatePressureBlobs(float leftPressure)
    {
        for (var i = 0; i < pressureBlobs.Length; i++)
        {
            var sidePressure = i < 3 ? leftPressure : 1f - leftPressure;
            var pulse = .8f + .2f * Mathf.Abs(Mathf.Sin(elapsed * 1.5f + i));
            var value = Mathf.Clamp01(sidePressure * pulse);
            pressureBlobs[i].rectTransform.sizeDelta = new Vector2(18f + 22f * value, 18f + 22f * value);
            pressureBlobs[i].color = Color.Lerp(Color.green, Color.red, value);
        }
    }

    private TMP_Text Text(string name, string value, Vector2 position, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400f, 32f);
        rect.anchoredPosition = position;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        SafeLiftKoreanText.Apply(text);
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    private Image Section(string name, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var image = go.AddComponent<Image>();
        image.color = color;
        go.transform.SetAsFirstSibling();
        return image;
    }

    private Image Gauge(string name, Vector2 position, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260f, 12f);
        rect.anchoredPosition = position;

        var background = go.AddComponent<Image>();
        background.color = Color.gray;

        var fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(go.transform, false);

        var fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        var fill = fillObject.AddComponent<Image>();
        fill.color = color;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        return fill;
    }

    private Image Blob(string name, Vector2 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(24f, 24f);
        rect.anchoredPosition = position;

        var image = go.AddComponent<Image>();
        image.color = Color.green;
        return image;
    }
}
