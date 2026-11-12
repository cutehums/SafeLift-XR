using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Real-data feedback panel for SET 2. It deliberately has no mock update
/// loop: when the backend has no usable state it shows NO SIGNAL instead of
/// inventing sensor values.
/// </summary>
public sealed class SafeLiftRealtimeFeedbackPanel : MonoBehaviour
{
    private Image panel;
    private TMP_Text mainText;
    private TMP_Text phaseText;
    private TMP_Text pressureText;
    private TMP_Text pressureStatus;
    private Image pressureGaugeBackground;
    private Image pressureGauge;
    private TMP_Text leftBalance;
    private TMP_Text rightBalance;
    private TMP_Text fsrDetails;
    private TMP_Text liveScoreDetails;
    private TMP_Text deductionEvent;
    private float previousFsrDeduction;
    private float previousBracingDeduction;
    private float previousSpineDeduction;
    private Image leftGauge;
    private Image rightGauge;
    private Image[] footSensorGauges;
    private Image[] footSensorGaugeBackgrounds;
    private TMP_Text[] footSensorGaugeTexts;
    private TMP_Text leftFootLabel;
    private TMP_Text rightFootLabel;
    private TMP_Text spineText;
    private TMP_Text spineStatus;
    private string stablePhaseText = "";
    private string phaseCandidateText = "";
    private float phaseCandidateSince;
    private string stableSpineText = "";
    private string spineCandidateText = "";
    private float spineCandidateSince;
    private bool visible;
    private const float SensorTimeoutSeconds = 1.5f;
    private const float TextStabilitySeconds = .30f;

    public void Attach(Transform parent)
    {
        var root = new GameObject("RealtimeFeedbackPanel");
        root.transform.SetParent(parent, false);
        var rect = root.AddComponent<RectTransform>();
        // Compact the card so its top aligns with the avatar/webcam row and
        // the unused lower space does not cover the SET2 header.
        rect.sizeDelta = new Vector2(500f, 600f);
        rect.anchoredPosition = new Vector2(110f, -18f);

        panel = root.AddComponent<Image>();
        panel.color = new Color(.025f, .045f, .075f, .98f);

        Text(root.transform, "FeedbackTitle", "실시간 자세 피드백", 28f, new Vector2(0f, 330f), new Vector2(460f, 42f), out _).fontStyle = FontStyles.Bold;
        mainText = Text(root.transform, "MainFeedbackText", "NO SIGNAL", 22f, new Vector2(0f, 275f), new Vector2(450f, 72f), out _);
        mainText.alignment = TextAlignmentOptions.Center;
        phaseText = Text(root.transform, "SquatPhaseText", "State: --", 16f, new Vector2(0f, 235f), new Vector2(440f, 28f), out _);
        phaseText.alignment = TextAlignmentOptions.Center;

        Text(root.transform, "PressureHeader", "복압", 18f, new Vector2(-140f, 205f), new Vector2(190f, 30f), out _);
        pressureGaugeBackground = Gauge(root.transform, "PressureGaugeBackground", new Vector2(-140f, 125f), Color.white, true);
        pressureGaugeBackground.fillAmount = 1f;
        pressureGauge = Gauge(root.transform, "PressureGauge", new Vector2(-140f, 125f), new Color(.25f, .9f, .45f), true);
        pressureText = Text(root.transform, "PressurePercentText", "--", 22f, new Vector2(-140f, 125f), new Vector2(130f, 32f), out _);
        pressureText.alignment = TextAlignmentOptions.Center;
        pressureText.color = Color.black;
        pressureStatus = Text(root.transform, "PressureInstructionText", "NO SIGNAL", 15f, new Vector2(-140f, 45f), new Vector2(270f, 48f), out _);
        pressureStatus.alignment = TextAlignmentOptions.Center;

        Text(root.transform, "BalanceHeader", "좌우 균형", 18f, new Vector2(145f, 205f), new Vector2(190f, 30f), out _);
        leftBalance = Text(root.transform, "LeftFootRow", "왼발 --", 16f, new Vector2(135f, 145f), new Vector2(250f, 28f), out _);
        rightBalance = Text(root.transform, "RightFootRow", "오른발 --", 16f, new Vector2(135f, 95f), new Vector2(250f, 28f), out _);
        leftGauge = Gauge(root.transform, "LeftFootGauge", new Vector2(145f, 120f), new Color(.2f, .8f, .45f), false);
        rightGauge = Gauge(root.transform, "RightFootGauge", new Vector2(145f, 70f), new Color(.2f, .8f, .45f), false);
        leftBalance.gameObject.SetActive(false);
        rightBalance.gameObject.SetActive(false);
        leftGauge.gameObject.SetActive(false);
        rightGauge.gameObject.SetActive(false);
        footSensorGauges = new Image[6];
        footSensorGaugeBackgrounds = new Image[6];
        footSensorGaugeTexts = new TMP_Text[6];
        CreateFootMap(root.transform, "LeftFootMap", new Vector2(75f, 115f), 0, out leftFootLabel);
        CreateFootMap(root.transform, "RightFootMap", new Vector2(225f, 115f), 3, out rightFootLabel);

        // Final HMD layout adjustments. Keep the pressure circle and the six
        // sensor gauges unchanged; move only the surrounding content.
        SetUiPosition(root.transform, "LeftFootMap", new Vector2(75f, 95f));
        SetUiPosition(root.transform, "RightFootMap", new Vector2(225f, 95f));
        SetUiText(root.transform, "PressureHeader", "\uBCF5\uC555");
        SetUiText(root.transform, "BalanceHeader", "좌우 밸런싱 / 뒤꿈치 들림");
        SetUiPosition(root.transform, "SpineHeader", new Vector2(0f, -75f));
        SetUiPosition(root.transform, "SpineAngleText", new Vector2(0f, -135f));
        SetUiPosition(root.transform, "SpineInstructionText", new Vector2(0f, -195f));
        SetUiPosition(root.transform, "SquatPhaseText", new Vector2(0f, -125f));

        Text(root.transform, "SpineHeader", "척추 정렬", 18f, new Vector2(0f, -5f), new Vector2(420f, 30f), out _);
        spineText = Text(root.transform, "SpineAngleText", "DeltaTheta: --", 18f, new Vector2(0f, -65f), new Vector2(420f, 32f), out _);
        spineText.alignment = TextAlignmentOptions.Center;
        spineStatus = Text(root.transform, "SpineInstructionText", "NO SIGNAL", 16f, new Vector2(0f, -125f), new Vector2(440f, 48f), out _);
        spineStatus.alignment = TextAlignmentOptions.Center;
        spineText.gameObject.SetActive(false);
        spineStatus.gameObject.SetActive(false);
        fsrDetails = Text(root.transform, "FsrDetailsText", "FSR L/R --", 13f, new Vector2(0f, -220f), new Vector2(460f, 86f), out _);
        fsrDetails.alignment = TextAlignmentOptions.TopLeft;
        fsrDetails.enableWordWrapping = true;
    liveScoreDetails = Text(root.transform, "LiveScoreDetailsText", "REP SCORE: --", 13f, new Vector2(0f, -305f), new Vector2(460f, 70f), out _);
        liveScoreDetails.alignment = TextAlignmentOptions.TopLeft;
        liveScoreDetails.enableWordWrapping = true;
        deductionEvent = Text(root.transform, "DeductionEventText", "REP DEDUCTIONS: --", 13f, new Vector2(0f, -365f), new Vector2(460f, 38f), out _);
        deductionEvent.alignment = TextAlignmentOptions.TopLeft;
        deductionEvent.color = new Color(1f, .72f, .2f, 1f);

        // These lower diagnostic blocks were useful during sensor integration,
        // but are intentionally hidden in the user-facing SET2 card.
        fsrDetails.gameObject.SetActive(false);
        liveScoreDetails.gameObject.SetActive(false);
        deductionEvent.gameObject.SetActive(false);

        SetVisible(false);
        ClearState();
    }

