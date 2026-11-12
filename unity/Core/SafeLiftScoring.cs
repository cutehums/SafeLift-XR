using System;
using UnityEngine;

public enum SafeLiftScoreStatus
{
    NORMAL,
    WARNING,
    DANGER,
    INVALID,
    NO_SIGNAL,
    INVALID_CALIBRATION
}

[Serializable]
public sealed class SafeLiftScoringInput
{
    public float leftTotal;
    public float rightTotal;
    public float leftHeelRatio;
    public float rightHeelRatio;
    public float pRelaxed = 110f;
    public float pBraced;
    public float pressureCurrent;
    public float relativeAngle;
    public float imuPelvisDelta;
    public float kneeAngle;
    public float spineDurationSeconds;
    public float recoveredDurationSeconds;
    public bool isSquatting;
    public bool depthReached;
    public bool buttWinkActive;
    public bool standingPelvicTuck;
    public bool spineValid;
    public bool fsrValid;
    public bool pressureValid;
    public bool hasPacket;
    public float activeMovementSeconds;
    public float balanceErrorSeconds;
    public float heelErrorSeconds;
    public float bracingErrorSeconds;
    public float imuErrorSeconds;
}

[Serializable]
public sealed class SafeLiftFsrScoreResult
{
    public float leftTotal;
    public float rightTotal;
    public float leftPercent;
    public float rightPercent;
    public float imbalancePercent;
    public float leftHeelRatio;
    public float rightHeelRatio;
    public string heelDroppedSide;
    public SafeLiftScoreStatus balanceStatus;
    public SafeLiftScoreStatus leftHeelStatus;
    public SafeLiftScoreStatus rightHeelStatus;
    public SafeLiftScoreStatus overallHeelStatus;
    public float balanceScore;
    public float heelScore;
    public float balanceDurationSeconds;
    public float heelDurationSeconds;
    public float score;
    public float durationSeconds;
}

[Serializable]
public sealed class SafeLiftBracingScoreResult
{
    public float pRelaxed;
    public float pBraced;
    public float current;
    public float bracingLoss;
    public SafeLiftScoreStatus status;
    public bool failed;
    public float score;
    public float durationSeconds;
}

[Serializable]
public sealed class SafeLiftSpineScoreResult
{
    public float relativeAngle;
    public float imuPelvisDelta;
    public bool buttWink;
    public bool standingPelvicTuck;
    public bool depthReached;
    public SafeLiftScoreStatus status;
    public float score;
    public float durationSeconds;
}

[Serializable]
public sealed class SafeLiftScoreResult
{
    public SafeLiftFsrScoreResult fsr;
    public SafeLiftBracingScoreResult bracing;
    public SafeLiftSpineScoreResult spine;
    public float activeMovementSeconds;
    public float totalScore;
    public string grade;
}

/// <summary>
/// Unity-owned scoring rules. Python supplies measurements and validity only;
/// this class owns status thresholds and the 100-point rubric.
/// </summary>
public static class SafeLiftScoring
{
    // The detector's 0.08s confirmation window is not penalized. A continuous
    // 1.0s Butt Wink consumes the full 30-point spine component. Both values
    // are intentionally centralized so the rubric can be tuned later.
    public const float ButtWinkGraceSeconds = 0.08f;
    public const float ButtWinkFullPenaltySeconds = 1.0f;

    public static SafeLiftScoreResult Evaluate(SafeLiftScoringInput input)
    {
        if (input == null) input = new SafeLiftScoringInput();
        var fsr = EvaluateFsr(input);
        var bracing = EvaluateBracing(input);
        var spine = EvaluateSpine(input);
        var total = fsr.score + bracing.score + spine.score;
        return new SafeLiftScoreResult
        {
            fsr = fsr,
            bracing = bracing,
            spine = spine,
            activeMovementSeconds = Mathf.Max(0f, input.activeMovementSeconds),
            totalScore = Mathf.Clamp(total, 0f, 100f),
            grade = Grade(total, 100f)
        };
    }

    public static SafeLiftFsrScoreResult EvaluateFsr(SafeLiftScoringInput input)
    {
        var result = new SafeLiftFsrScoreResult();
        if (input == null || !input.hasPacket || !input.fsrValid)
        {
            result.balanceStatus = SafeLiftScoreStatus.NO_SIGNAL;
            result.leftHeelStatus = SafeLiftScoreStatus.NO_SIGNAL;
            result.rightHeelStatus = SafeLiftScoreStatus.NO_SIGNAL;
            result.overallHeelStatus = SafeLiftScoreStatus.NO_SIGNAL;
            return result;
        }

        result.leftTotal = Mathf.Max(0f, input.leftTotal);
        result.rightTotal = Mathf.Max(0f, input.rightTotal);
        var total = result.leftTotal + result.rightTotal;
        if (total <= 0.0001f)
        {
            result.balanceStatus = SafeLiftScoreStatus.INVALID;
            result.overallHeelStatus = SafeLiftScoreStatus.INVALID;
            return result;
        }

        result.leftPercent = result.leftTotal / total * 100f;
        result.rightPercent = result.rightTotal / total * 100f;
        result.imbalancePercent = Mathf.Abs(result.leftTotal - result.rightTotal) / total * 100f;
        result.balanceStatus = BalanceStatus(result.imbalancePercent);

        result.leftHeelRatio = input.leftHeelRatio;
        result.rightHeelRatio = input.rightHeelRatio;
        result.leftHeelStatus = HeelStatus(result.leftHeelRatio);
        result.rightHeelStatus = HeelStatus(result.rightHeelRatio);
        result.overallHeelStatus = result.leftHeelStatus == SafeLiftScoreStatus.DANGER || result.rightHeelStatus == SafeLiftScoreStatus.DANGER
            ? SafeLiftScoreStatus.DANGER
            : result.leftHeelStatus == SafeLiftScoreStatus.WARNING || result.rightHeelStatus == SafeLiftScoreStatus.WARNING
                ? SafeLiftScoreStatus.WARNING
                : SafeLiftScoreStatus.NORMAL;
        result.heelDroppedSide = result.leftHeelRatio < 20f
            ? (result.rightHeelRatio < 20f ? "BOTH" : "LEFT")
            : (result.rightHeelRatio < 20f ? "RIGHT" : "NONE");

        result.balanceDurationSeconds = Mathf.Max(0f, input.balanceErrorSeconds);
        result.heelDurationSeconds = Mathf.Max(0f, input.heelErrorSeconds);
        var activeSeconds = Mathf.Max(0.001f, input.activeMovementSeconds);
        result.balanceScore = Mathf.Clamp(20f * (1f - result.balanceDurationSeconds / activeSeconds), 0f, 20f);
        result.heelScore = Mathf.Clamp(15f * (1f - result.heelDurationSeconds / activeSeconds), 0f, 15f);
        result.score = result.balanceScore + result.heelScore;
        return result;
    }

