using UnityEngine;
using UnityEngine.XR;

public sealed class SafeLiftXRHapticsManager : MonoBehaviour
{
    public float Cooldown = .35f;
    private float nextPulse;
    public void ButtonClick() => Pulse(.12f, .08f);
    public void RepCounted() => Pulse(.25f, .12f);
    public void ReportReady() => Pulse(.4f, .2f);
    public void TrackingBad() => Pulse(.25f, .12f);
    public void WeightShiftLeft() => Pulse(.35f, .15f, XRNode.LeftHand);
    public void WeightShiftRight() => Pulse(.35f, .15f, XRNode.RightHand);
    public void CorePressureDrop() { Pulse(.55f, .18f); Invoke(nameof(ButtonClick), .22f); }
    public void LiveCue(string cue) => LiveCue(cue, 0f);
    public void LiveCue(string cue, float leftPressureRatio)
    {
        if (string.IsNullOrEmpty(cue)) return;
        var value = cue.ToLowerInvariant();
        if (value.Contains("weight") || value.Contains("asymmetry") || value.Contains("shift"))
        {
            if (leftPressureRatio > .55f) WeightShiftLeft();
            else if (leftPressureRatio > 0f && leftPressureRatio < .45f) WeightShiftRight();
            else Pulse(.30f, .12f);
        }
        else if (value.Contains("core") || value.Contains("brace") || value.Contains("pressure"))
        {
            CorePressureDrop();
        }
        else if (value.Contains("tracking"))
        {
            TrackingBad();
        }
        else
        {
            Pulse(.22f, .1f);
        }
    }
    private void Pulse(float amplitude, float duration, XRNode node = XRNode.RightHand)
    {
        if (Time.unscaledTime < nextPulse) return; nextPulse = Time.unscaledTime + Cooldown;
        var devices = new System.Collections.Generic.List<InputDevice>(); InputDevices.GetDevicesAtXRNode(node, devices); foreach (var device in devices) if (device.isValid) device.SendHapticImpulse(0u, amplitude, duration);
    }
}