    public void SetVisible(bool state)
    {
        visible = state;
        if (panel != null) panel.gameObject.SetActive(state);
    }

    public void ClearState()
    {
        if (mainText == null) return;
        mainText.text = "NO SIGNAL";
        mainText.color = Color.gray;
        phaseText.text = "State: --";
        phaseText.color = new Color(.25f, .9f, .45f, 1f);
        phaseText.fontStyle = FontStyles.Bold;
        stablePhaseText = "State: --";
        phaseCandidateText = "";
        phaseCandidateSince = 0f;
        pressureGaugeBackground.fillAmount = 1f;
        pressureGauge.fillAmount = 0f;
        pressureText.text = "--";
        pressureStatus.text = "센서 데이터 대기 중";
        leftBalance.text = "왼발 --";
        rightBalance.text = "오른발 --";
        leftGauge.fillAmount = 0f;
        rightGauge.fillAmount = 0f;
        if (footSensorGauges != null)
            foreach (var gauge in footSensorGauges)
                if (gauge != null) { gauge.fillAmount = 0f; gauge.color = Color.gray; }
        if (footSensorGaugeTexts != null)
            foreach (var text in footSensorGaugeTexts)
                if (text != null) text.text = "--";
        if (leftFootLabel != null) leftFootLabel.text = "LEFT FOOT\n--";
        if (rightFootLabel != null) rightFootLabel.text = "RIGHT FOOT\n--";
        fsrDetails.text = "FSR TOTAL L/R: -- / --\nFSR PERCENT L/R: -- / --\nIMBALANCE: -- | HEEL: --";
        liveScoreDetails.text = "REP SCORE: --";
        deductionEvent.text = "REP DEDUCTIONS: --";
        previousFsrDeduction = 0f;
        previousBracingDeduction = 0f;
        previousSpineDeduction = 0f;
        spineText.text = "DeltaTheta: --";
        stableSpineText = "";
        spineCandidateText = "";
        spineCandidateSince = 0f;
        spineStatus.text = "";
        spineStatus.color = new Color(.25f, .9f, .45f, 1f);
        spineStatus.fontStyle = FontStyles.Bold;
        spineStatus.text = "";
        spineStatus.gameObject.SetActive(false);
    }