    public static SafeLiftBracingScoreResult EvaluateBracing(SafeLiftScoringInput input)
    {
        var result = new SafeLiftBracingScoreResult
        {
            pRelaxed = input != null ? input.pRelaxed : 110f,
            pBraced = input != null ? input.pBraced : 0f,
            current = input != null ? input.pressureCurrent : 0f,
            score = 35f
        };
        if (input == null || !input.hasPacket || !input.pressureValid)
        {
            result.status = SafeLiftScoreStatus.NO_SIGNAL;
            result.score = 0f;
            return result;
        }
        // Standing must not add new bracing error time, but a completed rep
        // is captured after the phase returns to standing. Keep using the
        // accumulated movement/error durations so that the completed rep is
        // not reset to a false 35/35 at capture time.
        if (!input.isSquatting)
        {
            result.status = SafeLiftScoreStatus.NORMAL;
            result.bracingLoss = 0f;
            result.failed = false;
            result.durationSeconds = Mathf.Max(0f, input.bracingErrorSeconds);
            result.score = Mathf.Clamp(35f * (1f - result.durationSeconds / Mathf.Max(0.001f, input.activeMovementSeconds)), 0f, 35f);
            return result;
        }
        if (result.pBraced - result.pRelaxed < 3f)
        {
            result.status = SafeLiftScoreStatus.INVALID_CALIBRATION;
            result.score = 0f;
            return result;
        }

        result.bracingLoss = Mathf.Clamp((result.pBraced - result.current) / (result.pBraced - result.pRelaxed) * 100f, 0f, 100f);
        result.failed = result.bracingLoss >= 50f;
        // Match the Python bracing evaluator's status boundary.
        result.status = result.failed
            ? SafeLiftScoreStatus.DANGER
            : result.bracingLoss >= 30f ? SafeLiftScoreStatus.WARNING : SafeLiftScoreStatus.NORMAL;
        result.durationSeconds = Mathf.Max(0f, input.bracingErrorSeconds);
        result.score = Mathf.Clamp(35f * (1f - result.durationSeconds / Mathf.Max(0.001f, input.activeMovementSeconds)), 0f, 35f);
        return result;
    }

    public static SafeLiftSpineScoreResult EvaluateSpine(SafeLiftScoringInput input)
    {
        var result = new SafeLiftSpineScoreResult
        {
            relativeAngle = input != null ? input.relativeAngle : 0f,
            imuPelvisDelta = input != null ? input.imuPelvisDelta : 0f,
            depthReached = input != null && input.depthReached,
            score = 30f
        };
        if (input == null || !input.hasPacket || !input.spineValid)
        {
            result.status = SafeLiftScoreStatus.NO_SIGNAL;
            result.score = 0f;
            return result;
        }

        result.standingPelvicTuck = !input.isSquatting && input.kneeAngle >= 160f
            && result.relativeAngle >= 3.5f && result.imuPelvisDelta >= 1.5f
            && input.spineDurationSeconds >= 0.08f;
        // Fusion Monitor already fuses IMU + MediaPipe and owns the Butt Wink
        // state. Unity must not apply the old raw-angle fallback, because the
        // current Python signal is pelvis-to-thigh relative rotation and can
        // be large during a normal hip hinge.
        result.buttWink = input.buttWinkActive;
        // Standing Pelvic Tuck remains available as diagnostic evidence, but is
        // intentionally excluded from the current score until its duration is
        // available from the detector.
        result.standingPelvicTuck = input.standingPelvicTuck || result.standingPelvicTuck;

        result.status = result.buttWink
            ? SafeLiftScoreStatus.DANGER
            : SafeLiftScoreStatus.NORMAL;
        result.durationSeconds = Mathf.Max(0f, input.imuErrorSeconds);
        result.score = Mathf.Clamp(30f * (1f - result.durationSeconds / Mathf.Max(0.001f, input.activeMovementSeconds)), 0f, 30f);
        return result;
    }

    private static SafeLiftScoreStatus BalanceStatus(float value) => value > 20f ? SafeLiftScoreStatus.DANGER : value >= 15f ? SafeLiftScoreStatus.WARNING : SafeLiftScoreStatus.NORMAL;
    private static SafeLiftScoreStatus HeelStatus(float value) => value < 20f ? SafeLiftScoreStatus.DANGER : value <= 25f ? SafeLiftScoreStatus.WARNING : SafeLiftScoreStatus.NORMAL;
    private static string Grade(float score, float max) => score >= max * .9f ? "S" : score >= max * .75f ? "A" : score >= max * .6f ? "B" : "C";
}
