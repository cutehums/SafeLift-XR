using System;
using UnityEngine;

/// <summary>
/// Bridges the live UDP sensor snapshot into the Unity-owned scoring rules.
/// Missing evidence is reported as NO_SIGNAL or INVALID_CALIBRATION.
/// </summary>
public sealed class SafeLiftUnityScoreController : MonoBehaviour
{
    public float sensorTimeoutSeconds = 1.5f;
    public float consoleLogInterval = 0.5f;
    public SafeLiftScoreResult Latest { get; private set; }
    public event Action<SafeLiftScoreResult> ScoreUpdated;

    private SafeLiftSquatSensorUdpReceiver receiver;
    private float nextLogTime;
    private SafeLiftState latestBackendState;
    private float activeMovementSeconds;
    private float balanceErrorSeconds;
    private float heelErrorSeconds;
    private float bracingErrorSeconds;
    private float imuErrorSeconds;
    private bool wasActiveMovement;
    private bool pendingMovementReset;

    public void SetBackendState(SafeLiftState state)
    {
        latestBackendState = state;
        Recalculate();
    }

    void Awake()
    {
        receiver = GetComponent<SafeLiftSquatSensorUdpReceiver>();
        if (receiver == null) receiver = gameObject.AddComponent<SafeLiftSquatSensorUdpReceiver>();
    }

    void Update()
    {
        Recalculate();
    }