    public void ApplyState(SafeLiftState state)
    {
        if (state == null || state.sensors == null || state.metrics == null)
        {
            ClearState();
            return;
        }

        var left = Mathf.Clamp01(state.sensors.left_pressure_ratio);
        var right = Mathf.Clamp01(state.sensors.right_pressure_ratio);
        var core = Mathf.Clamp01(state.sensors.core_pressure_ratio);
        var imbalance = Mathf.Abs(left - right) * 100f;
        var pressureLevel = Level(core >= .68f ? 0f : core >= .55f ? 8f : 11f);
        var balanceLevel = FsrDisplayLevel(null, imbalance);
        // IMU phase and Butt Wink are owned by the Fusion Monitor packet.
        // Do not recreate legacy angle-based reactions from backend metrics.
        var spineLevel = "NORMAL";

        mainText.text = ResolveMainMessage(state, pressureLevel, balanceLevel, spineLevel);
        mainText.color = LevelColor(HighestLevel(pressureLevel, balanceLevel, spineLevel));
        pressureGauge.fillAmount = core;
        pressureText.text = Mathf.RoundToInt(core * 100f) + "%";
        pressureStatus.text = pressureLevel == "NORMAL" ? "복압이 안정적이에요" : pressureLevel == "WARNING" ? "복압 유지에 주의하세요" : "복압 저하로 감점 위험";
        pressureStatus.color = LevelColor(pressureLevel);
        leftBalance.text = "왼발 " + Mathf.RoundToInt(left * 100f) + "%";
        rightBalance.text = "오른발 " + Mathf.RoundToInt(right * 100f) + "%";
        leftGauge.fillAmount = Mathf.Clamp01(left * 2f);
        fsrDetails.text = "FSR TOTAL L/R: -- / --\nFSR PERCENT L/R: "
            + Mathf.RoundToInt(left * 100f) + "% / " + Mathf.RoundToInt(right * 100f) + "%\n"
            + "IMBALANCE: " + imbalance.ToString("0.0") + "% | HEEL: --";
        rightGauge.fillAmount = Mathf.Clamp01(right * 2f);
        var leftOverloaded = left > right;
        leftGauge.color = leftOverloaded ? LevelColor(balanceLevel) : LevelColor("NORMAL");
        rightGauge.color = leftOverloaded ? LevelColor("NORMAL") : LevelColor(balanceLevel);
        spineStatus.text = "";
        spineStatus.color = new Color(.25f, .9f, .45f, 1f);
        spineStatus.fontStyle = FontStyles.Bold;
        spineStatus.gameObject.SetActive(false);
    }

    public void ApplyCompletedRepScore(SafeLiftScoreResult score, string setLabel, int repNumber)
    {
        if (liveScoreDetails == null) return;
        if (score == null)
        {
            liveScoreDetails.text = "REP SCORE: --";
            deductionEvent.text = "REP DEDUCTIONS: --";
            return;
        }

        var fsrDeduction = score.fsr != null ? Mathf.Max(0f, 35f - score.fsr.score) : 35f;
        var bracingDeduction = score.bracing != null ? Mathf.Max(0f, 35f - score.bracing.score) : 35f;
        var spineDeduction = score.spine != null ? Mathf.Max(0f, 30f - score.spine.score) : 30f;
        liveScoreDetails.text = setLabel + " REP " + repNumber + " SCORE: " + score.totalScore.ToString("0.0") + " / 100\n"
            + "FSR " + score.fsr.score.ToString("0.0") + "/35 | BRACING " + score.bracing.score.ToString("0.0") + "/35 | SPINE " + score.spine.score.ToString("0.0") + "/30\n"
            + "DEDUCTIONS: FSR -" + fsrDeduction.ToString("0.0")
            + " | BRACING -" + bracingDeduction.ToString("0.0")
            + " | SPINE -" + spineDeduction.ToString("0.0") + "\n"
            + "WHY: " + (score.fsr != null ? score.fsr.balanceStatus.ToString() : "FSR NO_SIGNAL")
            + " / " + (score.bracing != null ? score.bracing.status.ToString() : "BRACING NO_SIGNAL")
            + " / " + (score.spine != null ? score.spine.status.ToString() : "SPINE NO_SIGNAL");

        var eventText = string.Empty;
        if (score.fsr != null && score.fsr.balanceStatus != SafeLiftScoreStatus.NO_SIGNAL && fsrDeduction > previousFsrDeduction + .1f)
            eventText = "FSR -" + (fsrDeduction - previousFsrDeduction).ToString("0.0") + " / imbalance or heel pressure";
        else if (score.bracing != null && score.bracing.status != SafeLiftScoreStatus.NO_SIGNAL && bracingDeduction > previousBracingDeduction + .1f)
            eventText = "BRACING -" + (bracingDeduction - previousBracingDeduction).ToString("0.0") + " / pressure loss";
        else if (score.spine != null && score.spine.status != SafeLiftScoreStatus.NO_SIGNAL && spineDeduction > previousSpineDeduction + .1f)
            eventText = "SPINE -" + (spineDeduction - previousSpineDeduction).ToString("0.0") + " / butt wink or posture";
        deductionEvent.text = string.IsNullOrEmpty(eventText) ? setLabel + " REP " + repNumber + " DEDUCTIONS: --" : setLabel + " REP " + repNumber + " DEDUCTION: " + eventText;
        previousFsrDeduction = fsrDeduction;
        previousBracingDeduction = bracingDeduction;
        previousSpineDeduction = spineDeduction;
    }

