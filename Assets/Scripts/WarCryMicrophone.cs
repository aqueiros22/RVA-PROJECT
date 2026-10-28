using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// A short in-memory ring buffer only; no audio is saved or played back.
public class WarCryMicrophone : MonoBehaviour
{
    [Tooltip("Digital level below which there is no damage bonus (dBFS).")]
    public float thresholdDb = WarCryRules.ThresholdDb;
    [Tooltip("Digital level that reaches the maximum x2 damage bonus (dBFS).")]
    public float fullPowerDb = WarCryRules.FullPowerDb;
    public float Decibels { get; private set; } = -80f;
    public float PeakPower { get; private set; }
    public string Status { get; private set; } = "Microphone idle";
    private AudioClip clip;
    private string device;
    private float[] samples;
    private int previousPosition = -1;
    private bool measuring;
    private bool permissionRequested;

    public void Begin()
    {
        End();
        PeakPower = 0;
        Decibels = -80;
        StartCoroutine(Open());
    }

    private IEnumerator Open()
    {
        Status = "Waiting for microphone permission";
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            if (permissionRequested) { Status = "Microphone unavailable: check device permissions"; yield break; }
            permissionRequested = true;
            bool finished = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => finished = true;
            callbacks.PermissionDenied += _ => finished = true;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            while (!finished) yield return null;
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            { Status = "Microphone permission denied - normal damage"; yield break; }
        }
#elif UNITY_IOS || UNITY_WEBGL
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        { Status = "Microphone permission denied - normal damage"; yield break; }
#endif
        if (Microphone.devices.Length == 0) { Status = "No microphone - normal damage"; yield break; }
        device = Microphone.devices[0];
        try
        {
            clip = Microphone.Start(device, true, 1, 44100);
            if (clip == null) { Status = "Microphone unavailable - normal damage"; yield break; }
            samples = new float[512 * clip.channels];
            Status = "Microphone ready";
        }
        catch (System.Exception)
        { Status = "Microphone unavailable - normal damage"; }
        yield break;
    }

    public void SetMeasuring(bool value) { measuring = value; }

    // Called before gesture classification so the final stroke's audio is included.
    public void Sample()
    {
        if (!measuring || clip == null || !Microphone.IsRecording(device)) return;
        int position = Microphone.GetPosition(device);
        if (position < 512 || position == previousPosition) return;
        previousPosition = position;
        if (!clip.GetData(samples, position - 512)) return;
        double sum = 0;
        foreach (float sample in samples) sum += sample * sample;
        float rms = (float)System.Math.Sqrt(sum / samples.Length);
        Decibels = Mathf.Clamp(20f * Mathf.Log10(Mathf.Max(0.0001f, rms)), -80f, 0f);
        PeakPower = Mathf.Max(PeakPower, WarCryRules.Power(Decibels, thresholdDb, fullPowerDb));
    }

    public void End()
    {
        StopAllCoroutines();
        measuring = false;
        previousPosition = -1;
        if (clip != null)
        {
            Microphone.End(device);
            Destroy(clip);
            clip = null;
        }
    }
    void OnDisable() { End(); }
    void OnApplicationPause(bool paused) { if (paused) End(); }
    void OnApplicationFocus(bool focused) { if (!focused) End(); }
}