    public SafeLiftScoreResult Recalculate()
    {
        if (receiver == null) receiver = GetComponent<SafeLiftSquatSensorUdpReceiver>();
        var snapshot = receiver != null ? receiver.Latest : null;
        var sensorAge = receiver != null ? receiver.LastPacketAgeSeconds : float.PositiveInfinity;
        var phaseAge = receiver != null ? receiver.LastFusionPhaseAgeSeconds : float.PositiveInfinity;
        // SafeLift Fusion Monitor is the single authority for scoring phase.
        // Do not fall back to the backend pose phase: a missing/stale Fusion
        // phase must be treated as Standing so the rest interval is never
        // accidentally penalized.
        var phase = snapshot != null && phaseAge <= sensorTimeoutSeconds && !string.IsNullOrEmpty(snapshot.squatPhase)
            ? snapshot.squatPhase
            : "Standing";
        var active = IsSquatPhase(phase);
        // Keep the completed rep's score available while the backend reports
        // the return-to-standing frame. Reset only when the next active rep
        // actually begins, so CaptureFusionRepScore cannot see a reset score.
        if (!active && wasActiveMovement) pendingMovementReset = true;
        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, .25f);
        if (active)
        {
            if (pendingMovementReset)
            {
                activeMovementSeconds = 0f;
                balanceErrorSeconds = 0f;
                heelErrorSeconds = 0f;
                bracingErrorSeconds = 0f;
                imuErrorSeconds = 0f;
                pendingMovementReset = false;
            }
            activeMovementSeconds += dt;
            AccumulateErrorDurations(snapshot, sensorAge, latestBackendState, dt);
        }
        wasActiveMovement = active;
        var input = BuildInput(snapshot, sensorAge, latestBackendState, active);
        input.activeMovementSeconds = activeMovementSeconds;
        input.balanceErrorSeconds = balanceErrorSeconds;
        input.heelErrorSeconds = heelErrorSeconds;
        input.bracingErrorSeconds = bracingErrorSeconds;
        input.imuErrorSeconds = imuErrorSeconds;
        Latest = SafeLiftScoring.Evaluate(input);
        ScoreUpdated?.Invoke(Latest);

        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + Mathf.Max(0.05f, consoleLogInterval);
            Log(Latest, snapshot, sensorAge);
        }
        return Latest;
    }

    private void AccumulateErrorDurations(SafeLiftSquatSensorUdpReceiver.Snapshot snapshot, float age, SafeLiftState state, float dt)
    {
        if (snapshot == null || age > sensorTimeoutSeconds) return;
        if (snapshot.fsr != null && snapshot.fsr.valid)
        {
            var total = Mathf.Max(.001f, snapshot.fsr.leftTotal + snapshot.fsr.rightTotal);
            var imbalance = Mathf.Abs(snapshot.fsr.leftTotal - snapshot.fsr.rightTotal) / total * 100f;
            if (imbalance > 20f) balanceErrorSeconds += dt;
            if (snapshot.fsr.leftHeelRatio < 20f || snapshot.fsr.rightHeelRatio < 20f) heelErrorSeconds += dt;
        }
        if (snapshot.pressure != null && snapshot.pressure.valid)
        {
            var relaxed = snapshot.pressure.pRelaxed > 0f ? snapshot.pressure.pRelaxed : 110f;
            var braced = snapshot.pressure.pBraced > 0f ? snapshot.pressure.pBraced : snapshot.pressure.@base;
            if (braced - relaxed >= 3f)
            {
                var loss = Mathf.Clamp((braced - snapshot.pressure.current) / (braced - relaxed) * 100f, 0f, 100f);
                // Match the Python bracing evaluator: NORMAL below 30%
                // loss, WARNING from 30%, DANGER from 50%.
                if (loss >= 30f) bracingErrorSeconds += dt;
            }
        }
        if (snapshot.spine != null && snapshot.spine.valid)
        {
            // Fusion Monitor is the single authority for Butt Wink.  Do not
            // re-derive an error from the raw relative angle or IMU delta here:
            // those values also change during a normal squat/hip hinge and can
            // penalize a rep that Python reported as NORMAL.
            if (snapshot.spine.buttWink)
                imuErrorSeconds += dt;
        }
    }

    private SafeLiftScoringInput BuildInput(SafeLiftSquatSensorUdpReceiver.Snapshot snapshot, float age, SafeLiftState state, bool fusionActive)
    {
        var input = new SafeLiftScoringInput
        {
            hasPacket = snapshot != null && age <= sensorTimeoutSeconds,
            fsrValid = snapshot != null && snapshot.fsr != null && snapshot.fsr.valid,
            pressureValid = snapshot != null && snapshot.pressure != null && snapshot.pressure.valid,
            spineValid = snapshot != null && snapshot.spine != null && snapshot.spine.valid
        };

        if (snapshot != null && snapshot.fsr != null)
        {
            var fsr = snapshot.fsr;
            input.leftTotal = fsr.leftTotal > 0f ? fsr.leftTotal : Mathf.Max(0f, fsr.leftPercent);
            input.rightTotal = fsr.rightTotal > 0f ? fsr.rightTotal : Mathf.Max(0f, fsr.rightPercent);
            input.leftHeelRatio = fsr.leftHeelRatio;
            input.rightHeelRatio = fsr.rightHeelRatio;
        }

        if (snapshot != null && snapshot.pressure != null)
        {
            var pressure = snapshot.pressure;
            input.pressureCurrent = pressure.current;
            input.pRelaxed = pressure.pRelaxed > 0f ? pressure.pRelaxed : 110f;
            input.pBraced = pressure.pBraced > 0f ? pressure.pBraced : pressure.@base;
        }

        if (snapshot != null && snapshot.spine != null)
        {
            var spine = snapshot.spine;
            input.relativeAngle = spine.relativeAngle != 0f ? spine.relativeAngle : spine.deltaTheta;
            input.imuPelvisDelta = spine.imuPelvisDelta;
            input.spineDurationSeconds = spine.buttWinkDurationMs > 0f
                ? spine.buttWinkDurationMs / 1000f
                : spine.durationSeconds;
            input.buttWinkActive = spine.buttWink;
            input.standingPelvicTuck = spine.standingPelvicTuck;
            input.recoveredDurationSeconds = spine.recoveredDurationSeconds;
            input.kneeAngle = spine.kneeAngle > 0f ? spine.kneeAngle : (state != null && state.metrics != null ? state.metrics.avg_knee_angle : 0f);
            // Fusion Monitor's squatPhase is the sole authority for whether
            // this sample belongs to the scored movement interval.
            input.isSquatting = fusionActive;
            input.depthReached = spine.depthReached;
        }
        else if (state != null && state.metrics != null)
        {
            input.kneeAngle = state.metrics.avg_knee_angle;
            // Backend metrics may still be used as evidence, but never as the
            // scoring-phase authority. Fusion Monitor controls isSquatting.
            input.isSquatting = false;
        }
        return input;
    }

    private static bool IsSquatPhase(string phase)
    {
        if (string.IsNullOrEmpty(phase)) return false;
        var value = phase.ToLowerInvariant();
        return value.Contains("desc") || value.Contains("bottom") || value.Contains("asc") || value.Contains("squat");
    }

    private static void Log(SafeLiftScoreResult score, SafeLiftSquatSensorUdpReceiver.Snapshot snapshot, float age)
    {
        var source = snapshot != null && !string.IsNullOrEmpty(snapshot.lastSource) ? snapshot.lastSource : "none";
        var fsr = score.fsr;
        var bracing = score.bracing;
        var spine = score.spine;
        Debug.Log(string.Format(
            "SafeLift Unity score: source={0}, phase={1}, packetAge={2:0.00}s, TIME={3:0.00}s, errors balance={4:0.00}s heel={5:0.00}s bracing={6:0.00}s imu={7:0.00}s, FSR={8:0.0}/35 ({9}, imbalance={10:0.0}%), bracing={11:0.0}/35 ({12}, loss={13:0.0}%), spine={14:0.0}/30 ({15}), TOTAL={16:0.0}/100 grade={17}",
            source,
            snapshot != null ? snapshot.squatPhase : "-",
            age,
            score.activeMovementSeconds,
            fsr.balanceDurationSeconds,
            fsr.heelDurationSeconds,
            bracing.durationSeconds,
            spine.durationSeconds,
            fsr.score, fsr.balanceStatus, fsr.imbalancePercent,
            bracing.score, bracing.status, bracing.bracingLoss,
            spine.score, spine.status,
            score.totalScore, score.grade));
    }
}