    /// <summary>
    /// Merges the direct SquatDetection package stream with the backend state.
    /// Pressure and FSR are taken from the detector packet because that is the
    /// freshest hardware-derived value; phase/cue/spine remain backend-owned.
    /// </summary>
    public void ApplySensorSnapshot(SafeLiftSquatSensorUdpReceiver.Snapshot snapshot, float ageSeconds, float phaseAgeSeconds, SafeLiftState state, SafeLiftScoreResult unityScore)
    {
        if (snapshot == null || ageSeconds > SensorTimeoutSeconds)
        {
            ApplyState(state);
            if (snapshot == null || ageSeconds > SensorTimeoutSeconds)
                SetNoSignalIfBackendHasNoSensor(state);
            return;
        }

        var fsr = snapshot.fsr;
        var pressure = snapshot.pressure;
        var spine = snapshot.spine;
        var left = fsr != null ? Mathf.Clamp01(fsr.leftPercent / 100f) : 0f;
        var right = fsr != null ? Mathf.Clamp01(fsr.rightPercent / 100f) : 0f;
        var imbalance = fsr != null ? Mathf.Abs(fsr.imbalance) : Mathf.Abs(left - right) * 100f;
        var dropRatio = pressure != null ? Mathf.Max(0f, pressure.dropRatio) : 0f;
        var scoringPhase = phaseAgeSeconds <= SensorTimeoutSeconds && IsScoringPhase(snapshot.squatPhase);
        var fsrResult = unityScore != null ? unityScore.fsr : null;
        // Match the score controller exactly: FSR starts accumulating only
        // above 20% imbalance or below 20% heel ratio, and only in an active
        // Fusion phase.
        var heelError = fsr != null && (fsr.leftHeelRatio < 20f || fsr.rightHeelRatio < 20f);
        var balanceLevel = scoringPhase && (imbalance > 20f || heelError) ? "DANGER" : "NORMAL";

        if (state != null && state.metrics != null)
            ApplyState(state);

        var displayPhase = snapshot.squatPhase;
        if (string.Equals(displayPhase, "BOTTOM", System.StringComparison.OrdinalIgnoreCase))
            displayPhase = "DESCENDING";
        var stateLabel = "State: " + (scoringPhase ? displayPhase : "Standing");
        SetStableText(phaseText, stateLabel, new Color(.25f, .9f, .45f, 1f), FontStyles.Bold,
            ref stablePhaseText, ref phaseCandidateText, ref phaseCandidateSince, TextStabilitySeconds);
        var bracingResult = unityScore != null ? unityScore.bracing : null;
        var pRelaxed = pressure != null && pressure.pRelaxed > 0f ? pressure.pRelaxed : 110f;
        var pBraced = pressure != null
            ? (pressure.pBraced > 0f ? pressure.pBraced : pressure.@base)
            : 0f;
        var pressureRange = pBraced - pRelaxed;
        // Always derive the live display from the current pressure and the
        // calibration range. The merged bracingLoss field can be stale when
        // FSR and bracing packets arrive in different orders. Pressure above
        // pBraced therefore becomes a guaranteed 0% loss / 100% brace.
        var calculatedLoss = pressureRange >= 3f
            ? Mathf.Clamp((pBraced - pressure.current) / pressureRange * 100f, 0f, 100f)
            : Mathf.Clamp(dropRatio, 0f, 100f);
        var bracingPercent = pressure != null ? Mathf.Clamp01(1f - calculatedLoss / 100f) : 0f;
        var bracingLevel = bracingResult != null &&
            (bracingResult.status == SafeLiftScoreStatus.INVALID_CALIBRATION || bracingResult.status == SafeLiftScoreStatus.NO_SIGNAL)
            ? BracingDisplayLevel(bracingResult.status)
            : scoringPhase ? BracingDisplayLevelFromLoss(calculatedLoss) : "NORMAL";
        var pressureLevel = scoringPhase ? bracingLevel : "NORMAL";
        mainText.text = ResolveSensorMessage(pressureLevel, balanceLevel, null, state);
        mainText.color = LevelColor(HighestLevel(pressureLevel, balanceLevel, "NORMAL"));
        pressureGauge.color = LevelColor(bracingLevel);
        pressureGauge.fillAmount = bracingPercent;
        pressureText.text = Mathf.RoundToInt(bracingPercent * 100f) + "%";
        pressureStatus.text = bracingResult != null &&
            (bracingResult.status == SafeLiftScoreStatus.INVALID_CALIBRATION || bracingResult.status == SafeLiftScoreStatus.NO_SIGNAL)
            ? bracingResult.status.ToString()
            : scoringPhase ? (bracingLevel == "WARNING" ? "SCORE DEDUCTION ACTIVE" : bracingLevel) : "STANDING - NO SCORE";
        pressureStatus.color = LevelColor(bracingLevel);
        leftBalance.text = "LEFT " + Mathf.RoundToInt(left * 100f) + "% | heel " + (fsr != null ? fsr.leftHeelRatio.ToString("0") : "--") + "%";
        rightBalance.text = "RIGHT " + Mathf.RoundToInt(right * 100f) + "% | heel " + (fsr != null ? fsr.rightHeelRatio.ToString("0") : "--") + "%";
        leftGauge.fillAmount = Mathf.Clamp01(left * 2f);
        rightGauge.fillAmount = Mathf.Clamp01(right * 2f);
        var leftOverloaded = left > right;
        leftGauge.color = leftOverloaded ? LevelColor(balanceLevel) : LevelColor("NORMAL");
        rightGauge.color = leftOverloaded ? LevelColor("NORMAL") : LevelColor(balanceLevel);
        UpdateFootMap(fsr);
        fsrDetails.text = fsr != null
            ? "FSR TOTAL L/R: " + fsr.leftTotal.ToString("0.0") + " / " + fsr.rightTotal.ToString("0.0")
                + "\nFSR PERCENT L/R: " + fsr.leftPercent.ToString("0.0") + "% / " + fsr.rightPercent.ToString("0.0") + "%"
                + "\nIMBALANCE: " + fsr.imbalance.ToString("0.0") + "% | HEEL: " + fsr.heelRatio.ToString("0.0") + "%"
            : "FSR TOTAL L/R: -- / --\nFSR PERCENT L/R: -- / --\nIMBALANCE: -- | HEEL: --";
        if (spine != null && spine.valid)
        {
            // Fusion Monitor owns the fused Butt Wink decision. Do not turn
            // the raw angle or IMU magnitude into a second Unity-only error.
            var imuError = scoringPhase && spine.buttWink;
            if (imuError)
            {
                // Use the same State text object as Standing/Descending so
                // Butt Wink does not look like a separate UI component.
                var buttWinkState = "State: Butt Wink DETECTED";
                phaseText.text = buttWinkState;
                phaseText.color = new Color(.95f, .2f, .2f, 1f);
                phaseText.fontStyle = FontStyles.Bold;
                stablePhaseText = buttWinkState;
                phaseCandidateText = "";
                spineStatus.gameObject.SetActive(false);
            }
            else
            {
                spineStatus.gameObject.SetActive(false);
                stableSpineText = "";
                spineCandidateText = "";
            }
        }
        else if (state == null || state.metrics == null)
        {
            spineStatus.gameObject.SetActive(false);
        }
    }

