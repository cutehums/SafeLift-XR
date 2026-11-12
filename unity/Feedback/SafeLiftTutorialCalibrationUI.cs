using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SafeLiftTutorialCalibrationUI : MonoBehaviour
{
    private const float TutorialTotalSeconds = 20f;

    private GameObject panel;
    private TMP_Text instruction, countdown, ttsCueText, backendStatusText, imageStatusText;
    private Image progress, tutorialImage;
    private readonly Image[] phaseCards = new Image[4];
    private readonly TMP_Text[] phaseLabels = new TMP_Text[4];
    private readonly Sprite[] tutorialSprites = new Sprite[4];
    public int CurrentStep { get; private set; }
    public Transform PanelTransform => panel != null ? panel.transform : null;

    private readonly string[] phases =
    {
        "준비 자세",
        "내려가기",
        "가장 낮은 자세",
        "올라오기"
    };

    // Manual-required order: tutorial_1.png -> tutorial_2.png -> tutorial_3.png -> tutorial_4.png.
    private readonly string[] resourceNames =
    {
        "tutorial_1",
        "tutorial_2",
        "tutorial_3",
        "tutorial_4"
    };

    private readonly string[] phaseCues =
    {
        "발 전체로 바닥을 누르고 발 간격은 골반~어깨너비로 맞춰 주세요.",
        "무릎과 엉덩이를 함께 굽히면서 천천히 내려가세요.",
        "안정적으로 유지할 수 있는 깊이까지만 내려가세요.",
        "양발로 바닥을 밀어 몸통을 단단히 유지하며 올라오세요."
    };

    private readonly string[] ttsCues =
    {
        "튜토리얼 1번, 준비 자세입니다. 발 전체로 바닥을 누르고 기본 정렬을 맞춰 주세요.",
        "튜토리얼 2번, 내려가기입니다. 무릎과 엉덩이를 함께 굽히면서 천천히 내려가세요.",
        "튜토리얼 3번, 가장 낮은 자세입니다. 안정적으로 유지할 수 있는 깊이까지만 내려가세요.",
        "튜토리얼 4번, 올라오기입니다. 양발로 바닥을 밀어 일어나세요."
    };

    public void Attach(Transform parent)
    {
        LoadTutorialSprites();

        panel = new GameObject("TutorialCalibrationPanel");
        panel.transform.SetParent(parent, false);
        var bg = panel.AddComponent<Image>();
        // Keep the outer white panel as the clean tutorial background.
        bg.color = new Color(.96f, .98f, 1f, .99f);
        bg.raycastTarget = false;
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1120, 660);
        rt.anchoredPosition = new Vector2(0, -100);

        var header = Text("TutorialHeader", "스쿼트 튜토리얼 · Calibration", new Vector2(0, 270), 24, 920, 36);
        header.color = new Color(.02f, .12f, .32f);
        header.fontStyle = FontStyles.Bold;
        // The shared dashboard title/subtitle/progress group is positioned
        // above the image by SafeLiftUIStateMachine. Do not duplicate the
        // title inside the tutorial panel.
        header.gameObject.SetActive(false);

        var imageFrame = new GameObject("TutorialImageFrame");
        imageFrame.transform.SetParent(panel.transform, false);
        var frameRect = imageFrame.AddComponent<RectTransform>();
        frameRect.sizeDelta = new Vector2(920, 450);
        frameRect.anchoredPosition = new Vector2(0, 45);
        var frameBg = imageFrame.AddComponent<Image>();
        frameBg.color = Color.white;

        var imageObject = new GameObject("TutorialImage");
        imageObject.transform.SetParent(imageFrame.transform, false);
        var imageRect = imageObject.AddComponent<RectTransform>();
        imageRect.sizeDelta = new Vector2(900, 430);
        imageRect.anchoredPosition = Vector2.zero;
        tutorialImage = imageObject.AddComponent<Image>();
        tutorialImage.preserveAspect = true;
        tutorialImage.color = Color.white;

        imageStatusText = Text("TutorialImageStatus", "", new Vector2(0, -190), 13, 900, 24);
        imageStatusText.color = new Color(.05f, .35f, .55f);

        instruction = Text(
            "CalibrationInstruction",
            "첨부한 튜토리얼 이미지를 1번부터 4번까지 순서대로 보며 자세를 맞춰 주세요.",
            new Vector2(0, -218),
            16,
            940,
            28);
        instruction.color = new Color(.02f, .11f, .2f);

        countdown = Text("CalibrationCountdown", "Tutorial / Calibration 12초", new Vector2(-290, -250), 14, 300, 24);
        countdown.color = new Color(.03f, .24f, .5f);

        ttsCueText = Text("TutorialVoiceCue", "TTS cue: " + ttsCues[0], new Vector2(130, -250), 11, 650, 24);
        ttsCueText.color = new Color(.07f, .19f, .32f);

        backendStatusText = Text(
            "CalibrationBackendStatus",
            "Backend calibration: sensor baseline sample 대기 중",
            new Vector2(0, -274),
            11,
            920,
            20);
        backendStatusText.color = new Color(.12f, .34f, .42f);
        imageStatusText.gameObject.SetActive(false);
        countdown.gameObject.SetActive(false);
        ttsCueText.gameObject.SetActive(false);
        backendStatusText.gameObject.SetActive(false);

        var bar = new GameObject("CalibrationProgress");
        bar.transform.SetParent(panel.transform, false);
        var br = bar.AddComponent<RectTransform>();
        br.sizeDelta = new Vector2(780, 8);
        br.anchoredPosition = new Vector2(0, -296);
        bar.AddComponent<Image>().color = new Color(.78f, .85f, .92f);
        var fill = new GameObject("Fill");
        fill.transform.SetParent(bar.transform, false);
        var fr = fill.AddComponent<RectTransform>();
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;
        progress = fill.AddComponent<Image>();
        progress.color = new Color(.1f, .55f, .95f);
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Horizontal;

        for (int i = 0; i < phases.Length; i++)
        {
            var go = new GameObject("TutorialPhase_" + resourceNames[i]);
            go.transform.SetParent(panel.transform, false);
            var cr = go.AddComponent<RectTransform>();
            cr.sizeDelta = new Vector2(200, 28);
            cr.anchoredPosition = new Vector2(-330 + i * 220, -326);
            phaseCards[i] = go.AddComponent<Image>();
            phaseCards[i].color = new Color(.88f, .93f, .98f);
            phaseLabels[i] = Text("Label_" + resourceNames[i], (i + 1) + ". " + phases[i], Vector2.zero, 12, 190, 22);
            phaseLabels[i].transform.SetParent(go.transform, false);
            phaseLabels[i].rectTransform.anchoredPosition = Vector2.zero;
            phaseLabels[i].color = new Color(.02f, .12f, .32f);
            phaseCards[i].gameObject.SetActive(true);
        }

        instruction.gameObject.SetActive(true);

        SetTutorialImage(0);
        CurrentStep = 0;
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    public void SetProgress(float normalized, int phaseIndex)
    {
        var clamped = Mathf.Clamp01(normalized);
        var index = Mathf.Clamp(phaseIndex, 0, phases.Length - 1);

        if (progress != null) progress.fillAmount = clamped;
        if (countdown != null)
            countdown.text = "Image " + (index + 1) + "/4 · Calibration "
                + Mathf.CeilToInt((1f - clamped) * TutorialTotalSeconds) + "초";

        HighlightPhase(index);
        SetTutorialImage(index);

        if (instruction != null)
            instruction.text = (index + 1) + ". " + phases[index] + " — " + phaseCues[index];

        if (ttsCueText != null)
            ttsCueText.text = "TTS cue: " + ttsCues[index];

        if (backendStatusText != null)
            backendStatusText.text = "Backend calibration: tutorial image order 1→2→3→4";
    }

    public void SetStep(int step)
    {
        CurrentStep = Mathf.Clamp(step, 0, phases.Length - 1);
        HighlightPhase(CurrentStep);
        SetTutorialImage(CurrentStep);
        if (instruction != null)
            instruction.text = (CurrentStep + 1) + ". " + phases[CurrentStep] + " " + phaseCues[CurrentStep];
        if (ttsCueText != null)
            ttsCueText.text = "TTS cue: " + ttsCues[CurrentStep];
        if (countdown != null)
            countdown.text = "Tutorial " + (CurrentStep + 1) + "/4 / 수동 진행";
    }

    public void ApplyBackendCalibration(SafeLiftState state)
    {
        if (state == null) return;

        var normalized = Mathf.Clamp01(state.calibration_progress);
        if (progress != null) progress.fillAmount = normalized;

        var status = string.IsNullOrWhiteSpace(state.calibration_status) ? "measuring" : state.calibration_status;
        var failed = status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("retry", StringComparison.OrdinalIgnoreCase) >= 0;

        if (countdown != null)
            countdown.text = "Calibration " + status.ToUpperInvariant() + " · " + Mathf.RoundToInt(normalized * 100f) + "%";

        var message = string.IsNullOrWhiteSpace(state.calibration_message)
            ? "기본 자세를 유지하는 동안 센서 기준값과 추적 품질을 측정합니다."
            : state.calibration_message;

        if (failed)
            message = "Calibration failed. Stand still, face forward, and keep both feet on the pressure plates.";

        if (ttsCueText != null)
            ttsCueText.text = failed
                ? "TTS cue: Calibration failed. Please stand still and retry."
                : "TTS cue: " + ttsCues[CurrentStep];

        if (backendStatusText != null)
            backendStatusText.text = "Backend calibration: " + status + " / tracking " + (state.live_tracking_quality ?? "unknown");
    }

    private string FormatBaselineEvidence(SafeLiftCalibrationBaseline baseline)
    {
        if (baseline == null) return "Baseline evidence: waiting for sensor/pose samples.";

        return "Baseline evidence: "
            + "L " + FormatStatMean(baseline.left_pressure_ratio)
            + " / R " + FormatStatMean(baseline.right_pressure_ratio)
            + " / Core " + FormatStatMean(baseline.core_pressure_ratio)
            + " / IMU pitch " + FormatStatMean(baseline.imu_pitch)
            + " / roll " + FormatStatMean(baseline.imu_roll);
    }

    private string FormatStatMean(SafeLiftCalibrationStat stat)
    {
        if (stat == null || stat.samples <= 0) return "-";
        return stat.mean.ToString("0.00");
    }

    private void LoadTutorialSprites()
    {
        for (int i = 0; i < tutorialSprites.Length; i++)
        {
            var resourcePath = "Tutorial/" + resourceNames[i];
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null)
            {
                tutorialSprites[i] = sprite;
                continue;
            }

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) continue;
            tutorialSprites[i] = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f),
                100f);
        }
    }

    private void SetTutorialImage(int phaseIndex)
    {
        var index = Mathf.Clamp(phaseIndex, 0, tutorialSprites.Length - 1);
        if (tutorialImage == null) return;

        tutorialImage.sprite = tutorialSprites[index];
        tutorialImage.enabled = tutorialSprites[index] != null;

        if (imageStatusText != null)
            imageStatusText.text = tutorialSprites[index] != null
                ? "Tutorial image " + (index + 1) + "/4: " + resourceNames[index] + ".png"
                : "Tutorial image missing: Assets/SafeLiftXR/Resources/Tutorial/" + resourceNames[index] + ".png";
    }

    private void HighlightPhase(int phaseIndex)
    {
        for (int i = 0; i < phaseCards.Length; i++)
        {
            if (phaseCards[i] != null)
                phaseCards[i].color = i == phaseIndex ? new Color(.09f, .56f, .92f) : new Color(.88f, .93f, .98f);
            if (phaseLabels[i] != null)
                phaseLabels[i].color = i == phaseIndex ? Color.white : new Color(.02f, .12f, .32f);
        }
    }

    private TMP_Text Text(string name, string value, Vector2 pos, float size, float width = 190, float height = 28)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);
        var r = go.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = pos;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = value;
        SafeLiftKoreanText.Apply(t);
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        return t;
    }
}
