using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Receives the normalized feature packets emitted by the updated
/// SquatDetection_Package. The package bridge owns COM7; Unity listens only to
/// the detector output on UDP 24018.
/// </summary>
public sealed class SafeLiftSquatSensorUdpReceiver : MonoBehaviour
{
    [Serializable] public sealed class Packet
    {
        public string schema;
        public string packetType;
        public string eventType;
        public string eventState;
        public int eventDurationMs;
        public bool scoreEligible;
        public string source;
        public long timestampMs;
    public string squatPhase;
    public int repId;
    public int repCount;
        public FsrPayload fsr;
        public PressurePayload pressure;
        public SpinePayload spine;
        public ImuPayload imu;
        public CalibrationPayload calibration;
    }

    [Serializable] public sealed class FsrPayload
    {
        public float leftTotal;
        public float rightTotal;
        public float leftPercent;
        public float rightPercent;
        public float imbalance;
        public float heelRatio;
        public float leftHeelRatio;
        public float rightHeelRatio;
        public string heelDroppedSide;
        // Visualization-only individual FSR channels. Aggregate fields above
        // remain the source for the existing scoring logic.
        public float leftFront1;
        public float leftFront2;
        public float leftHeel;
        public float rightFront1;
        public float rightFront2;
        public float rightHeel;
        public float leftFront1Ratio;
        public float leftFront2Ratio;
        public float leftHeelSensorRatio;
        public float rightFront1Ratio;
        public float rightFront2Ratio;
        public float rightHeelSensorRatio;
        // Original channel order: L heel, L toe1, L toe2, R toe1, R heel, R toe2.
        // Keep this for compatibility with packets emitted before the named
        // visualization fields were added.
        public float[] channels;
        public bool valid;
    }

    [Serializable] public sealed class PressurePayload
    {
        public float current;
        public float @base;
        public float pRelaxed;
        public float pBraced;
        public float dropRatio;
        public float bracingLoss;
        public string status;
        public bool failed;
        public bool valid;
    }

    [Serializable] public sealed class SpinePayload
    {
        public float deltaTheta;
        public float relativeAngle;
        public float imuPelvisDelta;
        public float buttWinkDurationMs;
        public bool standingPelvicTuck;
        public bool depthReached;
        public float durationSeconds;
        public float recoveredDurationSeconds;
        public float kneeAngle;
        public bool isSquatting;
        public string status;
        public bool buttWink;
        public float confidence;
        public bool valid;
    }

    [Serializable] public sealed class ImuPayload
    {
        public float pitchDeg;
        public float rollDeg;
        public bool valid;
    }

    [Serializable] public sealed class CalibrationPayload
    {
        public bool ready;
        public int sampleCount;
    }

    [Serializable] public sealed class Snapshot
    {
        public long timestampMs;
    public string squatPhase;
    public int repId;
    public int repCount;
        public string packetType;
        public string eventType;
        public string eventState;
        public int eventDurationMs;
        public bool scoreEligible;
        public FsrPayload fsr;
        public PressurePayload pressure;
        public SpinePayload spine;
        public ImuPayload imu;
        public CalibrationPayload calibration;
        public int packetCount;
        public string lastSource;
    }

    public int port = 24018;
    [Min(0.05f)] public float consoleLogInterval = 0.5f;
    public bool logPackets;
    [Header("Backend sensor forwarding")]
    public bool forwardToBackend;
    public string backendSensorUrl = "http://127.0.0.1:8090";
    [Min(0.05f)] public float backendForwardInterval = 0.1f;

    public Snapshot Latest { get; private set; }
    public float LastPacketAgeSeconds => Latest == null ? float.PositiveInfinity : Mathf.Max(0f, Time.unscaledTime - lastPacketRealtime);
    public float LastFusionPhaseAgeSeconds => string.IsNullOrEmpty(latestSquatPhase)
        ? float.PositiveInfinity
        : Mathf.Max(0f, Time.unscaledTime - lastFusionPhaseRealtime);
    public event Action<Snapshot> SensorUpdated;

