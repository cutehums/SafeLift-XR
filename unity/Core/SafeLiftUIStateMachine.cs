using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

public enum SafeLiftScreenState
{
    StartDashboard,
    TutorialCalibration,
    Set1Practice,
    RestBeforeLiveFeedback,
    Set2LiveFeedback,
    BreathingReportGenerating,
    PostRepReview,
    QRShare
}

public enum SafeLiftDemoScenario
{
    NoScenario,
    Normal,
    KneeValgus,
    WeightShift,
    CorePressureDrop,
    ComplexError
}

/// <summary>
/// Self-contained SafeLift demo flow. It creates fallback world-space UI when
/// no prefab wiring is available, while still exposing public methods for
/// Inspector buttons and Quest input.
/// </summary>
public sealed class SafeLiftUIStateMachine : MonoBehaviour
{
    private static SafeLiftUIStateMachine activeInstance;
    private static TMP_FontAsset readableFontAsset;
    // Keep the dashboard in the user's comfortable mid-field, not near the face
    // and not above the headset view. This is the Quest target distance.
    private const float ComfortableCanvasDistance = 1.00f;
    // Keep the complete 900-unit dashboard card, including the primary action
    // at its lower edge, inside the desktop preview and the Quest view.
    private const float ComfortableCanvasHeight = 1.58f;
    private const float ComfortableCanvasScale = 0.00088f;
    // Leave the left side of the user's view available for the selected
    // MediaPipe avatar while keeping the dashboard readable.
    private const float ComfortableCanvasLateral = 0f;

    public SafeLiftScreenState State { get; private set; }
    public SafeLiftDemoScenario Scenario { get; set; } = SafeLiftDemoScenario.NoScenario;
    // Real backend/camera flow is the safe default. Demo mode must be an explicit
    // choice so a stale mock session can never look like live MediaPipe evidence.
    public bool DemoMode = false;
    public bool AutoAdvance = false;
    public float TutorialSeconds = 20f; // 4 tutorial images x about 5 seconds each.
    [SerializeField] private string fusionCalibrationHost = "127.0.0.1";
    [SerializeField] private int fusionCalibrationControlPort = 24019;
    [SerializeField] private int bracingCalibrationControlPort = 24022;
    [SerializeField] private int fsrCalibrationControlPort = 24023;
    public float RestSeconds = 10f;
    public float ReportSeconds = 10f;
    public int TargetReps = 3;
    public event Action<SafeLiftScreenState> StateChanged;

    private Canvas canvas;
    private GameObject card;
    private TMP_Text title;
    private TMP_Text subtitle;
    private TMP_Text progressText;
    private TMP_Text content;
    private TMP_Text status;
    private TMP_Text diagnosticsText;
    private TMP_Text sensorCalibrationText;
    private TMP_InputField backendUrlInput;
    private TMP_Text backendUrlHint;
    private Image realModeGateFrame;
    private TMP_Text realModeGateText;
    private RawImage webcamPreview;
    private Image webcamFrame;
    private RawImage avatarPreview;
    private Image avatarPreviewFrame;
    private Image contentFrame;
    private RectTransform contentOriginalParent;
    private GameObject reportScrollViewport;
    private RectTransform reportScrollContent;
    private ScrollRect reportScrollRect;
    private Scrollbar reportScrollbar;
    private Button reportScrollUpButton;
    private Button reportScrollDownButton;
    private GameObject dashboardControls;
    private GameObject backendControls;
    private GameObject flowControls;
    private Texture2D currentWebcamTexture;
    private Button primaryButton;
    private Button rightNextButton;
    private Button recalibrationButton;
    private TMP_Text scenarioButtonText;
    private TMP_Text sensorModeButtonText;
    private TMP_Text viewModeButtonText;
    private SafeLiftRealtimeFeedbackPanel realtimeFeedback;
    private SafeLiftSquatSensorUdpReceiver squatSensorReceiver;
    private SafeLiftUnityScoreController scoreController;
    private SafeLiftAvatarReviewController avatarReview;
    private SafeLiftQRShareUI qrShare;
    private SafeLiftTutorialCalibrationUI tutorialUI;
    private SafeLiftBreathingReportUI breathingUI;
    private SafeLiftVisualAssetAdapter visualAssets;
    private GameObject repScorePanel;
    private TMP_Text repScorePanelText;
    private GameObject liveScorePanel;
    private TMP_Text liveScorePanelText;
    private SafeLiftPhasePoseRecorder phaseRecorder;
    private SafeLiftAvatarRuntimeController avatarRuntime;
    private SafeLiftAvatarUIPreview avatarUIPreview;
    private SafeLiftBackendBridge backendBridge;
    private SafeLiftTtsCue tts;
    private SafeLiftXRHapticsManager haptics;
    private AudioSource squatCountAudio;
    private readonly AudioClip[] squatCountClips = new AudioClip[3];
    private AudioClip squatCount3First;
    private AudioClip squatCount3Second;
    private AudioClip startFirstClip;
    private AudioClip startSecondClip;
    private int lastSpokenRep;
    private float stateStarted;
    private int reps;
    private float nextRepAt;
    private bool hasReported;
    // Make the real MediaPipe camera evidence visible by default. The user can still
    // switch to the larger 3D avatar view with the dashboard toggle.
    // The exercise view must show the selected humanoid avatar by default.
    // The dashboard toggle can still switch to a large webcam preview.
    private bool webcamFocus = false;
    private float nextFontRefresh;
    private bool backendConnected;
    private bool liveFrameReceived;
    private string liveFrameStatus = "WAIT";
    private string liveFrameError = string.Empty;
    private bool backendRestPending;
    private bool setOneRestConsumed;
    private bool setTwoRestConsumed;
    private bool restAfterSetTwoPending;
    private bool backendReportReady;
    private string latestReportText = string.Empty;
    private bool sessionStarted;
    private bool waitingForBackendReset;
    private int backendRepCount;
    private int backendSetRepCount;
    private int fusionRepCount;
    private int previousFusionRepCount;
    private bool fusionRepCountSeen;
    private bool fusionRepResetPending;
    private string lastSet1PhaseLabel = "STANDING";
    private bool calibrationButtonActive;
    private float calibrationButtonStarted;
    private int calibrationStartPacketCount;
    private const float CalibrationButtonDurationSeconds = 3f;
    private readonly List<SafeLiftScoreResult> set1RepScores = new List<SafeLiftScoreResult>();
    private readonly List<SafeLiftScoreResult> set2RepScores = new List<SafeLiftScoreResult>();
    private SafeLiftScoreResult lastCompletedRepScore;
    private string lastCompletedRepSet = "--";
    private int lastCompletedRepNumber;
    private SafeLiftState latestBackendState;
    private SafeLiftHealth latestBackendHealth;
    private SafeLiftSetComparison latestSetComparison;
    private float nextIsolationCheck;
    private const float InitialCanvasRecenterSeconds = 5f;
    private float canvasRecenterUntil;
    private bool canvasWorldLocked;
    private bool xrPoseRecentered;
    private string currentBackendUrl = "http://127.0.0.1:8090";

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning("Duplicate SafeLiftUIStateMachine detected. Removing duplicate runtime UI to prevent overlapping screens.");
            Destroy(gameObject);
            return;
        }
        activeInstance = this;
        DisableDemoDebugHudOverlays();

        haptics = GetComponent<SafeLiftXRHapticsManager>();
        if (haptics == null) haptics = gameObject.AddComponent<SafeLiftXRHapticsManager>();

        backendBridge = GetComponent<SafeLiftBackendBridge>() ?? FindObjectOfType<SafeLiftBackendBridge>();
        // The runtime bootstrap runs in Start, after this Awake callback. Create
        // the bridge immediately so the state/live-frame polls cannot miss the
        // first backend connection.
        if (backendBridge == null)
            backendBridge = gameObject.AddComponent<SafeLiftBackendBridge>();

        // In the Editor the backend is local to this machine.  Keep the
        // dashboard and bridge in sync before the fallback UI is built so a
        // stale Quest IP cannot appear on the first screen.
        if (Application.isEditor)
        {
            currentBackendUrl = "http://127.0.0.1:8090";
            backendBridge.ServerUrl = currentBackendUrl;
        }

        tts = GetComponent<SafeLiftTtsCue>();
        if (tts == null) tts = gameObject.AddComponent<SafeLiftTtsCue>();

        squatCountAudio = gameObject.GetComponent<AudioSource>();
        if (squatCountAudio == null) squatCountAudio = gameObject.AddComponent<AudioSource>();
        squatCountAudio.playOnAwake = false;
        squatCountAudio.loop = false;
        squatCountAudio.spatialBlend = 0f;
        squatCountClips[0] = Resources.Load<AudioClip>("Audio/SquatCount_1");
        squatCountClips[1] = Resources.Load<AudioClip>("Audio/SquatCount_2");
        // The project currently uses the existing SqurtCount spelling.
        squatCount3First = Resources.Load<AudioClip>("Audio/SqurtCount_3_first");
        squatCount3Second = Resources.Load<AudioClip>("Audio/SqurtCount_3_second");
        startFirstClip = Resources.Load<AudioClip>("Audio/startFirst");
        startSecondClip = Resources.Load<AudioClip>("Audio/startSecond");

        BuildFallbackUI();
        BeginInitialCanvasRecenter();
        SetState(SafeLiftScreenState.StartDashboard);
    }

    private void OnEnable()
    {
        OVRManager.HMDMounted += HandleSafeLiftHmdMounted;
        OVRManager.InputFocusAcquired += HandleSafeLiftHmdMounted;
    }

    private void OnDisable()
    {
        OVRManager.HMDMounted -= HandleSafeLiftHmdMounted;
        OVRManager.InputFocusAcquired -= HandleSafeLiftHmdMounted;
    }

    private void HandleSafeLiftHmdMounted()
    {
        xrPoseRecentered = true;
        BeginInitialCanvasRecenter();
    }

    // Desktop QA has no tracked Touch controller, but the Game View still
    // needs the same controller/joystick affordance used to teach the XR
    // interaction. Draw a small, device-independent cue above the world UI.
    private void OnGUI()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
        GUI.depth = -10000;
        var size = Mathf.Clamp(Screen.height * 0.10f, 58f, 96f);
        var y = Screen.height - size * 1.20f;
        DrawDesktopJoystick(new Rect(size * 0.25f, y, size, size));
        DrawDesktopJoystick(new Rect(Screen.width - size * 1.25f, y, size, size));
#endif
    }

