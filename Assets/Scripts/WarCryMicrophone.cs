using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Records the attack in memory only: the loudest moment of the drag sets the damage bonus, and the
// shout around the loudest moment (TakeCry) is played back when that attack swings. Nothing is saved to disk or uploaded.
public class WarCryMicrophone : MonoBehaviour
{
    [Tooltip("Digital level below which there is no damage bonus (dBFS).")]
    public float thresholdDb = WarCryRules.ThresholdDb;
    [Tooltip("Digital level that reaches the maximum x1.5 damage bonus (dBFS).")]
    public float fullPowerDb = WarCryRules.FullPowerDb;
    [Tooltip("Seconds kept from Detect pen on: covers the countdown, the drag and a moment after it.")]
    public int recordSeconds = 10;
    [Tooltip("Longest cry played back in the clash (seconds).")]
    public float maxCrySeconds = 3.5f;
    public float Decibels { get; private set; } = -80f;
    public float PeakPower { get; private set; }
    public string Status { get; private set; } = "Microphone idle";
    // Still opening or waiting for permission counts as ready: only a failure is reported
    public bool Ready => clip != null || !Status.Contains("normal damage") && !Status.Contains("unavailable");
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
            Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
            int rate = maxRate == 0 ? 22050 : Mathf.Clamp(22050, minRate, maxRate);
            // Not looping: the whole countdown and drag stay in the clip, so the cry can be cut out of it
            clip = Microphone.Start(device, false, recordSeconds, rate);
            if (clip == null) { Status = "Microphone unavailable - normal damage"; yield break; }
            samples = new float[512 * clip.channels];
            Status = "Microphone ready";
        }
        catch (System.Exception)
        { Status = "Microphone unavailable - normal damage"; }
        yield break;
    }

    // Only the drag counts for the damage bonus; the recording itself starts at Detect pen
    public void SetMeasuring(bool value) { measuring = value; }

    // The whole shout around its loudest moment (countdown, drag and the moment after included),
    // with the level evened out, or null without a cry
    public AudioClip TakeCry()
    {
        if (clip == null) return null;
        int frames = Microphone.IsRecording(device) ? Microphone.GetPosition(device) : clip.samples;
        int channels = clip.channels;
        if (frames <= 0) return null;
        var data = new float[frames * channels];
        if (!clip.GetData(data, 0)) return null;

        // Loudness of each short window
        const int window = 256;
        int count = (frames + window - 1) / window;
        var rms = new float[count];
        int loudest = 0;
        for (int w = 0; w < count; w++)
        {
            int from = w * window * channels, to = Mathf.Min(data.Length, from + window * channels);
            double sum = 0;
            for (int i = from; i < to; i++) sum += data[i] * data[i];
            rms[w] = (float)System.Math.Sqrt(sum / (to - from));
            if (rms[w] > rms[loudest]) loudest = w;
        }
        // The shout fades below the bonus threshold before it really ends: follow it a little lower
        float threshold = Mathf.Pow(10f, (thresholdDb - 6f) / 20f);
        if (rms[loudest] < Mathf.Pow(10f, thresholdDb / 20f)) return null;

        // Grow from the loudest window while the shout goes on, through breaths of up to 0.3 s
        int gap = Mathf.Max(1, Mathf.RoundToInt(0.3f * clip.frequency / window));
        int maxWindows = Mathf.RoundToInt(maxCrySeconds * clip.frequency / window);
        int first = loudest, last = loudest;
        bool grew = true;
        while (grew && last - first + 1 < maxWindows)
        {
            grew = false;
            for (int w = last + 1; w <= Mathf.Min(count - 1, last + gap); w++)
                if (rms[w] >= threshold) { last = w; grew = true; break; }
            if (last - first + 1 >= maxWindows) break;
            for (int w = first - 1; w >= Mathf.Max(0, first - gap); w--)
                if (rms[w] >= threshold) { first = w; grew = true; break; }
        }
        int pad = clip.frequency / 10;
        int start = Mathf.Max(0, first * window - pad);
        int stop = Mathf.Min(frames, (last + 1) * window + pad);

        int length = stop - start;
        var cry = new float[length * channels];
        System.Array.Copy(data, start * channels, cry, 0, cry.Length);
        float peak = 0;
        foreach (float sample in cry) peak = Mathf.Max(peak, Mathf.Abs(sample));
        float gain = peak > 0 ? Mathf.Min(4f, 0.9f / peak) : 1f;
        // Short fades so the cut does not click
        int fade = Mathf.Min(length / 2, clip.frequency / 50);
        for (int f = 0; f < length; f++)
        {
            float envelope = Mathf.Min(1f, Mathf.Min(f, length - 1 - f) / (float)Mathf.Max(1, fade));
            for (int c = 0; c < channels; c++) cry[f * channels + c] *= gain * envelope;
        }
        var result = AudioClip.Create("War Cry", length, channels, clip.frequency, false);
        result.SetData(cry, 0);
        return result;
    }

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