    UdpClient client;
    readonly ConcurrentQueue<string> pendingPackets = new ConcurrentQueue<string>();
    float nextLogTime;
    int packetCount;
    int invalidPacketCount;
    FsrPayload latestFsr;
    PressurePayload latestPressure;
    SpinePayload latestSpine;
    ImuPayload latestImu;
    CalibrationPayload latestCalibration;
    int latestFusionRepCount;
    long latestTimestamp;
    string latestSquatPhase;
    string latestSource;
    float lastPacketRealtime = float.NegativeInfinity;
    float lastFusionPhaseRealtime = float.NegativeInfinity;
    float nextBackendForwardTime;
    string pendingBackendPayload;
    bool backendForwardInFlight;

    public void ConfigureBackendForwarding(string serverUrl, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(serverUrl))
            backendSensorUrl = serverUrl.TrimEnd('/');
        forwardToBackend = enabled;
    }

    void OnEnable()
    {
        try
        {
            client = new UdpClient(port);
            client.BeginReceive(Receive, null);
            Debug.Log("SafeLift squat sensor receiver listening on UDP " + port + ". Start the updated SquatDetection package to stream FSR/pressure values.");
        }
        catch (Exception e)
        {
            Debug.LogError("SafeLift squat sensor receiver could not bind UDP " + port + ": " + e.Message);
        }
    }

    void Update()
    {
        while (pendingPackets.TryDequeue(out var json))
            ApplyPacket(json);

        if (forwardToBackend && !backendForwardInFlight && !string.IsNullOrEmpty(pendingBackendPayload)
            && Time.unscaledTime >= nextBackendForwardTime)
        {
            nextBackendForwardTime = Time.unscaledTime + Mathf.Max(0.05f, backendForwardInterval);
            StartCoroutine(ForwardLatestSensorFrame(pendingBackendPayload));
            pendingBackendPayload = null;
        }

        if (Latest != null && Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + Mathf.Max(0.05f, consoleLogInterval);
            var fsr = Latest.fsr;
            var pressure = Latest.pressure;
            Debug.Log(string.Format(
                "SafeLift sensor values: packets={0}, source={1}, phase={2}, FSR L/R={3:0.00}%/{4:0.00}%, imbalance={5:0.00}%, heel={6} L/R={7:0.0}%/{8:0.0}%, pressure={9:0.00}kPa, bracingLoss={10:0.00}%, status={11}",
                Latest.packetCount,
                string.IsNullOrEmpty(Latest.lastSource) ? "unknown" : Latest.lastSource,
                string.IsNullOrEmpty(Latest.squatPhase) ? "-" : Latest.squatPhase,
                fsr != null ? fsr.leftPercent : 0f,
                fsr != null ? fsr.rightPercent : 0f,
                fsr != null ? fsr.imbalance : 0f,
                fsr != null && !string.IsNullOrEmpty(fsr.heelDroppedSide) ? fsr.heelDroppedSide : "-",
                fsr != null ? fsr.leftHeelRatio : 0f,
                fsr != null ? fsr.rightHeelRatio : 0f,
                pressure != null ? pressure.current : 0f,
                pressure != null ? (pressure.bracingLoss != 0f ? pressure.bracingLoss : pressure.dropRatio) : 0f,
                pressure != null && !string.IsNullOrEmpty(pressure.status) ? pressure.status : "WAIT"));
        }
    }

    void ApplyPacket(string json)
    {
        Packet packet;
        try { packet = JsonUtility.FromJson<Packet>(json); }
        catch (Exception) { invalidPacketCount++; return; }
        if (packet == null) { invalidPacketCount++; return; }

        var packetType = string.IsNullOrEmpty(packet.packetType) ? "BASE" : packet.packetType.ToUpperInvariant();
        if (packetType == "DEBUG")
        {
            if (logPackets) Debug.Log("SafeLift debug packet ignored for scoring: " + packet.source + " / " + packet.timestampMs);
            return;
        }

        packetCount++;
        if (packet.fsr != null && packet.fsr.valid) latestFsr = packet.fsr;
        if (packet.pressure != null) MergePressure(packet.pressure, packet.source);
        if (packet.spine != null)
        {
            // Do not let a transient invalid/no-landmark frame erase a
            // currently valid Fusion spine state. A valid NORMAL packet or a
            // valid BUTT_WINK packet is allowed to replace the previous one.
            if (packet.spine.valid || latestSpine == null || !latestSpine.valid || !latestSpine.buttWink)
                latestSpine = packet.spine;
        }
        if (packet.imu != null) latestImu = packet.imu;
        if (packet.calibration != null) latestCalibration = packet.calibration;
        latestTimestamp = packet.timestampMs;
        var sourceName = packet.source ?? string.Empty;
        if (sourceName.IndexOf("fusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sourceName.IndexOf("buttwink", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // FSR/Bracing packets do not own the Fusion repetition counter.
            // Keep the last Fusion value instead of letting their default 0
            // overwrite it in the merged snapshot.
            latestFusionRepCount = packet.repCount;
        }
        // Only the Fusion/ButtWink producer owns squatPhase. FSR publishes
        // READY and bracing publishes Standing, so accepting their phase
        // values would erase DESCENDING/BOTTOM/ASCENDING immediately.
        var ownsFusionPhase = sourceName.IndexOf("fusion", StringComparison.OrdinalIgnoreCase) >= 0
            || sourceName.IndexOf("buttwink", StringComparison.OrdinalIgnoreCase) >= 0;
        var preserveActiveFusionState = packet.spine != null && !packet.spine.valid
            && latestSpine != null && latestSpine.valid && latestSpine.buttWink;
        if (ownsFusionPhase && !preserveActiveFusionState && !string.IsNullOrEmpty(packet.squatPhase))
        {
            latestSquatPhase = packet.squatPhase;
            lastFusionPhaseRealtime = Time.unscaledTime;
        }
        latestSource = packet.source;
        lastPacketRealtime = Time.unscaledTime;
        Latest = new Snapshot
        {
            timestampMs = latestTimestamp,
            squatPhase = latestSquatPhase,
          repId = packet.repId,
          repCount = latestFusionRepCount,
            packetType = packetType,
            eventType = packet.eventType,
            eventState = packet.eventState,
            eventDurationMs = packet.eventDurationMs,
            scoreEligible = packet.scoreEligible,
            fsr = latestFsr,
            pressure = latestPressure,
            spine = latestSpine,
            imu = latestImu,
            calibration = latestCalibration,
            packetCount = packetCount,
            lastSource = latestSource
        };
        pendingBackendPayload = BuildBackendSensorPayload(Latest);
        if (logPackets) Debug.Log("SafeLift sensor packet received: " + packet.source + " / " + packet.timestampMs);
        SensorUpdated?.Invoke(Latest);
    }

    private string BuildBackendSensorPayload(Snapshot snapshot)
    {
        var fsr = snapshot != null ? snapshot.fsr : null;
        var pressure = snapshot != null ? snapshot.pressure : null;
        var imu = snapshot != null ? snapshot.imu : null;
        var left = fsr != null ? NormalizeRatio(fsr.leftPercent) : 0f;
        var right = fsr != null ? NormalizeRatio(fsr.rightPercent) : 0f;
        var bracingLoss = pressure != null
            ? NormalizeRatio(pressure.bracingLoss != 0f ? pressure.bracingLoss : pressure.dropRatio)
            : 0f;
        var payload = new BackendSensorPayload
        {
            timestamp = snapshot != null ? snapshot.timestampMs / 1000.0 : 0.0,
            left_pressure_ratio = left,
            right_pressure_ratio = right,
            heel_pressure_drop = fsr != null && !string.IsNullOrEmpty(fsr.heelDroppedSide),
            forefoot_bias = false,
            forefoot_shift_percent = 0f,
            fsr_scoring_type = "A",
            imu_pitch = imu != null ? imu.pitchDeg : 0f,
            imu_roll = imu != null ? imu.rollDeg : 0f,
            imu_instability = 0f,
            core_pressure_ratio = Mathf.Clamp01(1f - bracingLoss),
            source = string.IsNullOrEmpty(snapshot != null ? snapshot.lastSource : null)
                ? "unity_udp_forward"
                : snapshot.lastSource
        };
        return JsonUtility.ToJson(payload);
    }

    private static float NormalizeRatio(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
        return Mathf.Clamp01(value > 1f ? value / 100f : value);
    }

    private IEnumerator ForwardLatestSensorFrame(string payload)
    {
        backendForwardInFlight = true;
        using (var request = new UnityWebRequest(backendSensorUrl.TrimEnd('/') + "/api/sensor_frame", UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 2;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success && logPackets)
                Debug.LogWarning("SafeLift sensor backend forward failed: " + (request.error ?? string.Empty));
        }
        backendForwardInFlight = false;
    }

    [Serializable]
    private sealed class BackendSensorPayload
    {
        public double timestamp;
        public float left_pressure_ratio;
        public float right_pressure_ratio;
        public bool heel_pressure_drop;
        public bool forefoot_bias;
        public float forefoot_shift_percent;
        public string fsr_scoring_type;
        public float imu_pitch;
        public float imu_roll;
        public float imu_instability;
        public float core_pressure_ratio;
        public string source;
    }

    // FSR and bracing detectors publish separate pressure payloads to the same
    // UDP port. FSR owns the live current/base signal; bracing owns pBraced and
    // bracingLoss. Merge fields instead of letting the latest whole JSON object
    // erase calibration evidence from the other producer.
    void MergePressure(PressurePayload incoming, string source)
    {
        if (latestPressure == null)
        {
            latestPressure = incoming;
            return;
        }

        var isBracingPacket = (source ?? string.Empty).IndexOf("bracing", StringComparison.OrdinalIgnoreCase) >= 0;
        var pRelaxed = latestPressure.pRelaxed;
        var pBraced = latestPressure.pBraced;
        // Invalid/no-signal packets can deserialize a missing current value as
        // zero. Do not let that transient value erase the last valid pressure
        // and make the UI flicker between 100% and 0%.
        if (incoming.valid && incoming.current > 0f)
            latestPressure.current = incoming.current;
        if (incoming.@base > 0f) latestPressure.@base = incoming.@base;
        if (incoming.dropRatio != 0f || incoming.valid) latestPressure.dropRatio = incoming.dropRatio;
        if (isBracingPacket)
        {
            latestPressure.bracingLoss = incoming.bracingLoss;
            latestPressure.status = incoming.status;
            latestPressure.failed = incoming.failed;
            latestPressure.valid = incoming.valid;
        }
        else if (!string.IsNullOrEmpty(incoming.status) && latestPressure.pBraced <= 0f)
        {
            latestPressure.status = incoming.status;
            latestPressure.valid = incoming.valid;
        }
        if (incoming.pRelaxed > 0f) latestPressure.pRelaxed = incoming.pRelaxed;
        else if (pRelaxed > 0f) latestPressure.pRelaxed = pRelaxed;
        if (incoming.pBraced > 0f) latestPressure.pBraced = incoming.pBraced;
        else if (pBraced > 0f) latestPressure.pBraced = pBraced;
    }

    void Receive(IAsyncResult result)
    {
        try
        {
            if (client == null) return;
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);
            var bytes = client.EndReceive(result, ref endpoint);
            pendingPackets.Enqueue(Encoding.UTF8.GetString(bytes));
            if (client != null) client.BeginReceive(Receive, null);
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (Exception e)
        {
            Debug.LogWarning("SafeLift squat sensor UDP receive error: " + e.Message);
        }
    }

    void OnDisable()
    {
        var old = client;
        client = null;
        if (old == null) return;
        try { old.Close(); } catch (Exception) { }
    }
}