#if UNITY_EDITOR
    private static void DrawDesktopJoystick(Rect rect)
    {
        GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.88f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = new Color(0.08f, 0.78f, 0.92f, 0.95f);
        GUI.DrawTexture(new Rect(rect.x + rect.width * 0.47f, rect.y + rect.height * 0.22f, rect.width * 0.06f, rect.height * 0.45f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width * 0.28f, rect.y + rect.height * 0.17f, rect.width * 0.44f, rect.width * 0.44f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
#endif

    private void Update()
    {
        UpdateInitialCanvasRecenter();

        if (avatarUIPreview != null && avatarRuntime != null)
            avatarUIPreview.SetAvatarRoot(avatarRuntime.LiveAvatarRoot);

        // Desktop/Quest-Link QA fallback: when a world-space button is outside
        // the editor GameView crop, Enter/Space must still exercise the exact
        // same state transition as the primary UI action. This is editor-only
        // and does not replace controller or hand input on Quest.
        if (Application.isEditor && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
            PrimaryAction();

        // Keep backend-only widgets out of the demo layout even when a child
        // object is re-enabled by a legacy scene/prefab callback.
        if (backendControls != null)
            backendControls.SetActive(State == SafeLiftScreenState.StartDashboard && !DemoMode);
        if (backendUrlInput != null)
            backendUrlInput.gameObject.SetActive(State == SafeLiftScreenState.StartDashboard && !DemoMode);
        if (backendUrlHint != null)
            backendUrlHint.gameObject.SetActive(State == SafeLiftScreenState.StartDashboard && !DemoMode);

        if (Time.unscaledTime >= nextIsolationCheck)
        {
            nextIsolationCheck = Time.unscaledTime + .5f;
            EnforceSingleSafeLiftRuntimeCanvas();
        }
        if (Time.unscaledTime >= nextFontRefresh)
        {
            nextFontRefresh = Time.unscaledTime + .75f;
            SafeLiftKoreanText.ApplyAllSafeLiftTexts();
        }

        if (backendConnected)
        {
            UpdateCalibrationButtonProgress();
            UpdateBackendDrivenFlow();
            UpdateRealtimeSensorPanel();
            UpdateRestCountdown();
            UpdateRestCountdownLabel();
            UpdateDiagnosticsHud();
            return;
        }

        if (!sessionStarted || !DemoMode || !AutoAdvance) return;

        UpdateCalibrationButtonProgress();

        var elapsed = Time.unscaledTime - stateStarted;
        if (State == SafeLiftScreenState.Set1Practice && elapsed >= nextRepAt)
            AdvanceRep(SafeLiftScreenState.RestBeforeLiveFeedback);
        else if (State == SafeLiftScreenState.RestBeforeLiveFeedback && elapsed >= RestSeconds)
        {
            backendRestPending = false;
            if (restAfterSetTwoPending)
            {
                restAfterSetTwoPending = false;
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            }
            else
            {
                SetState(SafeLiftScreenState.Set2LiveFeedback);
            }
        }
        else if (State == SafeLiftScreenState.Set2LiveFeedback && elapsed >= nextRepAt)
            AdvanceRep(SafeLiftScreenState.BreathingReportGenerating);
        else if (State == SafeLiftScreenState.BreathingReportGenerating && elapsed >= ReportSeconds)
            SetState(SafeLiftScreenState.PostRepReview);

        UpdateRestCountdown();
        UpdateRestCountdownLabel();

        UpdateRealtimeSensorPanel();

        UpdateDiagnosticsHud();
    }

    private void UpdateRealtimeSensorPanel()
    {
        if (squatSensorReceiver == null)
            squatSensorReceiver = GetComponent<SafeLiftSquatSensorUdpReceiver>();
        var oldFusionRepCount = fusionRepCount;
        var oldSet1PhaseLabel = lastSet1PhaseLabel;
        var set1PhaseLabel = CurrentSet1PhaseLabel();
        RefreshFusionRepCount();
        if (oldFusionRepCount != fusionRepCount &&
            (State == SafeLiftScreenState.Set1Practice || State == SafeLiftScreenState.Set2LiveFeedback))
        {
            RenderState(State);
        }
        else if (State == SafeLiftScreenState.Set1Practice && set1PhaseLabel != oldSet1PhaseLabel)
        {
            // SET 1 shows only the basic phase label. It does not enable the
            // live feedback panel or expose Butt Wink/BOTTOM states.
            RenderState(State);
        }
        if (realtimeFeedback == null) return;
        if (scoreController == null)
            scoreController = GetComponent<SafeLiftUnityScoreController>();
        scoreController?.SetBackendState(latestBackendState);
        UpdateLiveScorePanel(scoreController != null ? scoreController.Latest : null);
        CaptureFusionRepScore();
        if (State == SafeLiftScreenState.Set1Practice)
            RenderSet1ScoreStatus();
        else if (State == SafeLiftScreenState.Set2LiveFeedback)
            RenderSet2ScoreStatus();
        realtimeFeedback.ApplySensorSnapshot(
            squatSensorReceiver != null ? squatSensorReceiver.Latest : null,
            squatSensorReceiver != null ? squatSensorReceiver.LastPacketAgeSeconds : float.PositiveInfinity,
            squatSensorReceiver != null ? squatSensorReceiver.LastFusionPhaseAgeSeconds : float.PositiveInfinity,
            latestBackendState,
            scoreController != null ? scoreController.Latest : null);
    }

    private string CurrentSet1PhaseLabel()
    {
        var snapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        if (snapshot == null || squatSensorReceiver.LastFusionPhaseAgeSeconds > 1.5f)
            return lastSet1PhaseLabel;

        var phase = (snapshot.squatPhase ?? string.Empty).Trim().ToUpperInvariant();
        if (phase.Contains("STAND"))
            lastSet1PhaseLabel = "STANDING";
        else if (phase.Contains("DESC"))
            lastSet1PhaseLabel = "DESCENDING";
        else if (phase.Contains("ASC"))
            lastSet1PhaseLabel = "ASCENDING";
        // BOTTOM and BUTT WINK are intentionally ignored for SET 1.
        return lastSet1PhaseLabel;
    }

    private void RenderSet1ScoreStatus()
    {
        if (State != SafeLiftScreenState.Set1Practice || content == null) return;

        var rep = CurrentSetRepCount();
        var text = "SET 1\n\n반복 횟수: " + rep + " / " + TargetReps
            + "\n동작 단계: " + CurrentSet1PhaseLabel()
            + "\n\nREP 점수\n";

        var hasScore = false;
        for (var i = 0; i < set1RepScores.Count; i++)
        {
            var score = set1RepScores[i];
            if (score == null) continue;
            hasScore = true;
            text += "REP " + (i + 1) + ": " + score.totalScore.ToString("0.0") + "점\n";
        }

        if (!hasScore)
            text += "완료된 REP 점수 대기 중";

        content.text = text;
    }

    private void RenderSet2ScoreStatus()
    {
        if (State != SafeLiftScreenState.Set2LiveFeedback || content == null) return;

        var rep = CurrentSetRepCount();
        var phase = "STANDING";
        var snapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        if (snapshot != null && squatSensorReceiver.LastFusionPhaseAgeSeconds <= 1.5f)
        {
            var rawPhase = (snapshot.squatPhase ?? string.Empty).Trim().ToUpperInvariant();
            if (rawPhase.Contains("DESC")) phase = "DESCENDING";
            else if (rawPhase.Contains("BOTTOM")) phase = "BOTTOM";
            else if (rawPhase.Contains("ASC")) phase = "ASCENDING";
        }

        var text = "SET 2\n\n반복 횟수: " + rep + " / " + TargetReps
            + "\n동작 단계: " + phase
            + "\n\nREP 점수\n";
        var hasScore = false;
        for (var i = 0; i < set2RepScores.Count; i++)
        {
            var score = set2RepScores[i];
            if (score == null) continue;
            hasScore = true;
            text += "REP " + (i + 1) + ": " + score.totalScore.ToString("0.0") + "점\n";
        }
        if (!hasScore)
            text += "완료된 REP 점수 대기 중";

        content.text = text;
    }

    private void CaptureFusionRepScore()
    {
        if (!fusionRepCountSeen || scoreController == null || scoreController.Latest == null)
            return;
        if (fusionRepCount <= 0 || fusionRepCount > TargetReps)
            return;

        var target = State == SafeLiftScreenState.Set1Practice ? set1RepScores
            : State == SafeLiftScreenState.Set2LiveFeedback ? set2RepScores : null;
        if (target == null) return;
        while (target.Count < fusionRepCount) target.Add(null);
        if (target[fusionRepCount - 1] == null)
        {
            target[fusionRepCount - 1] = scoreController.Latest;
            lastCompletedRepScore = scoreController.Latest;
            lastCompletedRepSet = State == SafeLiftScreenState.Set1Practice ? "SET 1" : "SET 2";
            lastCompletedRepNumber = fusionRepCount;
            backendBridge?.SendRepScore(lastCompletedRepScore, lastCompletedRepSet, lastCompletedRepNumber);
            realtimeFeedback?.ApplyCompletedRepScore(lastCompletedRepScore, lastCompletedRepSet, lastCompletedRepNumber);
        }
    }

    private void PlaySquatCountAudio(int rep)
    {
        if (rep <= lastSpokenRep || rep < 1 || rep > TargetReps || squatCountAudio == null)
            return;

        // Normal backend operation increments one rep at a time. If a delayed
        // poll skips a count, play the latest acknowledged rep only; the
        // spoken number still always matches the backend rep count.
        lastSpokenRep = rep;
        var clip = rep == 3
            ? (State == SafeLiftScreenState.Set1Practice ? squatCount3First : squatCount3Second)
            : squatCountClips[Mathf.Clamp(rep - 1, 0, squatCountClips.Length - 1)];
        if (clip != null)
            squatCountAudio.PlayOneShot(clip);
    }

    private void PlaySetStartAudio(SafeLiftScreenState setState)
    {
        if (squatCountAudio == null) return;
        var clip = setState == SafeLiftScreenState.Set1Practice ? startFirstClip : startSecondClip;
        if (clip != null)
            squatCountAudio.PlayOneShot(clip);
    }

    private void ResetSetScores(bool resetSetOne, bool resetSetTwo)
    {
        if (resetSetOne) set1RepScores.Clear();
        if (resetSetTwo) set2RepScores.Clear();
        if (resetSetOne)
        {
            lastCompletedRepScore = null;
            lastCompletedRepSet = "--";
            lastCompletedRepNumber = 0;
        }
    }

    private static float AverageSetScore(List<SafeLiftScoreResult> scores)
    {
        if (scores == null || scores.Count == 0) return -1f;
        var total = 0f;
        var count = 0;
        foreach (var score in scores)
        {
            if (score == null) continue;
            total += score.totalScore;
            count++;
        }
        return count == 0 ? -1f : total / count;
    }

    private static SafeLiftScoreResult BestRepScore(List<SafeLiftScoreResult> scores)
    {
        if (scores == null) return null;
        SafeLiftScoreResult best = null;
        foreach (var score in scores)
        {
            if (score == null) continue;
            if (best == null || score.totalScore > best.totalScore)
                best = score;
        }
        return best;
    }

    private static float AverageComponent(List<SafeLiftScoreResult> scores, int component)
    {
        if (scores == null || scores.Count == 0) return 0f;
        var total = 0f;
        var count = 0;
        foreach (var score in scores)
        {
            if (score == null) continue;
            total += component == 0 ? score.fsr.score : component == 1 ? score.bracing.score : score.spine.score;
            count++;
        }
        return count == 0 ? 0f : total / count;
    }

    private static string RepScoreList(List<SafeLiftScoreResult> scores)
    {
        if (scores == null || scores.Count == 0) return "no rep evidence";
        var text = string.Empty;
        for (var i = 0; i < scores.Count; i++)
            text += (i > 0 ? ", " : string.Empty) + "rep " + (i + 1) + " " + (scores[i] == null ? "--" : scores[i].totalScore.ToString("0.0"));
        return text;
    }

    private static string RepScoreDetails(string label, List<SafeLiftScoreResult> scores)
    {
        var text = label + "\n";
        for (var i = 0; i < 3; i++)
        {
            var score = scores != null && i < scores.Count ? scores[i] : null;
            if (score == null)
            {
                text += "  REP " + (i + 1) + "  --\n";
                continue;
            }

            var fsr = score.fsr != null ? score.fsr.score : 0f;
            var bracing = score.bracing != null ? score.bracing.score : 0f;
            var spine = score.spine != null ? score.spine.score : 0f;
            text += "  REP " + (i + 1) + "  " + score.totalScore.ToString("0.0") + "/100"
                + "  (FSR -" + Mathf.Max(0f, 35f - fsr).ToString("0.0")
                + " | B -" + Mathf.Max(0f, 35f - bracing).ToString("0.0")
                + " | S -" + Mathf.Max(0f, 30f - spine).ToString("0.0") + ")\n";
        }
        return text.TrimEnd('\n');
    }

    private string SetScoreSummaryText()
    {
        var set1Best = BestRepScore(set1RepScores);
        var set2Best = BestRepScore(set2RepScores);
        var finalBest = set1Best == null ? set2Best
            : set2Best == null ? set1Best
            : set2Best.totalScore > set1Best.totalScore ? set2Best : set1Best;
        if (finalBest == null) return string.Empty;
        var selectedLabel = finalBest == set2Best ? "SET 2" : "SET 1";
        var selectedList = selectedLabel == "SET 2" ? set2RepScores : set1RepScores;
        var selectedRepNumber = selectedList != null ? selectedList.IndexOf(finalBest) + 1 : 0;
        var set1Score = set1Best != null ? set1Best.totalScore : -1f;
        var set2Score = set2Best != null ? set2Best.totalScore : -1f;
        var finalScore = finalBest.totalScore;
        var fsr = finalBest.fsr != null ? finalBest.fsr.score : 0f;
        var bracing = finalBest.bracing != null ? finalBest.bracing.score : 0f;
        var spine = finalBest.spine != null ? finalBest.spine.score : 0f;
        var improvement = set1Score > 0f && set2Score >= 0f
            ? (set2Score - set1Score) / set1Score * 100f
            : 0f;
        var improvementText = improvement > 0f
            ? "실시간 피드백이 제공된 SET 2의 점수가 SET 1에 비해 " + improvement.ToString("0.0") + "% 향상되었습니다."
            : set1Score > 0f && set2Score >= 0f
                ? "실시간 피드백이 제공된 SET 2의 점수가 SET 1에 비해 향상되지 않았습니다."
                : "SET 1과 SET 2의 최고 REP 점수 비교를 기다리는 중입니다.";
        return RepScoreDetails("SET 1", set1RepScores) + "\n"
            + RepScoreDetails("SET 2", set2RepScores) + "\n"
            + "Score Analysis\n"
            + "Final = max(6 REP) = " + finalScore.ToString("0.0") + "/100\n"
            + "Final Grade          " + finalBest.grade + "\n"
            + "Selected REP         " + selectedLabel + " REP " + selectedRepNumber + "\n"
            + "Selected FSR        " + fsr.ToString("0.0") + " / 35 (deduction " + Mathf.Max(0f, 35f - fsr).ToString("0.0") + ")\n"
            + "Selected Bracing    " + bracing.ToString("0.0") + " / 35 (deduction " + Mathf.Max(0f, 35f - bracing).ToString("0.0") + ")\n"
            + "Selected Spine       " + spine.ToString("0.0") + " / 30 (deduction " + Mathf.Max(0f, 30f - spine).ToString("0.0") + ")\n"
            + improvementText;
    }

    private void RefreshFusionRepCount()
    {
        var snapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        if (snapshot == null) return;

        // When a new set is requested, ignore the last packet from the
        // previous set until Fusion publishes its reset value (0).
        if (fusionRepResetPending)
        {
            if (snapshot.repCount != 0) return;
            fusionRepResetPending = false;
            previousFusionRepCount = 0;
        }

        var previousRepCount = fusionRepCount;
        fusionRepCount = Mathf.Max(0, snapshot.repCount);
        fusionRepCountSeen = true;

        // Emit the count cue at the same point where the Fusion counter is
        // accepted. This prevents another backend polling path from updating
        // the cached count first and causing the TTS transition to be missed.
        if (fusionRepCount > previousRepCount &&
            (State == SafeLiftScreenState.Set1Practice || State == SafeLiftScreenState.Set2LiveFeedback))
        {
            PlaySquatCountAudio(fusionRepCount);
        }
    }

    private int CurrentSetRepCount()
    {
        RefreshFusionRepCount();
        if (fusionRepCountSeen) return fusionRepCount;
        return backendConnected ? backendSetRepCount : reps;
    }

    private void UpdateRestCountdownLabel()
    {
        if (State != SafeLiftScreenState.RestBeforeLiveFeedback || content == null)
            return;

        var remaining = Mathf.Max(0, Mathf.CeilToInt(RestSeconds - (Time.unscaledTime - stateStarted)));
        if (restAfterSetTwoPending)
        {
            content.text = "SET 2 완료\n\n잠시 휴식하세요.\n휴식이 끝나면 리포트를 준비합니다.\n\n남은 휴식 시간: " + remaining + "초";
            if (status != null) status.text = "SET 2 후 휴식 · 리포트 준비";
        }
        else
        {
            content.text = "SET 1 완료\n\n잠시 휴식하세요.\nSET 2부터 실시간 피드백이 제공됩니다.\n\n남은 휴식 시간: " + remaining + "초";
            if (status != null) status.text = "SET 1 후 휴식 · SET 2 준비";
        }
    }

    private void UpdateRestCountdown()
    {
        if (State != SafeLiftScreenState.RestBeforeLiveFeedback || content == null)
            return;

        var remaining = Mathf.Max(0, Mathf.CeilToInt(RestSeconds - (Time.unscaledTime - stateStarted)));
        content.text = "SET 1 완료\n\n잠시 휴식하세요.\nSET 2부터 실시간 피드백이 제공됩니다.\n\n남은 휴식 시간: " + remaining + "초";
        if (status != null)
            status.text = "휴식 중 · SET 2 준비";
    }

    private void LateUpdate()
    {
        // XR camera pose is finalized after Update. Reframe from the final head pose
        // so the world-space dashboard cannot lag one frame behind the user's gaze.
        if (IsSafeLiftXrRuntimeActive())
            ReframeCanvasForViewer();
    }

    private void UpdateBackendDrivenFlow()
    {
        if (!sessionStarted) return;

        var backendElapsed = Time.unscaledTime - stateStarted;
        // Fusion is the authoritative SET 2 counter. If the backend report
        // endpoint is silent or returns an empty body, do not leave the user
        // trapped on the breathing screen forever. After the local report
        // window, open the review with the available rule-based score/evidence.
        if (State == SafeLiftScreenState.BreathingReportGenerating &&
            setTwoRestConsumed &&
            !backendReportReady &&
            backendElapsed >= Mathf.Max(ReportSeconds, 20f))
        {
            backendReportReady = true;
            if (string.IsNullOrWhiteSpace(latestReportText))
                latestReportText = "Backend report body unavailable. Showing the available rule-based score and sensor evidence.";
            SetState(SafeLiftScreenState.PostRepReview);
            return;
        }
        if (State == SafeLiftScreenState.RestBeforeLiveFeedback && backendRestPending && backendElapsed >= RestSeconds)
        {
            backendRestPending = false;
            if (restAfterSetTwoPending)
            {
                restAfterSetTwoPending = false;
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            }
            else
            {
                SetState(SafeLiftScreenState.Set2LiveFeedback);
            }
        }

    }

    public void Play()
    {
        if (DemoMode) StartDemoMode();
        else StartRealMode();
    }

    public void StartDemoMode()
    {
        backendConnected = false;
        backendRestPending = false;
        setOneRestConsumed = false;
        setTwoRestConsumed = false;
        restAfterSetTwoPending = false;
        backendReportReady = false;
        latestReportText = string.Empty;
        latestBackendState = null;
        latestBackendHealth = null;
        latestSetComparison = null;
        DemoMode = true;
        AutoAdvance = false;
        sessionStarted = true;
        waitingForBackendReset = false;
        phaseRecorder?.ResetSnapshots();
        realtimeFeedback?.ClearState();
        SetState(SafeLiftScreenState.TutorialCalibration);
    }

    public void StartRealMode()
    {
        // Desktop/Editor runs the backend locally. Re-apply this at the
        // actual start action as a final guard against an old saved Quest IP.
        if (Application.isEditor)
            SetBackendUrl("http://127.0.0.1:8090");

        DemoMode = false;
        AutoAdvance = false;
        backendReportReady = false;
        latestReportText = string.Empty;
        sessionStarted = false;
        waitingForBackendReset = false;
        setOneRestConsumed = false;
        setTwoRestConsumed = false;
        restAfterSetTwoPending = false;

        if (!IsRealModeLaunchReady())
        {
            if (status != null)
                status.text = RealModeLaunchBlockerStatus();
            RenderState(SafeLiftScreenState.StartDashboard);
            return;
        }

        sessionStarted = true;
        waitingForBackendReset = true;
        backendBridge?.SendResetSessionCommand();
        phaseRecorder?.ResetSnapshots();
        SetState(SafeLiftScreenState.TutorialCalibration);
        if (status != null)
            status.text = "실전 모드: 백엔드 세션 초기화 후 튜토리얼/보정을 시작합니다. SET 1은 백엔드 rep count로만 진행됩니다.";
    }
    public void Next() => PrimaryAction();
    public void Back()
    {
        if (BlockManualBackInRealMode()) return;
        SetState(PreviousState(State));
    }
    public void ShowReview()
    {
        if (!DemoMode && !backendReportReady)
        {
            if (status != null) status.text = "실전 모드: 백엔드 리포트가 준비된 뒤에만 리뷰가 열립니다.";
            return;
        }
        SetState(SafeLiftScreenState.PostRepReview);
    }

    public void ShowQrShare()
    {
        if (!DemoMode && !backendReportReady)
        {
            if (status != null) status.text = "실전 모드: 백엔드 리포트가 준비된 뒤에만 QR 공유가 열립니다.";
            return;
        }
        SetState(SafeLiftScreenState.QRShare);
    }
    public void ReturnToDashboard() => SetState(SafeLiftScreenState.StartDashboard);

    public void PrimaryAction()
    {
        if (State == SafeLiftScreenState.TutorialCalibration)
        {
            NextTutorialStep();
            return;
        }

        if (BlockManualAdvanceInRealMode()) return;

        switch (State)
        {
            case SafeLiftScreenState.StartDashboard:
                Play();
                break;
            case SafeLiftScreenState.TutorialCalibration:
                if (!DemoMode && ((!IsBackendCalibrationReady() && !IsUnityAvatarCalibrationReady()) || !IsBackendSensorReady()))
                {
                    status.text = "실전 모드: 백엔드 보정/센서 준비가 끝나야 SET 1로 넘어갈 수 있습니다. " + RealModePreflightStatus();
                    return;
                }
                SetState(SafeLiftScreenState.Set1Practice);
                break;
            case SafeLiftScreenState.Set1Practice:
                AdvanceRep(SafeLiftScreenState.RestBeforeLiveFeedback);
                break;
            case SafeLiftScreenState.RestBeforeLiveFeedback:
                if (restAfterSetTwoPending)
                {
                    restAfterSetTwoPending = false;
                    SetState(SafeLiftScreenState.BreathingReportGenerating);
                }
                else
                {
                    SetState(SafeLiftScreenState.Set2LiveFeedback);
                }
                break;
            case SafeLiftScreenState.Set2LiveFeedback:
                AdvanceRep(SafeLiftScreenState.BreathingReportGenerating);
                break;
            case SafeLiftScreenState.BreathingReportGenerating:
                SetState(SafeLiftScreenState.PostRepReview);
                break;
            case SafeLiftScreenState.PostRepReview:
                SetState(SafeLiftScreenState.QRShare);
                break;
            case SafeLiftScreenState.QRShare:
                ReturnToDashboard();
                break;
        }
    }

    private bool BlockManualAdvanceInRealMode()
    {
        if (DemoMode) return false;
        if (State == SafeLiftScreenState.StartDashboard) return false;
        if (State == SafeLiftScreenState.PostRepReview || State == SafeLiftScreenState.QRShare) return false;

        if (!backendConnected)
        {
            if (status != null)
                status.text = "실전 모드: 백엔드 /api/session_state 연결 전까지 진행할 수 없습니다. 백엔드를 실행하거나 데모 모드로 전환하세요.";
            return true;
        }

        if (State == SafeLiftScreenState.TutorialCalibration && !IsBackendCalibrationReady())
        {
            if (status != null)
                status.text = "실전 모드: SET 1 시작 전 백엔드 보정 완료를 기다리는 중입니다.";
            return true;
        }

        if (State == SafeLiftScreenState.Set1Practice || State == SafeLiftScreenState.Set2LiveFeedback)
        {
            if (status != null)
                status.text = "실전 모드: 반복 횟수는 백엔드가 계산합니다. 스쿼트를 수행하면 Unity가 백엔드 상태를 따라갑니다.";
            return true;
        }

        if (State == SafeLiftScreenState.RestBeforeLiveFeedback)
        {
            if (status != null)
                status.text = "실전 모드: SET 1 → SET 2 휴식 전환을 기다리는 중입니다.";
            return true;
        }

        if (State == SafeLiftScreenState.BreathingReportGenerating && !backendReportReady)
        {
            if (status != null)
                status.text = "실전 모드: 운동 후 리뷰 전 백엔드 리포트 준비를 기다리는 중입니다.";
            return true;
        }

        return false;
    }

    private bool BlockManualBackInRealMode()
    {
        if (DemoMode) return false;
        if (State == SafeLiftScreenState.StartDashboard ||
            State == SafeLiftScreenState.PostRepReview ||
            State == SafeLiftScreenState.QRShare)
            return false;

        if (status != null)
            status.text = "실전 모드: 백엔드 기반 운동 진행 중에는 수동 되감기 단축키가 잠겨 있습니다.";
        return true;
    }

    private bool IsBackendCalibrationReady()
    {
        if (latestBackendState == null) return false;
        var calibration = latestBackendState.calibration_status ?? string.Empty;
        if (calibration.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return latestBackendState.calibration_progress >= .99f;
    }

    private bool IsUnityAvatarCalibrationReady()
    {
        var legIK = FindObjectOfType<MediaPipeLegIKDriver>();
        return legIK != null && legIK.IsCalibrated;
    }

    private bool IsBackendSensorReady()
    {
        if (latestBackendState == null) return false;
        if (latestBackendState.sensor_ready) return true;
        var mode = latestBackendState.sensor_mode ?? string.Empty;
        return mode.IndexOf("mock", StringComparison.OrdinalIgnoreCase) >= 0 ||
               mode.IndexOf("serial", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SensorStatusText(SafeLiftState state)
    {
        if (state == null) return "NO BACKEND SENSOR STATE";
        if (!string.IsNullOrWhiteSpace(state.sensor_status)) return state.sensor_status;
        var mode = string.IsNullOrWhiteSpace(state.sensor_mode) ? "UNKNOWN" : state.sensor_mode;
        return state.sensor_ready ? mode + " SENSOR READY" : mode + " SENSOR NOT READY";
    }

    private static string ExceptionStatusText(SafeLiftState state)
    {
        if (state == null) return string.Empty;

        var code = state.exception_code ?? string.Empty;
        var message = state.exception_message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code) || code.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (state.report_timeout)
                return "REPORT TIMEOUT: AI 리포트 생성이 지연되고 있습니다. 기본 분석 결과를 먼저 확인해주세요.";
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            if (code.Equals("CalibrationFailed", StringComparison.OrdinalIgnoreCase))
                message = "초기 보정에 실패했습니다. 다시 측정해주세요.";
            else if (code.Equals("TrackingBad", StringComparison.OrdinalIgnoreCase))
                message = "전신이 화면에 들어오도록 위치를 조정해주세요.";
            else if (code.Equals("SensorDisconnected", StringComparison.OrdinalIgnoreCase))
                message = "센서 연결이 불안정합니다. Mock sensor 값으로 표시합니다.";
            else if (code.Equals("ReportTimeout", StringComparison.OrdinalIgnoreCase))
                message = "AI 리포트 생성이 지연되고 있습니다. 기본 분석 결과를 먼저 확인해주세요.";
            else
                message = code;
        }

        var level = string.IsNullOrWhiteSpace(state.exception_level) ? "warning" : state.exception_level.ToUpperInvariant();
        return level + ": " + message;
    }

    public void ToggleViewMode()
    {
        webcamFocus = !webcamFocus;
        ApplyViewModeLayout(State);
        if (status != null) status.text = "View Mode: " + ViewModeLabel();
        if (State == SafeLiftScreenState.StartDashboard || State == SafeLiftScreenState.Set1Practice || State == SafeLiftScreenState.Set2LiveFeedback)
            RenderState(State);
    }

    private string ViewModeLabel()
    {
        return webcamFocus ? "웹캠 크게 / 아바타 미리보기" : "3D 아바타 크게 / 웹캠 미리보기";
    }

    private static string ScenarioLabel(SafeLiftDemoScenario scenario)
    {
        switch (scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return "무릎 안쪽 쏠림";
            case SafeLiftDemoScenario.WeightShift: return "체중 쏠림";
            case SafeLiftDemoScenario.CorePressureDrop: return "코어 압력 저하";
            case SafeLiftDemoScenario.ComplexError: return "복합 오류";
            default: return "정상";
        }
    }

    private void UpdateViewModeButton()
    {
        if (viewModeButtonText == null) return;
        viewModeButtonText.text = webcamFocus ? "화면: 웹캠" : "화면: 아바타";
    }

    private void ApplyViewModeLayout(SafeLiftScreenState screen)
    {
        // SET2 header coordinates must survive every live-frame layout pass.
        // The webcam texture refresh calls this method repeatedly.
        if (screen == SafeLiftScreenState.Set2LiveFeedback)
        {
            if (title != null) title.rectTransform.anchoredPosition = new Vector2(360f, 420f);
            if (subtitle != null) subtitle.rectTransform.anchoredPosition = new Vector2(360f, 390f);
            if (progressText != null) progressText.rectTransform.anchoredPosition = new Vector2(360f, 362f);
            if (content != null)
            {
       content.rectTransform.anchorMin = new Vector2(1f, 0.5f);
       content.rectTransform.anchorMax = new Vector2(1f, 0.5f);
       content.rectTransform.pivot = new Vector2(1f, 0.5f);
                content.rectTransform.anchoredPosition = new Vector2(-300f, 45f);
            }
        }
        else if (screen == SafeLiftScreenState.PostRepReview)
        {
            // Re-apply the review layout whenever the view-mode/layout pass
            // runs. This prevents a late canvas refresh from restoring the
            // old left/right positions after the report screen is shown.
            if (contentFrame != null)
            {
                contentFrame.rectTransform.anchoredPosition = new Vector2(-140f, 5f);
                contentFrame.rectTransform.sizeDelta = new Vector2(520f, 540f);
            }
            if (reportScrollViewport != null)
            {
                reportScrollViewport.GetComponent<RectTransform>().anchoredPosition = new Vector2(-510f, 5f);
            }
            if (content != null && content.transform.parent == (reportScrollViewport != null ? reportScrollViewport.transform : null))
            {
                content.rectTransform.anchorMin = new Vector2(0f, 1f);
                content.rectTransform.anchorMax = new Vector2(0f, 1f);
                content.rectTransform.pivot = new Vector2(0f, 1f);
                content.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        UpdateViewModeButton();

        // In avatar mode the exercise composition is deliberately split into
        // two clear columns: the live avatar occupies the viewer's left side
        // and the information card stays on the right.  The card is a child
        // of the world-space canvas, so moving its RectTransform (rather than
        // changing world position) keeps XR ray coordinates and text layout
        // stable.
        if (card != null)
        {
            var cardRt = card.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                // The dashboard itself must always stay centered in the HMD.
                // The previous avatar-mode offset moved the whole card to the
                // right, which made every exercise screen appear off-center
                // and clipped the left side in Quest/Editor preview.
                var offset = Vector2.zero;
                cardRt.anchoredPosition = offset;
                // Some world-space Canvas layouts retain an old localPosition
                // after their anchors are rebuilt by the XR UI binder. Keep
                // both representations explicit so the right-column offset
                // survives that binder pass.
                cardRt.localPosition = new Vector3(offset.x, offset.y, cardRt.localPosition.z);
            }
        }

        var isExerciseView = screen == SafeLiftScreenState.Set1Practice || screen == SafeLiftScreenState.Set2LiveFeedback;
        // The dashboard is a preflight screen. Show the live Avatar only once
        // the user enters calibration or an exercise set; leaving it on the
        // first screen makes the dashboard look like the exercise view and
        // hides the fact that REAL mode is waiting for the backend.
        var showAvatarView = screen == SafeLiftScreenState.TutorialCalibration || isExerciseView;
        if (avatarUIPreview != null)
            avatarUIPreview.SetVisible(showAvatarView);
        if (avatarPreviewFrame != null)
            avatarPreviewFrame.gameObject.SetActive(showAvatarView);
        if (webcamPreview != null)
            webcamPreview.gameObject.SetActive(isExerciseView);
        if (webcamFrame != null)
            webcamFrame.gameObject.SetActive(isExerciseView);
        if (webcamPreview == null) return;

        var tutorialAvatarLayout = screen == SafeLiftScreenState.TutorialCalibration;
        var avatarX = tutorialAvatarLayout ? -45f : -315f;
        var avatarSize = tutorialAvatarLayout ? new Vector2(220f, 220f) : new Vector2(300f, 455f);
        var avatarFrameSize = tutorialAvatarLayout ? new Vector2(240f, 240f) : new Vector2(318f, 473f);
        if (avatarPreview != null)
        {
            avatarPreview.rectTransform.anchoredPosition = new Vector2(avatarX, 45f);
            avatarPreview.rectTransform.sizeDelta = avatarSize;
        }
        if (avatarPreviewFrame != null)
        {
            avatarPreviewFrame.rectTransform.anchoredPosition = new Vector2(avatarX, 45f);
            avatarPreviewFrame.rectTransform.sizeDelta = avatarFrameSize;
        }
        if (!isExerciseView)
        {
            webcamPreview.rectTransform.sizeDelta = new Vector2(170, 96);
            webcamPreview.rectTransform.anchoredPosition = new Vector2(365, 145);
            if (webcamFrame != null)
            {
                webcamFrame.rectTransform.sizeDelta = new Vector2(190, 116);
                webcamFrame.rectTransform.anchoredPosition = new Vector2(365, 145);
            }
            return;
        }

        // Set2 uses a balanced left media area: the webcam matches the avatar
        // size and sits beside it without entering the right feedback card.
        {
            webcamPreview.rectTransform.sizeDelta = State == SafeLiftScreenState.Set2LiveFeedback
                ? new Vector2(300, 455)
                : new Vector2(170, 96);
            webcamPreview.rectTransform.anchoredPosition = State == SafeLiftScreenState.Set2LiveFeedback
                ? new Vector2(25, 45)
                : new Vector2(360, 145);
            if (webcamFrame != null)
            {
                webcamFrame.rectTransform.sizeDelta = webcamPreview.rectTransform.sizeDelta + new Vector2(18, 18);
                webcamFrame.rectTransform.anchoredPosition = webcamPreview.rectTransform.anchoredPosition;
                webcamFrame.transform.SetAsLastSibling();
            }
            webcamPreview.transform.SetAsLastSibling();
        }

        if (screen == SafeLiftScreenState.Set1Practice)
            ApplySet1VisualLayout();
    }

    private void ApplySet1VisualLayout()
    {
        if (avatarPreview != null)
        {
            avatarPreview.rectTransform.anchoredPosition = new Vector2(-165f, 45f);
            avatarPreview.rectTransform.sizeDelta = new Vector2(300f, 455f);
        }
        if (avatarPreviewFrame != null)
        {
            avatarPreviewFrame.rectTransform.anchoredPosition = new Vector2(-165f, 45f);
            avatarPreviewFrame.rectTransform.sizeDelta = new Vector2(318f, 473f);
        }
        if (webcamPreview != null)
        {
            webcamPreview.rectTransform.anchoredPosition = new Vector2(175f, 45f);
            webcamPreview.rectTransform.sizeDelta = new Vector2(300f, 455f);
            webcamPreview.transform.SetAsLastSibling();
        }
        if (webcamFrame != null)
        {
            webcamFrame.rectTransform.anchoredPosition = new Vector2(175f, 45f);
            webcamFrame.rectTransform.sizeDelta = new Vector2(318f, 473f);
            webcamFrame.transform.SetAsLastSibling();
        }

        // Re-apply the SET 1 information column whenever the live frame
        // layout is refreshed, so it cannot fall back to the old left-side
        // position after a webcam update.
        if (content != null)
        {
            content.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            content.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            content.rectTransform.pivot = new Vector2(0f, 0.5f);
            content.rectTransform.anchoredPosition = new Vector2(1030f, 45f);
            content.rectTransform.sizeDelta = new Vector2(430f, 330f);
            content.alignment = TextAlignmentOptions.TopLeft;
            content.enableWordWrapping = true;
        }
    }

    public void ApplyLiveFrame(Texture2D texture)
    {
        if (texture == null || webcamPreview == null)
        {
            SetLiveFrameStatus(false, "texture/preview missing");
            return;
        }
        if (currentWebcamTexture != null && currentWebcamTexture != texture) Destroy(currentWebcamTexture);
        currentWebcamTexture = texture;
        webcamPreview.texture = texture;
        webcamPreview.transform.SetAsLastSibling();
        liveFrameReceived = true;
        liveFrameStatus = "LIVE";
        liveFrameError = string.Empty;
        ApplyViewModeLayout(State);
    }

    public void SetLiveFrameStatus(bool available, string error)
    {
        if (available)
        {
            liveFrameStatus = "LIVE";
            liveFrameError = string.Empty;
            return;
        }

        liveFrameStatus = liveFrameReceived ? "STALE" : "WAIT";
        liveFrameError = string.IsNullOrWhiteSpace(error) ? "no frame" : error;
    }

    public void ConfigureBackendUrl(string backendUrl)
    {
        if (Application.isEditor)
            currentBackendUrl = "http://127.0.0.1:8090";
        else if (!string.IsNullOrWhiteSpace(backendUrl))
            currentBackendUrl = NormalizeBackendUrl(backendUrl);

        if (backendUrlInput != null && !string.Equals(backendUrlInput.text, currentBackendUrl, StringComparison.Ordinal))
            backendUrlInput.SetTextWithoutNotify(currentBackendUrl);

        if (backendUrlHint != null)
            backendUrlHint.text = "Backend URL: " + currentBackendUrl + "\nQuest에서는 127.0.0.1 대신 노트북/PC IP를 입력하세요.";

        UpdateRealModeGateVisibility(State);
        qrShare?.ConfigureBackendUrl(backendUrl);
    }

    public void ApplyBackendUrlFromInput()
    {
        var nextUrl = backendUrlInput != null ? backendUrlInput.text : currentBackendUrl;
        SetBackendUrl(nextUrl);
    }

    public void SetBackendUrl(string backendUrl)
    {
        var normalized = Application.isEditor
            ? "http://127.0.0.1:8090"
            : NormalizeBackendUrl(backendUrl);
        if (normalized.IndexOf("PC_IP", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("YOUR_PC", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (status != null)
                status.text = "잘못된 Backend URL입니다. PC_IP를 실제 PC IPv4 주소로 바꾼 뒤 APPLY URL을 누르세요.";
            UpdateRealModeGateVisibility(State);
            return;
        }

        currentBackendUrl = normalized;
        if (backendUrlInput != null) backendUrlInput.SetTextWithoutNotify(currentBackendUrl);
        if (backendBridge == null) backendBridge = GetComponent<SafeLiftBackendBridge>() ?? FindObjectOfType<SafeLiftBackendBridge>();
        if (backendBridge != null) backendBridge.ConfigureServerUrl(currentBackendUrl);
        else ConfigureBackendUrl(currentBackendUrl);

        if (status != null)
            status.text = "Backend URL set to " + currentBackendUrl + ". " + QuestBackendUrlWarning() + " REAL mode will wait for /api/session_state.";
        UpdateRealModeGateVisibility(State);
    }

    public void UseLocalBackendUrl() => SetBackendUrl("http://127.0.0.1:8090");

    public void UseQuestBackendUrlHint()
    {
        var localIpv4 = ResolveLocalIpv4();
        if (backendUrlInput != null)
            backendUrlInput.SetTextWithoutNotify(string.IsNullOrEmpty(localIpv4) ? "http://PC_IP:8090" : "http://" + localIpv4 + ":8090");
        if (status != null)
            status.text = string.IsNullOrEmpty(localIpv4)
                ? "PC IPv4를 찾지 못했습니다. 실제 PC IPv4 주소를 입력한 뒤 APPLY URL을 누르세요."
                : "PC IPv4를 자동 입력했습니다. backend 실행 후 APPLY URL을 누르세요.";
    }

    private static string ResolveLocalIpv4()
    {
        try
        {
            foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) continue;
                var value = address.ToString();
                if (!value.StartsWith("169.254.", StringComparison.Ordinal)) return value;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SafeLift could not resolve local PC IPv4: " + ex.Message);
        }
        return string.Empty;
    }

    public void SelectScenario(int value)
    {
        // Scenario selection is retained only for backward compatibility with
        // old Inspector events. Live mode is driven by backend measurements.
        Scenario = SafeLiftDemoScenario.NoScenario;
        if (status != null) status.text = "Scenario: " + Scenario;
        if (scenarioButtonText != null) scenarioButtonText.text = "시나리오: " + ScenarioLabel(Scenario);
        if (State == SafeLiftScreenState.Set2LiveFeedback || State == SafeLiftScreenState.PostRepReview) RenderState(State);
    }

    public void ToggleSensorMode()
    {
        DemoMode = !DemoMode;
        AutoAdvance = false;
        backendConnected = false;
        backendRestPending = false;
        setOneRestConsumed = false;
        setTwoRestConsumed = false;
        restAfterSetTwoPending = false;
        backendReportReady = false;
        latestReportText = string.Empty;
        latestBackendState = null;
        latestBackendHealth = null;
        latestSetComparison = null;
        realtimeFeedback?.ClearState();
        UpdateSensorModeButton();
        if (status != null)
            status.text = DemoMode ? "센서 모드: 데모 / 모의 자동 진행" : "센서 모드: 실전 / 백엔드 기반 진행";
        if (State == SafeLiftScreenState.StartDashboard) RenderState(State);
        else UpdateRealModeGateVisibility(State);
    }

    public void ApplyBackendState(SafeLiftState state)
    {
        if (state == null) return;

        backendConnected = true;
        latestBackendState = state;
        latestSetComparison = state.set_comparison;
        DemoMode = false;
        AutoAdvance = false;
        // Preserve the value received from the backend before updating the
        // cached fields. The reset gate must inspect the incoming response;
        // checking backendRepCount after assigning it made every stale session
        // look as if reset_rep had failed forever.
        var previousRepCount = backendRepCount;
        var incomingRepCount = Mathf.Max(0, state.rep_count);
        var incomingSetRepCount = Mathf.Max(0, state.set_rep_count);
        backendRepCount = incomingRepCount;
        backendSetRepCount = incomingSetRepCount;
        RefreshFusionRepCount();
        Scenario = ScenarioFromBackendIssue(state.main_issue, state.detected_errors);
        phaseRecorder?.CaptureFromState(state);
        tutorialUI?.ApplyBackendCalibration(state);
        if (scenarioButtonText != null) scenarioButtonText.text = "BACKEND: " + Scenario.ToString().ToUpperInvariant();

        if (!sessionStarted)
        {
            if (State != SafeLiftScreenState.StartDashboard)
                SetState(SafeLiftScreenState.StartDashboard);
            else
                RenderState(SafeLiftScreenState.StartDashboard);
            status.text = RealModePreflightStatus();
            UpdateRealModeGateVisibility(State);
            realtimeFeedback?.ClearState();
            return;
        }

        if (waitingForBackendReset)
        {
            var reportAlreadyReady = string.Equals(state.report_status, "ready", StringComparison.OrdinalIgnoreCase);
            if (incomingRepCount > 0 || reportAlreadyReady)
            {
                status.text = "실전 모드: 이전 백엔드 세션 초기화 대기 중입니다. 백엔드 /command reset_rep 반영 후 0/3부터 시작합니다.";
                UpdateRealModeGateVisibility(State);
                return;
            }
            waitingForBackendReset = false;
        }

        var exceptionStatus = ExceptionStatusText(state);
        if (!string.IsNullOrWhiteSpace(exceptionStatus) && status != null)
            status.text = exceptionStatus;

        if (State == SafeLiftScreenState.StartDashboard && backendRepCount == 0)
        {
            status.text = RealModePreflightStatus();
            UpdateRealModeGateVisibility(State);
            return;
        }
        if (State == SafeLiftScreenState.TutorialCalibration && backendRepCount == 0)
        {
            // Keep the tutorial visible even when the backend baseline was
            // already ready before Play. Calibration status is shown inside
            // the tutorial; the user advances with the tutorial action.
            return;
        }
        if (State == SafeLiftScreenState.RestBeforeLiveFeedback && backendRestPending) return;

        // The backend reports SET 2 as soon as rep 3 is completed (its
        // set_index is already 2 at that boundary). Gate the rest on the
        // completed rep count, not on set_index, otherwise SET 1 rest is
        // skipped and the UI jumps directly into SET 2.
        // Unity polls the backend at a lower rate than the camera loop, so the
        // one-frame rep_completed flag can be missed. The count crossing from
        // SET 1's last rep into exactly 3 is an equivalent completion signal.
        var currentFusionRepCount = CurrentSetRepCount();
        // SET2 must not consume the previous SET1 count. SetState() requests
        // a Fusion reset when entering a set; wait until the reset packet (0)
        // has actually arrived before evaluating SET2 completion.
        var fusionSetReady = fusionRepCountSeen && !fusionRepResetPending;
        var crossedFusionBoundary = previousFusionRepCount < TargetReps && currentFusionRepCount >= TargetReps;
        previousFusionRepCount = currentFusionRepCount;
        var crossedSetOneBoundary = previousRepCount < TargetReps && incomingRepCount >= TargetReps;
        // A delayed poll can jump from a pre-boundary count directly past 3
        // (for example 4 -> 10 in the fast mock session). The rest screen is
        // still mandatory; do not let the report/set-2 branches consume that
        // boundary before the five-second countdown has run.
        var fusionOwnsRepGate = fusionRepCountSeen;
        var setOneRepGateReached = fusionOwnsRepGate
            ? currentFusionRepCount >= TargetReps
            : incomingRepCount >= TargetReps;
        if (State == SafeLiftScreenState.Set1Practice &&
            !setOneRestConsumed &&
            (state.rep_completed || crossedSetOneBoundary || crossedFusionBoundary) &&
             setOneRepGateReached)
        {
            backendRestPending = true;
            setOneRestConsumed = true;
            SetState(SafeLiftScreenState.RestBeforeLiveFeedback);
        }
        else if (State == SafeLiftScreenState.Set2LiveFeedback &&
                 fusionSetReady && currentFusionRepCount >= TargetReps)
        {
            if (!setTwoRestConsumed)
            {
                setTwoRestConsumed = true;
                restAfterSetTwoPending = false;
                backendRestPending = false;
                // Fusion has completed the required SET 2 reps. Start the
                // final report immediately; do not wait for the separate
                // backend MediaPipe counter or a second rest transition.
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            }
        }
        else if (backendRepCount >= TargetReps * 2 &&
                 (State != SafeLiftScreenState.Set2LiveFeedback || fusionSetReady))
        {
            if (!setTwoRestConsumed && State == SafeLiftScreenState.Set2LiveFeedback)
            {
                setTwoRestConsumed = true;
                restAfterSetTwoPending = true;
                backendRestPending = true;
                SetState(SafeLiftScreenState.RestBeforeLiveFeedback);
            }
            else if (State != SafeLiftScreenState.RestBeforeLiveFeedback &&
                State != SafeLiftScreenState.BreathingReportGenerating &&
                State != SafeLiftScreenState.PostRepReview &&
                State != SafeLiftScreenState.QRShare)
            {
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            }
        }
        // After SET 2 is complete, the backend continues to publish SET 2 / 3
        // for a short time while the final rest/report transition is pending.
        // Do not let that stale completion packet re-enter live SET 2 from
        // the mandatory post-SET2 rest screen.
        else if (state.set_index >= 2 && fusionSetReady && currentFusionRepCount >= TargetReps &&
                 State != SafeLiftScreenState.RestBeforeLiveFeedback &&
                 State != SafeLiftScreenState.BreathingReportGenerating &&
                 State != SafeLiftScreenState.PostRepReview &&
                 State != SafeLiftScreenState.QRShare &&
                 !restAfterSetTwoPending)
        {
            if (State != SafeLiftScreenState.Set2LiveFeedback)
                SetState(SafeLiftScreenState.Set2LiveFeedback);
        }
        else if (State != SafeLiftScreenState.Set2LiveFeedback &&
                 State != SafeLiftScreenState.BreathingReportGenerating &&
                 !setOneRestConsumed &&
                 State != SafeLiftScreenState.TutorialCalibration &&
                 State != SafeLiftScreenState.Set1Practice)
        {
            SetState(SafeLiftScreenState.Set1Practice);
        }

        realtimeFeedback?.ApplyState(state);

        // Once the final-report flow starts, the report UI owns the main
        // content text. Do not let the high-frequency sensor refresh write
        // exercise instructions over it, even if backendRepCount is still
        // behind while Fusion is authoritative.
        if (State == SafeLiftScreenState.BreathingReportGenerating ||
            State == SafeLiftScreenState.PostRepReview ||
            State == SafeLiftScreenState.QRShare)
        {
            return;
        }

        var statusPrefix = string.IsNullOrWhiteSpace(exceptionStatus)
            ? "Backend live - " + SensorStatusText(state)
            : exceptionStatus + " / " + SensorStatusText(state);
        status.text = statusPrefix + " - Rep " + currentFusionRepCount + " / " + TargetReps + " - Issue " + MainIssue();
        content.text = "SET " + (state.set_index >= 2 ? "2" : "1") + (state.set_index >= 2 ? " - LIVE FEEDBACK" : " - PRACTICE")
            + "\nView: " + ViewModeLabel()
            + "\nSET " + (state.set_index >= 2 ? "2" : "1") + " · REP " + backendSetRepCount + " / " + TargetReps
            + "\nPhase: " + (state.phase ?? "Unknown")
            + "\nTracking: " + (state.live_tracking_quality ?? "UNKNOWN")
            + "\nSensor: " + SensorStatusText(state)
            + (string.IsNullOrWhiteSpace(exceptionStatus) ? "" : "\nSystem notice: " + exceptionStatus)
            + "\n\n" + (state.current_cue ?? "No active cue")
            + "\n\nDetected errors: " + (state.detected_errors == null || state.detected_errors.Length == 0 ? "None" : string.Join(", ", state.detected_errors));
        // Backend polling can redraw the generic content panel with its own
        // cumulative counter. Re-render the exercise card so Fusion's
        // per-set repCount remains the visible source of truth.
        if (State == SafeLiftScreenState.Set1Practice || State == SafeLiftScreenState.Set2LiveFeedback)
            RenderState(State);
    }

    public void UseMockFallback()
    {
        backendConnected = false;
        backendRestPending = false;
        latestBackendState = null;
        latestBackendHealth = null;
        latestSetComparison = null;
        realtimeFeedback?.ClearState();
        if (DemoMode)
        {
            AutoAdvance = false;
            if (status != null) status.text = "백엔드 연결이 끊어졌습니다. Mock Mode로 전환합니다. 다음 버튼으로 수동 진행하세요.";
        }
        else
        {
            AutoAdvance = false;
            if (status != null) status.text = "실전 모드: 백엔드 연결이 끊겼습니다. 모의 자동 진행은 잠겨 있습니다. 백엔드 연결을 확인해주세요.";
        }
    }

    public void ApplyBackendHealth(SafeLiftHealth health)
    {
        if (health == null) return;
        latestBackendHealth = health;
        if (State == SafeLiftScreenState.StartDashboard && !DemoMode)
        {
            status.text = RealModePreflightStatus();
            UpdateRealModeGateVisibility(State);
        }
        UpdateDiagnosticsHud();
    }

    public void ApplyBackendReport(SafeLiftReport report)
    {
        if (report == null) return;
        if (!sessionStarted || waitingForBackendReset) return;
        // Do not let a fast report response replace the mandatory SET 1 rest
        // countdown. The state machine will resume backend progression after
        // the countdown has completed.
        if (State == SafeLiftScreenState.RestBeforeLiveFeedback && backendRestPending)
            return;
        // Report polling can win the race with the session-state poll when the
        // tenth rep completes. SET 2 must still show its own rest countdown
        // before any generating/ready report is allowed to replace the screen.
        if (backendRepCount >= TargetReps * 2 &&
            !setTwoRestConsumed &&
            State == SafeLiftScreenState.Set2LiveFeedback &&
            fusionRepCountSeen &&
            !fusionRepResetPending)
        {
            setTwoRestConsumed = true;
            restAfterSetTwoPending = true;
            backendRestPending = true;
            SetState(SafeLiftScreenState.RestBeforeLiveFeedback);
            return;
        }
        // Fusion is the authoritative counter for the current Unity flow.
        // The separate MediaPipe counter can remain at 0/6 when Fusion has
        // already completed SET 2, so do not discard a valid report response
        // solely because that optional counter is behind.
        var fusionSetTwoComplete = fusionRepCountSeen &&
            !fusionRepResetPending &&
            fusionRepCount >= TargetReps &&
            (State == SafeLiftScreenState.Set2LiveFeedback ||
             State == SafeLiftScreenState.RestBeforeLiveFeedback ||
             State == SafeLiftScreenState.BreathingReportGenerating ||
             State == SafeLiftScreenState.PostRepReview ||
             State == SafeLiftScreenState.QRShare);
        if (backendRepCount < TargetReps * 2 && !fusionSetTwoComplete)
        {
            backendReportReady = false;
            if (State == SafeLiftScreenState.BreathingReportGenerating && !setTwoRestConsumed)
                SetState(backendRepCount >= TargetReps ? SafeLiftScreenState.Set2LiveFeedback : SafeLiftScreenState.Set1Practice);
            return;
        }
        breathingUI?.ApplyReportStatus(report);
        if (report.status == "generating")
        {
            if (State == SafeLiftScreenState.PostRepReview || State == SafeLiftScreenState.QRShare) return;

            DemoMode = false;
            AutoAdvance = false;
            if (State != SafeLiftScreenState.BreathingReportGenerating)
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            title.text = "Waiting for AI Report";
            status.text = "Gemini / template fallback report is generating";
            content.text = "SET 2 COMPLETE\n\nPlease breathe and wait for a moment.\nGenerating AI Coach Report...";
            return;
        }

        if (report.status == "timeout")
        {
            if (State == SafeLiftScreenState.PostRepReview || State == SafeLiftScreenState.QRShare) return;

            DemoMode = false;
            AutoAdvance = false;
            if (State != SafeLiftScreenState.BreathingReportGenerating)
                SetState(SafeLiftScreenState.BreathingReportGenerating);
            title.text = "REPORT DELAYED";
            status.text = "AI report delayed - rule-based summary remains available";
            content.text = "SET 2 COMPLETE\n\nAI report generation is taking longer than expected.\nBasic score, detected issues, and phase snapshots are being prepared first.\n\n"
                + (string.IsNullOrWhiteSpace(report.message) ? "Please keep breathing while the final report finishes." : report.message);
            return;
        }

        if (report.status != "ready") return;

        // A fast/stale report response can arrive while Unity is still on
        // SET 2. Never jump directly to the review screen in that case:
        // the final-rep transition must announce report generation and show
        // the Waiting for AI Report screen first.
        var fusionReportComplete = fusionRepCountSeen &&
            !fusionRepResetPending &&
            fusionRepCount >= TargetReps;
        var backendReportComplete = backendRepCount >= TargetReps * 2;
        if (State == SafeLiftScreenState.Set2LiveFeedback &&
            (fusionReportComplete || backendReportComplete))
        {
            setTwoRestConsumed = true;
            restAfterSetTwoPending = false;
            backendRestPending = false;
            SetState(SafeLiftScreenState.BreathingReportGenerating);
            return;
        }
        if (State == SafeLiftScreenState.RestBeforeLiveFeedback &&
            restAfterSetTwoPending)
        {
            // Keep the mandatory rest countdown visible. The normal rest
            // transition will enter report generation and emit its TTS cue.
            return;
        }

        // Even when Gemini/template generation finishes immediately, keep
        // the report-waiting screen visible for the configured report window
        // so the user can see and hear the completion transition.
        if (State == SafeLiftScreenState.BreathingReportGenerating &&
            Time.unscaledTime - stateStarted < ReportSeconds)
            return;

        if (State == SafeLiftScreenState.QRShare) return;

        DemoMode = false;
        AutoAdvance = false;
        backendReportReady = true;
        latestReportText = string.IsNullOrWhiteSpace(report.report_text)
            ? "Backend report status is READY, but the report body was empty. The rule-based score and sensor evidence are available for review."
            : report.report_text;
        if (State != SafeLiftScreenState.PostRepReview)
        {
            SetState(SafeLiftScreenState.PostRepReview);
            haptics?.ReportReady();
        }
        if (State == SafeLiftScreenState.PostRepReview && content != null)
        {
            var compactReport = BuildCompactReviewContent();
            if (content.text != compactReport)
                content.text = compactReport;
            visualAssets?.ApplyReport(latestBackendState, latestSetComparison, Scenario, FinalUnityScore());
            return;
        }
                title.text = "AI Report and Score Analysis";
        status.text = "백엔드 리포트 준비 완료";
        content.text = "AI 코치 리포트\n\n" + report.report_text + "\n\n" + BackendSetComparisonText() + "\n\nSafeLift 점수와 phase 비교는 리뷰 아바타에서 확인할 수 있습니다.";
        visualAssets?.ApplyReport(latestBackendState, latestSetComparison, Scenario, FinalUnityScore());
        if (State == SafeLiftScreenState.PostRepReview && content != null)
        {
            var compactReport = BuildCompactReviewContent();
            if (content.text != compactReport)
                content.text = compactReport;
        }
    }

    public void SetState(SafeLiftScreenState next)
    {
        if (next == SafeLiftScreenState.StartDashboard)
        {
            sessionStarted = false;
            waitingForBackendReset = false;
        }
        State = next;
        stateStarted = Time.unscaledTime;
        reps = 0;
        nextRepAt = 1f;
        if (next == SafeLiftScreenState.TutorialCalibration)
            tutorialUI?.SetStep(0);
        if (next == SafeLiftScreenState.StartDashboard || next == SafeLiftScreenState.TutorialCalibration)
        {
            backendReportReady = false;
            phaseRecorder?.ResetSnapshots();
        }
        if (next == SafeLiftScreenState.Set1Practice || next == SafeLiftScreenState.Set2LiveFeedback)
        {
            ResetSetScores(next == SafeLiftScreenState.Set1Practice, next == SafeLiftScreenState.Set2LiveFeedback);
            lastSpokenRep = 0;
            if (next == SafeLiftScreenState.Set1Practice)
                lastSet1PhaseLabel = "STANDING";
            fusionRepCount = 0;
            previousFusionRepCount = 0;
            fusionRepCountSeen = false;
            fusionRepResetPending = true;
            RequestFusionRepReset();
            PlaySetStartAudio(next);
        }
        if (next == SafeLiftScreenState.BreathingReportGenerating)
            backendBridge?.SendFinishReportCommand();
        primaryButton.gameObject.SetActive(true);
        if (recalibrationButton != null)
        {
            // Calibration is a session-start operation. Once the six REP
            // sequence begins, changing the baseline would invalidate the
            // already captured REP scores and sensor timeline.
            var canRecalibrate = next == SafeLiftScreenState.TutorialCalibration;
            recalibrationButton.gameObject.SetActive(canRecalibrate);
        }
        title.text = Label(next);
        if (next == SafeLiftScreenState.TutorialCalibration)
            title.text = "Tutorial with Calibration";
        subtitle.text = "SafeLift XR - " + RuntimeModeLabel();
        UpdateSensorModeButton();
        UpdateProgressLabel(next);
        RenderState(next);
        ConfigureTutorialControls(next);
        // TutorialCalibrationPanel is created after the flow controls and can
        // otherwise cover the visible right-side Next button.
        if (flowControls != null) flowControls.transform.SetAsLastSibling();
        if (primaryButton != null) primaryButton.transform.SetAsLastSibling();
        if (rightNextButton != null) rightNextButton.transform.SetAsLastSibling();
        if (recalibrationButton != null) recalibrationButton.transform.SetAsLastSibling();
        BringFlowControlsToFront();
        UpdateDiagnosticsHud();
        Debug.Log("SafeLift XR state -> " + ((int)next + 1) + "/8 " + next);
        tts?.Speak(VoiceCue(next));
        StateChanged?.Invoke(next);
    }

    public void RequestAvatarCalibration()
    {
        if (State != SafeLiftScreenState.TutorialCalibration)
            return;
        calibrationButtonActive = true;
        calibrationButtonStarted = Time.unscaledTime;
        calibrationStartPacketCount = squatSensorReceiver != null && squatSensorReceiver.Latest != null
            ? squatSensorReceiver.Latest.packetCount
            : 0;
        SetCalibrationButtonProgress(0f);
        RequestFusionCalibration();
        var legIK = FindObjectOfType<MediaPipeLegIKDriver>();
        if (legIK == null)
        {
            Debug.LogWarning("SafeLift calibration request ignored: MediaPipeLegIKDriver was not found.");
            return;
        }
        legIK.RequestCalibration();
        if (status != null)
            status.text = "재캘리브레이션 요청됨: 정면을 보고 양발이 보이게 서 있어주세요.";
    }

    private void UpdateCalibrationButtonProgress()
    {
        if (recalibrationButton == null || !calibrationButtonActive) return;

        var elapsedProgress = Mathf.Clamp01(
            (Time.unscaledTime - calibrationButtonStarted) /
            Mathf.Max(.1f, CalibrationButtonDurationSeconds));
        var backendProgress = latestBackendState != null
            ? Mathf.Clamp01(latestBackendState.calibration_progress)
            : 0f;
        var allSensorsReady = AreAllSensorCalibrationsReady();
        var progress = Mathf.Min(.99f, Mathf.Max(elapsedProgress, backendProgress));
        // The detector package completes FSR/Bracing calibration on its own
        // timers. Unity may not receive a dedicated completion flag for every
        // producer, so a completed backend calibration is also an accepted
        // completion barrier after the shared three-second window.
        var calibrationComplete = elapsedProgress >= 1f
            && (allSensorsReady || backendProgress >= 1f);
        if (calibrationComplete)
            progress = 1f;
        SetCalibrationButtonProgress(progress);

        if (calibrationComplete)
        {
            calibrationButtonActive = false;
            SetCalibrationButtonIdle();
        }
    }

    private bool AreAllSensorCalibrationsReady()
    {
        var snapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        if (snapshot == null || snapshot.packetCount <= calibrationStartPacketCount)
            return false;

        var fsrReady = snapshot.fsr != null && snapshot.fsr.valid
            && snapshot.calibration != null && snapshot.calibration.ready;
        var pressureStatus = snapshot.pressure != null && snapshot.pressure.status != null
            ? snapshot.pressure.status.ToUpperInvariant()
            : string.Empty;
        var pressureReady = snapshot.pressure != null && snapshot.pressure.valid
            && snapshot.pressure.pBraced > 0f
            && pressureStatus != "CALIBRATING"
            && pressureStatus != "WAITING_CALIBRATION"
            && pressureStatus != "INVALID";
        var imuReady = snapshot.imu != null && snapshot.imu.valid
            && snapshot.spine != null && snapshot.spine.valid
            && !string.Equals(snapshot.spine.status, "NO_SIGNAL", StringComparison.OrdinalIgnoreCase);
        return fsrReady && pressureReady && imuReady;
    }

    private void SetCalibrationButtonProgress(float progress)
    {
        if (recalibrationButton == null) return;
        var label = recalibrationButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = "보정 중...";
        SetCalibrationButtonColor(new Color(1f, .72f, .08f, 1f));
    }

    private void SetCalibrationButtonIdle()
    {
        if (recalibrationButton == null) return;
        var label = recalibrationButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "보정";
        SetCalibrationButtonColor(new Color(.12f, .55f, .9f, 1f));
    }

    private void SetCalibrationButtonColor(Color color)
    {
        if (recalibrationButton == null) return;
        var image = recalibrationButton.GetComponent<Image>();
        if (image != null) image.color = color;

        var colors = recalibrationButton.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, .15f);
        colors.pressedColor = Color.Lerp(color, Color.black, .12f);
        colors.selectedColor = colors.highlightedColor;
        recalibrationButton.colors = colors;
    }

    public void PreviousTutorialStep()
    {
        if (State != SafeLiftScreenState.TutorialCalibration || tutorialUI == null) return;
        tutorialUI.SetStep(tutorialUI.CurrentStep - 1);
        if (status != null) status.text = "튜토리얼 " + (tutorialUI.CurrentStep + 1) + "/4 단계";
    }

    public void NextTutorialStep()
    {
        if (State != SafeLiftScreenState.TutorialCalibration || tutorialUI == null) return;
        if (tutorialUI.CurrentStep < 3)
        {
            tutorialUI.SetStep(tutorialUI.CurrentStep + 1);
            if (status != null) status.text = "튜토리얼 " + (tutorialUI.CurrentStep + 1) + "/4 단계";
            return;
        }

        if (!DemoMode && ((!IsBackendCalibrationReady() && !IsUnityAvatarCalibrationReady()) || !IsBackendSensorReady()))
        {
            if (status != null)
                status.text = "보정 버튼을 눌러 센서 보정을 완료한 뒤 SET 1로 이동하세요. " + RealModePreflightStatus();
            return;
        }
        SetState(SafeLiftScreenState.Set1Practice);
    }

    private void ConfigureTutorialControls(SafeLiftScreenState next)
    {
        if (primaryButton == null || rightNextButton == null || recalibrationButton == null) return;
        var isTutorial = next == SafeLiftScreenState.TutorialCalibration;
        var isSet1 = next == SafeLiftScreenState.Set1Practice;
        var isSet2 = next == SafeLiftScreenState.Set2LiveFeedback;
        var isRest = next == SafeLiftScreenState.RestBeforeLiveFeedback;
        var isBreathingReport = next == SafeLiftScreenState.BreathingReportGenerating;
        var isPostRepReview = next == SafeLiftScreenState.PostRepReview;
        var isQrShare = next == SafeLiftScreenState.QRShare;
        // The shared title, subtitle, and progress trail stay together above
        // the tutorial image and are centered as one header group.
        if (title != null)
            title.gameObject.SetActive(true);
        if (title != null)
            title.rectTransform.anchoredPosition = isTutorial ? new Vector2(340f, 315f) : (isSet1 ? new Vector2(350f, 400f) : (isSet2 ? new Vector2(360f, 420f) : (isRest ? new Vector2(360f, 396f) : (isBreathingReport ? new Vector2(230f, 326f) : (isPostRepReview ? new Vector2(250f, 396f) : (isQrShare ? new Vector2(250f, 326f) : new Vector2(0f, 226f)))))));
        if (subtitle != null)
            subtitle.rectTransform.anchoredPosition = isTutorial ? new Vector2(340f, 282f) : (isSet1 ? new Vector2(350f, 370f) : (isSet2 ? new Vector2(360f, 390f) : (isRest ? new Vector2(360f, 366f) : (isBreathingReport ? new Vector2(230f, 296f) : (isPostRepReview ? new Vector2(250f, 366f) : (isQrShare ? new Vector2(250f, 296f) : new Vector2(0f, 196f)))))));
        if (progressText != null)
            progressText.rectTransform.anchoredPosition = isTutorial ? new Vector2(340f, 252f) : (isSet1 ? new Vector2(350f, 342f) : (isSet2 ? new Vector2(360f, 362f) : (isRest ? new Vector2(360f, 340f) : (isBreathingReport ? new Vector2(230f, 270f) : (isPostRepReview ? new Vector2(250f, 340f) : (isQrShare ? new Vector2(250f, 270f) : new Vector2(0f, 170f)))))));
        if (isTutorial)
        {
            // The tutorial panel is created after the shared header. Bring
            // the three header texts above the panel/image in render order.
            title.transform.SetAsLastSibling();
            subtitle.transform.SetAsLastSibling();
            progressText.transform.SetAsLastSibling();
        }
        var primaryLabel = primaryButton.GetComponentInChildren<TMP_Text>();
        var rightLabel = rightNextButton.GetComponentInChildren<TMP_Text>();
        var recalibrationLabel = recalibrationButton.GetComponentInChildren<TMP_Text>();
        if (isTutorial)
        {
            calibrationButtonActive = false;
            SetCalibrationButtonIdle();
            primaryButton.onClick.RemoveAllListeners();
            primaryButton.onClick.AddListener(PreviousTutorialStep);
            rightNextButton.onClick.RemoveAllListeners();
            rightNextButton.onClick.AddListener(NextTutorialStep);
            if (primaryLabel != null) primaryLabel.text = "이전";
            if (rightLabel != null) rightLabel.text = "다음";
            if (recalibrationLabel != null) recalibrationLabel.text = "보정";
            primaryButton.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 56f);
            rightNextButton.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 56f);
            primaryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-395f, -120f);
            rightNextButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(395f, -120f);
            recalibrationButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-300f, -250f);
            primaryButton.gameObject.SetActive(true);
            rightNextButton.gameObject.SetActive(true);
            recalibrationButton.gameObject.SetActive(true);
        }
        else
        {
            primaryButton.onClick.RemoveAllListeners();
            primaryButton.onClick.AddListener(PrimaryAction);
            rightNextButton.onClick.RemoveAllListeners();
            rightNextButton.onClick.AddListener(PrimaryAction);
            if (recalibrationLabel != null) recalibrationLabel.text = "자세 다시 보정";
            primaryButton.GetComponent<RectTransform>().sizeDelta = new Vector2(250f, 64f);
            rightNextButton.GetComponent<RectTransform>().sizeDelta = new Vector2(250f, 64f);
            primaryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -270f);
            rightNextButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(700f, -270f);
            // The recalibration button is hidden outside TutorialCalibration;
            // keep its old layout only for inactive-object consistency.
            recalibrationButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(700f, -340f);
            // The start dashboard already has the primary bottom-center
            // start button. Hide the duplicate bottom-right action there.
            rightNextButton.gameObject.SetActive(
                next != SafeLiftScreenState.StartDashboard &&
                next != SafeLiftScreenState.Set2LiveFeedback);
            if (next == SafeLiftScreenState.Set2LiveFeedback)
            {
                // SET 2 is driven by the backend/repetition flow; the
                // duplicate manual "반복 완료" action is not needed.
                primaryButton.gameObject.SetActive(false);
            }
            if (next == SafeLiftScreenState.BreathingReportGenerating)
            {
                // Report generation is automatic. Keep both manual actions
                // hidden until the ten-second waiting window completes.
                primaryButton.gameObject.SetActive(false);
                rightNextButton.gameObject.SetActive(false);
            }
            if (next == SafeLiftScreenState.RestBeforeLiveFeedback)
            {
                // Rest is an automatic transition; both SET 2 start buttons
                // are intentionally hidden.
                primaryButton.gameObject.SetActive(false);
                rightNextButton.gameObject.SetActive(false);
            }
		else if (next == SafeLiftScreenState.PostRepReview)
		{
			// The review screen keeps the QR action on the bottom-right;
			// remove the duplicate bottom-center QR button.
			primaryButton.gameObject.SetActive(false);
			rightNextButton.gameObject.SetActive(true);
		}
		else if (next == SafeLiftScreenState.QRShare)
		{
			// Keep the final QR screen's bottom-center restart button and
			// remove the duplicate bottom-right button.
			primaryButton.gameObject.SetActive(true);
			rightNextButton.gameObject.SetActive(false);
		}
        }
    }

    private void RequestFusionCalibration()
    {
        try
        {
            using (var client = new UdpClient())
            {
                var payload = System.Text.Encoding.UTF8.GetBytes("recalibrate");
                var host = IPAddress.Parse(fusionCalibrationHost);
                client.Send(payload, payload.Length, new IPEndPoint(host, fusionCalibrationControlPort));
                client.Send(payload, payload.Length, new IPEndPoint(host, bracingCalibrationControlPort));
                client.Send(payload, payload.Length, new IPEndPoint(host, fsrCalibrationControlPort));
            }
            Debug.Log("SafeLift all-sensor calibration requested: IMU/Fusion=" + fusionCalibrationControlPort
                + ", bracing=" + bracingCalibrationControlPort + ", FSR=" + fsrCalibrationControlPort);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SafeLift Fusion calibration request unavailable: " + ex.Message);
        }
    }

    private void RequestFusionRepReset()
    {
        try
        {
            using (var client = new UdpClient())
            {
                var payload = System.Text.Encoding.UTF8.GetBytes("reset_rep_count");
                var endpoint = new IPEndPoint(IPAddress.Parse(fusionCalibrationHost), fusionCalibrationControlPort);
                client.Send(payload, payload.Length, endpoint);
            }
            Debug.Log("SafeLift Fusion rep count reset requested for new set.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SafeLift Fusion rep count reset unavailable: " + ex.Message);
        }
    }

    private void RenderState(SafeLiftScreenState next)
    {
        HideAllScenarioPanels();
        if (avatarReview != null) avatarReview.Scenario = Scenario;
        // SET 1 is baseline/practice: keep live posture feedback hidden.
        // The separate current-score overlay remains visible for both sets.
        realtimeFeedback?.SetVisible(next == SafeLiftScreenState.Set2LiveFeedback);
        if (liveScorePanel != null)
            // Current Score is a development-only verification panel. Keep
            // score calculation/storage active, but hide this panel in SET 1/2.
            liveScorePanel.SetActive(false);
        tutorialUI?.SetVisible(next == SafeLiftScreenState.TutorialCalibration);
        breathingUI?.SetVisible(next == SafeLiftScreenState.BreathingReportGenerating, ReportSeconds);
        // The final review must expose the phase tabs captured during this
        // session. The recorder supplies the real humanoid snapshot; the
        // review controller falls back to its placeholder only when a phase
        // was not received.
        avatarReview?.SetVisible(next == SafeLiftScreenState.PostRepReview);
        // Replace the old blue gauge/comparison card with the per-rep score
        // analysis panel built by this state machine.
        visualAssets?.SetVisible(false);
        UpdateRepScorePanel(next == SafeLiftScreenState.PostRepReview);
        if (next == SafeLiftScreenState.PostRepReview)
            visualAssets?.ApplyReport(latestBackendState, latestSetComparison, Scenario, FinalUnityScore());
        qrShare?.SetVisible(next == SafeLiftScreenState.QRShare);
        EnforceCurrentPanelVisibility(next);
        ConfigureLayout(next);

        switch (next)
        {
            case SafeLiftScreenState.StartDashboard:
                content.text = "세이프리프트 XR\n\n모드: "
                    + (DemoMode ? "데모" : "실전")
                    + "\n현재 화면: " + ViewModeLabel()
                    + "\n\n실행 전 준비\n1. 백엔드와 센서 상태를 확인하세요.\n2. 화면과 시나리오를 선택하세요.\n3. 아래 시작 버튼을 누르세요.";
                status.text = DemoMode ? "보정 준비 완료 (데모)" : RealModePreflightStatus();
                primaryButton.GetComponentInChildren<TMP_Text>().text = "시작";
                break;

            case SafeLiftScreenState.TutorialCalibration:
                content.text = "스쿼트 튜토리얼 1→2→3→4\n준비 자세 → 내려가기 → 가장 낮은 자세 → 올라오기\n\n첨부된 튜토리얼 이미지를 순서대로 보여주며 센서 기준값과 tracking quality를 측정합니다.\n\n남은 튜토리얼 시간: "
                    + Mathf.Max(0, Mathf.CeilToInt(TutorialSeconds - (Time.unscaledTime - stateStarted))) + "초";
                status.text = "보정 중: 튜토리얼 이미지와 센서 기준값 확인";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "다음";
                break;

            case SafeLiftScreenState.Set1Practice:
            {
                if (!backendConnected) phaseRecorder?.CaptureDemoPhase(PhaseForRep(reps), reps);
                RenderSet1ScoreStatus();
                status.text = "SET 1: 연습 및 기준값 수집";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "반복 완료";
                break;
            }

            case SafeLiftScreenState.RestBeforeLiveFeedback:
                content.text = "SET 1 완료\n\n잠시 휴식하세요.\nSET 2부터 실시간 피드백이 제공됩니다.\n\n"
                    + Mathf.Max(0, Mathf.CeilToInt(RestSeconds - (Time.unscaledTime - stateStarted))) + "초";
                status.text = "실시간 피드백 단계로 전환 중";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "SET 2 시작";
                break;

           case SafeLiftScreenState.Set2LiveFeedback:
           {
                if (!backendConnected) phaseRecorder?.CaptureDemoPhase(PhaseForRep(reps), reps);
                RenderSet2ScoreStatus();
                status.text = "실시간 피드백 활성화";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "반복 완료";
                break;
            }

            case SafeLiftScreenState.BreathingReportGenerating:
                content.text = "SET 2 완료\n\n호흡을 고르고 잠시 기다려주세요.\nAI 코치 리포트를 생성합니다.\n\nSafeLift 점수 계산\n동작 phase 스냅샷 준비";
                status.text = "리포트 생성 중 - Gemini / 템플릿 fallback";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "리뷰 보기";
                hasReported = true;
                break;

            case SafeLiftScreenState.PostRepReview:
                    content.text = "AI Report and Score Analysis\n\nAI 코치 리포트\nSET 1 → SET 2 개선 요약\n\n"
                    + ScoreSummaryText()
                    + "\n주요 문제: " + MainIssue() + "\n"
                    + "확인 phase: " + ReviewPhaseForIssue() + "\n"
                    + "다음 cue: " + NextCueForIssue() + "\n\n"
                    + BackendSetComparisonText() + "\n\n"
                    + "컨트롤러 ray + Trigger로 phase/시점/회전/확대 버튼을 눌러 아바타를 확인하세요.";
                status.text = hasReported ? "리포트 준비 완료" : "모의 리포트 미리보기";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "QR 공유";
                break;

        case SafeLiftScreenState.QRShare:
                content.text = "리포트 공유\n\nHMD를 벗고 노트북 브라우저의 QR 코드를 확인하세요.\nAI 리포트, 점수, SET 1 / SET 2 비교를 볼 수 있습니다.";
                status.text = "QR 공유 준비 완료";
                primaryButton.GetComponentInChildren<TMP_Text>().text = "다시 시작";
                break;
        }
        if (rightNextButton != null && primaryButton != null)
        {
            var primaryLabel = primaryButton.GetComponentInChildren<TMP_Text>();
            var rightLabel = rightNextButton.GetComponentInChildren<TMP_Text>();
            if (primaryLabel != null && rightLabel != null) rightLabel.text = primaryLabel.text;
        }

        UpdateRealModeGateVisibility(next);
        if (next == SafeLiftScreenState.PostRepReview && content != null)
            content.text = BuildCompactReviewContent();
    }

    private string BuildReviewContent()
    {
        var report = string.IsNullOrWhiteSpace(latestReportText)
            ? "리포트 본문을 기다리는 중입니다. 기본 점수와 세트 비교는 오른쪽 패널에서 확인하세요."
            : latestReportText;
        if (report.Length > 900) report = report.Substring(0, 900) + "\n...";
        return "AI 코치 리포트\n\n" + report;
    }

    private string BuildCompactReviewContent()
    {
        var report = string.IsNullOrWhiteSpace(latestReportText)
            ? "리포트 본문을 기다리는 중입니다. 전체 내용은 QR에서 확인하세요."
            : latestReportText;
        // Scores are shown in the separate REP SCORE ANALYSIS panel, so keep
        // the AI report body independent and allow the report viewport to scroll.
        if (report.Length > 900)
            report = report.Substring(0, 900) + "\n...";
        return "AI 코치 리포트\n\n" + report;
    }

    private void ConfigureLayout(SafeLiftScreenState next)
    {
        ConfigureStepControlGroups(next);
        ConfigureReportScroll(next == SafeLiftScreenState.PostRepReview);

        var isSet1Layout = next == SafeLiftScreenState.Set1Practice;
        if (isSet1Layout)
        {
            // SET 1 uses a compact status card in the upper-left and reserves
            // the center of the dashboard for the avatar and webcam.
            if (primaryButton != null) primaryButton.gameObject.SetActive(false);
            if (rightNextButton != null) rightNextButton.gameObject.SetActive(false);
        }

        if (content != null)
        {
            content.gameObject.SetActive(true);
            content.rectTransform.sizeDelta = new Vector2(760, 260);
            content.rectTransform.anchoredPosition = new Vector2(0, 15);
            content.fontSize = 23;
            ConfigureContentFrame(true, content.rectTransform.anchoredPosition, new Vector2(805, 292));
        }

        if (webcamPreview != null)
        {
            webcamPreview.gameObject.SetActive(next == SafeLiftScreenState.Set1Practice || next == SafeLiftScreenState.Set2LiveFeedback);
        }
        if (webcamFrame != null)
            webcamFrame.gameObject.SetActive(next == SafeLiftScreenState.Set1Practice || next == SafeLiftScreenState.Set2LiveFeedback);

        if (backendUrlInput != null)
            backendUrlInput.gameObject.SetActive(next == SafeLiftScreenState.StartDashboard && !DemoMode);
        if (backendUrlHint != null)
            backendUrlHint.gameObject.SetActive(next == SafeLiftScreenState.StartDashboard && !DemoMode);

        if (next == SafeLiftScreenState.StartDashboard && !DemoMode)
        {
            if (content != null) content.gameObject.SetActive(false);
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }
        else if (next == SafeLiftScreenState.TutorialCalibration)
        {
            // The tutorial screen is image-first. Hide the generic text card so
            // the provided tutorial_1 -> tutorial_4 images are not covered by
            // the normal dashboard/exercise content panel.
            content.gameObject.SetActive(false);
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }
        else if (next == SafeLiftScreenState.RestBeforeLiveFeedback)
        {
            // Keep the dark rest card and its three-line message centered on
            // the main UI canvas, independent of the shared header position.
            content.gameObject.SetActive(true);
            content.rectTransform.anchoredPosition = Vector2.zero;
            content.alignment = TextAlignmentOptions.Center;
            content.enableWordWrapping = true;
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }
        else if (next == SafeLiftScreenState.Set2LiveFeedback)
        {
            content.gameObject.SetActive(true);
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }
        else if (next == SafeLiftScreenState.BreathingReportGenerating)
        {
            // The breathing/report panel owns this screen. Keep the generic
            // exercise content card hidden so SET 1/SET 2 text cannot show
            // through or overlap the report panel.
            content.gameObject.SetActive(false);
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }
        else if (next == SafeLiftScreenState.PostRepReview)
        {
            // Review uses two non-overlapping columns: report body on the
            // left and the score/comparison card on the right.
            content.gameObject.SetActive(true);
            content.rectTransform.sizeDelta = new Vector2(500, 500);
            // The report text is parented to the centered scroll viewport below.
            // Keep its local position at the viewport origin so it cannot be
            // shifted/clipped a second time by the card coordinates.
            content.rectTransform.anchoredPosition = Vector2.zero;
            content.fontSize = 14;
            content.alignment = TextAlignmentOptions.TopLeft;
            content.enableWordWrapping = true;
            content.rectTransform.sizeDelta = new Vector2(440, 1200);
            ConfigureContentFrame(true, new Vector2(-140f, 5f), new Vector2(520f, 540f));
        }
        else if (next == SafeLiftScreenState.QRShare)
        {
            content.gameObject.SetActive(false);
            ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
        }

        ApplyViewModeLayout(next);

        if (isSet1Layout)
        {
            if (content != null)
            {
                content.gameObject.SetActive(true);
                content.rectTransform.sizeDelta = new Vector2(430f, 330f);
                content.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                content.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                content.rectTransform.pivot = new Vector2(0f, 0.5f);
                content.rectTransform.anchoredPosition = new Vector2(1030f, 45f);
                content.fontSize = 21f;
                content.alignment = TextAlignmentOptions.TopLeft;
                content.enableWordWrapping = true;
                // SET1 keeps the squat status text, but does not need the
                // decorative dark background behind it.
                ConfigureContentFrame(false, Vector2.zero, Vector2.zero);
            }

            if (avatarPreview != null)
            {
            avatarPreview.rectTransform.anchoredPosition = new Vector2(-165f, 45f);
                avatarPreview.rectTransform.sizeDelta = new Vector2(300f, 455f);
            }
            if (avatarPreviewFrame != null)
            {
            avatarPreviewFrame.rectTransform.anchoredPosition = new Vector2(-165f, 45f);
                avatarPreviewFrame.rectTransform.sizeDelta = new Vector2(318f, 473f);
            }
            if (webcamPreview != null)
            {
            webcamPreview.rectTransform.anchoredPosition = new Vector2(175f, 45f);
            webcamPreview.rectTransform.sizeDelta = new Vector2(300f, 455f);
                webcamPreview.transform.SetAsLastSibling();
            }
            if (webcamFrame != null)
            {
            webcamFrame.rectTransform.anchoredPosition = new Vector2(175f, 45f);
            webcamFrame.rectTransform.sizeDelta = new Vector2(318f, 473f);
                webcamFrame.transform.SetAsLastSibling();
            }
        }

        if (next == SafeLiftScreenState.Set2LiveFeedback && content != null)
        {
            // Small status column between the media area and live feedback.
            content.gameObject.SetActive(true);
            content.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            content.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            content.rectTransform.pivot = new Vector2(1f, 0.5f);
            // Place the SET2 rep/score text just to the right of the live
            // feedback card, keeping a small visual gap.
            content.rectTransform.anchoredPosition = new Vector2(-300f, 45f);
            content.rectTransform.sizeDelta = new Vector2(240f, 330f);
            content.fontSize = 18f;
            content.alignment = TextAlignmentOptions.TopLeft;
            content.enableWordWrapping = true;
        }

        if (next == SafeLiftScreenState.StartDashboard)
        {
            // Dashboard zones: instructions/gate on the left, webcam on the right.
            if (content != null && DemoMode)
            {
                content.rectTransform.sizeDelta = new Vector2(620, 218);
                content.rectTransform.anchoredPosition = new Vector2(-170, 42);
                ConfigureContentFrame(true, content.rectTransform.anchoredPosition, new Vector2(660, 250));
            }
            if (webcamPreview != null)
            {
                webcamPreview.rectTransform.sizeDelta = new Vector2(170, 96);
                webcamPreview.rectTransform.anchoredPosition = new Vector2(410, 125);
            }
            if (webcamFrame != null)
            {
                webcamFrame.rectTransform.sizeDelta = new Vector2(190, 116);
                webcamFrame.rectTransform.anchoredPosition = new Vector2(410, 125);
            }
            if (realModeGateFrame != null)
            {
                realModeGateFrame.rectTransform.sizeDelta = new Vector2(660, 148);
                realModeGateFrame.rectTransform.anchoredPosition = new Vector2(-170, 42);
                if (realModeGateText != null)
                    realModeGateText.rectTransform.sizeDelta = new Vector2(610, 116);
            }
        }
    }

    private void ConfigureStepControlGroups(SafeLiftScreenState next)
    {
        var startOnly = next == SafeLiftScreenState.StartDashboard;
        if (dashboardControls != null) dashboardControls.SetActive(startOnly);
        if (backendControls != null) backendControls.SetActive(startOnly && !DemoMode);
        if (flowControls != null) flowControls.SetActive(true);

        if (primaryButton == null) return;
        var primaryRt = primaryButton.GetComponent<RectTransform>();
        if (primaryRt == null) return;

        if (next == SafeLiftScreenState.PostRepReview)
        {
            primaryRt.sizeDelta = new Vector2(200, 54);
            primaryRt.anchoredPosition = new Vector2(320, -270);
        }
        else
        {
            primaryRt.sizeDelta = new Vector2(240, 60);
            primaryRt.anchoredPosition = new Vector2(0, -270);
        }

        var label = primaryButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.rectTransform.sizeDelta = primaryRt.sizeDelta;
    }

    private void ConfigureContentFrame(bool visible, Vector2 position, Vector2 size)
    {
        if (contentFrame == null) return;
        contentFrame.gameObject.SetActive(visible);
        contentFrame.rectTransform.anchoredPosition = position;
        contentFrame.rectTransform.sizeDelta = size;
    }

    private void CreateRepScorePanel(Transform parent)
    {
        repScorePanel = new GameObject("SafeLiftRepScoreAnalysisPanel");
        repScorePanel.transform.SetParent(parent, false);
        var panelRect = repScorePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(.5f, .5f);
        panelRect.anchorMax = new Vector2(.5f, .5f);
        panelRect.pivot = new Vector2(.5f, .5f);
        // Match the AI report viewport height and align both panels at the same top Y coordinate.
        panelRect.anchoredPosition = new Vector2(330f, 0f);
        panelRect.sizeDelta = new Vector2(310f, 540f);
        var background = repScorePanel.AddComponent<Image>();
        background.color = new Color(.025f, .065f, .10f, .98f);
        var outline = repScorePanel.AddComponent<Outline>();
        outline.effectColor = new Color(.25f, .85f, .70f, .9f);
        outline.effectDistance = new Vector2(2f, -2f);

        var titleText = Text(repScorePanel.transform, "RepScorePanelTitle", 16f, new Vector2(0f, 240f));
        titleText.text = "REP SCORE ANALYSIS";
        titleText.rectTransform.sizeDelta = new Vector2(280f, 30f);
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(.65f, 1f, .82f, 1f);

        repScorePanelText = Text(repScorePanel.transform, "RepScorePanelDetails", 11f, new Vector2(0f, -10f));
        repScorePanelText.text = "SET 1\nREP 1  --";
        repScorePanelText.rectTransform.sizeDelta = new Vector2(280f, 475f);
        repScorePanelText.alignment = TextAlignmentOptions.TopLeft;
        repScorePanelText.enableWordWrapping = true;
        repScorePanelText.color = new Color(.9f, .98f, 1f, 1f);
        repScorePanel.SetActive(false);
    }

    private void UpdateRepScorePanel(bool visible)
    {
        if (repScorePanel == null) return;
        repScorePanel.SetActive(visible);
        if (!visible || repScorePanelText == null) return;

        repScorePanelText.text = RepScorePanelBlock("SET 1", set1RepScores)
            + "\n\n" + RepScorePanelBlock("SET 2", set2RepScores);
    }

    private void CreateLiveScorePanel(Transform parent)
    {
        liveScorePanel = new GameObject("SafeLiftCurrentScorePanel");
        liveScorePanel.transform.SetParent(parent, false);
        var rect = liveScorePanel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(.5f, .5f);
        rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(420f, 215f);
        var background = liveScorePanel.AddComponent<Image>();
        background.color = new Color(.02f, .10f, .14f, .98f);
        var scoreCanvas = liveScorePanel.AddComponent<Canvas>();
        scoreCanvas.overrideSorting = true;
        scoreCanvas.sortingOrder = 2000;
        liveScorePanel.transform.SetAsLastSibling();
        var outline = liveScorePanel.AddComponent<Outline>();
        outline.effectColor = new Color(.25f, .95f, .75f, .95f);
        outline.effectDistance = new Vector2(2f, -2f);
        var titleText = Text(liveScorePanel.transform, "CurrentScoreTitle", 16f, new Vector2(0f, 55f));
        titleText.text = "CURRENT SCORE";
        titleText.rectTransform.sizeDelta = new Vector2(350f, 25f);
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(.65f, 1f, .82f, 1f);
        liveScorePanelText = Text(liveScorePanel.transform, "CurrentScoreDetails", 14f, new Vector2(0f, -12f));
        liveScorePanelText.rectTransform.sizeDelta = new Vector2(390f, 165f);
        liveScorePanelText.alignment = TextAlignmentOptions.TopLeft;
        liveScorePanelText.text = "SET 1  SCORE 100/100\nFSR 35/35\nIMU 30/30\nBRACING 35/35";
        liveScorePanelText.color = Color.white;
        liveScorePanel.SetActive(false);
    }

    private void UpdateLiveScorePanel(SafeLiftScoreResult score)
    {
        if (liveScorePanelText == null) return;
        // Use the Fusion Monitor phase for the live score display as well as
        // accumulation. Backend phase must not re-enable scoring during rest.
        var fusionSnapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        var phase = fusionSnapshot != null && squatSensorReceiver.LastFusionPhaseAgeSeconds <= 1.5f
            ? fusionSnapshot.squatPhase
            : "Standing";
        var normalizedPhase = (phase ?? string.Empty).ToLowerInvariant();
        var scoringPhase = normalizedPhase.Contains("desc") || normalizedPhase.Contains("bottom")
            || normalizedPhase.Contains("asc") || normalizedPhase.Contains("squat");
        var standing = !scoringPhase;
        var fsr = standing ? 35f : score != null && score.fsr != null ? score.fsr.score : 35f;
        var bracing = standing ? 35f : score != null && score.bracing != null ? score.bracing.score : 35f;
        var imu = standing ? 30f : score != null && score.spine != null ? score.spine.score : 30f;
        var total = fsr + bracing + imu;
        var setLabel = latestBackendState != null && latestBackendState.set_index >= 2 ? "SET 2" : "SET 1";
        var rep = latestBackendState != null ? Mathf.Max(1, latestBackendState.set_rep_count) : 1;
        var movementTime = score != null ? score.activeMovementSeconds : 0f;
        var balanceTime = score != null && score.fsr != null ? score.fsr.balanceDurationSeconds : 0f;
        var heelTime = score != null && score.fsr != null ? score.fsr.heelDurationSeconds : 0f;
        var imuTime = score != null && score.spine != null ? score.spine.durationSeconds : 0f;
        var bracingTime = score != null && score.bracing != null ? score.bracing.durationSeconds : 0f;
        liveScorePanelText.text = setLabel + " REP " + rep + "  SCORE " + total.ToString("0.0") + "/100\n"
            + "SQUAT TIME " + movementTime.ToString("0.0") + "s\n"
            + "FSR " + fsr.ToString("0.0") + "/35 (-" + (35f - fsr).ToString("0.0") + ")\n"
            + "  balance error " + balanceTime.ToString("0.0") + "s | heel error " + heelTime.ToString("0.0") + "s\n"
            + "IMU " + imu.ToString("0.0") + "/30 (-" + (30f - imu).ToString("0.0") + ")  error " + imuTime.ToString("0.0") + "s\n"
            + "BRACING " + bracing.ToString("0.0") + "/35 (-" + (35f - bracing).ToString("0.0") + ")  error " + bracingTime.ToString("0.0") + "s";
    }

    private static string RepScorePanelBlock(string label, List<SafeLiftScoreResult> scores)
    {
        var text = label + "\n";
        for (var i = 0; i < 3; i++)
        {
            var score = scores != null && i < scores.Count ? scores[i] : null;
            if (score == null)
            {
                text += "REP " + (i + 1) + "   --\n";
                continue;
            }

            var fsr = score.fsr != null ? score.fsr.score : 0f;
            var bracing = score.bracing != null ? score.bracing.score : 0f;
            var spine = score.spine != null ? score.spine.score : 0f;
            text += "REP " + (i + 1) + "   " + score.totalScore.ToString("0.0") + "/100\n"
                + "  TIME " + score.activeMovementSeconds.ToString("0.0") + "s"
                + " | balance " + (score.fsr != null ? score.fsr.balanceDurationSeconds : 0f).ToString("0.0") + "s"
                + " | heel " + (score.fsr != null ? score.fsr.heelDurationSeconds : 0f).ToString("0.0") + "s\n"
                + "  IMU error " + (score.spine != null ? score.spine.durationSeconds : 0f).ToString("0.0") + "s"
                + " | bracing error " + (score.bracing != null ? score.bracing.durationSeconds : 0f).ToString("0.0") + "s\n"
                + "  FSR -" + Mathf.Max(0f, 35f - fsr).ToString("0.0")
                + " | BRACING -" + Mathf.Max(0f, 35f - bracing).ToString("0.0")
                + " | IMU -" + Mathf.Max(0f, 30f - spine).ToString("0.0") + "\n";
        }
        return text.TrimEnd('\n');
    }

    private void ConfigureReportScroll(bool enabled)
    {
        if (content == null || card == null) return;

        if (reportScrollViewport == null)
        {
            reportScrollViewport = new GameObject("SafeLiftReportScrollViewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            reportScrollViewport.transform.SetParent(card.transform, false);
            var viewport = reportScrollViewport.GetComponent<RectTransform>();
            viewport.anchorMin = new Vector2(.5f, .5f);
            viewport.anchorMax = new Vector2(.5f, .5f);
            viewport.pivot = new Vector2(.5f, .5f);
            viewport.anchoredPosition = new Vector2(-510f, 5f);
            viewport.sizeDelta = new Vector2(460, 500);
            reportScrollRect = reportScrollViewport.GetComponent<ScrollRect>();
            reportScrollRect.horizontal = false;
            reportScrollRect.vertical = true;
            reportScrollRect.movementType = ScrollRect.MovementType.Clamped;
            reportScrollRect.scrollSensitivity = 28f;

            var scrollbarObject = new GameObject("SafeLiftReportScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(card.transform, false);
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(.5f, .5f);
            scrollbarRect.anchorMax = new Vector2(.5f, .5f);
            scrollbarRect.pivot = new Vector2(.5f, .5f);
            scrollbarRect.anchoredPosition = new Vector2(-70f, 5f);
            scrollbarRect.sizeDelta = new Vector2(12f, 490f);
            scrollbarObject.GetComponent<Image>().color = new Color(.04f, .12f, .16f, .95f);
            reportScrollbar = scrollbarObject.GetComponent<Scrollbar>();
            reportScrollbar.direction = Scrollbar.Direction.BottomToTop;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(scrollbarObject.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(1f, 1f);
            handleRect.offsetMax = new Vector2(-1f, -1f);
            var handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(.25f, .9f, .72f, .95f);
            reportScrollbar.handleRect = handleRect;
            reportScrollbar.targetGraphic = handleImage;
            reportScrollRect.verticalScrollbar = reportScrollbar;
            reportScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            reportScrollUpButton = ButtonSized(card.transform, "▲", new Vector2(-70f, 255f), new Vector2(42f, 34f), ScrollReportUp);
            reportScrollDownButton = ButtonSized(card.transform, "▼", new Vector2(-70f, -245f), new Vector2(42f, 34f), ScrollReportDown);
        }

        reportScrollViewport.SetActive(enabled);
        if (reportScrollbar != null) reportScrollbar.gameObject.SetActive(enabled);
        if (reportScrollUpButton != null) reportScrollUpButton.gameObject.SetActive(enabled);
        if (reportScrollDownButton != null) reportScrollDownButton.gameObject.SetActive(enabled);
        if (!enabled)
        {
            if (contentOriginalParent != null && content.rectTransform.parent != contentOriginalParent)
                content.rectTransform.SetParent(contentOriginalParent, false);
            // Restore the normal dashboard text layout after leaving the
            // report scroll viewport. The report uses top-left anchoring;
            // leaving that state in place makes the text jump on restart.
            content.rectTransform.anchorMin = new Vector2(.5f, .5f);
            content.rectTransform.anchorMax = new Vector2(.5f, .5f);
            content.rectTransform.pivot = new Vector2(.5f, .5f);
            content.rectTransform.anchoredPosition = new Vector2(0f, 63f);
            content.rectTransform.sizeDelta = new Vector2(670f, 198f);
            return;
        }

        content.rectTransform.SetParent(reportScrollViewport.transform, false);
        content.rectTransform.anchorMin = new Vector2(0, 1);
        content.rectTransform.anchorMax = new Vector2(0, 1);
        content.rectTransform.pivot = new Vector2(0, 1);
        content.rectTransform.anchoredPosition = Vector2.zero;
        content.rectTransform.sizeDelta = new Vector2(440, 1200);
        reportScrollRect.content = content.rectTransform;
        reportScrollRect.viewport = reportScrollViewport.GetComponent<RectTransform>();
        reportScrollRect.verticalNormalizedPosition = 1f;
    }

    private void ScrollReportUp()
    {
        if (reportScrollRect == null) return;
        reportScrollRect.verticalNormalizedPosition = Mathf.Clamp01(reportScrollRect.verticalNormalizedPosition + .25f);
    }

    private void ScrollReportDown()
    {
        if (reportScrollRect == null) return;
        reportScrollRect.verticalNormalizedPosition = Mathf.Clamp01(reportScrollRect.verticalNormalizedPosition - .25f);
    }

    private void UpdateRealModeGateVisibility(SafeLiftScreenState state)
    {
        if (realModeGateFrame == null || realModeGateText == null) return;
        var visible = state == SafeLiftScreenState.StartDashboard && !DemoMode;
        realModeGateFrame.gameObject.SetActive(visible);
        if (!visible) return;

        if (content != null) content.gameObject.SetActive(false);
        ConfigureContentFrame(false, Vector2.zero, Vector2.zero);

        realModeGateFrame.color = IsRealModeLaunchReady()
            ? new Color(.035f, .14f, .09f, .96f)
            : new Color(.16f, .075f, .045f, .96f);
        realModeGateText.text = RealModeStartGateText();
    }

    private string RealModeStartGateText()
    {
        var readiness = IsRealModeLaunchReady() ? "READY TO START TUTORIAL" : "WAIT / BLOCKED";
        return "REAL MODE START GATE - " + readiness + "\n"
            + "Quest/REAL mode is backend-driven only: Unity will not auto-run mock reps.\n\n"
            + "1 URL: " + RealModeGateUrlLine() + "\n"
            + "2 Backend: " + (backendConnected ? "OK /api/session_state connected" : "WAIT: start run_backend.ps1 and check /health") + "\n"
            + "3 Sensor provider: " + (IsBackendSensorReady() ? "OK: " + SensorStatusText(latestBackendState) : "WAIT: backend has not published a usable SafeLift sensor source") + "\n"
            + "4 Calibration: " + (IsBackendCalibrationReady() ? "OK: baseline ready" : "WILL RUN IN TUTORIAL: SET 1 stays locked until calibration is ready") + "\n"
            + "5 Laptop QR: OK: open /qr in the laptop browser; QR is not rendered inside VR\n\n"
            + "Next action: " + RealModeGateNextAction();
    }

    private string RealModeGateUrlLine()
    {
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl))
            return "BLOCKED: Quest cannot use 127.0.0.1; enter http://PC_IP:8090 and APPLY URL";
        if (IsLoopbackBackendUrl(currentBackendUrl))
            return "OK for Unity Editor; Quest must use laptop PC IP later";
        return "OK: " + currentBackendUrl;
    }

    private string RealModeGateNextAction()
    {
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl)) return "Press QUEST PC IP, replace it with the laptop IPv4 URL, then APPLY URL.";
        if (!backendConnected) return "Run .\\run_backend.ps1 -OpenHealth, then wait for Backend OK.";
        if (!IsBackendSensorReady()) return "Connect or start the SafeLift sensor provider; raw acquisition is outside Unity scope.";
        if (!IsBackendCalibrationReady()) return "Press PLAY to enter Tutorial + Calibration; SET 1 will remain locked until calibration is ready.";
        return "Press PLAY. SET 1/SET 2, rep count, feedback, report, and QR handoff will follow backend state.";
    }

    private string RealModeLaunchBlockerStatus()
    {
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl))
            return "REAL MODE START BLOCKED: Quest cannot use 127.0.0.1. Enter the laptop/PC IPv4 URL, then press APPLY URL.";
        if (!backendConnected)
            return "REAL MODE START BLOCKED: backend /api/session_state is not connected. Run backend first.";
        if (!IsBackendSensorReady())
            return "REAL MODE START READY: Tutorial/Calibration can start; SafeLift sensor readiness is required before SET 1.";
        return "REAL MODE START READY: calibration continues inside the Tutorial screen.";
    }

    private bool IsRealModeLaunchReady()
    {
        if (DemoMode) return true;
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl)) return false;
        // Play should enter Tutorial/Calibration as soon as the backend state
        // is reachable. Sensor readiness is enforced when advancing from
        // Tutorial to SET 1, not at the initial Play gate.
        return backendConnected;
    }

    private void AdvanceRep(SafeLiftScreenState afterSet)
    {
        reps++;
        nextRepAt = Time.unscaledTime - stateStarted + 1f;
        if (reps >= TargetReps)
        {
            if (State == SafeLiftScreenState.Set1Practice && afterSet == SafeLiftScreenState.RestBeforeLiveFeedback)
                setOneRestConsumed = true;
            if (State == SafeLiftScreenState.Set2LiveFeedback && afterSet == SafeLiftScreenState.BreathingReportGenerating)
                setTwoRestConsumed = true;
            if (afterSet == SafeLiftScreenState.BreathingReportGenerating)
            {
                setTwoRestConsumed = true;
                restAfterSetTwoPending = true;
                backendRestPending = true;
                SetState(SafeLiftScreenState.RestBeforeLiveFeedback);
            }
            else
            {
                SetState(afterSet);
            }
        }
        else RenderState(State);
        if (State == SafeLiftScreenState.Set2LiveFeedback) haptics.RepCounted();
    }

    private void BuildFallbackUI()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var eventRoot = new GameObject("SafeLiftDesktopEventSystem");
            eventRoot.AddComponent<EventSystem>();
            eventRoot.AddComponent<StandaloneInputModule>();
        }

        var root = new GameObject("SafeLiftDemoWorldCanvas");
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 500;
        canvas.worldCamera = FindSafeLiftViewCamera();
