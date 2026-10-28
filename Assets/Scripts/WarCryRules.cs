using System;

// Digital microphone level (dBFS), not calibrated sound pressure (dB SPL).
public static class WarCryRules
{
    public const float ThresholdDb = -35f;
    public const float FullPowerDb = -8f;
    public static float Power(float db, float threshold = ThresholdDb, float fullPower = FullPowerDb)
    {
        if (float.IsNaN(db)) return 0;
        return Math.Max(0f, Math.Min(1f, (db - threshold) / Math.Max(1f, fullPower - threshold)));
    }
    public static double Multiplier(float power) => 1d + Math.Max(0f, Math.Min(1f, float.IsNaN(power) ? 0f : power));
}
