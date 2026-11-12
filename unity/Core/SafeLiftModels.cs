using System;

[Serializable]
public class SafeLiftState
{
    public double timestamp;
    public string phase;
    public string live_tracking_quality;
    public string last_rep_quality;
    public string sensor_mode;
    public string sensor_status;
    public bool sensor_ready;
    public string exception_code;
    public string exception_level;
    public string exception_message;
    public string current_cue;
    public SafeLiftLiveFeedback live_feedback;
    public string live_feedback_issue;
    public string live_feedback_body_area;
    public string live_feedback_phase;
    public string live_feedback_severity;
    public string live_feedback_haptic_channel;
    public string live_feedback_reason;
    public string[] detected_errors;
    public string main_issue;
    public SafeLiftMetrics metrics;
    public SafeLiftSensors sensors;
    public SafeLiftReportSource report_source;
    public int rep_count;
    public int set_index;
    public int set_rep_count;
    public bool rep_completed;
    public string calibration_status;
    public float calibration_progress;
    public string calibration_message;
    public SafeLiftCalibrationBaseline calibration_baseline;
    public string report_status;
    public float report_elapsed_seconds;
    public float report_timeout_seconds;
    public bool report_timeout;
    public string report_message;
    public bool report_fallback;
    public SafeLiftSetComparison set_comparison;
    public SafeLiftScoreSummary score_summary;

    public static SafeLiftState Mock()
    {
        return new SafeLiftState
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
            phase = "Standing",
            live_tracking_quality = "GOOD",
            last_rep_quality = "NONE",
            sensor_mode = "MOCK",
            sensor_status = "MOCK SENSOR SIMULATOR READY",
            sensor_ready = true,
            exception_code = "None",
            exception_level = "ok",
            exception_message = "",
            current_cue = "Keep your chest tall and push through the whole foot.",
            live_feedback = new SafeLiftLiveFeedback { issue = "None", body_area = "none", phase = "Standing", severity = "none", haptic_channel = "none", display_priority = 0, cue = "", reason = "mock" },
            live_feedback_issue = "None",
            live_feedback_body_area = "none",
            live_feedback_phase = "Standing",
            live_feedback_severity = "none",
            live_feedback_haptic_channel = "none",
            live_feedback_reason = "mock",
            detected_errors = new string[0],
            main_issue = "None",
            metrics = new SafeLiftMetrics { avg_knee_angle = 168f, torso_angle = 4f, knee_valgus_score = 0.02f },
            sensors = new SafeLiftSensors { left_pressure_ratio = .5f, right_pressure_ratio = .5f, imu_pitch = 1f, imu_roll = 0f, core_pressure_ratio = .6f },
            report_source = new SafeLiftReportSource { rep_id = 0, source = "mock", reporter = "mock", last_rep_quality = "NONE", main_issue = "None" },
            calibration_status = "ready",
            calibration_progress = 1f,
            calibration_message = "Mock calibration ready.",
            calibration_baseline = new SafeLiftCalibrationBaseline { sensor_mode = "mock", sample_count = 1, pose_confidence_mean = .92f },
            report_status = "none",
            report_elapsed_seconds = 0f,
            report_timeout_seconds = 10f,
            report_timeout = false,
            report_message = "Report has not started.",
            report_fallback = false,
            set_comparison = SafeLiftSetComparison.Mock(),
            score_summary = SafeLiftScoreSummary.Mock()
        };
    }
}

[Serializable] public class SafeLiftLiveFeedback { public string issue; public string body_area; public string phase; public string severity; public string haptic_channel; public int display_priority; public string cue; public string reason; }
[Serializable] public class SafeLiftMetrics { public float avg_knee_angle; public float torso_angle; public float knee_valgus_score; }
[Serializable] public class SafeLiftSensors { public float left_pressure_ratio; public float right_pressure_ratio; public float imu_pitch; public float imu_roll; public float core_pressure_ratio; }
[Serializable] public class SafeLiftCalibrationStat { public float mean; public float min; public float max; public int samples; }
[Serializable]
public class SafeLiftCalibrationBaseline
{
    public string sensor_mode;
    public int sample_count;
    public float pose_confidence_mean;
    public SafeLiftCalibrationStat left_pressure_ratio;
    public SafeLiftCalibrationStat right_pressure_ratio;
    public SafeLiftCalibrationStat core_pressure_ratio;
    public SafeLiftCalibrationStat imu_pitch;
    public SafeLiftCalibrationStat imu_roll;
}
[Serializable] public class SafeLiftReportSource { public int rep_id; public string source; public string reporter; public string last_rep_quality; public string main_issue; }
[Serializable]
public class SafeLiftScoreSummary
{
    public int safe_lift_score;
    public int knee_alignment_score;
    public int balance_score;
    public int torso_stability_score;
    public int spine_alignment_score;
    public int core_bracing_score;
    public int improvement_delta;
    public string basis;
    public string summary;
    public string grade;
    public string grade_label;
    public string grade_message;
    public bool score_evidence_complete;
    public string score_confidence;
    public string[] missing_score_evidence;

