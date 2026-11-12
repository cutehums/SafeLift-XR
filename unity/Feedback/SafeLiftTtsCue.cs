using UnityEngine;

public sealed class SafeLiftTtsCue : MonoBehaviour
{
    public bool Enabled = true;
    public float MinSecondsBetweenCues = 0.75f;
    public float SpeechRate = 0.95f;
    public float Pitch = 1f;
    public string LastCue { get; private set; }

    private float lastSpeakAt = -999f;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject androidTts;
    private bool androidTtsReady;
#endif

    public void Speak(string message)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(message)) return;

        if (Time.unscaledTime - lastSpeakAt < MinSecondsBetweenCues)
            return;

        LastCue = message.Trim();
        lastSpeakAt = Time.unscaledTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            EnsureAndroidTts();
            if (androidTtsReady && androidTts != null)
                androidTts.Call<int>("speak", LastCue, 0, null, "safelift");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("SafeLift TTS unavailable: " + ex.Message);
        }
#else
        Debug.Log("SafeLift TTS cue: " + LastCue);
#endif
    }

    public void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (androidTts != null) androidTts.Call<int>("stop");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("SafeLift TTS stop unavailable: " + ex.Message);
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void EnsureAndroidTts()
    {
        if (androidTtsReady && androidTts != null) return;

        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            androidTts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, null);
            androidTts.Call<int>("setSpeechRate", SpeechRate);
            androidTts.Call<int>("setPitch", Pitch);
            androidTtsReady = true;
        }
    }
#endif

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (androidTts != null)
            {
                androidTts.Call<int>("stop");
                androidTts.Call("shutdown");
                androidTts.Dispose();
                androidTts = null;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("SafeLift TTS shutdown unavailable: " + ex.Message);
        }
        androidTtsReady = false;
#endif
    }
}