#if UNITY_EDITOR
        // BuildFallbackUI can run before the runtime bootstrap callback. Keep
        // the editor test self-contained so a copied scene never ends up with
        // a world-space Canvas whose camera is null.
        if (canvas.worldCamera == null)
        {
            var previewObject = GameObject.Find("SafeLiftEditorPreviewCamera");
            if (previewObject == null)
            {
                previewObject = new GameObject("SafeLiftEditorPreviewCamera");
                DontDestroyOnLoad(previewObject);
            }

            var previewCamera = previewObject.GetComponent<Camera>();
            if (previewCamera == null) previewCamera = previewObject.AddComponent<Camera>();
            previewObject.SetActive(true);
            previewCamera.enabled = true;
            previewCamera.tag = "MainCamera";
            previewCamera.targetDisplay = 0;
            previewCamera.depth = 1000f;
            previewCamera.clearFlags = CameraClearFlags.Skybox;
            previewCamera.nearClipPlane = 0.03f;
            previewCamera.farClipPlane = 80f;
            previewCamera.fieldOfView = 34f;
            previewCamera.transform.position = new Vector3(0f, 1.62f, -0.35f);
            previewCamera.transform.LookAt(new Vector3(0f, 1.45f, 1.15f), Vector3.up);
            canvas.worldCamera = previewCamera;
            Debug.Log("SafeLift Editor fallback camera created from UI state machine.");
        }
