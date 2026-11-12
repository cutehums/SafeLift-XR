using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SafeLiftAvatarReviewController : MonoBehaviour
{
    public SafeLiftDemoScenario Scenario { get; set; } = SafeLiftDemoScenario.NoScenario;

    private GameObject panel, avatar;
    private GameObject marker, directionArrow, coreRing, torsoAxisLine, weightCenterLine, weightShiftArrow, depthGuide;
    private TMP_Text phaseText, issueText, comparisonText, controlsText;
    private int phase;
    private float zoom = 1f;
    private bool usingRecordedAvatar;
    private Vector3 avatarBaseScale;

    private static readonly Vector3 BaseAvatarScale = new Vector3(55f, 85f, 35f);
    private static readonly Vector3 BaseMarkerScale = Vector3.one * .05f;
    private static readonly Vector3 BaseArrowScale = new Vector3(.13f, .015f, .015f);
    private static readonly Vector3 BaseGuideScale = new Vector3(.16f, .012f, .012f);
    private readonly string[] phases = { "Standing", "Descent", "Bottom", "Ascent" };

    public void Attach(Transform parent)
    {
        panel = new GameObject("PostRepAvatarReviewPanel");
        panel.transform.SetParent(parent, false);
        panel.SetActive(false);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(.08f, .04f, .12f, 1f);
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(440, 360);
        rt.anchoredPosition = new Vector2(-235, -100);

        phaseText = Text("ReviewPhase", "Phase: 준비 자세", new Vector2(0, -92), 18);
        issueText = Text("IssueHighlight", "문제 하이라이트: 없음", new Vector2(0, -124), 13);
        comparisonText = Text("SetComparison", "SET 1 → SET 2: 리포트 대기 중", new Vector2(0, -150), 12);
        controlsText = Text("ReviewControls", "컨트롤러 ray + Trigger로 phase / 시점 / 회전 / 확대 버튼을 누르세요", new Vector2(0, -184), 10);

        CreateAvatar();
        for (int i = 0; i < phases.Length; i++)
        {
            int phaseIndex = i;
            Button("PhaseTab_" + phases[i], PhaseLabel(phases[i]), new Vector2(-120 + i * 80, -165), () => SetPhase(phaseIndex));
        }
        Button("ZoomOutButton", "축소", new Vector2(-180, -213), () => Zoom(-.08f));
        Button("RotateLeftButton", "좌회전", new Vector2(-120, -213), () => Rotate(-18f));
        Button("FrontViewButton", "정면", new Vector2(-60, -213), FrontView);
        Button("SideViewButton", "측면", new Vector2(0, -213), SideView);
        Button("ResetViewButton", "초기화", new Vector2(60, -213), ResetView);
        Button("RotateRightButton", "우회전", new Vector2(120, -213), () => Rotate(18f));
        Button("ZoomInButton", "확대", new Vector2(180, -213), () => Zoom(.08f));
    }

    public void SetVisible(bool visible)
    {
        if (panel == null) return;

        var becameVisible = visible && !panel.activeSelf;
        panel.SetActive(visible);
        if (visible) SetPhase(becameVisible ? RecommendedPhaseIndex() : phase);
    }

    public void SetPhase(int value)
    {
        phase = (value + phases.Length) % phases.Length;
        var recorder = SafeLiftPhasePoseRecorder.Instance;
        if (phaseText != null)
        {
            var recordedState = recorder != null && recorder.HasSnapshot(phases[phase]) ? "RECORDED" : "FALLBACK";
            phaseText.text = "Phase " + (phase + 1) + " / " + phases.Length + ": " + PhaseLabel(phases[phase]) + "  [" + recordedState + "]";
        }

        if (issueText != null) issueText.text = IssueTextForCurrentPhase();
        if (comparisonText != null) comparisonText.text = recorder != null ? ComparisonText() + "\n" + recorder.SnapshotStatusForPhase(phases[phase]) : ComparisonText();

        var usedRecordedAvatar = TryApplyRecordedAvatarPhase();
        if (avatar != null)
        {
            if (!usedRecordedAvatar)
            {
                usingRecordedAvatar = false;
                float[] heights = { 45f, 36f, 25f, 36f };
                float[] leans = { 0f, -10f, -18f, -8f };
                avatar.transform.localPosition = new Vector3(0, heights[phase], 0);
                avatar.transform.localRotation = Quaternion.Euler(0, phase * 18f, leans[phase]);
            }
            UpdateIssueVisuals();
        }
    }

    public void NextPhase() => SetPhase(phase + 1);
    public void PreviousPhase() => SetPhase(phase - 1);
    public void Rotate(float degrees) { if (avatar != null) avatar.transform.Rotate(Vector3.up, degrees, Space.Self); }
    public void FrontView() => SetViewYaw(180f);
    public void SideView() => SetViewYaw(90f);

    public void Zoom(float delta)
    {
        zoom = Mathf.Clamp(zoom + delta, .65f, 1.6f);
        ApplyZoom();
    }

    public void ResetView()
    {
        zoom = 1f;
        if (avatar != null)
        {
            avatar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            ApplyZoom();
        }
    }

    private void SetViewYaw(float yaw)
    {
        if (avatar == null) return;
        avatar.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        ApplyZoom();
    }

    private void CreateAvatar()
    {
        avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        avatar.name = "SafeLiftReviewAvatar";
        avatar.transform.SetParent(panel.transform, false);
        avatar.transform.localPosition = new Vector3(0, 45, 0);
        avatar.transform.localScale = BaseAvatarScale;
        var r = avatar.GetComponent<Renderer>();
        r.material.color = new Color(.2f, .75f, .95f);
        avatarBaseScale = BaseAvatarScale;

        marker = CreateOverlay("IssueHighlightMarker", PrimitiveType.Sphere, BaseMarkerScale, Color.red);
        directionArrow = CreateOverlay("IssueDirectionArrow", PrimitiveType.Cube, BaseArrowScale, Color.yellow);
        coreRing = CreateOverlay("CoreBracingIndicator", PrimitiveType.Cylinder, new Vector3(.08f, .01f, .08f), Color.cyan);
        torsoAxisLine = CreateOverlay("TorsoAxisLine", PrimitiveType.Cube, new Vector3(.018f, .25f, .018f), new Color(.35f, .85f, 1f));
        weightCenterLine = CreateOverlay("WeightCenterLine", PrimitiveType.Cube, new Vector3(.012f, .62f, .012f), new Color(.7f, .75f, 1f));
        weightShiftArrow = CreateOverlay("WeightShiftArrow", PrimitiveType.Cube, BaseArrowScale, new Color(1f, .72f, .05f));
        depthGuide = CreateOverlay("DepthGuide", PrimitiveType.Cube, BaseGuideScale, new Color(.15f, 1f, .6f));
        UpdateIssueVisuals();
    }

    private GameObject CreateOverlay(string name, PrimitiveType type, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(avatar.transform, false);
        go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = color;
        return go;
    }

    private bool TryApplyRecordedAvatarPhase()
    {
        var recorder = SafeLiftPhasePoseRecorder.Instance;
        if (recorder == null || panel == null) return false;

        var previousAvatar = avatar;
        var applied = recorder.TryApplyPhaseToReview(panel.transform, phases[phase], ref avatar);
        if (!applied) return false;

        avatarBaseScale = avatar.transform.localScale;
        usingRecordedAvatar = true;
        ReparentIssueVisualsToCurrentAvatar();
        ApplyZoom();
        DestroyPlaceholderIfReplaced(previousAvatar);

        if (comparisonText != null)
            comparisonText.text = ComparisonText()
                + "\n" + recorder.SnapshotStatusForPhase(phases[phase])
                + "\n" + recorder.SnapshotStatus()
                + "\nReview source: recorded humanoid avatar";
        return true;
    }

    private void ApplyZoom()
    {
        if (avatar != null) avatar.transform.localScale = avatarBaseScale * zoom;
        if (marker != null) marker.transform.localScale = BaseMarkerScale;
        if (directionArrow != null) directionArrow.transform.localScale = BaseArrowScale;
        if (weightShiftArrow != null) weightShiftArrow.transform.localScale = BaseArrowScale;
        if (depthGuide != null) depthGuide.transform.localScale = BaseGuideScale;
    }

    private void ReparentIssueVisualsToCurrentAvatar()
    {
        if (avatar == null) return;
        ReparentOverlay(marker);
        ReparentOverlay(directionArrow);
        ReparentOverlay(coreRing);
        ReparentOverlay(torsoAxisLine);
        ReparentOverlay(weightCenterLine);
        ReparentOverlay(weightShiftArrow);
        ReparentOverlay(depthGuide);
    }

    private void ReparentOverlay(GameObject overlay)
    {
        if (overlay != null && overlay.transform.parent != avatar.transform)
            overlay.transform.SetParent(avatar.transform, false);
    }

    private void DestroyPlaceholderIfReplaced(GameObject previousAvatar)
    {
        if (previousAvatar == null || previousAvatar == avatar) return;
        if (previousAvatar.name != "SafeLiftReviewAvatar") return;

        previousAvatar.SetActive(false);
        Destroy(previousAvatar);
    }

    private void UpdateIssueVisuals()
    {
        HideIssueOverlays();
        bool phaseMatchesIssue = phase == IssuePhaseIndex();
        if (!phaseMatchesIssue || Scenario == SafeLiftDemoScenario.NoScenario || Scenario == SafeLiftDemoScenario.Normal) return;

        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus:
                SetOverlay(marker, true, new Vector3(.22f, -.55f, -.2f), Quaternion.identity, BaseMarkerScale);
                SetOverlay(directionArrow, true, new Vector3(.15f, -.55f, -.2f), Quaternion.Euler(0f, 0f, 35f), BaseArrowScale);
                break;
            case SafeLiftDemoScenario.WeightShift:
                SetOverlay(marker, true, new Vector3(.32f, -.35f, -.2f), Quaternion.identity, BaseMarkerScale);
                SetOverlay(weightCenterLine, true, new Vector3(0f, -.2f, -.22f), Quaternion.identity, new Vector3(.012f, .62f, .012f));
                SetOverlay(weightShiftArrow, true, new Vector3(.18f, -.38f, -.2f), Quaternion.Euler(0f, 0f, -18f), BaseArrowScale);
                break;
            case SafeLiftDemoScenario.CorePressureDrop:
                SetOverlay(coreRing, true, new Vector3(0f, -.05f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(.08f, .01f, .08f));
                SetOverlay(torsoAxisLine, true, new Vector3(0f, .08f, -.22f), Quaternion.Euler(0f, 0f, -16f), new Vector3(.018f, .25f, .018f));
                break;
            case SafeLiftDemoScenario.ComplexError:
                SetOverlay(marker, true, new Vector3(.24f, -.52f, -.2f), Quaternion.identity, BaseMarkerScale);
                SetOverlay(directionArrow, true, new Vector3(.15f, -.52f, -.2f), Quaternion.Euler(0f, 0f, 35f), BaseArrowScale);
                SetOverlay(coreRing, true, new Vector3(0f, -.05f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(.08f, .01f, .08f));
                SetOverlay(torsoAxisLine, true, new Vector3(0f, .08f, -.22f), Quaternion.Euler(0f, 0f, -22f), new Vector3(.018f, .27f, .018f));
                SetOverlay(depthGuide, true, new Vector3(0f, -.62f, -.22f), Quaternion.identity, BaseGuideScale);
                break;
        }
    }

    private void HideIssueOverlays()
    {
        SetOverlayActive(marker, false);
        SetOverlayActive(directionArrow, false);
        SetOverlayActive(coreRing, false);
        SetOverlayActive(torsoAxisLine, false);
        SetOverlayActive(weightCenterLine, false);
        SetOverlayActive(weightShiftArrow, false);
        SetOverlayActive(depthGuide, false);
    }

    private void SetOverlay(GameObject go, bool active, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
    {
        if (go == null) return;
        go.SetActive(active);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;
        go.transform.localScale = localScale;
    }

    private void SetOverlayActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }

    private int RecommendedPhaseIndex()
    {
        var issuePhase = IssuePhaseIndex();
        return issuePhase >= 0 ? issuePhase : 0;
    }

    private int IssuePhaseIndex()
    {
        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return 3;
            case SafeLiftDemoScenario.WeightShift: return 2;
            case SafeLiftDemoScenario.CorePressureDrop: return 1;
            case SafeLiftDemoScenario.ComplexError: return 2;
            default: return -1;
        }
    }

    private string IssueTextForCurrentPhase()
    {
        if (Scenario == SafeLiftDemoScenario.NoScenario || Scenario == SafeLiftDemoScenario.Normal)
            return usingRecordedAvatar ? "문제 하이라이트: 없음 - 저장된 아바타 replay" : "문제 하이라이트: 없음 - fallback 미리보기";
        if (phase != IssuePhaseIndex()) return "문제 하이라이트: 이 phase에는 표시할 문제가 없습니다";

        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return "문제 하이라이트: 상승 phase의 무릎 안쪽 쏠림";
            case SafeLiftDemoScenario.WeightShift: return "문제 하이라이트: 최저 phase의 좌우 체중 불균형";
            case SafeLiftDemoScenario.CorePressureDrop: return "문제 하이라이트: 하강 중 코어 압력 저하";
            case SafeLiftDemoScenario.ComplexError: return "문제 하이라이트: 무릎/몸통/코어/깊이 복합 문제";
            default: return "문제 하이라이트: 없음";
        }
    }

    private string ComparisonText()
    {
        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return "SET1 → SET2: 무릎 정렬 +12, cue: 무릎을 발끝 방향으로";
            case SafeLiftDemoScenario.WeightShift: return "SET1 → SET2: 균형 +9, cue: 중심부 정렬 유지";
            case SafeLiftDemoScenario.CorePressureDrop: return "SET1 → SET2: 코어 안정 +15, cue: 다시 복압 잡기";
            case SafeLiftDemoScenario.ComplexError: return "SET1 → SET2: 점수 +8, 다음 cue: 속도 줄이기";
            default: return "SET1 → SET2: 안정적입니다. 같은 템포를 유지하세요";
        }
    }

    private static string PhaseLabel(string phaseName)
    {
        switch (phaseName)
        {
            case "Standing": return "준비";
            case "Descent": return "하강";
            case "Bottom": return "최저";
            case "Ascent": return "상승";
            default: return phaseName;
        }
    }
    private TMP_Text Text(string name, string value, Vector2 pos, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);
        var r = go.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(390, 32);
        r.anchoredPosition = pos;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = value;
        SafeLiftKoreanText.Apply(t);
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    private void Button(string name, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel.transform, false);
        var r = go.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(62, 28);
        r.anchoredPosition = pos;
        var image = go.AddComponent<Image>();
        image.color = new Color(.16f, .35f, .5f);
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(action);
        var t = Text(name + "Label", label, Vector2.zero, 10);
        t.transform.SetParent(go.transform, false);
        t.rectTransform.sizeDelta = r.sizeDelta;
    }
}
