# Backend API

Default development address: `http://127.0.0.1:8090`

For standalone Quest, localhost points to the headset. Configure the Unity client with the development PC's IPv4 address, for example `http://PC_IP:8090`.

## Unity-facing endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | `/api/session_state` | Pose phase, rep/set state, tracking, cue, sensor and calibration state |
| GET | `/api/state` | Compatibility alias for session state |
| GET | `/api/latest_report` | Report generation state and report text |
| GET | `/api/health` | Machine-readable readiness and integration checks |
| GET | `/api/live_frame.jpg` | Annotated camera frame |
| GET | `/api/phase_capture/{phase}.jpg` | Standing, descent, bottom or ascent evidence image |
| GET | `/api/qr_link` | QR/report URL metadata |
| POST | `/api/sensor_frame` | Push an already-normalized sensor frame |
| POST | `/api/unity_rep_score` | Send Unity/Fusion rep score evidence |

## Browser endpoints

| Endpoint | Purpose |
|---|---|
| `/health` | Human-readable preflight and readiness page |
| `/qr` | Laptop QR handoff page |
| `/report/latest` | Latest report |
| `/report/{report_id}` | Archived fixed report URL when archive data exists |

## Report state

`/api/latest_report` uses the following status values:

- `none`
- `generating`
- `timeout`
- `ready`

The payload also exposes elapsed time, timeout state, fallback status and a user-facing message.

## Security note

The included development server is not presented as an authenticated production API. Do not expose it directly to the public internet. Use a trusted local network and remove personal data from report payloads and images.
