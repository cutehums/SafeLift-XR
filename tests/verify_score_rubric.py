from __future__ import annotations

import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
BACKEND = ROOT / "backend"
sys.path.insert(0, str(BACKEND))

from analysis.score_rubric import (  # noqa: E402
    build_rubric_score_summary,
    core_bracing_score,
    fsr_balance_score,
    grade_for,
    spine_alignment_score,
)


CHECKS: list[tuple[str, bool, str]] = []


def approx(actual: float, expected: float, tolerance: float = 0.05) -> bool:
    return abs(float(actual) - float(expected)) <= tolerance


def check(name: str, ok: bool, detail: str = "") -> None:
    CHECKS.append((name, ok, detail))


def main() -> int:
    core_full = core_bracing_score(1.0, 0.90)
    check(
        "Core bracing full score at 10% pressure drop",
        approx(core_full["drop_ratio_percent"], 10.0) and approx(core_full["score"], 35.0),
        str(core_full),
    )

    core_penalty = core_bracing_score(1.0, 0.80)
    check(
        "Core bracing subtracts 1.16 per excess 1%",
        approx(core_penalty["drop_ratio_percent"], 20.0) and approx(core_penalty["score"], 23.4),
        str(core_penalty),
    )

    core_clamp = core_bracing_score(1.0, 0.50)
    check("Core bracing clamps to 0 minimum", approx(core_clamp["score"], 0.0), str(core_clamp))

    fsr_type_a_full = fsr_balance_score(0.55, 0.45, heel_maintained=True, scoring_type="A")
    check(
        "FSR Type A gives 35 at 10% left/right imbalance with heel maintained",
        approx(fsr_type_a_full["imbalance_percent"], 10.0) and approx(fsr_type_a_full["score"], 35.0),
        str(fsr_type_a_full),
    )

    fsr_type_a_penalty = fsr_balance_score(0.65, 0.35, heel_maintained=True, scoring_type="A")
    check(
        "FSR Type A subtracts 0.6 per excess 1% imbalance",
        approx(fsr_type_a_penalty["imbalance_percent"], 30.0)
        and approx(fsr_type_a_penalty["imbalance_score"], 11.0)
        and approx(fsr_type_a_penalty["heel_maintain_score"], 12.0)
        and approx(fsr_type_a_penalty["score"], 23.0),
        str(fsr_type_a_penalty),
    )

    fsr_type_a_heel_fail = fsr_balance_score(0.55, 0.45, heel_maintained=False, scoring_type="A")
    check(
        "FSR Type A heel pressure failure removes the 12 heel-maintain points",
        approx(fsr_type_a_heel_fail["imbalance_score"], 23.0)
        and approx(fsr_type_a_heel_fail["heel_maintain_score"], 0.0)
        and approx(fsr_type_a_heel_fail["score"], 23.0),
        str(fsr_type_a_heel_fail),
    )

    fsr_type_b_full = fsr_balance_score(0.5, 0.5, scoring_type="B", type_b_forefoot_shift_percent=100)
    check("FSR Type B gives full forefoot stability at 100%", approx(fsr_type_b_full["score"], 35.0), str(fsr_type_b_full))

    fsr_type_b_penalty = fsr_balance_score(0.5, 0.5, scoring_type="B", type_b_forefoot_shift_percent=130)
    check(
        "FSR Type B subtracts 1.2 per excess 10% forefoot shift",
        approx(fsr_type_b_penalty["forefoot_stability_score"], 8.4) and approx(fsr_type_b_penalty["score"], 31.4),
        str(fsr_type_b_penalty),
    )

    spine_full = spine_alignment_score(10, 5, 0)
    check(
        "Spine alignment full score at 5 degree delta",
        approx(spine_full["delta_theta_degrees"], 5.0) and approx(spine_full["score"], 30.0),
        str(spine_full),
    )

    spine_penalty = spine_alignment_score(12, 5, 0)
    check(
        "Spine alignment subtracts 2 points per excess degree",
        approx(spine_penalty["delta_theta_degrees"], 7.0) and approx(spine_penalty["score"], 26.0),
        str(spine_penalty),
    )

    spine_clamp = spine_alignment_score(25, 0, 0)
    check("Spine alignment clamps to 0 minimum", approx(spine_clamp["score"], 0.0), str(spine_clamp))

    check("Grade S starts at 90", grade_for(95)["grade"] == "S")
    check("Grade A starts at 80", grade_for(85)["grade"] == "A")
    check("Grade B starts at 65", grade_for(70)["grade"] == "B")
    check("Grade C is below 65", grade_for(60)["grade"] == "C")

    missing_core = core_bracing_score(None, None)
    missing_fsr = fsr_balance_score(None, 0.5, heel_maintained=None, scoring_type="A")
    missing_spine = spine_alignment_score(None, None, 0)
    check(
        "Rubric marks incomplete core evidence instead of silently treating it as final",
        missing_core["evidence_complete"] is False and "core_pressure_baseline" in missing_core["missing_evidence"],
        str(missing_core),
    )
    check(
        "Rubric marks incomplete FSR evidence instead of silently treating it as final",
        missing_fsr["evidence_complete"] is False and "left_pressure_ratio" in missing_fsr["missing_evidence"],
        str(missing_fsr),
    )
    check(
        "Rubric marks incomplete spine evidence instead of silently treating it as final",
        missing_spine["evidence_complete"] is False and "mediapipe_torso_angle" in missing_spine["missing_evidence"],
        str(missing_spine),
    )

    summary = build_rubric_score_summary(
        {
            "metrics": {
                "core_pressure_min_ratio": 0.80,
                "max_torso_angle": 12,
                "imu_pitch_peak": 5,
                "heel_pressure_drop": False,
            },
            "calibration_baseline": {"core_pressure_ratio": {"mean": 1.0}},
            "sensor_summary": {
                "left_pressure_ratio": {"mean": 0.65},
                "right_pressure_ratio": {"mean": 0.35},
            },
        },
        {"error_count_delta": -2},
    )
    expected_total = 23.4 + 23.0 + 26.0
    check("Rubric summary uses safelift_rubric_score_v1 basis", summary["basis"] == "safelift_rubric_score_v1", str(summary))
    check("Rubric summary keeps 100 max points", summary["max_points"] == 100, str(summary))
    check("Rubric summary marks complete score evidence", summary["score_evidence_complete"] is True and summary["score_confidence"] == "complete", str(summary))
    check(
        "Rubric summary includes all three scoring components",
        {"core_bracing", "ground_balance_fsr", "spine_alignment"}.issubset(summary["rubric"].keys()),
        str(summary["rubric"]),
    )
    check("Rubric total equals 35+35+30 component sum", approx(summary["safe_lift_score_raw"], expected_total), str(summary))

    summary_b = build_rubric_score_summary(
        {
            "metrics": {
                "core_pressure_min_ratio": 0.90,
                "max_torso_angle": 10,
                "imu_pitch_peak": 5,
                "fsr_scoring_type": "B",
                "forefoot_shift_peak_percent": 130,
            },
            "calibration_baseline": {"core_pressure_ratio": {"mean": 1.0}},
            "sensor_summary": {
                "left_pressure_ratio": {"mean": 0.5},
                "right_pressure_ratio": {"mean": 0.5},
            },
        },
        {},
    )
    check(
        "Rubric summary applies FSR Type B forefoot stability when selected",
        summary_b["rubric"]["ground_balance_fsr"]["scoring_type"] == "B"
        and approx(summary_b["rubric"]["ground_balance_fsr"]["forefoot_stability_score"], 8.4)
        and approx(summary_b["safe_lift_score_raw"], 35.0 + 31.4 + 30.0),
        str(summary_b),
    )

    failed = [(name, detail) for name, ok, detail in CHECKS if not ok]
    for name, ok, detail in CHECKS:
        prefix = "PASS" if ok else "FAIL"
        suffix = f" :: {detail}" if detail else ""
        print(f"[{prefix}] {name}{suffix}")

    print()
    print(f"SafeLift scoring rubric verification: {len(CHECKS) - len(failed)}/{len(CHECKS)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