    private void SetNoSignalIfBackendHasNoSensor(SafeLiftState state)
    {
        if (state == null || state.sensors == null)
            ClearState();
    }

    private static string ResolveSensorMessage(string pressure, string balance, string packetStatus, SafeLiftState state)
    {
        if (pressure == "DANGER") return "PRESSURE DROP - CHECK BRACING";
        if (balance == "DANGER") return "WEIGHT SHIFT - BALANCE FEEDBACK";
        if (pressure == "WARNING" || balance == "WARNING") return "ADJUST YOUR FOOT PRESSURE";
        if (!string.IsNullOrWhiteSpace(packetStatus) && packetStatus != "NORMAL") return packetStatus;
        return state != null && !string.IsNullOrWhiteSpace(state.current_cue) ? state.current_cue : "SENSOR DATA LIVE";
    }

    private static string ResolveMainMessage(SafeLiftState state, string pressure, string balance, string spine)
    {
        if (pressure == "DANGER") return "복압을 유지하세요";
        if (balance == "DANGER") return "좌우 체중을 균형 있게 분배하세요";
        if (spine == "WARNING" || pressure == "WARNING" || balance == "WARNING") return "자세를 조금 조정해 주세요";
        return string.IsNullOrWhiteSpace(state.current_cue) ? "자세가 안정적이에요" : state.current_cue;
    }

