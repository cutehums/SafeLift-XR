from __future__ import annotations

import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
BACKEND = ROOT / "backend"
sys.path.insert(0, str(BACKEND))

from sensors.http_sensor_provider import HttpSensorProvider  # noqa: E402
from sensors.jsonl_sensor_provider import JsonlSensorProvider  # noqa: E402
from sensors.sensor_frame import SensorFrame  # noqa: E402
from ui.dashboard import BrowserDashboard  # noqa: E402


def check(name: str, ok: bool) -> tuple[str, bool]:
    print(f"[{'PASS' if ok else 'FAIL'}] {name}")
    return name, ok


def main() -> int:
    valid = SensorFrame.from_payload({
        "timestamp": 123.0,
        "left_pressure_ratio": 0.52,
        "right_pressure_ratio": 0.48,
        "core_pressure_ratio": 0.71,
        "imu_pitch": 8.5,
        "imu_roll": 2.1,
        "imu_instability": 0.12,
        "heel_pressure_drop": "false",
        "forefoot_bias": 0,
        "forefoot_shift_percent": 130,
        "fsr_scoring_type": "B",
    }, source="serial")
    invalid = SensorFrame.from_payload({
        "left_pressure_ratio": 1.5,
        "right_pressure_ratio": "bad",
        "core_pressure_ratio": -0.2,
        "imu_pitch": 400,
        "heel_pressure_drop": "maybe",
        "forefoot_bias": "sometimes",
        "forefoot_shift_percent": -1,
        "fsr_scoring_type": "C",
    }, source="serial")

    sample_path = ROOT / "tests" / "fixtures" / "sample_sensor_frames.jsonl"
    jsonl = JsonlSensorProvider(str(sample_path))
    jsonl_first = jsonl.read()
    for _ in range(10):
        jsonl_looped = jsonl.read()

    http_provider = HttpSensorProvider(stale_after_seconds=60)
    http_frame = http_provider.push({
        "left_pressure_ratio": 0.51,
        "right_pressure_ratio": 0.49,
        "core_pressure_ratio": 0.88,
        "imu_pitch": 5.0,
        "imu_roll": 1.0,
    })
    http_read = http_provider.read()

    dashboard = BrowserDashboard()
    dashboard.update_state({
        "timestamp": 1,
        "phase": "Standing",
        "sensor": {
            "source": invalid.source,
            "contract_ok": invalid.contract_ok,
            "contract_errors": list(invalid.contract_errors),
        },
        "calibration_status": "ready",
        "calibration_progress": 1.0,
        "live_tracking_quality": "GOOD",
    })
    unity_state = dashboard.get_unity_state()
    health = dashboard.get_health("127.0.0.1:8090")
    mode = health.get("mode") or {}

    checks = [
        check("valid provider payload is accepted", valid.contract_ok and valid.source == "serial"),
        check("valid provider booleans are coerced", valid.heel_pressure_drop is False and valid.forefoot_bias is False),
        check("valid provider carries optional FSR Type B score fields", valid.fsr_scoring_type == "B" and valid.forefoot_shift_percent == 130.0),
        check("JSONL provider reads normalized frames", jsonl_first.contract_ok and jsonl_first.source == "jsonl" and jsonl_first.left_pressure_ratio == 0.52),
        check("JSONL provider loops by default for stable Real Mode replay", jsonl_looped.contract_ok and jsonl_looped.source == "jsonl"),
        check("HTTP provider accepts pushed normalized frames", http_frame.contract_ok and http_read.source == "http" and http_read.left_pressure_ratio == 0.51),
        check("invalid provider payload is rejected", not invalid.contract_ok and invalid.source == "serial-contract-invalid"),
        check("invalid payload names concrete contract errors", len(invalid.contract_errors) >= 4 and any("0.0 to 1.0" in item for item in invalid.contract_errors)),
        check("Unity state marks invalid sensor not ready", unity_state.get("sensor_ready") is False and unity_state.get("exception_code") == "SensorDisconnected"),
        check("Unity state exposes sensor contract errors", (unity_state.get("sensors") or {}).get("contract_ok") is False and bool((unity_state.get("sensors") or {}).get("contract_errors"))),
        check(
            "health allows Tutorial launch but blocks Real Mode SET 1 for invalid sensors",
            mode.get("sensor_ready") is False
            and health.get("unity_launch_ready") is True
            and health.get("ready_for_real_mode_start") is False,
        ),
        check("health integration contract reports invalid sensor reason", any(item.get("name") == "Sensor provider contract" and item.get("ok") is False and "SENSOR CONTRACT INVALID" in str(item.get("detail")) for item in health.get("integration_checks") or [])),
    ]
    failed = [name for name, ok in checks if not ok]
    print()
    print(f"SafeLift sensor contract verification: {len(checks) - len(failed)}/{len(checks)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
