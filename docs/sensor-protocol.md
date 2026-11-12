# Sensor Protocol

## Arduino acquisition format

The included sketch emits a line at approximately 20Hz:

```text
SLXR,millis,fsr1,fsr2,fsr3,fsr4,fsr5,fsr6,
accX,accY,accZ,gyroX,gyroY,gyroZ,
quatW,quatX,quatY,quatZ,pressure_kPa
```

Required Arduino libraries and hardware:

- `Wire` and `SPI`
- `Adafruit_BNO08x`
- `SparkFun_MicroPressure`
- board with six analog FSR inputs
- BNO085 connected over SPI
- SEN-16476 pressure sensor connected to `Wire1` at address `0x18`

Pin assignments in the snapshot:

- BNO085 CS: 10
- BNO085 INT: 9
- BNO085 RESET: 5
- FSR: A0 to A5

These values are hardware-specific and must be verified before upload.

## Normalized backend contract

Provider payload example:

```json
{
  "timestamp": 12345.1,
  "left_pressure_ratio": 0.52,
  "right_pressure_ratio": 0.48,
  "heel_pressure_drop": false,
  "forefoot_bias": false,
  "forefoot_shift_percent": 75.0,
  "fsr_scoring_type": "A",
  "imu_pitch": 8.5,
  "imu_roll": 2.1,
  "imu_instability": 0.12,
  "core_pressure_ratio": 0.71
}
```

Validation rules:

- pressure ratios and `imu_instability`: finite `0.0..1.0`
- `imu_pitch` and `imu_roll`: finite `-180..180`
- heel/forefoot flags: boolean-compatible values
- `forefoot_shift_percent`: non-negative when provided
- `fsr_scoring_type`: `A` or `B` when provided

Invalid payloads are retained as contract failures instead of silently clamped into normal sensor data.

## Provider modes

- Serial: raw `SLXR` stream or supported normalized serial payload
- JSONL: deterministic replay of normalized frames
- HTTP: `POST /api/sensor_frame`
- Mock: development-only generated normalized values

The raw hardware-to-normalized calibration process must be verified for the actual device and user before Real Mode use.