    private static bool ContainsIssue(SafeLiftState state, string token)
    {
        if (state == null) return false;
        if ((state.main_issue ?? "").ToLowerInvariant().Contains(token)) return true;
        if (state.detected_errors == null) return false;
        foreach (var issue in state.detected_errors)
            if ((issue ?? "").ToLowerInvariant().Contains(token)) return true;
        return false;
    }

    private static string Level(float value) => value < 8f ? "NORMAL" : value <= 10f ? "WARNING" : "DANGER";
    private static string BracingDisplayLevel(SafeLiftScoreStatus status)
    {
        if (status == SafeLiftScoreStatus.DANGER || status == SafeLiftScoreStatus.INVALID_CALIBRATION || status == SafeLiftScoreStatus.NO_SIGNAL)
            return "DANGER";
        if (status == SafeLiftScoreStatus.WARNING) return "WARNING";
        return "NORMAL";
    }
    private static string BracingDisplayLevelFromLoss(float loss)
    {
        if (loss >= 50f) return "DANGER";
        // Match the Python bracing evaluator and Unity score accumulator.
        if (loss >= 30f) return "WARNING";
        return "NORMAL";
    }
    private static string FsrDisplayLevel(SafeLiftScoreStatus? status, float imbalance)
    {
        if (status.HasValue && status.Value == SafeLiftScoreStatus.DANGER) return "DANGER";
        if (status.HasValue && status.Value == SafeLiftScoreStatus.WARNING) return "WARNING";
        if (imbalance > 10f) return "DANGER";
        if (imbalance >= 8f) return "WARNING";
        return "NORMAL";
    }
    private static bool IsScoringPhase(string phase)
    {
        if (string.IsNullOrEmpty(phase)) return false;
        var value = phase.ToLowerInvariant();
        return value.Contains("desc") || value.Contains("bottom") || value.Contains("asc") || value.Contains("squat");
    }
    private static string HighestLevel(string a, string b, string c) => a == "DANGER" || b == "DANGER" || c == "DANGER" ? "DANGER" : a == "WARNING" || b == "WARNING" || c == "WARNING" ? "WARNING" : "NORMAL";
    private static Color LevelColor(string level) => level == "DANGER" ? new Color(.95f, .25f, .25f) : level == "WARNING" ? new Color(1f, .75f, .15f) : new Color(.25f, .9f, .45f);

    private static TMP_Text Text(Transform parent, string name, string value, float size, Vector2 position, Vector2 dimensions, out GameObject created)
    {
        created = new GameObject(name);
        created.transform.SetParent(parent, false);
        var rect = created.AddComponent<RectTransform>();
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        var text = created.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        SafeLiftKoreanText.Apply(text);
        return text;
    }

    private static void SetStableText(
        TMP_Text target,
        string next,
        Color color,
        FontStyles fontStyle,
        ref string stable,
        ref string candidate,
        ref float candidateSince,
        float delay)
    {
        if (target == null) return;
        if (stable == next)
        {
            target.color = color;
            target.fontStyle = fontStyle;
            return;
        }
        if (candidate != next)
        {
            candidate = next;
            candidateSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - candidateSince < delay) return;
        stable = next;
        target.text = next;
        target.color = color;
        target.fontStyle = fontStyle;
    }

    private static void SetUiPosition(Transform parent, string name, Vector2 position)
    {
        var child = parent.Find(name);
        if (child == null) return;
        var rect = child.GetComponent<RectTransform>();
        if (rect != null) rect.anchoredPosition = position;
    }

    private static void SetUiText(Transform parent, string name, string value)
    {
        var child = parent.Find(name);
        if (child == null) return;
        var text = child.GetComponent<TMP_Text>();
        if (text != null) text.text = value;
    }