#endif
        canvas.transform.localPosition = new Vector3(0, 1.35f, ComfortableCanvasDistance);
        canvas.transform.localScale = Vector3.one * ComfortableCanvasScale;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        SafeLiftMetaInteractionBinder.Ensure(canvas);

        card = new GameObject("SafeLiftMainCard");
        card.transform.SetParent(root.transform, false);
        var bg = card.AddComponent<Image>();
        bg.color = new Color(.035f, .055f, .09f, 1f);
        var rt = card.GetComponent<RectTransform>();
        // One enclosing surface for every state panel. The widest tutorial/live
        // panels are 1040/940, so the base must be wider than the old 900px card.
        rt.sizeDelta = new Vector2(1900, 900);

        title = Text(card.transform, "Title", 34, new Vector2(0, 226));
        subtitle = Text(card.transform, "Subtitle", 19, new Vector2(0, 196));
        progressText = Text(card.transform, "ScenarioProgress", 14, new Vector2(0, 170));
        progressText.rectTransform.sizeDelta = new Vector2(780, 28);
        progressText.enableWordWrapping = false;
        contentFrame = Frame(card.transform, "SafeLiftContentFrame", new Vector2(0, 45), new Vector2(760, 245));
        content = Text(card.transform, "Content", 22, new Vector2(0, 63));
        contentOriginalParent = content.rectTransform.parent as RectTransform;
        content.alignment = TextAlignmentOptions.TopLeft;
        content.rectTransform.sizeDelta = new Vector2(670, 198);
        status = Text(card.transform, "Status", 17, new Vector2(0, -310));
        status.rectTransform.sizeDelta = new Vector2(780, 38);
        status.rectTransform.anchoredPosition = new Vector2(0, -310);
        status.gameObject.SetActive(false);
        diagnosticsText = Text(card.transform, "SafeLiftDiagnostics", 11, new Vector2(0, -350));
        diagnosticsText.rectTransform.sizeDelta = new Vector2(780, 22);
        diagnosticsText.color = new Color(.55f, .95f, 1f, .88f);
        diagnosticsText.enableWordWrapping = false;
        diagnosticsText.gameObject.SetActive(false);
        sensorCalibrationText = Text(card.transform, "SafeLiftSensorCalibration", 13, new Vector2(900, 285));
        sensorCalibrationText.rectTransform.sizeDelta = new Vector2(460, 360);
        sensorCalibrationText.alignment = TextAlignmentOptions.TopLeft;
        sensorCalibrationText.enableWordWrapping = false;
        sensorCalibrationText.color = new Color(.35f, 1f, .55f, .95f);
        // This was a development-only sensor/calibration monitor. Keep the
        // data path active for scoring, but hide the monitor in every scenario.
        sensorCalibrationText.gameObject.SetActive(false);

        dashboardControls = ControlRoot(card.transform, "StartDashboardControls");
        backendControls = ControlRoot(card.transform, "BackendUrlControls");
        flowControls = ControlRoot(card.transform, "StepActionControls");
        var flowCanvas = flowControls.AddComponent<Canvas>();
        flowCanvas.overrideSorting = true;
        // The root dashboard Canvas uses sortingOrder 500. This must be
        // higher than the root, otherwise the panel is rendered over the
        // buttons and they appear black/hidden.
        flowCanvas.sortingOrder = 1000;
        flowControls.AddComponent<GraphicRaycaster>();

        backendUrlHint = Text(backendControls.transform, "BackendUrlHint", 13, new Vector2(0, -80));
        backendUrlHint.rectTransform.sizeDelta = new Vector2(700, 32);
        backendUrlHint.color = new Color(.75f, .9f, 1f, .92f);
        realModeGateFrame = Frame(card.transform, "RealModeGateFrame", new Vector2(0, 42), new Vector2(700, 148));
        realModeGateFrame.color = new Color(.12f, .075f, .045f, .96f);
        realModeGateText = Text(realModeGateFrame.transform, "RealModeGateText", 12, Vector2.zero);
        realModeGateText.rectTransform.sizeDelta = new Vector2(650, 116);
        realModeGateText.alignment = TextAlignmentOptions.TopLeft;
        realModeGateText.enableWordWrapping = true;
        realModeGateText.color = new Color(.96f, .98f, 1f, 1f);
        realModeGateFrame.gameObject.SetActive(false);

        backendUrlInput = InputField(backendControls.transform, "BackendUrlInput", currentBackendUrl, new Vector2(-110, -125));
        ButtonSized(backendControls.transform, "LOCAL", new Vector2(-320, -125), new Vector2(120, 36), UseLocalBackendUrl);
        ButtonSized(backendControls.transform, "QUEST PC IP", new Vector2(105, -125), new Vector2(138, 36), UseQuestBackendUrlHint);
        ButtonSized(backendControls.transform, "APPLY URL", new Vector2(265, -125), new Vector2(138, 36), ApplyBackendUrlFromInput);

        webcamPreview = new GameObject("SafeLiftWebcamPreview").AddComponent<RawImage>();
        webcamFrame = Frame(card.transform, "SafeLiftWebcamFrame", new Vector2(260, 115), new Vector2(198, 120));
        webcamFrame.color = new Color(.01f, .02f, .035f, .98f);
        var webcamFrameCanvas = webcamFrame.gameObject.AddComponent<Canvas>();
        webcamFrameCanvas.overrideSorting = true;
        webcamFrameCanvas.sortingOrder = 850;
        webcamPreview.transform.SetParent(card.transform, false);
        webcamPreview.rectTransform.sizeDelta = new Vector2(180, 102);
        webcamPreview.rectTransform.anchoredPosition = new Vector2(260, 115);
        webcamPreview.color = new Color(1f, 1f, 1f, .95f);
        var webcamCanvas = webcamPreview.gameObject.AddComponent<Canvas>();
        webcamCanvas.overrideSorting = true;
        webcamCanvas.sortingOrder = 851;
        webcamPreview.transform.SetAsLastSibling();

        avatarPreviewFrame = Frame(card.transform, "SafeLiftAvatarPreviewFrame", new Vector2(-315, 45), new Vector2(324, 324));
        avatarPreviewFrame.color = new Color(.01f, .02f, .035f, .98f);
        var avatarFrameCanvas = avatarPreviewFrame.gameObject.AddComponent<Canvas>();
        avatarFrameCanvas.overrideSorting = true;
        avatarFrameCanvas.sortingOrder = 800;
        avatarPreview = new GameObject("SafeLiftAvatarPreview").AddComponent<RawImage>();
        avatarPreview.transform.SetParent(card.transform, false);
        avatarPreview.rectTransform.sizeDelta = new Vector2(300, 300);
        avatarPreview.rectTransform.anchoredPosition = new Vector2(-315, 45);
        avatarPreview.color = Color.white;
        var avatarCanvas = avatarPreview.gameObject.AddComponent<Canvas>();
        avatarCanvas.overrideSorting = true;
        avatarCanvas.sortingOrder = 801;
        avatarPreview.gameObject.SetActive(false);

        primaryButton = ButtonSized(flowControls.transform, "PLAY", new Vector2(0, -270), new Vector2(250, 64), PrimaryAction);
        rightNextButton = ButtonSized(flowControls.transform, "다음 단계", new Vector2(700, -270), new Vector2(250, 64), PrimaryAction);
        recalibrationButton = ButtonSized(flowControls.transform, "자세 다시 보정", new Vector2(0, -340), new Vector2(220, 42), RequestAvatarCalibration);
        // Avatar/webcam selection was removed: both are always shown during
        // SET 1 and SET 2, matching the original exercise layout.
        viewModeButtonText = null;
        var scenarioButton = ButtonSized(dashboardControls.transform, "실제 측정값 사용", new Vector2(210, -190), new Vector2(210, 52), () => SelectScenario(0));
        scenarioButton.gameObject.SetActive(false);
        scenarioButtonText = scenarioButton.GetComponentInChildren<TMP_Text>();
        var sensorModeButton = ButtonSized(dashboardControls.transform, "모드: 데모", new Vector2(-330, 226), new Vector2(200, 48), ToggleSensorMode);
        sensorModeButtonText = sensorModeButton.GetComponentInChildren<TMP_Text>();

        var existingPanelObject = new GameObject("ExistingUIPanel");
        existingPanelObject.transform.SetParent(card.transform, false);
        var existingPanelRect = existingPanelObject.AddComponent<RectTransform>();
        existingPanelRect.sizeDelta = new Vector2(1050f, 820f);
        existingPanelRect.anchoredPosition = new Vector2(-360f, 0f);
        var existingChildren = new List<Transform>();
        foreach (Transform child in card.transform)
            if (child != existingPanelObject.transform) existingChildren.Add(child);
        foreach (var child in existingChildren) child.SetParent(existingPanelObject.transform, false);

        // Keep the SET1 status text independent from the shared legacy panel
        // container. Its authored local position remains explicit below, so
        // the text can be moved without shifting the avatar/webcam group.
        if (content != null)
        {
            content.transform.SetParent(card.transform, false);
            contentOriginalParent = content.rectTransform.parent as RectTransform;
            content.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            content.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            content.rectTransform.pivot = new Vector2(0f, 0.5f);
            content.rectTransform.anchoredPosition = new Vector2(20f, -40f);
        }

        realtimeFeedback = gameObject.AddComponent<SafeLiftRealtimeFeedbackPanel>();
        realtimeFeedback.Attach(card.transform);
        squatSensorReceiver = GetComponent<SafeLiftSquatSensorUdpReceiver>();
        scoreController = GetComponent<SafeLiftUnityScoreController>();
        phaseRecorder = GetComponent<SafeLiftPhasePoseRecorder>();
        if (phaseRecorder == null) phaseRecorder = gameObject.AddComponent<SafeLiftPhasePoseRecorder>();
        avatarRuntime = GetComponent<SafeLiftAvatarRuntimeController>();
        if (avatarRuntime == null) avatarRuntime = gameObject.AddComponent<SafeLiftAvatarRuntimeController>();
        avatarRuntime.Configure(this, phaseRecorder);
        avatarUIPreview = gameObject.AddComponent<SafeLiftAvatarUIPreview>();
        avatarUIPreview.Configure(avatarRuntime.LiveAvatarRoot, avatarPreview);
        avatarReview = gameObject.AddComponent<SafeLiftAvatarReviewController>();
        avatarReview.Attach(card.transform);
        visualAssets = gameObject.AddComponent<SafeLiftVisualAssetAdapter>();
        visualAssets.Attach(card.transform);
        CreateRepScorePanel(card.transform);
        CreateLiveScorePanel(card.transform);
        visualAssets.ApplyVisionStyle(card.transform);
        SafeLiftVisionProStyle.Apply(card.transform);
        breathingUI = gameObject.AddComponent<SafeLiftBreathingReportUI>();
        breathingUI.Attach(card.transform);
        qrShare = gameObject.AddComponent<SafeLiftQRShareUI>();
        qrShare.Attach(card.transform);
        tutorialUI = gameObject.AddComponent<SafeLiftTutorialCalibrationUI>();
        tutorialUI.Attach(card.transform);
        UpdateSensorModeButton();
        UpdateViewModeButton();
        ApplyViewModeLayout(SafeLiftScreenState.StartDashboard);
        ConfigureBackendUrl(currentBackendUrl);
    }

    private void UpdateSensorModeButton()
    {
        if (sensorModeButtonText == null) return;
        sensorModeButtonText.text = DemoMode ? "모드: 데모" : "모드: 실전";
    }

    private void EnforceSingleSafeLiftRuntimeCanvas()
    {
        DisableDemoDebugHudOverlays();

        var canvases = FindObjectsOfType<Canvas>(true);
        foreach (var candidate in canvases)
        {
            if (candidate == null) continue;
            if (candidate == canvas) continue;
            if (candidate.name == "SafeLiftDemoWorldCanvas")
            {
                candidate.gameObject.SetActive(false);
                Debug.LogWarning("Disabled extra SafeLiftDemoWorldCanvas to prevent duplicated/overlapping UI.");
            }
        }

        HidePanelsOutsideCurrentCard();
        EnforceCurrentPanelVisibility(State);
        BringFlowControlsToFront();
    }

    private void BringFlowControlsToFront()
    {
        if (flowControls == null || card == null) return;

        // During the tutorial, make the controls children of the tutorial
        // panel itself. Their final sibling order then wins over the image,
        // labels, and progress bar inside that panel.
        var tutorialPanel = State == SafeLiftScreenState.TutorialCalibration
            ? tutorialUI?.PanelTransform
            : null;
        var desiredParent = tutorialPanel != null ? tutorialPanel : card.transform;
        if (flowControls.transform.parent != desiredParent)
            flowControls.transform.SetParent(desiredParent, false);

        if (tutorialPanel != null)
        {
            var flowRect = flowControls.GetComponent<RectTransform>();
            if (flowRect != null)
            {
                flowRect.anchorMin = new Vector2(.5f, .5f);
                flowRect.anchorMax = new Vector2(.5f, .5f);
                flowRect.pivot = new Vector2(.5f, .5f);
                flowRect.anchoredPosition = Vector2.zero;
                flowRect.localRotation = Quaternion.identity;
                flowRect.localScale = Vector3.one;
            }
        }

        // The world-space canvas faces away from the viewer. A negative local
        // Z moves the controls toward the HMD/camera, so they physically sit
        // in front of the tutorial panel instead of merely being later in the
        // sibling order.
        var flowLocalPosition = flowControls.transform.localPosition;
        flowLocalPosition.z = -30f;
        flowControls.transform.localPosition = flowLocalPosition;
        flowControls.transform.SetAsLastSibling();

        if (primaryButton != null) primaryButton.transform.SetAsLastSibling();
        if (rightNextButton != null) rightNextButton.transform.SetAsLastSibling();
        if (recalibrationButton != null) recalibrationButton.transform.SetAsLastSibling();

        // HMD/world-space rendering is not determined by hierarchy alone.
        // The nested override-sorting Canvas guarantees the controls render
        // above the tutorial/report panels on the same dashboard Canvas.
        var flowCanvas = flowControls.GetComponent<Canvas>();
        if (flowCanvas == null) flowCanvas = flowControls.AddComponent<Canvas>();
        flowCanvas.overrideSorting = true;
        flowCanvas.sortingOrder = 1000;
    }

    private void EnforceCurrentPanelVisibility(SafeLiftScreenState state)
    {
        SetScenarioPanelActiveGlobally("TutorialCalibrationPanel", state == SafeLiftScreenState.TutorialCalibration);
        SetScenarioPanelActiveGlobally("Set2LiveFeedbackPanel", state == SafeLiftScreenState.Set2LiveFeedback);
        SetScenarioPanelActiveGlobally("BreathingReportPanel", state == SafeLiftScreenState.BreathingReportGenerating);
        // The scene contains a legacy ReportPanel with AIReportText. The
        // current PostRepReview report is generated under SafeLiftMainCard;
        // keep the legacy panel disabled so the two layouts cannot overlap.
        SetScenarioPanelActiveGlobally("ReportPanel", false);
        SetScenarioPanelActiveGlobally("PostRepAvatarReviewPanel", false);
        SetScenarioPanelActiveGlobally("QRSharePanel", state == SafeLiftScreenState.QRShare);
    }

    private void HideAllScenarioPanels()
    {
        SetNamedChildActive("TutorialCalibrationPanel", false);
        SetNamedChildActive("Set2LiveFeedbackPanel", false);
        SetNamedChildActive("BreathingReportPanel", false);
        SetNamedChildActive("ReportPanel", false);
        SetNamedChildActive("PostRepAvatarReviewPanel", false);
        SetNamedChildActive("QRSharePanel", false);
    }

    private void HidePanelsOutsideCurrentCard()
    {
        var panelNames = new[]
        {
            "TutorialCalibrationPanel",
            "Set2LiveFeedbackPanel",
            "BreathingReportPanel",
            "ReportPanel",
            "PostRepAvatarReviewPanel",
            "QRSharePanel"
        };

        foreach (var transform in FindObjectsOfType<Transform>(true))
        {
            if (transform == null || transform.gameObject == null) continue;
            var isScenarioPanel = false;
            foreach (var panelName in panelNames)
            {
                if (transform.name == panelName)
                {
                    isScenarioPanel = true;
                    break;
                }
            }
            if (!isScenarioPanel) continue;
            if (card != null && transform.IsChildOf(card.transform)) continue;
            if (transform.gameObject.activeSelf) transform.gameObject.SetActive(false);
        }
    }

    private static void DisableDemoDebugHudOverlays()
    {
        foreach (var receiver in FindObjectsOfType<AvatarPoseUdpReceiver>(true))
            receiver.SetStatusOverlayVisible(false);

        foreach (var behaviour in FindObjectsOfType<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "MediaPipeDebugHud") continue;
            var field = behaviour.GetType().GetField("showHud");
            if (field != null && field.FieldType == typeof(bool)) field.SetValue(behaviour, false);
        }
    }

    private void UpdateDiagnosticsHud()
    {
        if (diagnosticsText == null) return;

        var activePanels = CountActiveScenarioPanels();
        var avatarStatus = avatarRuntime != null ? avatarRuntime.DiagnosticsLabel() : "avatar controller missing";
        var canvasMode = canvas != null && canvas.transform.parent == transform ? "world" : "head-locked";
        var cameraName = canvas != null && canvas.worldCamera != null ? canvas.worldCamera.name : "none";
        diagnosticsText.text = "Debug: state " + ((int)State + 1) + "/8 " + State
            + " | active scenario panels " + activePanels
            + " | backend " + (backendConnected ? "connected" : "mock/fallback")
            + " | real preflight " + (IsRealModePreflightReady() ? "ready" : "waiting")
            + " | view " + ViewModeLabel()
            + " | webcam " + liveFrameStatus
            + " | canvas " + canvasMode
            + " | cam " + cameraName
            + " | " + avatarStatus;
        diagnosticsText.color = activePanels <= 1 ? new Color(.55f, .95f, 1f, .88f) : new Color(1f, .35f, .2f, 1f);
        UpdateSensorCalibrationHud();
    }

    private void UpdateSensorCalibrationHud()
    {
        if (sensorCalibrationText == null) return;

        var snapshot = squatSensorReceiver != null ? squatSensorReceiver.Latest : null;
        var backendCalibration = IsBackendCalibrationReady() ? "READY" : "WAIT";
        var backendSensor = IsBackendSensorReady() ? "READY" : "WAIT";
        var avatar = IsUnityAvatarCalibrationReady() ? "READY" : "WAIT";
        var imu = snapshot != null && snapshot.imu != null && snapshot.imu.valid ? "READY" : "WAIT";
        var fsr = snapshot != null && snapshot.fsr != null && snapshot.fsr.valid ? "READY" : "WAIT/INVALID";
        var pressure = snapshot != null ? snapshot.pressure : null;
        var bracingStatus = pressure != null && !string.IsNullOrEmpty(pressure.status) ? pressure.status : "WAIT";
        var pBraced = pressure != null ? pressure.pBraced.ToString("0.0") : "--";
        var packets = snapshot != null ? snapshot.packetCount.ToString() : "0";
        var fusionReps = snapshot != null ? snapshot.repCount.ToString() : "0";
        var reportStatus = latestBackendState != null && !string.IsNullOrWhiteSpace(latestBackendState.report_status)
            ? latestBackendState.report_status.ToUpperInvariant()
            : "NONE";
        var backendTotal = backendRepCount + " / " + (TargetReps * 2);
        var baseline = latestBackendState != null && latestBackendState.set_comparison != null ? "READY" : "WAIT/UNKNOWN";
        var reportGate = ReportGateText(reportStatus);
        var lastRepScoreText = lastCompletedRepScore != null
            ? lastCompletedRepScore.totalScore.ToString("0.0") + "/100  FSR " + lastCompletedRepScore.fsr.score.ToString("0.0") + "/35  BRACING " + lastCompletedRepScore.bracing.score.ToString("0.0") + "/35  IMU " + lastCompletedRepScore.spine.score.ToString("0.0") + "/30"
            : "WAIT";
        var gate = (!IsBackendCalibrationReady() && !IsUnityAvatarCalibrationReady()) || !IsBackendSensorReady()
            ? "BLOCKED"
            : "READY";

        sensorCalibrationText.text = "CALIBRATION / NEXT GATE\n"
            + "Backend calibration : " + backendCalibration + "\n"
            + "Backend sensor       : " + backendSensor + "\n"
            + "IMU pose             : " + imu + " / Avatar " + avatar + "\n"
            + "FSR UDP              : " + fsr + "\n"
            + "Bracing              : " + bracingStatus + " / pBraced " + pBraced + " kPa\n"
            + "Fusion squat count   : " + fusionReps + "\n"
            + "Fusion SET 2 gate    : " + fusionRepCount + " / " + TargetReps + "\n"
            + "Backend total reps   : " + backendTotal + "\n"
            + "SET 1 baseline       : " + baseline + "\n"
            + "Report status        : " + reportStatus + "\n"
            + "Report gate          : " + reportGate + "\n"
            + "Last rep score       : " + lastCompletedRepSet + " rep " + lastCompletedRepNumber + " / " + lastRepScoreText + "\n"
            + "UDP packets          : " + packets + "\n"
            + "Tutorial next gate   : " + gate;
        sensorCalibrationText.color = gate == "READY"
            ? new Color(.35f, 1f, .55f, .95f)
            : new Color(1f, .75f, .25f, .95f);
    }

    private string ReportGateText(string reportStatus)
    {
        if (backendReportReady || string.Equals(reportStatus, "READY", StringComparison.OrdinalIgnoreCase))
            return "READY";
        if (string.Equals(reportStatus, "GENERATING", StringComparison.OrdinalIgnoreCase))
            return "GENERATING";
        if (State != SafeLiftScreenState.BreathingReportGenerating)
            return "WAIT / SET 2 incomplete";
        if (latestBackendState != null && !string.IsNullOrWhiteSpace(latestBackendState.report_message))
            return latestBackendState.report_message.Length > 34
                ? latestBackendState.report_message.Substring(0, 34) + "..."
                : latestBackendState.report_message;
        return "BLOCKED / no backend report status";
    }

    private int CountActiveScenarioPanels()
    {
        if (card == null) return 0;

        var count = 0;
        if (IsNamedChildActive("TutorialCalibrationPanel")) count++;
        if (IsNamedChildActive("Set2LiveFeedbackPanel")) count++;
        if (IsNamedChildActive("BreathingReportPanel")) count++;
        if (IsNamedChildActive("PostRepAvatarReviewPanel")) count++;
        if (IsNamedChildActive("QRSharePanel")) count++;
        return count;
    }

    private bool IsNamedChildActive(string objectName)
    {
        if (card == null) return false;

        var transforms = card.GetComponentsInChildren<Transform>(true);
        foreach (var item in transforms)
        {
            if (item == null || item.name != objectName) continue;
            return item.gameObject.activeSelf;
        }

        return false;
    }

    private void SetNamedChildActive(string objectName, bool active)
    {
        if (card == null) return;

        var transforms = card.GetComponentsInChildren<Transform>(true);
        foreach (var item in transforms)
        {
            if (item == null || item.name != objectName) continue;
            if (item.gameObject.activeSelf != active) item.gameObject.SetActive(active);
        }
    }

    private void SetScenarioPanelActiveGlobally(string objectName, bool active)
    {
        foreach (var item in FindObjectsOfType<Transform>(true))
        {
            if (item == null || item.name != objectName) continue;
            var belongsToCurrentCard = card != null && item.IsChildOf(card.transform);
            var shouldBeActive = active && belongsToCurrentCard;
            if (item.gameObject.activeSelf != shouldBeActive)
                item.gameObject.SetActive(shouldBeActive);
        }
    }

    private static TMP_Text Text(Transform parent, string name, float size, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(760, 70);
        rt.anchoredPosition = pos;
        var text = go.AddComponent<TextMeshProUGUI>();
        SafeLiftKoreanText.Apply(text);
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    private static GameObject ControlRoot(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(.5f, .5f);
        rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        return go;
    }

    private static TMP_FontAsset ReadableFontAsset()
    {
        if (readableFontAsset != null) return readableFontAsset;

        try
        {
            var font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Arial Unicode MS", "Segoe UI" },
                90);
            if (font == null) return null;

            readableFontAsset = TMP_FontAsset.CreateFontAsset(font);
            if (readableFontAsset != null)
            {
                readableFontAsset.name = "SafeLiftRuntimeReadableFont";
                readableFontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SafeLift XR could not create runtime Korean TMP font. Falling back to default TMP font: " + ex.Message);
        }

        return readableFontAsset;
    }

    private static Image Frame(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var image = go.AddComponent<Image>();
        image.color = new Color(.015f, .025f, .04f, 1f);
        return image;
    }

    private static Button Button(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
    {
        return ButtonSized(parent, label, pos, new Vector2(240, 60), action);
    }

    private static Button ButtonSized(Transform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("SafeLiftButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var image = go.AddComponent<Image>();
        image.color = new Color(.12f, .55f, .9f);
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(action);
        var text = Text(go.transform, "Label", 16, Vector2.zero);
        text.rectTransform.sizeDelta = rt.sizeDelta;
        text.text = label;
        return button;
    }

    private static TMP_InputField InputField(Transform parent, string name, string value, Vector2 pos)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280, 40);
        rt.anchoredPosition = pos;
        var image = root.AddComponent<Image>();
        image.color = new Color(.02f, .04f, .07f, 1f);

        var viewport = new GameObject("TextViewport");
        viewport.transform.SetParent(root.transform, false);
        var viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(12, 4);
        viewportRt.offsetMax = new Vector2(-12, -4);
        viewport.AddComponent<RectMask2D>();

        var text = Text(viewport.transform, "Text", 15, Vector2.zero);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        text.enableWordWrapping = false;

        var placeholder = Text(viewport.transform, "Placeholder", 14, Vector2.zero);
        placeholder.text = "http://PC_IP:8090";
        placeholder.color = new Color(.65f, .7f, .78f, .65f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = Vector2.zero;
        placeholder.rectTransform.offsetMax = Vector2.zero;
        placeholder.enableWordWrapping = false;

        var input = root.AddComponent<TMP_InputField>();
        input.textViewport = viewportRt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterValidation = TMP_InputField.CharacterValidation.None;
        SafeLiftKoreanText.ApplyAllUnder(root.transform);
        return input;
    }

    private string RealModePreflightText()
    {
        var urlCheck = IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl)
            ? "BLOCKED: Quest cannot reach a PC backend through 127.0.0.1."
            : "OK: backend URL format is acceptable for this runtime.";
        var backendCheck = backendConnected
            ? "OK: /api/session_state connected."
            : "WAIT: backend is not connected yet.";
        var calibrationCheck = IsBackendCalibrationReady()
            ? "OK: backend calibration ready."
            : "WAIT: backend calibration will gate SET 1.";
        var sensorCheck = IsBackendSensorReady()
            ? "OK: " + SensorStatusText(latestBackendState)
            : "WAIT: backend has not published a usable SafeLift sensor source yet.";
        var qrCheck = "OK: QR is laptop-browser only (/qr), not an in-VR QR image.";
        var healthChecklist = HealthChecklistText();

        return "REAL MODE PREFLIGHT\n"
            + "- " + urlCheck + "\n"
            + "- " + backendCheck + "\n"
            + "- " + calibrationCheck + "\n"
            + "- " + sensorCheck + "\n"
            + "- " + qrCheck
            + healthChecklist;
    }

    private void BeginInitialCanvasRecenter()
    {
        canvasWorldLocked = false;
        canvasRecenterUntil = Time.unscaledTime + InitialCanvasRecenterSeconds;
        ReframeCanvasForViewer();
    }

    private void UpdateInitialCanvasRecenter()
    {
        if (canvas == null) return;

        if (!xrPoseRecentered && IsSafeLiftXrRuntimeActive())
        {
            xrPoseRecentered = true;
            BeginInitialCanvasRecenter();
        }

        var xrRuntime = IsSafeLiftXrRuntimeActive();
        if (canvasWorldLocked && !xrRuntime) return;

        // In XR keep the dashboard in the user's comfortable forward field of view. The panel is
        // world-space (not a child of the headset), but it is refreshed from the current head pose
        // so walking, turning, or changing posture cannot leave the UI behind the user.
        if (xrRuntime || Time.unscaledTime <= canvasRecenterUntil)
        {
            ReframeCanvasForViewer();
            return;
        }

        if (!ReframeCanvasForViewer() && xrRuntime)
        {
            // Do not lock the UI to a bad startup pose. Quest Link can report XR active before
            // the mounted HMD pose is stable; keep retrying in short windows until the head pose is usable.
            canvasRecenterUntil = Time.unscaledTime + .25f;
            return;
        }

        canvasWorldLocked = true;
        Debug.Log("SafeLift XR dashboard recentered from a valid viewer pose and locked in world space.");
    }

    public void RecenterDashboardForUser()
    {
        BeginInitialCanvasRecenter();
    }

    private bool ReframeCanvasForViewer()
    {
        if (canvas == null) return false;

        var camera = FindSafeLiftViewCamera();
        if (camera == null) return false;
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        canvas.enabled = true;
        canvas.worldCamera = camera;
        // A copied world-space Canvas can be placed on a layer omitted from
        // the OVR eye camera. Include that layer so the dashboard cannot
        // disappear while the environment continues to render.
        camera.cullingMask |= 1 << canvas.gameObject.layer;
        var viewer = FindSafeLiftHeadTransform(camera);
        var xrActive = IsSafeLiftXrRuntimeActive();
        var useEditorPreviewPose = Application.isEditor && !xrActive;
        var validViewerPose = !useEditorPreviewPose && viewer != null && viewer.position.y > 0.65f && viewer.position.y < 2.25f;

        if (xrActive && validViewerPose)
        {
            // Quest UI must use the same eye anchor that renders the Game/Left Eye view.
            // Parenting the world-space canvas to centerEyeAnchor removes the camera-space
            // mismatch that previously left the dashboard above/right of the user's view.
            if (canvas.transform.parent != viewer)
                canvas.transform.SetParent(viewer, false);
            // The 900-unit card needs room below its title and controls. Keep
            // its center above the HMD origin so PLAY and the lower action
            // row remain inside the visible frustum.
            canvas.transform.localPosition = new Vector3(0f, -0.10f, ComfortableCanvasDistance);
            canvas.transform.localRotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one * ComfortableCanvasScale;
            canvas.worldCamera = camera;
            return true;
        }

        if (canvas.transform.parent != transform)
            canvas.transform.SetParent(transform, true);

        Vector3 viewerPosition;
        Vector3 forward;
        if (useEditorPreviewPose || viewer == null || !validViewerPose)
        {
            // The editor/Quest-Link fallback camera is not always at the HMD
            // eye height yet. Lift the preview pose enough to keep the full
            // world-space card and its bottom PLAY button in frame.
            viewerPosition = new Vector3(0f, 2.05f, -0.35f);
            forward = viewer != null ? Vector3.ProjectOnPlane(viewer.forward, Vector3.up) : Vector3.forward;
        }
        else
        {
            viewerPosition = viewer.position;
            // Keep the panel slightly below eye center so seated Quest users see it immediately.
            viewerPosition.y = Mathf.Clamp(viewerPosition.y - 0.20f, 0.85f, 1.75f);
            forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        var right = new Vector3(forward.z, 0f, -forward.x);
        canvas.transform.position = viewerPosition + forward * ComfortableCanvasDistance;
        canvas.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        canvas.transform.localScale = Vector3.one * ComfortableCanvasScale;
        canvas.worldCamera = camera;
        return useEditorPreviewPose || validViewerPose;
    }


    private static Camera FindSafeLiftViewCamera()
    {
        // A loaded XR flag can lag behind the actual rig during Quest Link
        // startup. Prefer the Center Eye camera whenever the rig exists so a
        // fixed editor preview can never replace HMD tracking.
        var rig = UnityEngine.Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
        {
            var centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (centerEyeCamera != null && centerEyeCamera.isActiveAndEnabled)
                return centerEyeCamera;
        }
#if UNITY_EDITOR
        var editorPreview = GameObject.Find("SafeLiftEditorPreviewCamera");
        var editorPreviewCamera = editorPreview != null ? editorPreview.GetComponent<Camera>() : null;
        if (!IsSafeLiftXrRuntimeActive() && editorPreviewCamera != null && editorPreviewCamera.isActiveAndEnabled)
            return editorPreviewCamera;
#endif
        if (rig != null && rig.centerEyeAnchor != null)
        {
            var centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (centerEyeCamera != null) return centerEyeCamera;
        }

        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == null || !camera.isActiveAndEnabled) continue;
            if (camera.GetComponentInParent<OVRCameraRig>() != null) return camera;
        }

        var mainCamera = Camera.main;
        return mainCamera != null ? mainCamera : UnityEngine.Object.FindObjectOfType<Camera>();
    }

    private static bool IsSafeLiftXrRuntimeActive()
    {
        var activeRig = UnityEngine.Object.FindObjectOfType<OVRCameraRig>();
        if (activeRig != null && activeRig.centerEyeAnchor != null)
        {
            var eyeCamera = activeRig.centerEyeAnchor.GetComponent<Camera>();
            if (eyeCamera != null && eyeCamera.isActiveAndEnabled) return true;
        }
#if UNITY_EDITOR
        if (XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            return true;

        try
        {
            if (OVRManager.isHmdPresent) return true;
        }
        catch
        {
        }

        return false;
#else
        if (XRSettings.isDeviceActive || !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            return true;

        try
        {
            if (OVRManager.isHmdPresent) return true;
        }
        catch
        {
        }

        return false;
#endif
    }

    private static Transform FindSafeLiftHeadTransform(Camera camera)
    {
        var rig = UnityEngine.Object.FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;

        if (camera != null && camera.isActiveAndEnabled)
            return camera.transform;

        return camera != null ? camera.transform : null;
    }
    private string HealthChecklistText()
    {
        if (latestBackendHealth == null) return "\n- WAIT: /api/health checklist has not been received yet.";

        var text = "\n\n/api/health Launch Checklist";
        if (latestBackendHealth.launch_steps != null && latestBackendHealth.launch_steps.Length > 0)
        {
            foreach (var step in latestBackendHealth.launch_steps)
            {
                if (step == null) continue;
                text += "\n" + (step.ok ? "OK" : "WAIT") + ": " + (step.step ?? "Launch gate") + " - " + (step.action ?? "-");
            }
        }

        if (latestBackendHealth.integration_checks != null && latestBackendHealth.integration_checks.Length > 0)
        {
            text += "\n\nIntegration Contract";
            foreach (var check in latestBackendHealth.integration_checks)
            {
                if (check == null) continue;
                var status = check.out_of_scope ? "N/A" : (check.ok ? "OK" : "WAIT");
                text += "\n" + status + ": " + (check.name ?? "Contract") + " - " + (check.detail ?? "-");
            }
        }

        text += "\nSensor raw acquisition in Unity scope: " + (latestBackendHealth.sensor_raw_acquisition_in_scope ? "YES" : "NO");

        if (latestBackendHealth.real_mode_blockers != null && latestBackendHealth.real_mode_blockers.Length > 0)
        {
            text += "\nBlockers: " + string.Join(" / ", latestBackendHealth.real_mode_blockers);
        }
        return text;
    }

    private string RealModePreflightStatus()
    {
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl))
            return "REAL MODE PREFLIGHT BLOCKED: replace 127.0.0.1 with the laptop/PC IPv4 address.";
        if (latestBackendHealth != null && !latestBackendHealth.ready_for_real_mode_start)
            return "REAL MODE PREFLIGHT: " + HealthBlockerSummary();
        if (!backendConnected)
            return "REAL MODE PREFLIGHT: waiting for backend /api/session_state.";
        if (!IsBackendCalibrationReady())
            return "REAL MODE PREFLIGHT: backend connected; waiting for calibration ready.";
        if (!IsBackendSensorReady())
            return "REAL MODE PREFLIGHT: waiting for backend SafeLift sensor source.";
        return "REAL MODE PREFLIGHT READY: perform squats; rep count and report are backend-driven. " + SensorStatusText(latestBackendState);
    }

    private string HealthBlockerSummary()
    {
        if (latestBackendHealth == null || latestBackendHealth.real_mode_blockers == null || latestBackendHealth.real_mode_blockers.Length == 0)
            return "waiting for /api/health launch checklist.";
        return string.Join(" / ", latestBackendHealth.real_mode_blockers);
    }

    private bool IsRealModePreflightReady()
    {
        if (DemoMode) return true;
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl)) return false;
        if (latestBackendHealth != null) return latestBackendHealth.ready_for_real_mode_start;
        return backendConnected && IsBackendCalibrationReady() && IsBackendSensorReady();
    }

    private string QuestBackendUrlWarning()
    {
        if (IsQuestRuntime() && IsLoopbackBackendUrl(currentBackendUrl))
            return "Quest warning: 127.0.0.1 points to the headset, not the laptop/PC.";
        if (IsLoopbackBackendUrl(currentBackendUrl))
            return "Editor/local backend URL is OK; Quest standalone must use laptop/PC IPv4.";
        return "Quest/phone LAN URL ready.";
    }

    private static bool IsQuestRuntime()
    {
        return Application.platform == RuntimePlatform.Android && !Application.isEditor;
    }

    private static bool IsLoopbackBackendUrl(string url)
    {
        var value = (url ?? string.Empty).Trim().ToLowerInvariant();
        return value.Contains("://127.0.0.1") ||
               value.Contains("://localhost") ||
               value.Contains("://0.0.0.0");
    }

    private static string Label(SafeLiftScreenState state)
    {
        if (state == SafeLiftScreenState.Set1Practice) return "[Set1]";
        if (state == SafeLiftScreenState.Set2LiveFeedback) return "[Set2 with LiveFeedBack]";
        if (state == SafeLiftScreenState.RestBeforeLiveFeedback) return "REST";
            if (state == SafeLiftScreenState.BreathingReportGenerating) return "Waiting for AI Report";
            if (state == SafeLiftScreenState.PostRepReview) return "AI Report and Score Analysis";
        return state.ToString().Replace("BeforeLiveFeedback", " / Rest").Replace("ReportGenerating", " / Report");
    }
    private string RuntimeModeLabel() => DemoMode ? "DEMO / MOCK SENSOR" : backendConnected ? "REAL / BACKEND CONNECTED" : "REAL / WAITING BACKEND";
    private static string NormalizeBackendUrl(string url)
    {
        var value = (url ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value)) return "http://127.0.0.1:8090";
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "http://" + value;
        return value.TrimEnd('/');
    }
    private static string PhaseForRep(int rep) => new[] { "Standing", "Descent", "Bottom", "Ascent" }[Mathf.Clamp(rep, 0, 3)];
    private string MainIssue() => Scenario == SafeLiftDemoScenario.NoScenario || Scenario == SafeLiftDemoScenario.Normal ? "None / live measurement" : Scenario.ToString();

    private SafeLiftDemoScenario ScenarioFromBackendIssue(string mainIssue, string[] detectedErrors)
    {
        if (MatchesIssue(mainIssue, "KneeValgus")) return SafeLiftDemoScenario.KneeValgus;
        if (MatchesIssue(mainIssue, "WeightShift")) return SafeLiftDemoScenario.WeightShift;
        if (MatchesIssue(mainIssue, "CorePressureDrop")) return SafeLiftDemoScenario.CorePressureDrop;
        if (MatchesIssue(mainIssue, "TorsoLean") || MatchesIssue(mainIssue, "ShallowDepth") || MatchesIssue(mainIssue, "TrackingUncertain")) return SafeLiftDemoScenario.ComplexError;

        if (detectedErrors != null)
        {
            foreach (var error in detectedErrors)
            {
                if (MatchesIssue(error, "KneeValgus")) return SafeLiftDemoScenario.KneeValgus;
                if (MatchesIssue(error, "WeightShift")) return SafeLiftDemoScenario.WeightShift;
                if (MatchesIssue(error, "CorePressureDrop")) return SafeLiftDemoScenario.CorePressureDrop;
                if (MatchesIssue(error, "TorsoLean") || MatchesIssue(error, "ShallowDepth") || MatchesIssue(error, "TrackingUncertain")) return SafeLiftDemoScenario.ComplexError;
            }
        }

        return SafeLiftDemoScenario.NoScenario;
    }

    private static bool MatchesIssue(string value, string token)
    {
        return !string.IsNullOrWhiteSpace(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string ReviewPhaseForIssue()
    {
        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return "Ascent";
            case SafeLiftDemoScenario.WeightShift: return "Bottom";
            case SafeLiftDemoScenario.CorePressureDrop: return "Descent";
            case SafeLiftDemoScenario.ComplexError: return "Bottom";
            default: return "Standing";
        }
    }

    private string NextCueForIssue()
    {
        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus: return "Push knees outward over toes.";
            case SafeLiftDemoScenario.WeightShift: return "Keep pressure centered over midfoot.";
            case SafeLiftDemoScenario.CorePressureDrop: return "Re-brace before starting the descent.";
            case SafeLiftDemoScenario.ComplexError: return "Slow down and stabilize before ascent.";
            default: return "Keep the same tempo and posture.";
        }
    }

    private SafeLiftScoreResult CurrentUnityScore()
    {
        if (scoreController == null)
            scoreController = GetComponent<SafeLiftUnityScoreController>();
        return scoreController != null ? scoreController.Recalculate() : null;
    }

    private static SafeLiftScoreStatus AverageStatus(List<SafeLiftScoreResult> scores, Func<SafeLiftScoreResult, SafeLiftScoreStatus> selector)
    {
        var hasWarning = false;
        foreach (var score in scores)
        {
            if (score == null) continue;
            var status = selector(score);
            if (status == SafeLiftScoreStatus.DANGER) return SafeLiftScoreStatus.DANGER;
            if (status == SafeLiftScoreStatus.WARNING) hasWarning = true;
        }
        return hasWarning ? SafeLiftScoreStatus.WARNING : SafeLiftScoreStatus.NORMAL;
    }

    private static SafeLiftScoreResult AverageRepComponents(List<SafeLiftScoreResult> scores)
    {
        var valid = scores.Where(score => score != null).ToList();
        if (valid.Count == 0) return null;

        var result = new SafeLiftScoreResult
        {
            fsr = new SafeLiftFsrScoreResult(),
            bracing = new SafeLiftBracingScoreResult(),
            spine = new SafeLiftSpineScoreResult()
        };

        foreach (var score in valid)
        {
            if (score.fsr != null)
            {
                result.fsr.score += score.fsr.score;
                result.fsr.balanceScore += score.fsr.balanceScore;
                result.fsr.heelScore += score.fsr.heelScore;
                result.fsr.balanceDurationSeconds += score.fsr.balanceDurationSeconds;
                result.fsr.heelDurationSeconds += score.fsr.heelDurationSeconds;
            }
            if (score.bracing != null)
            {
                result.bracing.score += score.bracing.score;
                result.bracing.durationSeconds += score.bracing.durationSeconds;
                result.bracing.bracingLoss += score.bracing.bracingLoss;
                result.bracing.pRelaxed += score.bracing.pRelaxed;
                result.bracing.pBraced += score.bracing.pBraced;
                result.bracing.current += score.bracing.current;
            }
            if (score.spine != null)
            {
                result.spine.score += score.spine.score;
                result.spine.durationSeconds += score.spine.durationSeconds;
                result.spine.relativeAngle += score.spine.relativeAngle;
                result.spine.imuPelvisDelta += score.spine.imuPelvisDelta;
                result.spine.buttWink |= score.spine.buttWink;
                result.spine.standingPelvicTuck |= score.spine.standingPelvicTuck;
                result.spine.depthReached |= score.spine.depthReached;
            }
            result.activeMovementSeconds += score.activeMovementSeconds;
        }

        var count = valid.Count;
        result.totalScore = valid.Average(score => score.totalScore);
        result.activeMovementSeconds /= count;
        result.fsr.score /= count;
        result.fsr.balanceScore /= count;
        result.fsr.heelScore /= count;
        result.fsr.balanceDurationSeconds /= count;
        result.fsr.heelDurationSeconds /= count;
        result.bracing.score /= count;
        result.bracing.durationSeconds /= count;
        result.bracing.bracingLoss /= count;
        result.bracing.pRelaxed /= count;
        result.bracing.pBraced /= count;
        result.bracing.current /= count;
        result.spine.score /= count;
        result.spine.durationSeconds /= count;
        result.spine.relativeAngle /= count;
        result.spine.imuPelvisDelta /= count;
        result.fsr.balanceStatus = AverageStatus(valid, score => score.fsr != null ? score.fsr.balanceStatus : SafeLiftScoreStatus.NORMAL);
        result.fsr.overallHeelStatus = AverageStatus(valid, score => score.fsr != null ? score.fsr.overallHeelStatus : SafeLiftScoreStatus.NORMAL);
        result.bracing.status = AverageStatus(valid, score => score.bracing != null ? score.bracing.status : SafeLiftScoreStatus.NORMAL);
        result.bracing.failed = result.bracing.status == SafeLiftScoreStatus.DANGER;
        result.spine.status = AverageStatus(valid, score => score.spine != null ? score.spine.status : SafeLiftScoreStatus.NORMAL);
        result.grade = result.totalScore >= 90f ? "S" : result.totalScore >= 75f ? "A" : result.totalScore >= 60f ? "B" : "C";
        return result;
    }

    private SafeLiftScoreResult FinalUnityScore()
    {
        var current = CurrentUnityScore();
        var set1Best = BestRepScore(set1RepScores);
        var set2Best = BestRepScore(set2RepScores);
        var best = set1Best == null ? set2Best
            : set2Best == null ? set1Best
            : set2Best.totalScore > set1Best.totalScore ? set2Best : set1Best;
        if (best == null) return current;
        return best;
    }

    private string ScoreSummaryText()
    {
        var setScoreSummary = SetScoreSummaryText();
        if (!string.IsNullOrWhiteSpace(setScoreSummary))
            return setScoreSummary;
        var unityScore = CurrentUnityScore();
        if (unityScore == null)
            return "Unity score unavailable\nSensor evidence was not available at report time.";

        var fsr = unityScore.fsr;
        var bracing = unityScore.bracing;
        var spine = unityScore.spine;
        var fsrScore = fsr != null ? fsr.score : 0f;
        var bracingScore = bracing != null ? bracing.score : 0f;
        var spineScore = spine != null ? spine.score : 0f;
        var fsrDeduction = Mathf.Max(0f, 35f - fsrScore);
        var bracingDeduction = Mathf.Max(0f, 35f - bracingScore);
        var spineDeduction = Mathf.Max(0f, 30f - spineScore);
        var fsrReason = fsr == null ? "NO_SIGNAL" : "imbalance " + fsr.imbalancePercent.ToString("0.0") + "% / heel L " + fsr.leftHeelRatio.ToString("0.0") + "% R " + fsr.rightHeelRatio.ToString("0.0") + "%";
        var bracingReason = bracing == null ? "NO_SIGNAL" : "loss " + bracing.bracingLoss.ToString("0.0") + "% / current " + bracing.current.ToString("0.0") + " / braced " + bracing.pBraced.ToString("0.0") + " kPa";
        var spineReason = spine == null ? "NO_SIGNAL" : (spine.buttWink ? "BUTT_WINK" : "no butt wink") + " / angle " + spine.relativeAngle.ToString("0.0") + " / depth " + (spine.depthReached ? "OK" : "NOT_REACHED");
        return "SafeLift Unity Score  " + unityScore.totalScore.ToString("0.0") + " / 100\n"
            + "Grade                 " + (string.IsNullOrWhiteSpace(unityScore.grade) ? "-" : unityScore.grade) + "\n"
            + "FSR Balance           " + fsrScore.ToString("0.0") + " / 35  (-" + fsrDeduction.ToString("0.0") + ")\n"
            + "  reason: " + fsrReason + "\n"
            + "Core Bracing          " + bracingScore.ToString("0.0") + " / 35  (-" + bracingDeduction.ToString("0.0") + ")\n"
            + "  reason: " + bracingReason + "\n"
            + "IMU / Butt Wink       " + spineScore.ToString("0.0") + " / 30  (-" + spineDeduction.ToString("0.0") + ")\n"
            + "  reason: " + spineReason + "\n"
            + "Total deduction       -" + Mathf.Max(0f, 100f - unityScore.totalScore).ToString("0.0") + "\n"
            + "FSR status            " + (fsr != null ? fsr.balanceStatus.ToString() : "NO_SIGNAL") + "\n"
            + "Bracing status        " + (bracing != null ? bracing.status.ToString() : "NO_SIGNAL") + "\n"
            + "Spine status          " + (spine != null ? spine.status.ToString() : "NO_SIGNAL") + "\n"
            + "Depth reached        " + (spine != null && spine.depthReached ? "YES" : "NO");
    }

    private string LegacyBackendScoreSummaryText()
    {
        var score = latestBackendState != null ? latestBackendState.score_summary : null;
        if (score != null && score.safe_lift_score > 0)
        {
            var delta = score.improvement_delta >= 0 ? "+" + score.improvement_delta : score.improvement_delta.ToString();
            return "SafeLift Score     " + score.safe_lift_score + "   (" + delta + ")\n"
                + "Knee Alignment     " + score.knee_alignment_score + "\n"
                + "Balance            " + score.balance_score + "\n"
                + "Spine Alignment    " + (score.spine_alignment_score > 0 ? score.spine_alignment_score : score.torso_stability_score) + " / 30\n"
                + "Core Bracing       " + score.core_bracing_score + " / 35\n"
                + "FSR Balance        " + score.balance_score + " / 35\n"
                + "Grade              " + (string.IsNullOrWhiteSpace(score.grade) ? "-" : score.grade + " · " + score.grade_label) + "\n"
                + "Evidence           " + (score.score_evidence_complete ? "complete" : "partial") + "\n"
                + "Missing evidence   " + MissingScoreEvidenceText(score) + "\n"
                + "Score basis        " + (string.IsNullOrWhiteSpace(score.basis) ? "safelift_rubric_score_v1" : score.basis) + "\n";
        }

        return "SafeLift Score     84   (+8)\n"
            + "Knee Alignment     " + ScoreFor("knee") + "\n"
            + "Balance            " + ScoreFor("balance") + "\n"
            + "Torso Stability    " + ScoreFor("torso") + "\n"
            + "Core Bracing       " + ScoreFor("core") + "\n";
    }

    private string MissingScoreEvidenceText(SafeLiftScoreSummary score)
    {
        if (score == null || score.score_evidence_complete)
            return "none";
        if (score.missing_score_evidence == null || score.missing_score_evidence.Length == 0)
            return string.IsNullOrWhiteSpace(score.score_confidence) ? "partial" : score.score_confidence;
        return string.Join(", ", score.missing_score_evidence);
    }

    private string ScoreFor(string area)
    {
        switch (Scenario)
        {
            case SafeLiftDemoScenario.KneeValgus:
                return area == "knee" ? "78   (+12)" : area == "balance" ? "86   (+5)" : area == "torso" ? "82   (+4)" : "89   (+6)";
            case SafeLiftDemoScenario.WeightShift:
                return area == "balance" ? "76   (+9)" : area == "knee" ? "84   (+5)" : area == "torso" ? "83   (+4)" : "86   (+6)";
            case SafeLiftDemoScenario.CorePressureDrop:
                return area == "core" ? "74   (+15)" : area == "knee" ? "86   (+4)" : area == "balance" ? "84   (+6)" : "80   (+5)";
            case SafeLiftDemoScenario.ComplexError:
                return area == "knee" ? "72   (+8)" : area == "balance" ? "74   (+7)" : area == "torso" ? "70   (+6)" : "73   (+8)";
            default:
                return "90   (+3)";
        }
    }

    private string BackendSetComparisonText()
    {
        var comparison = latestSetComparison;
        if (comparison == null || string.IsNullOrWhiteSpace(comparison.summary))
            return "SET comparison: waiting for backend summary.";

        var set1Errors = comparison.set1 != null ? comparison.set1.error_count : 0;
        var set2Errors = comparison.set2 != null ? comparison.set2.error_count : 0;
        var set1Cues = comparison.set1 != null && comparison.set1.metrics != null ? comparison.set1.metrics.feedback_count : 0;
        var set2Cues = comparison.set2 != null && comparison.set2.metrics != null ? comparison.set2.metrics.feedback_count : 0;

        return "BACKEND SET 1 vs SET 2\n"
            + comparison.summary + "\n"
            + "Errors: SET1 " + set1Errors + " -> SET2 " + set2Errors + " (delta " + comparison.error_count_delta + ")\n"
            + "Live cues: SET1 " + set1Cues + " -> SET2 " + set2Cues + " (delta " + comparison.feedback_count_delta + ")\n"
            + "Torso peak delta: " + comparison.torso_angle_peak_abs_delta.ToString("0.###")
            + " / Knee valgus delta: " + comparison.knee_valgus_peak_delta.ToString("0.###");
    }

    private void UpdateProgressLabel(SafeLiftScreenState state)
    {
        if (progressText == null) return;

        string[] labels =
        {
            "START",
            "TUTORIAL",
            "SET1",
            "REST",
            "SET2 LIVE",
            "REPORTING",
            "REVIEW",
            "QR"
        };

        var parts = new string[labels.Length];
        var activeIndex = Mathf.Clamp((int)state, 0, labels.Length - 1);
        for (var i = 0; i < labels.Length; i++)
        {
            var step = (i + 1).ToString();
            parts[i] = i == activeIndex ? "[" + step + "/8 " + labels[i] + "]" : step + " " + labels[i];
        }

        progressText.text = string.Join("  >  ", parts);
        progressText.color = Color.cyan;
    }

    private static string VoiceCue(SafeLiftScreenState state)
    {
        switch (state)
        {
            case SafeLiftScreenState.StartDashboard:
                return "Choose webcam or avatar view, then press play.";
            case SafeLiftScreenState.TutorialCalibration:
                return "Stand still. Measuring baseline and tracking quality.";
            case SafeLiftScreenState.Set1Practice:
                return "Set one practice. Live feedback is off. Complete three reps.";
            case SafeLiftScreenState.RestBeforeLiveFeedback:
                return "Set one complete. Rest for five seconds. Live feedback starts next.";
            case SafeLiftScreenState.Set2LiveFeedback:
                return "Set two. Follow one live cue at a time.";
            case SafeLiftScreenState.BreathingReportGenerating:
                return "Workout complete. Breathe while the AI coach report is prepared.";
            case SafeLiftScreenState.PostRepReview:
                return "Report ready. Review the saved avatar phase and score.";
            case SafeLiftScreenState.QRShare:
                return "Take off the headset and scan the laptop QR code.";
            default:
                return "SafeLift XR dashboard.";
        }
    }

    private void OnDestroy()
    {
        if (activeInstance == this) activeInstance = null;
        if (currentWebcamTexture != null) Destroy(currentWebcamTexture);
    }

    private static SafeLiftScreenState NextState(SafeLiftScreenState state) => (SafeLiftScreenState)Mathf.Min(7, (int)state + 1);
    private static SafeLiftScreenState PreviousState(SafeLiftScreenState state) => (SafeLiftScreenState)Mathf.Max(0, (int)state - 1);
}
