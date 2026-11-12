# System Architecture

## Runtime components

| Component | Responsibility |
|---|---|
| Arduino sketch | Read six FSR channels, BNO085 acceleration/gyro/quaternion and pressure sensor data |
| Sensor providers | Convert serial, JSONL or HTTP payloads into a validated `SensorFrame` |
| MediaPipe tracker | Convert camera frames into pose landmarks |
| Analysis pipeline | Compute geometry, phase, tracking quality, errors and rep state |
| Report pipeline | Build phase evidence and generate Gemini or template report text |
| Browser dashboard | Serve Unity state, health, report, phase images and QR handoff pages |
| Unity backend bridge | Poll backend state and report endpoints |
| Unity sensor receiver | Receive optional fusion JSON over UDP 24018 |
| Unity state machine | Coordinate Tutorial, SET 1, Rest, SET 2, Report and Review screens |
| Avatar controllers | Display live pose and replay phase snapshots |
| XR input/haptics | Handle controller/hand UI input and feedback pulses |

## Transport map

| Port / protocol | Producer | Consumer | Payload |
|---|---|---|---|
| Serial 115200 | Arduino | Python serial provider | `SLXR` CSV line |
| UDP 24018 | Optional sensor fusion process | Unity sensor receiver | Fusion JSON |
| UDP 5054 | MediaPipe pose sender | Unity avatar receiver | Pose landmark JSON with message delimiter |
| HTTP 8090 | Python browser dashboard | Unity, laptop and phone browser | Session state, reports, images and health |

## Decision ownership

- Python owns pose geometry, tracking quality, squat phase, rep counting and rule-based feedback selection.
- Unity owns XR screen flow, interaction, haptics, visualization and one implementation of the 100-point score.
- The browser dashboard owns HTTP delivery, report pages, health checks and QR handoff metadata.
- Gemini does not participate in real-time posture decisions.