    public static SafeLiftScoreSummary Mock()
    {
        return new SafeLiftScoreSummary { safe_lift_score = 86, knee_alignment_score = 84, balance_score = 88, torso_stability_score = 87, core_bracing_score = 82, improvement_delta = 8, basis = "safelift_rubric_score_v1", summary = "Mock SafeLift rubric score payload.", score_evidence_complete = true, score_confidence = "complete", missing_score_evidence = new string[0] };
    }
}
[Serializable] public class SafeLiftSetMetrics { public float torso_angle_peak_abs; public float knee_valgus_peak; public int feedback_count; }
[Serializable] public class SafeLiftSetSummary { public string label; public int frame_count; public int error_count; public string[] detected_errors; public SafeLiftSetMetrics metrics; }
[Serializable]
public class SafeLiftSetComparison
{
    public SafeLiftSetSummary set1;
    public SafeLiftSetSummary set2;
    public int error_count_delta;
    public int feedback_count_delta;
    public float torso_angle_peak_abs_delta;
    public float knee_valgus_peak_delta;
    public string summary;
    public string grade;
    public string grade_label;
    public string grade_message;

    public static SafeLiftSetComparison Mock()
    {
        return new SafeLiftSetComparison
        {
            set1 = new SafeLiftSetSummary { label = "SET 1", frame_count = 60, error_count = 4, detected_errors = new[] { "KneeValgus" }, metrics = new SafeLiftSetMetrics { feedback_count = 0, torso_angle_peak_abs = 10f, knee_valgus_peak = .18f } },
            set2 = new SafeLiftSetSummary { label = "SET 2", frame_count = 60, error_count = 2, detected_errors = new[] { "KneeValgus" }, metrics = new SafeLiftSetMetrics { feedback_count = 2, torso_angle_peak_abs = 7.5f, knee_valgus_peak = .10f } },
            error_count_delta = -2,
            feedback_count_delta = 2,
            torso_angle_peak_abs_delta = -2.5f,
            knee_valgus_peak_delta = -.08f,
            summary = "SET 2 detected fewer rule-based issues than SET 1."
        };
    }
}
[Serializable] public class SafeLiftHealthMode { public string sensor_mode; public string sensor_status; public bool sensor_ready; public bool real_mode_ready; public bool demo_mode_available; }
[Serializable] public class SafeLiftHealthCalibration { public string status; public float progress; public bool ready; public int sample_count; public SafeLiftCalibrationBaseline baseline; }
[Serializable] public class SafeLiftHealthLaunchStep { public string step; public bool ok; public string action; }
[Serializable] public class SafeLiftHealthIntegrationCheck { public string name; public bool ok; public bool out_of_scope; public string detail; }
[Serializable]
public class SafeLiftHealth
{
    public bool ok;
    public bool ready_for_real_mode_start;
    public bool ready_for_full_review;
    public bool sensor_raw_acquisition_in_scope;
    public bool ready_without_raw_sensor;
    public string[] real_mode_blockers;
    public SafeLiftHealthLaunchStep[] launch_steps;
    public SafeLiftHealthIntegrationCheck[] integration_checks;
    public SafeLiftHealthMode mode;
    public SafeLiftHealthCalibration calibration;
}

[Serializable]
public class SafeLiftReport
{
    public int rep_id;
    public string status;
    public string report_text;
    public float elapsed_seconds;
    public float timeout_seconds;
    public bool timed_out;
    public bool fallback;
    public string message;
    public string source;
}