    private void CreateFootMap(Transform parent, string name, Vector2 position, int gaugeOffset, out TMP_Text label)
    {
        var map = new GameObject(name);
        map.transform.SetParent(parent, false);
        var rect = map.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(120f, 190f);
        rect.anchoredPosition = position;

        var silhouette = map.AddComponent<Image>();
        silhouette.sprite = CreateExternalFootSprite(gaugeOffset == 0) ?? CreateFootSprite();
        silhouette.color = Color.white;

        var leftFoot = gaugeOffset == 0;
        var firstX = leftFoot ? -10f : -26f;
        var secondX = leftFoot ? 26f : 10f;
        var heelX = leftFoot ? 7f : -7f;
        var firstBackground = Gauge(map.transform, name + "Sensor1Background", new Vector2(firstX, 46f), FsrGaugeBackgroundColor(), true);
        var secondBackground = Gauge(map.transform, name + "Sensor2Background", new Vector2(secondX, 46f), FsrGaugeBackgroundColor(), true);
        var heelBackground = Gauge(map.transform, name + "HeelSensorBackground", new Vector2(heelX, -48f), FsrGaugeBackgroundColor(), true);
        firstBackground.fillAmount = 1f;
        secondBackground.fillAmount = 1f;
        heelBackground.fillAmount = 1f;
        var first = Gauge(map.transform, name + "Sensor1", new Vector2(firstX, 46f), Color.gray, true);
        var second = Gauge(map.transform, name + "Sensor2", new Vector2(secondX, 46f), Color.gray, true);
        var heel = Gauge(map.transform, name + "HeelSensor", new Vector2(heelX, -48f), Color.gray, true);
        first.rectTransform.sizeDelta = new Vector2(30f, 30f);
        second.rectTransform.sizeDelta = new Vector2(30f, 30f);
        heel.rectTransform.sizeDelta = new Vector2(30f, 30f);
        firstBackground.rectTransform.sizeDelta = new Vector2(30f, 30f);
        secondBackground.rectTransform.sizeDelta = new Vector2(30f, 30f);
        heelBackground.rectTransform.sizeDelta = new Vector2(30f, 30f);
        footSensorGaugeBackgrounds[gaugeOffset] = firstBackground;
        footSensorGaugeBackgrounds[gaugeOffset + 1] = secondBackground;
        footSensorGaugeBackgrounds[gaugeOffset + 2] = heelBackground;
        footSensorGauges[gaugeOffset] = first;
        footSensorGauges[gaugeOffset + 1] = second;
        footSensorGauges[gaugeOffset + 2] = heel;
        footSensorGaugeTexts[gaugeOffset] = GaugeText(map.transform, name + "Sensor1Text", new Vector2(firstX, 46f));
        footSensorGaugeTexts[gaugeOffset + 1] = GaugeText(map.transform, name + "Sensor2Text", new Vector2(secondX, 46f));
        footSensorGaugeTexts[gaugeOffset + 2] = GaugeText(map.transform, name + "HeelSensorText", new Vector2(heelX, -48f));

        label = Text(map.transform, name + "Label", gaugeOffset == 0 ? "LEFT FOOT\n--" : "RIGHT FOOT\n--", 10f, new Vector2(0f, -100f), new Vector2(120f, 30f), out _);
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.85f, .95f, 1f, 1f);
    }

    private void UpdateFootMap(SafeLiftSquatSensorUdpReceiver.FsrPayload fsr)
    {
        if (fsr == null || footSensorGauges == null || footSensorGauges.Length < 6) return;
        var ratios = new[]
        {
            // Physical layout: left top-left=FSR3, top-right=FSR2, heel=FSR1.
            fsr.leftFront2Ratio, fsr.leftFront1Ratio, fsr.leftHeelSensorRatio,
            fsr.rightFront1Ratio, fsr.rightFront2Ratio, fsr.rightHeelSensorRatio
        };
        var raw = new[]
        {
            fsr.leftFront2, fsr.leftFront1, fsr.leftHeel,
            fsr.rightFront1, fsr.rightFront2, fsr.rightHeel
        };
        // Fallback for packets that contain the original six-channel array
        // but not the newer named visualization fields.
        if (fsr.channels != null && fsr.channels.Length >= 6)
        {
            if (raw[0] <= 0f) raw[0] = fsr.channels[2];
            if (raw[1] <= 0f) raw[1] = fsr.channels[1];
            if (raw[2] <= 0f) raw[2] = fsr.channels[0];
            if (raw[3] <= 0f) raw[3] = fsr.channels[3];
            if (raw[4] <= 0f) raw[4] = fsr.channels[5];
            if (raw[5] <= 0f) raw[5] = fsr.channels[4];
        }
        var maxRaw = 0f;
        foreach (var value in raw) maxRaw = Mathf.Max(maxRaw, value);
        for (var i = 0; i < footSensorGauges.Length; i++)
        {
            var ratio = ratios[i] > 0f
                ? Mathf.Clamp01(ratios[i] / 100f)
                : maxRaw > 0f ? Mathf.Clamp01(raw[i] / maxRaw) : 0f;
            footSensorGauges[i].fillAmount = ratio;
            footSensorGauges[i].color = FsrGaugeColor(ratio);
            if (footSensorGaugeTexts != null && footSensorGaugeTexts[i] != null)
                footSensorGaugeTexts[i].text = Mathf.RoundToInt(ratio * 100f) + "%";
        }
        // Front sensors visualize the independent left/right balance rule;
        // heel sensors visualize the independent heel-lift rule. The center
        // percentages remain the individual sensor ratios.
        var balanceColor = LevelColor(BalanceDisplayLevel(fsr.imbalance));
        var leftHeelColor = LevelColor(HeelDisplayLevel(fsr.leftHeelRatio));
        var rightHeelColor = LevelColor(HeelDisplayLevel(fsr.rightHeelRatio));
        footSensorGauges[0].color = balanceColor;
        footSensorGauges[1].color = balanceColor;
        footSensorGauges[2].color = leftHeelColor;
        footSensorGauges[3].color = balanceColor;
        footSensorGauges[4].color = balanceColor;
        footSensorGauges[5].color = rightHeelColor;
        if (leftFootLabel != null)
            leftFootLabel.text = "LEFT FOOT  " + fsr.leftPercent.ToString("0") + "%\n" + raw[0].ToString("0") + "  " + raw[1].ToString("0") + "  heel " + raw[2].ToString("0");
        if (rightFootLabel != null)
            rightFootLabel.text = "RIGHT FOOT  " + fsr.rightPercent.ToString("0") + "%\n" + raw[3].ToString("0") + "  " + raw[4].ToString("0") + "  heel " + raw[5].ToString("0");
    }

    private static Color FsrGaugeColor(float ratio)
    {
        return ratio < .20f ? new Color(.95f, .25f, .25f, 1f)
            : ratio <= .25f ? new Color(1f, .75f, .15f, 1f)
            : new Color(.25f, .9f, .45f, 1f);
    }

    private static Color FsrGaugeBackgroundColor()
    {
        return new Color(.92f, .88f, .82f, .95f);
    }

    private static string BalanceDisplayLevel(float imbalance)
    {
        if (imbalance > 20f) return "DANGER";
        if (imbalance >= 15f) return "WARNING";
        return "NORMAL";
    }

    private static string HeelDisplayLevel(float heelRatio)
    {
        if (heelRatio < 20f) return "DANGER";
        if (heelRatio <= 25f) return "WARNING";
        return "NORMAL";
    }

    private static TMP_Text GaugeText(Transform parent, string name, Vector2 position)
    {
        var text = Text(parent, name, "--", 8f, position, new Vector2(30f, 20f), out _);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.black;
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    private static Image Gauge(Transform parent, string name, Vector2 position, Color color, bool radial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = radial ? new Vector2(130f, 130f) : new Vector2(250f, 16f);
        rect.anchoredPosition = position;
        var image = go.AddComponent<Image>();
        image.color = color;
        image.type = Image.Type.Filled;
        if (radial)
            image.sprite = CreateCircleSprite();
        image.fillMethod = radial ? Image.FillMethod.Radial360 : Image.FillMethod.Horizontal;
        image.fillOrigin = radial ? 0 : 1;
        image.fillAmount = 0f;
        return image;
    }

    private static Sprite CreateFootSprite()
    {
        const int width = 128;
        const int height = 220;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "SafeLiftRuntimeFootSole";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var nx = (x - (width - 1) * .5f) / (width * .5f);
                var ny = y / (float)(height - 1);
                // Human-foot silhouette: five rounded toes, a broad ball,
                // a narrower arch, and a separate rounded heel. The waist is
                // intentionally asymmetric enough to avoid a mushroom shape.
                var toeBall = Ellipse(nx, ny, 0f, .78f, .46f, .19f);
                var toe1 = Ellipse(nx, ny, -.34f, .91f, .16f, .12f);
                var toe2 = Ellipse(nx, ny, -.17f, .96f, .16f, .12f);
                var toe3 = Ellipse(nx, ny, 0f, .98f, .16f, .12f);
                var toe4 = Ellipse(nx, ny, .17f, .96f, .15f, .12f);
                var toe5 = Ellipse(nx, ny, .32f, .92f, .13f, .11f);
                var ball = Ellipse(nx, ny, 0f, .67f, .43f, .20f);
                var arch = Ellipse(nx, ny, 0f, .45f, .28f, .30f);
                var heel = Ellipse(nx, ny, 0f, .15f, .36f, .18f);
                pixels[y * width + x] = toeBall || toe1 || toe2 || toe3 || toe4 || toe5 || ball || arch || heel
                    ? new Color32(210, 148, 110, 245)
                    : new Color32(0, 0, 0, 0);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(.5f, .5f), 100f);
    }

    private static Sprite CreateExternalFootSprite(bool left)
    {
        var texture = Resources.Load<Texture2D>("SafeLiftFootSoles");
        if (texture == null || texture.width < 2 || texture.height < 2) return null;
        var halfWidth = texture.width / 2;
        var x = left ? 0 : halfWidth;
        return Sprite.Create(
            texture,
            new Rect(x, 0f, halfWidth, texture.height),
            new Vector2(.5f, .5f),
            100f);
    }

    private static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        var dx = (x - cx) / rx;
        var dy = (y - cy) / ry;
        return dx * dx + dy * dy <= 1f;
    }

    private static Sprite CreateCircleSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "SafeLiftRuntimeCircleGauge";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        var center = (size - 1) * .5f;
        var radius = size * .5f - 1f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var alpha = Mathf.Clamp01(radius + 1f - distance);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), 100f);
    }
}
